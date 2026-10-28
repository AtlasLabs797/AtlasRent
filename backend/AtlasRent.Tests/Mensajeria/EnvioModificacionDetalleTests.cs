using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Mensajeria;

/// <summary>
/// El detalle de la factura y la respuesta de modificar dicen <c>modificadaTrasEnvio</c> igual que la
/// vista de cuentas. Va en su propia clase y fábrica porque avanza el reloj de pruebas, y eso caduca la
/// sesión del siguiente test que comparta fábrica (ver <see cref="EnvioModificacionTests"/>).
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class EnvioModificacionDetalleTests(ContenedorBdFixture bd, ApiTestFactoryCorreo api)
    : IClassFixture<ApiTestFactoryCorreo>
{
    [Fact]
    public async Task ElDetalleYLaRespuestaDeModificarMarcanModificadaTrasEnvio()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaEnvios.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var factura = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);
        await AyudaEnvios.EnviarAsync(admin, AyudaEnvios.PeticionFactura(factura, AyudaEnvios.CorreoDePrueba()), ct);

        var antes = await admin.GetAsync($"/api/v1/facturas/{factura:D}", ct);
        (await antes.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("modificadaTrasEnvio")
            .GetBoolean().Should().BeFalse("se mandó y no se ha tocado");

        api.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        var respuesta = await AyudaEnvios.ModificarFacturaAsync(admin, factura, ct);

        respuesta.GetProperty("modificadaTrasEnvio").GetBoolean()
            .Should().BeTrue("la propia respuesta de modificar lo dice");

        var despues = await admin.GetAsync($"/api/v1/facturas/{factura:D}", ct);
        (await despues.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("modificadaTrasEnvio")
            .GetBoolean().Should().BeTrue();
    }
}
