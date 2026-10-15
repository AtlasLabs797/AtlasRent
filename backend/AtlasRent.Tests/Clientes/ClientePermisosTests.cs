using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Clientes;

/// <summary>
/// F1-09, permisos: el empleado ve la cartera (<c>cliente.ver</c>) y puede darla de alta
/// (<c>cliente.editar</c>), pero no toca lo que no le toca.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ClientePermisosTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    [Fact]
    public async Task ElEmpleadoLeeLosClientesYLosPuedeDarDeAlta()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await SesionAsync(Roles.Admin, ct);
        var nombre = $"Cartera {Guid.NewGuid():N}"[..20];
        (await admin.PostAsync(
            "/api/v1/clientes",
            new { nombre, tipo = "PersonaFisica", documento = $"001-{Guid.NewGuid():N}"[..13] },
            ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var empleado = await SesionAsync(Roles.Empleado, ct);

        (await empleado.GetAsync("/api/v1/clientes", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await empleado.GetAsync("/api/v1/prospectos", ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var alta = await empleado.PostAsync(
            "/api/v1/clientes",
            new { nombre = $"Alta {Guid.NewGuid():N}"[..20], tipo = "PersonaFisica" },
            ct);
        alta.StatusCode.Should().Be(HttpStatusCode.OK, "cliente.editar incluye dar de alta");
    }

    [Fact]
    public async Task ElEmpleadoRecibe403EnUnaEscrituraQueNoLeToca()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(Roles.Empleado, ct);

        // El mismo empleado lee clientes sin problema; lo que no tiene es configuración.
        (await empleado.GetAsync("/api/v1/clientes", ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var escritura = await empleado.PostAsync(
            "/api/v1/config/subempresas",
            new { nombre = $"No permitida {Guid.NewGuid():N}"[..20], propietaria = true },
            ct);

        escritura.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SinSesionNoSeVenLosClientes()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anonimo = api.CrearCliente();

        (await anonimo.GetAsync("/api/v1/clientes", ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Los contactos cuelgan del cliente: el empleado los lee y puede mantenerlos, y solo uno es
    /// principal por cliente.
    /// </summary>
    [Fact]
    public async Task LosContactosDelClienteSeCreanYSoloUnoQuedaComoPrincipal()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(Roles.Empleado, ct);

        var cliente = await empleado.PostAsync(
            "/api/v1/clientes",
            new { nombre = $"Con contactos {Guid.NewGuid():N}"[..20], tipo = "PersonaFisica" },
            ct);
        cliente.StatusCode.Should().Be(HttpStatusCode.OK);
        var id = (await cliente.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();

        var primero = await empleado.PostAsync(
            $"/api/v1/clientes/{id:D}/contactos",
            new { nombre = "Propietario", relacion = "propietario", telefono = "8090000000", esPrincipal = true },
            ct);
        primero.StatusCode.Should().Be(HttpStatusCode.OK);

        var segundo = await empleado.PostAsync(
            $"/api/v1/clientes/{id:D}/contactos",
            new { nombre = "Contable", relacion = "contable", email = "contable@example.test" },
            ct);
        segundo.StatusCode.Should().Be(HttpStatusCode.OK);

        var contactos = await (await empleado.GetAsync($"/api/v1/clientes/{id:D}/contactos", ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);

        contactos.EnumerateArray().Should().HaveCount(2);
        contactos.EnumerateArray()
            .Count(c => c.GetProperty("esPrincipal").GetBoolean())
            .Should().Be(1);

        (await empleado.GetAsync($"/api/v1/clientes/{Guid.NewGuid():D}/contactos", ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>El prospecto lo ve y lo gestiona el empleado, igual que un cliente.</summary>
    [Fact]
    public async Task ElEmpleadoGestionaLosProspectos()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(Roles.Empleado, ct);

        var alta = await empleado.PostAsync(
            "/api/v1/prospectos",
            new { nombre = $"Prospecto {Guid.NewGuid():N}"[..20], tipo = "Empresa" },
            ct);
        alta.StatusCode.Should().Be(HttpStatusCode.OK);
        var id = (await alta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();

        var edicion = await empleado.PutAsync(
            $"/api/v1/prospectos/{id:D}",
            new { nombre = $"Prospecto editado {Guid.NewGuid():N}"[..20], tipo = "Empresa" },
            ct);
        edicion.StatusCode.Should().Be(HttpStatusCode.OK);

        (await empleado.DeleteAsync($"/api/v1/prospectos/{id:D}", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var lista = await (await empleado.GetAsync("/api/v1/prospectos", ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);
        lista.GetProperty("data").EnumerateArray()
            .Should().NotContain(p => p.GetProperty("id").GetGuid() == id, "la baja es lógica");
    }

    private async Task<SesionCliente> SesionAsync(string rol, CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: rol, ct: ct);
        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }
}