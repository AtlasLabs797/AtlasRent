using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Reservas;

/// <summary>
/// F2-03: los permisos de las reservas no son nuevos. Ver es <c>cliente.ver</c>, que en la matriz de
/// docs/ESPECIFICACION.md §2 es literalmente "Clientes, prospectos y reservas", y crear, editar y
/// cancelar es <c>cliente.editar</c>, lo mismo que dar de alta o de baja en la cartera.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ReservaPermisosTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>El empleado ve y hace reservas: son parte de la cartera.</summary>
    [Fact]
    public async Task ElEmpleadoCreaYVeReservas()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct), Contrasena, ct);

        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var fecha = AyudaReserva.Dia(170);

        var id = await AyudaReserva.CrearAsync(
            empleado, AyudaReserva.Corta(clienteId, unidadId, fecha), ct);

        (await empleado.GetAsync("/api/v1/reservas", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await empleado.GetAsync($"/api/v1/reservas/{id:D}", ct)).StatusCode
            .Should().Be(HttpStatusCode.OK, "cliente.ver incluye las reservas");

        (await empleado.PutAsync(
            $"/api/v1/reservas/{id:D}",
            AyudaReserva.Corta(clienteId, unidadId, fecha, dias: new[]
            {
                AyudaReserva.Franja(fecha, "08:00:00", "12:00:00"),
            }),
            ct)).StatusCode.Should().Be(HttpStatusCode.OK, "cliente.editar incluye editar una reserva");

        (await empleado.DeleteAsync($"/api/v1/reservas/{id:D}", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK, "y cancelar");
    }

    /// <summary>
    /// Sin permisos recortados (D17) se recibe un 403 antes de tocar nada. Se quitan las dos
    /// acciones al mismo usuario —leer y escribir— porque las dos le sirven para las reservas.
    /// </summary>
    [Fact]
    public async Task SinLosPermisosDeCarteraNoSeVenNiSeCreanReservas()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct), Contrasena, ct);
        var empleado = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);

        foreach (var accion in new[] { Acciones.ClienteVer, Acciones.ClienteEditar })
        {
            (await admin.PutAsync(
                $"/api/v1/usuarios/{empleado.Id:D}/permisos",
                new { accion, permitido = false },
                ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using var sesion = await AyudaPermisos.SesionAsync(api, empleado, Contrasena, ct);
        var cuerpo = AyudaReserva.Corta(
            await AyudaReserva.ClienteAsync(admin, ct),
            await AyudaReserva.UnidadAsync(admin, ct),
            AyudaReserva.Dia(171));

        (await AyudaReserva.CrearRawAsync(sesion, cuerpo, ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await sesion.GetAsync("/api/v1/reservas", ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Sin sesión tampoco se ven, como en el resto de la API.</summary>
    [Fact]
    public async Task SinSesionNoSeVenLasReservas()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anonimo = api.CrearCliente();

        (await anonimo.GetAsync("/api/v1/reservas", ct)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }
}