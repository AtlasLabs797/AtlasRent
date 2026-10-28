using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Inmuebles;

/// <summary>Lectura global, baja y reactivación de unidades (docs/ESPECIFICACION.md §3.1).</summary>
[Collection(nameof(ColeccionBd))]
public sealed class UnidadGlobalTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    [Theory]
    [InlineData(Roles.Empleado)]
    [InlineData(Roles.Gerente)]
    [InlineData(Roles.Admin)]
    public async Task SeLeeSeDesactivaYSeReactivaUnaUnidad(string rol)
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(rol, ct);
        var (inmuebleId, unidadId) = await AyudaReserva.InmuebleYUnidadAsync(sesion, ct);

        var ficha = await sesion.GetAsync($"/api/v1/inmuebles/unidades/{unidadId:D}", ct);
        ficha.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ficha.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("inmuebleId").GetGuid()
            .Should().Be(inmuebleId);

        (await sesion.DeleteAsync($"/api/v1/inmuebles/unidades/{unidadId:D}", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await IdsAsync(sesion, $"/api/v1/inmuebles/unidades?inmuebleId={inmuebleId:D}&activa=true", ct))
            .Should().NotContain(unidadId);
        (await IdsAsync(sesion, $"/api/v1/inmuebles/unidades?inmuebleId={inmuebleId:D}&activa=false", ct))
            .Should().Contain(unidadId);

        var reactivada = await sesion.PostAsync($"/api/v1/inmuebles/unidades/{unidadId:D}/reactivar", new { }, ct);
        reactivada.StatusCode.Should().Be(HttpStatusCode.OK);
        (await reactivada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("activo").GetBoolean().Should().BeTrue();
        (await sesion.PostAsync($"/api/v1/inmuebles/unidades/{unidadId:D}/reactivar", new { }, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK, "repetir no es un error");
    }

    [Fact]
    public async Task ElListadoGlobalFiltraPorNombreInmuebleYModalidad()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(Roles.Empleado, ct);
        var (inmuebleId, unidadId) = await AyudaReserva.InmuebleYUnidadAsync(sesion, ct);
        var otra = await AyudaReserva.UnidadAsync(sesion, ct);

        var lista = await sesion.GetAsync("/api/v1/inmuebles/unidades?pageSize=500", ct);
        lista.StatusCode.Should().Be(HttpStatusCode.OK);
        var ficha = await (await sesion.GetAsync($"/api/v1/inmuebles/unidades/{unidadId:D}", ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);
        var nombre = ficha.GetProperty("nombre").GetString()!;

        (await IdsAsync(sesion, $"/api/v1/inmuebles/unidades?q={Uri.EscapeDataString(nombre)}", ct))
            .Should().Contain(unidadId).And.NotContain(otra);
        (await IdsAsync(sesion, $"/api/v1/inmuebles/unidades?inmuebleId={inmuebleId:D}", ct))
            .Should().Equal(unidadId);
        (await IdsAsync(sesion, $"/api/v1/inmuebles/unidades?inmuebleId={inmuebleId:D}&modalidad=MedioDia", ct))
            .Should().Equal(unidadId);
        (await IdsAsync(sesion, $"/api/v1/inmuebles/unidades?inmuebleId={inmuebleId:D}&modalidad=Mensual", ct))
            .Should().Equal(unidadId);
    }

    [Fact]
    public async Task UnaUnidadConReservaVivaNoSeDesactiva()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(Roles.Gerente, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var cliente = await AyudaReserva.ClienteAsync(sesion, ct);
        var reserva = await AyudaReserva.CrearAsync(
            sesion, AyudaReserva.Mensual(cliente, unidadId, AyudaReserva.Dia(500)), ct);

        (await sesion.DeleteAsync($"/api/v1/inmuebles/unidades/{unidadId:D}", ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Cancelada la reserva ya no estorba.
        (await sesion.DeleteAsync($"/api/v1/reservas/{reserva:D}", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await sesion.DeleteAsync($"/api/v1/inmuebles/unidades/{unidadId:D}", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NoSeReactivaUnaUnidadDeUnInmuebleInactivo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(Roles.Gerente, ct);
        var (inmuebleId, unidadId) = await AyudaReserva.InmuebleYUnidadAsync(sesion, ct);
        (await sesion.DeleteAsync($"/api/v1/inmuebles/unidades/{unidadId:D}", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await sesion.DeleteAsync($"/api/v1/inmuebles/{inmuebleId:D}", ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await sesion.PostAsync($"/api/v1/inmuebles/unidades/{unidadId:D}/reactivar", new { }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UnaUnidadQueNoExisteDa404()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(Roles.Empleado, ct);
        var id = Guid.NewGuid();

        (await sesion.GetAsync($"/api/v1/inmuebles/unidades/{id:D}", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await sesion.DeleteAsync($"/api/v1/inmuebles/unidades/{id:D}", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await sesion.PostAsync($"/api/v1/inmuebles/unidades/{id:D}/reactivar", new { }, ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SinElPermisoNoSeLeeNiSeEscribe()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await SesionAsync(Roles.Admin, ct);
        var unidadId = await AyudaReserva.UnidadAsync(admin, ct);

        using var sinEditar = await SesionRecortadaAsync(Acciones.ClienteEditar, ct);
        (await sinEditar.GetAsync($"/api/v1/inmuebles/unidades/{unidadId:D}", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await sinEditar.DeleteAsync($"/api/v1/inmuebles/unidades/{unidadId:D}", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sinEditar.PostAsync($"/api/v1/inmuebles/unidades/{unidadId:D}/reactivar", new { }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var sinVer = await SesionRecortadaAsync(Acciones.ClienteVer, ct);
        (await sinVer.GetAsync($"/api/v1/inmuebles/unidades/{unidadId:D}", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sinVer.GetAsync("/api/v1/inmuebles/unidades", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<List<Guid>> IdsAsync(SesionCliente sesion, string ruta, CancellationToken ct)
    {
        var respuesta = await sesion.GetAsync(ruta, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, ruta);
        var lista = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        return [.. lista.GetProperty("data").EnumerateArray().Select(f => f.GetProperty("id").GetGuid())];
    }

    private async Task<SesionCliente> SesionAsync(string rol, CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: rol, ct: ct);
        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }

    private async Task<SesionCliente> SesionRecortadaAsync(string accion, CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);
        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            contexto.UsuarioPermiso.Add(new UsuarioPermiso
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuario.Id,
                Accion = accion,
                Permitido = false,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await contexto.SaveChangesAsync(ct);
        }

        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }
}
