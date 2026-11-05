using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Clientes;

/// <summary>Reactivar clientes, prospectos e inmuebles, y búsqueda remota <c>q</c> de clientes y prospectos.</summary>
[Collection(nameof(ColeccionBd))]
public sealed class ReactivarYBusquedaTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    [Theory]
    [InlineData(Roles.Empleado)]
    [InlineData(Roles.Gerente)]
    [InlineData(Roles.Admin)]
    public async Task SeReactivaUnClienteUnProspectoYUnInmuebleDesactivados(string rol)
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(rol, ct);
        var cliente = await AyudaReserva.ClienteAsync(sesion, ct);
        var prospecto = await AyudaReserva.ProspectoAsync(sesion, ct);
        var (inmueble, _) = await AyudaReserva.InmuebleYUnidadAsync(sesion, ct);

        foreach (var ruta in new[] { $"clientes/{cliente:D}", $"prospectos/{prospecto:D}", $"inmuebles/{inmueble:D}" })
        {
            (await sesion.DeleteAsync($"/api/v1/{ruta}", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
            var reactivado = await sesion.PostAsync($"/api/v1/{ruta}/reactivar", new { }, ct);
            reactivado.StatusCode.Should().Be(HttpStatusCode.OK, ruta);
            (await reactivado.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("activo").GetBoolean()
                .Should().BeTrue(ruta);

            // Reactivar algo activo es una operación repetida: 200 sin cambios.
            (await sesion.PostAsync($"/api/v1/{ruta}/reactivar", new { }, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task ReactivarUnProspectoConvertidoDa409()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(Roles.Empleado, ct);
        var prospecto = await AyudaReserva.ProspectoAsync(sesion, ct);
        (await sesion.PostAsync(
            $"/api/v1/prospectos/{prospecto:D}/convertir",
            new { nombre = $"Conv {Guid.NewGuid():N}"[..16], tipo = "PersonaFisica" },
            ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await sesion.PostAsync($"/api/v1/prospectos/{prospecto:D}/reactivar", new { }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ReactivarAlgoQueNoExisteDa404()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(Roles.Empleado, ct);

        foreach (var ruta in new[] { "clientes", "prospectos", "inmuebles" })
        {
            (await sesion.PostAsync($"/api/v1/{ruta}/{Guid.NewGuid():D}/reactivar", new { }, ct))
                .StatusCode.Should().Be(HttpStatusCode.NotFound, ruta);
        }
    }

    [Fact]
    public async Task SinClienteEditarReactivarEs403()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await SesionAsync(Roles.Admin, ct);
        var cliente = await AyudaReserva.ClienteAsync(admin, ct);
        var prospecto = await AyudaReserva.ProspectoAsync(admin, ct);
        var (inmueble, _) = await AyudaReserva.InmuebleYUnidadAsync(admin, ct);

        using var recortado = await SesionRecortadaAsync(ct);
        foreach (var ruta in new[] { $"clientes/{cliente:D}", $"prospectos/{prospecto:D}", $"inmuebles/{inmueble:D}" })
        {
            (await recortado.PostAsync($"/api/v1/{ruta}/reactivar", new { }, ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, ruta);
        }
    }

    [Fact]
    public async Task LaBusquedaQDeClientesEncuentraPorNombreYPorNumeroPeroNoPorDocumento()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(Roles.Empleado, ct);
        var marca = Guid.NewGuid().ToString("N")[..10];
        var documento = $"001-{Guid.NewGuid():N}"[..13];
        var alta = await sesion.PostAsync(
            "/api/v1/clientes",
            new { nombre = $"Razon {marca}", tipo = "Empresa", documento },
            ct);
        var json = await alta.Content.ReadFromJsonAsync<JsonElement>(ct);
        var numero = json.GetProperty("numero").GetInt32();
        var id = json.GetProperty("id").GetGuid();

        (await IdsAsync(sesion, $"/api/v1/clientes?q={marca}", ct)).Should().Contain(id);
        (await IdsAsync(sesion, $"/api/v1/clientes?q={numero}", ct)).Should().Contain(id);
        (await IdsAsync(sesion, $"/api/v1/clientes?q={documento}", ct)).Should().NotContain(id);
    }

    [Fact]
    public async Task LaBusquedaQDeProspectosEncuentraPorNombre()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(Roles.Empleado, ct);
        var marca = Guid.NewGuid().ToString("N")[..10];
        var alta = await sesion.PostAsync(
            "/api/v1/prospectos", new { nombre = $"Prosp {marca}", tipo = "Empresa" }, ct);
        var id = (await alta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();

        (await IdsAsync(sesion, $"/api/v1/prospectos?q={marca}", ct)).Should().Contain(id);
        (await IdsAsync(sesion, $"/api/v1/prospectos?q=zzz{marca}", ct)).Should().NotContain(id);
    }

    private static async Task<List<Guid>> IdsAsync(SesionCliente sesion, string ruta, CancellationToken ct)
    {
        var lista = await (await sesion.GetAsync(ruta, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        return [.. lista.GetProperty("data").EnumerateArray().Select(f => f.GetProperty("id").GetGuid())];
    }

    private async Task<SesionCliente> SesionAsync(string rol, CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: rol, ct: ct);
        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }

    private async Task<SesionCliente> SesionRecortadaAsync(CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);
        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            contexto.UsuarioPermiso.Add(new UsuarioPermiso
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuario.Id,
                Accion = Acciones.ClienteEditar,
                Permitido = false,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await contexto.SaveChangesAsync(ct);
        }

        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }
}
