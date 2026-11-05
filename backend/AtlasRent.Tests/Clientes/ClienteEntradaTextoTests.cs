using System.Net.Http.Json;
using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Clientes;

/// <summary>SEC-028: correos y nombres con CR/LF se rechazan en el borde con 400.</summary>
[Collection(nameof(ColeccionBd))]
public sealed class ClienteEntradaTextoTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    [Fact]
    public async Task UnClienteConEmailConSaltoDeLineaSeRechaza()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);

        var respuesta = await sesion.PostAsync("/api/v1/clientes",
            new { nombre = "Cliente prueba", tipo = "PersonaFisica", email = "a@b.com\r\nBcc: x@y.z" }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnClienteConNombreConSaltoDeLineaSeRechaza()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);

        var respuesta = await sesion.PostAsync("/api/v1/clientes",
            new { nombre = "Cliente\r\nBcc: x@y.z", tipo = "PersonaFisica", email = "a@b.com" }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnContactoConEmailConSaltoDeLineaSeRechaza()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);

        var alta = await sesion.PostAsync("/api/v1/clientes",
            new { nombre = "Cliente contacto", tipo = "PersonaFisica", email = "a@b.com" }, ct);
        alta.StatusCode.Should().Be(HttpStatusCode.OK);
        var id = (await alta.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct)).GetProperty("id").GetGuid();

        var respuesta = await sesion.PostAsync($"/api/v1/clientes/{id:D}/contactos",
            new { nombre = "Contacto", email = "a@b.com\nBcc: x@y.z" }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<SesionCliente> SesionAsync(CancellationToken ct)
    {
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        return await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);
    }
}
