using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Mensajeria;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Recordatorios;

/// <summary>
/// F4-05: las dos alertas que levanta este trabajo. La de envÃ­os fallidos es de Â§3.16 â€”Â«envÃ­os
/// fallidosÂ», que era una de las que no existÃ­anâ€” y la otra es la del criterio de BUG-007: sin plazos
/// configurados no se avisa a nadie y se dice quÃ© configurar.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class RecordatorioAlertasTests(ContenedorBdFixture bd, ApiTestFactoryCorreo api)
    : IClassFixture<ApiTestFactoryCorreo>
{
    /// <summary>
    /// Un envÃ­o que no sale levanta su alerta una sola vez por envÃ­o, con la clave del envÃ­o y no la
    /// del dÃ­a, y el motivo no lleva ni una credencial ni una cadena de conexiÃ³n: la alerta se lee
    /// fuera del sistema, en una bandeja.
    /// </summary>
    [Fact]
    public async Task UnEnvioFallidoLevantaUnaAlertaPorEnvioSinCredenciales()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaRecordatorios.SesionAsync(bd, api, Roles.Admin, ct);
        await AyudaRecordatorios.PonerModoAsync(admin, ModoRecordatorios.Manual, ct: ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, hoy.AddDays(-5), 300m, ct);

        await AyudaRecordatorios.EjecutarTrabajoAsync(api, ct);

        var aviso = (await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct)).Single();
        var envio = await AyudaRecordatorios.EnvioDelAvisoAsync(bd, aviso, ct);

        // El fallo trae una cadena de conexiÃ³n dentro, a propÃ³sito: es lo que no puede salir de aquÃ­.
        api.Doble.Fallar = new InvalidOperationException(
            "Host=smtp.banco;Username=usuario;Password=clave-secreta;Database=atlas_rent");

        await AyudaRecordatorios.EnviarAsync(admin, aviso.Id, ct);
        await AyudaRecordatorios.EnviarAsync(admin, aviso.Id, ct);

        var alertas = await AyudaRecordatorios.AlertasAsync(bd, ServicioEnvios.TipoAlertaEnvioFallido, ct);
        var delEnvio = alertas
            .Where(a => a.Clave == ServicioEnvios.ClaveAlertaEnvioFallido(envio.Id))
            .ToList();

        delEnvio.Should().ContainSingle("la clave lleva el envÃ­o, asÃ­ que reintentar no llena la bandeja de copias");
        delEnvio.Single().Repeticiones.Should().Be(2, "lo que se ve es que el mismo envÃ­o lleva dos intentos");

        var mensaje = delEnvio.Single().Mensaje;
        mensaje.Should().NotContain("clave-secreta");
        mensaje.Should().NotContain("Password");
        mensaje.Should().NotContain("smtp.banco");
        mensaje.Should().Contain("InvalidOperationException", "de una excepciÃ³n ajena solo sale el tipo");

        (await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct))
            .Single()
            .Estado.Should().Be(EstadoRecordatorio.Pendiente, "un envÃ­o fallido se puede volver a intentar");

        api.Doble.Restablecer();
    }

    /// <summary>
    /// Sin plazos configurados el trabajo no manda nada y deja la alerta que dice quÃ© escribir. Un
    /// recordatorio con plazos sin decidir es ruido para el cliente (criterio de BUG-007).
    /// </summary>
    [Fact]
    public async Task SinPlazosConfiguradosNoEnviaYDejaLaAlerta()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaRecordatorios.SesionAsync(bd, api, Roles.Admin, ct);
        await AyudaRecordatorios.PonerModoAsync(
            admin, ModoRecordatorios.Automatico, primero: null, entre: null, ct: ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, hoy.AddDays(-5), 300m, ct);

        await AyudaRecordatorios.EjecutarTrabajoAsync(api, ct);

        (await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct))
            .Should().BeEmpty("sin plazos no se avisa, y no se avisa Â«a los cinco por defectoÂ»");
        (await AyudaRecordatorios.CorreosDe(bd, api, escenario.ClienteId, ct)).Should().BeEmpty();

        var alertas = await AyudaRecordatorios.AlertasAsync(bd, ServicioRecordatorios.TipoAlerta, ct);
        var aviso = alertas.SingleOrDefault(a => a.Clave == ServicioRecordatorios.ClaveSinPlazos);

        aviso.Should().NotBeNull("si no se dice nada, nadie sabe por quÃ© no llega ningÃºn aviso");
        aviso!.Mensaje.Should().Contain("dias_primer_recordatorio");
        aviso.Mensaje.Should().Contain("dias_entre_recordatorios");

        // Y con los plazos puestos, el aviso sale y la alerta se cierra sola.
        await AyudaRecordatorios.PonerModoAsync(admin, ModoRecordatorios.Automatico, ct: ct);
        await AyudaRecordatorios.EjecutarTrabajoAsync(api, ct);

        (await AyudaRecordatorios.CorreosDe(bd, api, escenario.ClienteId, ct)).Should().ContainSingle();
        (await AyudaRecordatorios.AlertasAsync(bd, ServicioRecordatorios.TipoAlerta, ct))
            .Should().NotContain(a => a.Clave == ServicioRecordatorios.ClaveSinPlazos);
    }
}