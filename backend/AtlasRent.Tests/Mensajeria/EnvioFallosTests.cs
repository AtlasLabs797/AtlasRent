using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Reservas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Mensajeria;

/// <summary>
/// F4-02: los tres caminos en los que un envío no sale como se pidió —el servidor falla, el correo no
/// está configurado y el documento ya se mandó— y que ninguno se quede en silencio.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class EnvioFallosTests(ContenedorBdFixture bd, ApiTestFactoryCorreo api)
    : IClassFixture<ApiTestFactoryCorreo>
{
    /// <summary>
    /// Un fallo del SMTP deja la fila en <c>Fallido</c> con su motivo, y el motivo no lleva ni el
    /// usuario, ni la clave, ni el host.
    /// <para>
    /// El doble lanza una excepción cuyo mensaje los trae todos, a propósito: es la forma de
    /// comprobar que el criterio de F3-01 (DOC_TECNICA §8.0.1) se está aplicando de verdad y no de
    /// palabra. De una excepción que no es del sistema solo se copia el tipo, y con eso basta para
    /// diagnosticar sin filtrar nada.
    /// </para>
    /// <para>
    /// Y el motivo sale vacío en el PDF del correo: la copia es lo que el cliente recibió, y lo que se
    /// puede contar de un envío fallido se dice en la lista de envíos, no dentro del papel.
    /// </para>
    /// </summary>
    [Fact]
    public async Task UnFalloDelCorreoDejaElEnvioFallidoSinCredenciales()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaEnvios.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaId = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        api.Doble.Fallar = new InvalidOperationException(
            "no se pudo conectar a smtp.ejemplo.invalid con el usuario usuario-de-ejemplo "
            + "y la clave clave-secreta-de-ejemplo");

        var destino = AyudaEnvios.CorreoDePrueba();
        var respuesta = await admin.PostAsync(
            "/api/v1/envios", AyudaEnvios.PeticionFactura(facturaId, destino), ct);

        // Un fallo del servidor de correo no es un error de la petición: la petición era buena, así
        // que el envío sale con su fila y el 201 implícito. Quien pidió el correo tiene que poder ver
        // lo que pasó, no recibir un error sin explicación.
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());

        var envioId = (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var fila = await AyudaEnvios.FilaAsync(bd, envioId, ct);

        fila.Estado.Should().Be(EstadoEnvio.Fallido);
        fila.UltimoError.Should().NotBeNullOrWhiteSpace("un fallo sin motivo no se puede reintentar a ciegas");
        fila.EnviadoAt.Should().BeNull("nunca llegó a salir");
        fila.PdfCopia.Should().NotBeEmpty("la copia es lo que se intentó mandar, también cuando falló");

        foreach (var secreto in new[] { "usuario-de-ejemplo", "clave-secreta-de-ejemplo", "smtp.ejemplo.invalid" })
        {
            fila.UltimoError.Should().NotContain(secreto, "el motivo de un fallo no puede llevar credenciales");
        }

        fila.UltimoError.Should().Be("InvalidOperationException sin detalle.");

        // El log de la instancia tampoco puede llevar nada de eso: se lee en cuanto algo sale mal y las
        // tres cosas son credenciales o infraestructura.
        var log = await LeerLogAsync(api.RutaLog, ct);
        foreach (var secreto in new[] { "usuario-de-ejemplo", "clave-secreta-de-ejemplo", "smtp.ejemplo.invalid" })
        {
            log.Should().NotContain(secreto, "el log no puede llevar credenciales del servidor de correo");
        }
    }

    /// <summary>
    /// Sin correo configurado, el envío es un 409 que dice exactamente qué escribir. Es el criterio que
    /// fijó BUG-007: un ajuste que se permite sin que nadie haya escrito un valor es una pérdida
    /// silenciosa, y un «Pendiente» en la fila affirmaría que hay un envío en la cola que no existe.
    /// </summary>
    [Fact]
    public async Task SinCorreoConfiguradoElEnvioDiceQueConfigurar()
    {
        var ct = TestContext.Current.CancellationToken;

        // Fábrica sin la sección «Correo»: es exactamente el despliegue que aún no ha escrito nada.
        using var api = new ApiTestFactory(bd);
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaId = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        var respuesta = await admin.PostAsync(
            "/api/v1/envios",
            AyudaEnvios.PeticionFactura(facturaId, AyudaEnvios.CorreoDePrueba()),
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict, await respuesta.Cuerpo());

        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("codigoReferencia").GetString().Should().Be("API-005");

        // El detalle tiene que decir qué escribir y dónde, no solo que falta algo.
        var detalle = cuerpo.GetProperty("detail").GetString()!;
        detalle.Should().Contain("Correo__Host");
        detalle.Should().Contain("Correo__Remitente");
        detalle.Should().Contain("user-secrets");

        // Y no se ha escrito ninguna fila: un envío que no llegó a intentarse no es un envío.
        (await AyudaEnvios.UltimoEnvioAsync(bd, facturaId, ct)).Should().BeNull();
    }

    /// <summary>
    /// El mismo documento al mismo destinatario ya enviado se excluye (§3.14), y con <c>forzar</c> sale.
    /// <para>
    /// Un envío <c>Fallido</c> no bloquea: si bloqueara, reintentar un correo que falló por un
    /// problema del servidor obligaría a forzarlo cada vez, y forzar es lo que hay que reservar para el
    /// reenvío que alguien ha pedido dos veces a propósito.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ElAntidiplicadoSeExcluyeSalvoForzado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaEnvios.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaId = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        var destino = AyudaEnvios.CorreoDePrueba();
        var primera = await AyudaEnvios.EnviarAsync(admin, AyudaEnvios.PeticionFactura(facturaId, destino), ct);

        var segunda = await admin.PostAsync(
            "/api/v1/envios", AyudaEnvios.PeticionFactura(facturaId, destino), ct);
        segunda.StatusCode.Should().Be(HttpStatusCode.Conflict, await segunda.Cuerpo());
        (await segunda.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("detail").GetString()
            .Should().Contain("forzar", "el 409 tiene que decir cómo se manda cuando el duplicado es a propósito");

        (await AyudaEnvios.EnviosAsync(bd, facturaId, destino, ct)).Should().Be(1, "el segundo no llegó a enviarse");

        var forzada = await AyudaEnvios.EnviarAsync(
            admin, AyudaEnvios.PeticionFactura(facturaId, destino, forzar: true), ct);

        forzada.GetProperty("id").GetGuid().Should().NotBe(primera.GetProperty("id").GetGuid());
        (await AyudaEnvios.EnviosAsync(bd, facturaId, destino, ct)).Should().Be(2);

        // Y un fallo previo no bloquea el reintento, sin necesidad de forzar.
        var otra = AyudaEnvios.CorreoDePrueba();
        api.Doble.Fallar = new InvalidOperationException("el servidor de correo no responde");
        await AyudaEnvios.EnviarAsync(admin, AyudaEnvios.PeticionFactura(facturaId, otra), ct);
        api.Doble.Restablecer();

        await AyudaEnvios.EnviarAsync(admin, AyudaEnvios.PeticionFactura(facturaId, otra), ct);

        (await AyudaEnvios.EnviosAsync(bd, facturaId, otra, ct))
            .Should().Be(2, "el primero falló y el segundo sí salió, sin forzar");
    }

    /// <summary>El log de Serilog puede no existir todavía; eso no es un fallo de la prueba.</summary>
    private static async Task<string> LeerLogAsync(string ruta, CancellationToken ct)
    {
        for (var intento = 0; intento < 20; intento++)
        {
            if (File.Exists(ruta))
            {
                return await File.ReadAllTextAsync(ruta, ct);
            }

            await Task.Delay(50, ct);
        }

        return File.Exists(ruta) ? await File.ReadAllTextAsync(ruta, ct) : string.Empty;
    }
}