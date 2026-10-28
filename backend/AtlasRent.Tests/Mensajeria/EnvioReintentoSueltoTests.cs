using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cotizaciones;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Mensajeria;

/// <summary>
/// <c>POST /api/v1/envios/{id}/reintentar</c>: reintento de un envío suelto fallido (§3.14). Misma
/// fila, un intento más, sin duplicar correos y con el permiso del tipo de documento.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class EnvioReintentoSueltoTests(ContenedorBdFixture bd, ApiTestFactoryCorreo api)
    : IClassFixture<ApiTestFactoryCorreo>
{
    private async Task<(Guid EnvioId, string Destino)> FacturaFallidaAsync(
        SesionCliente admin, CancellationToken ct)
    {
        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaId = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);
        var destino = AyudaEnvios.CorreoDePrueba();

        api.Doble.Fallar = new InvalidOperationException("caído");
        try
        {
            var fila = await AyudaEnvios.EnviarAsync(admin, AyudaEnvios.PeticionFactura(facturaId, destino), ct);
            var envioId = fila.GetProperty("id").GetGuid();
            (await AyudaEnvios.FilaAsync(bd, envioId, ct)).Estado.Should().Be(EstadoEnvio.Fallido);

            return (envioId, destino);
        }
        finally
        {
            api.Doble.Restablecer();
        }
    }

    [Fact]
    public async Task ReintentarUnFallidoLoManda()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaEnvios.SesionAsync(bd, api, Roles.Admin, ct);
        var (envioId, destino) = await FacturaFallidaAsync(admin, ct);

        var respuesta = await admin.PostAsync($"/api/v1/envios/{envioId:D}/reintentar", new { }, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());

        var fila = await AyudaEnvios.FilaAsync(bd, envioId, ct);
        fila.Estado.Should().Be(EstadoEnvio.Enviado);
        fila.Intentos.Should().Be(2);
        fila.UltimoError.Should().BeNull();
        api.Doble.Enviados.Count(m => m.DestinatarioEmail == destino).Should().Be(1);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Envio.CountAsync(e => e.DocumentoId == fila.DocumentoId, ct))
            .Should().Be(1, "reintentar no crea otra fila");

        await using var sinRls = bd.CrearContexto(bd.CadenaSuperusuario);
        var auditoria = await sinRls.Auditoria.AsNoTracking()
            .SingleAsync(a => a.Accion == "envio.reintentado" && a.EntidadId == envioId.ToString(), ct);
        auditoria.Ip.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ReintentarUnEnvioQueNoEstaFallidoEsConflicto()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaEnvios.SesionAsync(bd, api, Roles.Admin, ct);
        var (envioId, destino) = await FacturaFallidaAsync(admin, ct);

        (await admin.PostAsync($"/api/v1/envios/{envioId:D}/reintentar", new { }, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var segunda = await admin.PostAsync($"/api/v1/envios/{envioId:D}/reintentar", new { }, ct);
        segunda.StatusCode.Should().Be(HttpStatusCode.Conflict, "ya salió");

        api.Doble.Enviados.Count(m => m.DestinatarioEmail == destino).Should().Be(1);
        (await AyudaEnvios.FilaAsync(bd, envioId, ct)).Intentos.Should().Be(2);

        (await admin.PostAsync($"/api/v1/envios/{Guid.NewGuid():D}/reintentar", new { }, ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task LaMismaClaveDeIdempotenciaNoMandaDosVeces()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaEnvios.SesionAsync(bd, api, Roles.Admin, ct);
        var (envioId, destino) = await FacturaFallidaAsync(admin, ct);
        var clave = $"reintento-{Guid.NewGuid():N}";

        var primera = await admin.PostAsync($"/api/v1/envios/{envioId:D}/reintentar", new { }, clave, ct);
        var segunda = await admin.PostAsync($"/api/v1/envios/{envioId:D}/reintentar", new { }, clave, ct);

        primera.StatusCode.Should().Be(HttpStatusCode.OK, await primera.Cuerpo());
        segunda.StatusCode.Should().Be(HttpStatusCode.OK, await segunda.Cuerpo());
        (await segunda.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid().Should().Be(envioId);

        api.Doble.Enviados.Count(m => m.DestinatarioEmail == destino).Should().Be(1);
        (await AyudaEnvios.FilaAsync(bd, envioId, ct)).Intentos.Should().Be(2);
    }

    [Fact]
    public async Task DosReintentosSimultaneosMandanUnSoloCorreo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaEnvios.SesionAsync(bd, api, Roles.Admin, ct);
        var (envioId, destino) = await FacturaFallidaAsync(admin, ct);

        // El correo tarda para que las dos peticiones coincidan dentro del envío.
        api.Doble.AntesDeEnviar = () => Thread.Sleep(700);
        try
        {
            var ruta = $"/api/v1/envios/{envioId:D}/reintentar";
            var respuestas = await Task.WhenAll(
                admin.PostAsync(ruta, new { }, $"a-{Guid.NewGuid():N}", ct),
                admin.PostAsync(ruta, new { }, $"b-{Guid.NewGuid():N}", ct));

            respuestas.Select(r => r.StatusCode).Should().BeEquivalentTo(
                [HttpStatusCode.OK, HttpStatusCode.Conflict]);
        }
        finally
        {
            api.Doble.AntesDeEnviar = null;
        }

        api.Doble.Enviados.Count(m => m.DestinatarioEmail == destino).Should().Be(1);
        (await AyudaEnvios.FilaAsync(bd, envioId, ct)).Intentos.Should().Be(2);
    }

    [Fact]
    public async Task ElEmpleadoNoReintentaFacturasPeroSiCotizaciones()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaEnvios.SesionAsync(bd, api, Roles.Admin, ct);
        using var empleado = await AyudaEnvios.SesionAsync(bd, api, Roles.Empleado, ct);
        using var gerente = await AyudaEnvios.SesionAsync(bd, api, Roles.Gerente, ct);

        var (facturaEnvio, _) = await FacturaFallidaAsync(admin, ct);

        (await empleado.PostAsync($"/api/v1/envios/{facturaEnvio:D}/reintentar", new { }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AyudaEnvios.FilaAsync(bd, facturaEnvio, ct)).Estado.Should().Be(EstadoEnvio.Fallido);

        (await gerente.PostAsync($"/api/v1/envios/{facturaEnvio:D}/reintentar", new { }, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var cotizacionId = await AyudaCotizacion.CrearAsync(admin, escenario.ClienteId, ct: ct);
        api.Doble.Fallar = new InvalidOperationException("caído");
        Guid cotizacionEnvio;
        try
        {
            var fila = await AyudaEnvios.EnviarAsync(
                admin, AyudaEnvios.PeticionCotizacion(cotizacionId, AyudaEnvios.CorreoDePrueba()), ct);
            cotizacionEnvio = fila.GetProperty("id").GetGuid();
        }
        finally
        {
            api.Doble.Restablecer();
        }

        (await empleado.PostAsync($"/api/v1/envios/{cotizacionEnvio:D}/reintentar", new { }, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK, "cotizacion.enviar sí lo tiene el empleado");
    }
}
