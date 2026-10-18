using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Mensajeria;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Recordatorios;

/// <summary>
/// F4-05: los tres modos de D12 y el criterio de aceptaciÃ³n de <c>docs/PLAN.md</c> â€”Â«modo manual no
/// envÃ­a nada sin confirmaciÃ³nÂ»â€” con su otra mitad, Â«se detienen al pagarÂ».
/// <para>
/// El atraso se hace con la fecha de vencimiento en el pasado, no moviendo el reloj: asÃ­ el dÃ­a que
/// ve la aplicaciÃ³n es siempre el mismo y las pruebas no dependen de una sesiÃ³n abierta que el salto
/// de dÃ­as dejarÃ­a caducada.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class RecordatorioModoTests(ContenedorBdFixture bd, ApiTestFactoryCorreo api)
    : IClassFixture<ApiTestFactoryCorreo>
{
    /// <summary>Apagado: el trabajo no hace nada, ni avisos ni correos.</summary>
    [Fact]
    public async Task ElModoApagadoNoHaceNada()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaRecordatorios.SesionAsync(bd, api, Roles.Admin, ct);
        await AyudaRecordatorios.PonerModoAsync(admin, ModoRecordatorios.Apagado, ct: ct);

        var escenario = await VencidaAsync(admin, 300m, ct);

        await AyudaRecordatorios.EjecutarTrabajoAsync(api, ct);

        (await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct))
            .Should().BeEmpty("el interruptor apagado no deja pasar ni una fila");
        (await AyudaRecordatorios.CorreosDe(bd, api, escenario.ClienteId, ct)).Should().BeEmpty();
    }

    /// <summary>AutomÃ¡tico: prepara y manda. Es el Ãºnico modo que manda sin que nadie pulse nada.</summary>
    [Fact]
    public async Task ElModoAutomaticoEnviaSolo()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaRecordatorios.SesionAsync(bd, api, Roles.Admin, ct);
        await AyudaRecordatorios.PonerModoAsync(admin, ModoRecordatorios.Automatico, ct: ct);

        var escenario = await VencidaAsync(admin, 300m, ct);

        await AyudaRecordatorios.EjecutarTrabajoAsync(api, ct);

        var aviso = (await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct)).Single();
        var envio = await AyudaRecordatorios.EnvioDelAvisoAsync(bd, aviso, ct);

        aviso.Estado.Should().Be(EstadoRecordatorio.Enviado);
        envio.Estado.Should().Be(EstadoEnvio.Enviado);
        (await AyudaRecordatorios.CorreosDe(bd, api, escenario.ClienteId, ct)).Should().ContainSingle();
    }

    /// <summary>
    /// Manual: el trabajo deja la fila <c>Pendiente</c> y no manda nada. Solo sale un correo despuÃ©s
    /// del <c>POST .../enviar</c>. Es la primera mitad del criterio de aceptaciÃ³n de la tarea.
    /// </summary>
    [Fact]
    public async Task ElModoManualNoEnviaNadaSinConfirmacion()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaRecordatorios.SesionAsync(bd, api, Roles.Admin, ct);
        await AyudaRecordatorios.PonerModoAsync(admin, ModoRecordatorios.Manual, ct: ct);

        var escenario = await VencidaAsync(admin, 300m, ct);

        await AyudaRecordatorios.EjecutarTrabajoAsync(api, ct);

        var aviso = (await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct)).Single();

        aviso.Estado.Should().Be(EstadoRecordatorio.Pendiente);
        (await AyudaRecordatorios.CorreosDe(bd, api, escenario.ClienteId, ct)).Should().BeEmpty("el modo manual no manda nada sin que alguien lo confirme");

        var cuerpo = await AyudaRecordatorios.EnviarAsync(admin, aviso.Id, ct);

        (await AyudaRecordatorios.CorreosDe(bd, api, escenario.ClienteId, ct)).Should().ContainSingle("solo tras la confirmaciÃ³n sale el correo");
        cuerpo.Should().Contain(nameof(EstadoRecordatorio.Enviado));

        (await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct))
            .Single()
            .Estado.Should().Be(EstadoRecordatorio.Enviado);
    }

    /// <summary>
    /// Se detiene al pagar: entre que el trabajo prepara el aviso y que alguien confirma, el cliente
    /// paga, y la confirmaciÃ³n no manda nada y lo deja <c>Omitido</c> con su motivo. Es la segunda
    /// mitad del criterio de aceptaciÃ³n.
    /// </summary>
    [Fact]
    public async Task SeDetienenAlPagar()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaRecordatorios.SesionAsync(bd, api, Roles.Admin, ct);
        await AyudaRecordatorios.PonerModoAsync(admin, ModoRecordatorios.Manual, ct: ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaId = await AyudaCuentas.EmitidaAsync(admin, escenario, hoy.AddDays(-5), 300m, ct);

        await AyudaRecordatorios.EjecutarTrabajoAsync(api, ct);

        var aviso = (await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct)).Single();
        aviso.Estado.Should().Be(EstadoRecordatorio.Pendiente);

        await AyudaCuentas.CobrarAsync(admin, escenario, facturaId, 300m, hoy, ct);

        await AyudaRecordatorios.EnviarAsync(admin, aviso.Id, ct);

        (await AyudaRecordatorios.CorreosDe(bd, api, escenario.ClienteId, ct)).Should().BeEmpty("un cliente al que acaban de cobrarle no recibe un recordatorio");

        var tras = (await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct)).Single();
        tras.Estado.Should().Be(EstadoRecordatorio.Omitido);
        tras.MotivoOmision.Should().NotBeNull("el aviso se salta y dice por quÃ©, no desaparece");
    }

    /// <summary>La pausa del cliente frena tambiÃ©n el envÃ­o manual de uno en uno.</summary>
    [Fact]
    public async Task UnClienteConRecordatoriosPausadosNoRecibeAvisos()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaRecordatorios.SesionAsync(bd, api, Roles.Admin, ct);
        await AyudaRecordatorios.PonerModoAsync(admin, ModoRecordatorios.Manual, ct: ct);

        var escenario = await VencidaAsync(admin, 300m, ct);

        await AyudaRecordatorios.PausarAsync(admin, escenario.ClienteId, pausados: true, ct);

        await AyudaRecordatorios.EjecutarTrabajoAsync(api, ct);

        (await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct))
            .Should().BeEmpty("el trabajo ni siquiera escribe la fila de un cliente con la pausa puesta");
        (await AyudaRecordatorios.CorreosDe(bd, api, escenario.ClienteId, ct)).Should().BeEmpty();

        // Con la pausa levantada vuelve a entrar, y el mismo envÃ­o manual tambiÃ©n se frena.
        await AyudaRecordatorios.PausarAsync(admin, escenario.ClienteId, pausados: false, ct);
        await AyudaRecordatorios.EjecutarTrabajoAsync(api, ct);

        var aviso = (await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct)).Single();

        await AyudaRecordatorios.PausarAsync(admin, escenario.ClienteId, pausados: true, ct);
        await AyudaRecordatorios.EnviarAsync(admin, aviso.Id, ct);

        (await AyudaRecordatorios.CorreosDe(bd, api, escenario.ClienteId, ct)).Should().BeEmpty("la pausa manda sobre el envÃ­o manual, no solo sobre el trabajo");
        (await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct))
            .Single()
            .Estado.Should().Be(EstadoRecordatorio.Omitido);
    }

    /// <summary>El endpoint de pausa devuelve el cliente con la bandera cambiada.</summary>
    [Fact]
    public async Task ElEndpointDePausaDevuelveElCliente()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaRecordatorios.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);

        var respuesta = await admin.PutAsync(
            $"/api/v1/clientes/{escenario.ClienteId:D}/recordatorios-pausados",
            new { pausados = true },
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());

        var cliente = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);

        cliente.GetProperty("recordatoriosPausados").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// Un cliente y una unidad con varias facturas vencidas en plazo reciben un aviso, no uno por
    /// factura: Â§3.14 dice Â«un mensaje por cliente y unidadÂ».
    /// </summary>
    [Fact]
    public async Task UnAvisoPorClienteYUnidadAunqueTengaVariasFacturasVencidas()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaRecordatorios.SesionAsync(bd, api, Roles.Admin, ct);
        await AyudaRecordatorios.PonerModoAsync(admin, ModoRecordatorios.Manual, ct: ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);

        await AyudaCuentas.EmitidaAsync(admin, escenario, hoy.AddDays(-5), 300m, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, hoy.AddDays(-5), 150m, ct);

        await AyudaRecordatorios.EjecutarTrabajoAsync(api, ct);

        var avisos = await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct);

        avisos.Should().ContainSingle("dos facturas del mismo cliente y unidad son un solo aviso");
        avisos.Should().OnlyContain(a => a.UnidadId == escenario.UnidadId);

        var envio = await AyudaRecordatorios.EnvioDelAvisoAsync(bd, avisos.Single(), ct);

        envio.Cuerpo.Should().Contain("450,00 USD", "y el aviso menciona el pendiente de las dos");
    }

    /// <summary>Cliente, contrato y una factura emitida que venciÃ³ hace cinco dÃ­as.</summary>
    private async Task<AyudaCuentas.Escenario> VencidaAsync(SesionCliente sesion, decimal total, CancellationToken ct)
    {
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);
        var escenario = await AyudaCuentas.EscenarioAsync(sesion, bd, ct);

        await AyudaCuentas.EmitidaAsync(sesion, escenario, hoy.AddDays(-5), total, ct);

        return escenario;
    }
}