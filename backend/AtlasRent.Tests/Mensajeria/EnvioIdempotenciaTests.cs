using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Mensajeria;

/// <summary>
/// F4-02: un reintento con la misma <c>Idempotency-Key</c> manda una sola vez.
/// <para>
/// Sin esto, el doble clic de quien no está seguro de si le salió —o el reintento automático de un
/// proxy— le mandaría al cliente la misma factura dos veces, y el segundo correo no lo distingue del
/// primero.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class EnvioIdempotenciaTests(ContenedorBdFixture bd, ApiTestFactoryCorreo api)
    : IClassFixture<ApiTestFactoryCorreo>
{
    /// <summary>
    /// La misma clave dos veces con el mismo cuerpo devuelve el mismo envío y no manda dos correos.
    /// </summary>
    [Fact]
    public async Task ElMismoEnvioConLaMismaClaveSaleUnaSolaVez()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaEnvios.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaId = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        var destino = AyudaEnvios.CorreoDePrueba();
        var peticion = AyudaEnvios.PeticionFactura(facturaId, destino);
        var clave = $"clave-envio-{Guid.NewGuid():N}";

        var primera = await admin.PostAsync("/api/v1/envios", peticion, clave, ct);
        primera.StatusCode.Should().Be(HttpStatusCode.OK, await primera.Cuerpo());

        var segunda = await admin.PostAsync("/api/v1/envios", peticion, clave, ct);
        segunda.StatusCode.Should().Be(HttpStatusCode.OK, await segunda.Cuerpo());

        var idPrimera = (await primera.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var idSegunda = (await segunda.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();

        idSegunda.Should().Be(idPrimera, "el reintento devuelve el envío ya hecho");

        (await AyudaEnvios.EnviosAsync(bd, facturaId, destino, ct))
            .Should().Be(1, "el reintento no escribe una segunda fila de envío");

        var correos = api.Doble.Enviados;

        correos.Should().ContainSingle(
            m => m.DestinatarioEmail == destino,
            "y lo que importa: el cliente recibió un correo, no dos");
    }
}