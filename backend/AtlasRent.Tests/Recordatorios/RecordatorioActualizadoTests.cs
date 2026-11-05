using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Mensajeria;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Recordatorios;

/// <summary>BUG-040: el recordatorio manual sale con el saldo vencido total de ahora, no con el de cuando se preparó.</summary>
[Collection(nameof(ColeccionBd))]
public sealed class RecordatorioActualizadoTests(ContenedorBdFixture bd, ApiTestFactoryCorreo api)
    : IClassFixture<ApiTestFactoryCorreo>
{
    [Fact]
    public async Task UnPagoParcialEntrePrepararYConfirmarActualizaElCorreo()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaRecordatorios.SesionAsync(bd, api, Roles.Admin, ct);
        await AyudaRecordatorios.PonerModoAsync(admin, ModoRecordatorios.Manual, ct: ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaId = await AyudaCuentas.EmitidaAsync(admin, escenario, hoy.AddDays(-5), 300m, ct);

        await AyudaRecordatorios.EjecutarTrabajoAsync(api, ct);
        var aviso = (await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct)).Single();

        await AyudaCuentas.CobrarAsync(admin, escenario, facturaId, 100m, hoy, ct);
        await AyudaRecordatorios.EnviarAsync(admin, aviso.Id, ct);

        var correo = (await AyudaRecordatorios.CorreosDe(bd, api, escenario.ClienteId, ct)).Should().ContainSingle().Which;
        correo.Cuerpo.Should().Contain("200,00 USD").And.NotContain("300,00 USD");
        correo.Adjunto.Should().NotBeNullOrEmpty();

        var envio = await AyudaRecordatorios.EnvioDelAvisoAsync(bd, aviso, ct);
        envio.Cuerpo.Should().Be(correo.Cuerpo, "lo guardado es lo que salió");
    }

    [Fact]
    public async Task ElImporteCitadoEsElSaldoVencidoTotalYNoSoloElDeLasFacturasEnPlazo()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaRecordatorios.SesionAsync(bd, api, Roles.Admin, ct);
        await AyudaRecordatorios.PonerModoAsync(admin, ModoRecordatorios.Manual, ct: ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);

        // A 5 días toca avisar; la de 8 días ya vencida no está en plazo de cadencia pero se debe.
        await AyudaCuentas.EmitidaAsync(admin, escenario, hoy.AddDays(-5), 300m, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, hoy.AddDays(-8), 100m, ct);

        await AyudaRecordatorios.EjecutarTrabajoAsync(api, ct);
        var aviso = (await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct)).Single();
        var envio = await AyudaRecordatorios.EnvioDelAvisoAsync(bd, aviso, ct);

        envio.Cuerpo.Should().Contain("400,00 USD");
    }
}
