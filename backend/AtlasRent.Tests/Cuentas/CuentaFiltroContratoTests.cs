using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Cuentas;

/// <summary>
/// El filtro <c>contratoId</c> de la vista de cuentas: acota facturas y fianzas al contrato, y no
/// cambia lo que cada rol puede ver (el empleado nunca recibe totales).
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class CuentaFiltroContratoTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    [Fact]
    public async Task ElFiltroDeContratoSoloTraeLoDeEseContrato()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var uno = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var otro = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaUno = await AyudaCuentas.EmitidaAsync(admin, uno, uno.Hoy.AddDays(10), 300m, ct);
        var facturaOtro = await AyudaCuentas.EmitidaAsync(admin, otro, otro.Hoy.AddDays(10), 400m, ct);
        await AyudaCuentas.FianzaAsync(admin, uno, 500m, ct);
        await AyudaCuentas.FianzaAsync(admin, otro, 600m, ct);

        var vista = await AyudaCuentas.VistaAsync(admin, $"contratoId={uno.ContratoId:D}", ct);

        AyudaCuentas.Contiene(vista, facturaUno).Should().BeTrue();
        AyudaCuentas.Contiene(vista, facturaOtro).Should().BeFalse();
        AyudaCuentas.Filas(vista).Should().HaveCount(2, "la factura y la fianza del contrato, y nada más");
        vista.GetProperty("total").GetInt32().Should().Be(2);
    }

    [Theory]
    [InlineData(Roles.Empleado, false)]
    [InlineData(Roles.Gerente, true)]
    [InlineData(Roles.Admin, true)]
    public async Task ElFiltroDeContratoNoCambiaQuienRecibeTotales(string rol, bool recibeTotales)
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);
        using var sesion = await AyudaCuentas.SesionAsync(bd, api, rol, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        var vista = await AyudaCuentas.VistaAsync(sesion, $"contratoId={escenario.ContratoId:D}", ct);

        AyudaCuentas.Filas(vista).Should().ContainSingle();
        (vista.GetProperty("totales").ValueKind != System.Text.Json.JsonValueKind.Null)
            .Should().Be(recibeTotales);
    }
}
