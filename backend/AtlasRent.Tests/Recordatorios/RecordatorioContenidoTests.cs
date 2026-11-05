using AtlasRent.Api.Authorization;
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
/// F4-05: qué dice el aviso y qué lleva adjunto (docs/ESPECIFICACION.md §3.14: «texto corto», «PDF de
/// estado de cuenta adjunto», «con pago parcial, mencionan solo el pendiente»).
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class RecordatorioContenidoTests(ContenedorBdFixture bd, ApiTestFactoryCorreo api)
    : IClassFixture<ApiTestFactoryCorreo>
{
    /// <summary>
    /// Va con el estado de cuenta adjunto y, con pago parcial, dice lo que falta y no lo que se
    /// facturó. Recordar 300 cuando ya se pagaron 150 es pedir 150 de más, y es el error que hace que
    /// un cliente deje de leer el correo.
    /// </summary>
    [Fact]
    public async Task ElRecordatorioVaConElEstadoDeCuentaAdjuntoYSoloHablaDelPendiente()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaRecordatorios.SesionAsync(bd, api, Roles.Admin, ct);
        await AyudaRecordatorios.PonerModoAsync(admin, ModoRecordatorios.Manual, ct: ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaId = await AyudaCuentas.EmitidaAsync(admin, escenario, hoy.AddDays(-5), 300m, ct);

        await AyudaCuentas.CobrarAsync(admin, escenario, facturaId, 150m, hoy, ct);

        await AyudaRecordatorios.EjecutarTrabajoAsync(api, ct);

        var aviso = (await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct)).Single();
        var envio = await AyudaRecordatorios.EnvioDelAvisoAsync(bd, aviso, ct);

        envio.PdfCopia.Should().NotBeEmpty("§3.14 manda el estado de cuenta adjunto");
        envio.PdfCopia.Take(5).Should().Equal("%PDF-"u8.ToArray(), "lo adjunto es el PDF de F4-01");

        envio.Cuerpo.Should().Contain("150,00 USD", "el aviso menciona el pendiente");
        envio.Cuerpo.Should().NotContain("300,00", "y no el total de lo facturado");
        envio.Cuerpo.Should().NotContain("{", "no queda ninguna variable sin sustituir");
    }

    /// <summary>
    /// El PDF se compone una vez, al preparar, y el envío manda esa misma copia: si lo recompusiera
    /// al enviar, lo que el cliente recibiría y lo que se revisó podrían ser dos generaciones
    /// distintas del mismo documento.
    /// </summary>
    [Fact]
    public async Task ElEnvioMandaLaCopiaCompuestaAlPreparar()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaRecordatorios.SesionAsync(bd, api, Roles.Admin, ct);
        await AyudaRecordatorios.PonerModoAsync(admin, ModoRecordatorios.Manual, ct: ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, hoy.AddDays(-5), 300m, ct);

        await AyudaRecordatorios.EjecutarTrabajoAsync(api, ct);

        var aviso = (await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct)).Single();
        var antes = await AyudaRecordatorios.EnvioDelAvisoAsync(bd, aviso, ct);

        antes.PdfCopia.Should().NotBeEmpty("la copia existe ya con la fila en Pendiente");

        await AyudaRecordatorios.EnviarAsync(admin, aviso.Id, ct);

        var despues = await AyudaRecordatorios.EnvioDelAvisoAsync(bd, aviso, ct);

        despues.PdfCopia.Should().Equal(antes.PdfCopia, "lo que sale es exactamente lo que se compuso");
        api.Doble.Ultimo!.Adjunto.Should().Equal(antes.PdfCopia, "y el adjunto es esa misma copia");
        api.Doble.Ultimo.NombreAdjunto.Should().StartWith("estado-de-cuenta-");
    }
}