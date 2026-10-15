using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Calendario;

/// <summary>
/// F2-04: los permisos del calendario no son nuevos. Ver es <c>cliente.ver</c>, que en la matriz de
/// docs/ESPECIFICACION.md §2 es literalmente "Ver clientes, unidades, reservas y contratos", la
/// misma fila que usan <c>GET /api/v1/reservas</c> y los contratos.
/// <para>
/// Y como el calendario es de solo lectura, esa es la única acción que necesita: no hay ninguna
/// acción nueva en <c>Acciones</c> ni en la base.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class CalendarioPermisosTests(
    ContenedorBdFixture bd,
    ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// Con el permiso de cartera se ve el calendario, y sin él se recibe un 403 antes de tocar nada.
    /// Se quita solo <c>cliente.ver</c>, que es lo único que el endpoint declara.
    /// </summary>
    [Fact]
    public async Task SinClienteVerNoSeVeElCalendarioYConElSi()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct), Contrasena, ct);

        var unidadId = await AyudaReserva.UnidadAsync(admin, ct);
        var desde = AyudaCalendario.Dia(0);
        var hasta = AyudaCalendario.Dia(2);

        (await AyudaCalendario.GetAsync(admin, unidadId, desde, hasta, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK, "el administrador lo tiene todo");

        var empleado = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);
        using var conPermiso = await AyudaPermisos.SesionAsync(api, empleado, Contrasena, ct);
        (await AyudaCalendario.GetAsync(conPermiso, unidadId, desde, hasta, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK, "cliente.ver incluye ver el calendario");

        (await admin.PutAsync(
            $"/api/v1/usuarios/{empleado.Id:D}/permisos",
            new { accion = Acciones.ClienteVer, permitido = false },
            ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var sinPermiso = await AyudaPermisos.SesionAsync(api, empleado, Contrasena, ct);
        (await AyudaCalendario.GetAsync(sinPermiso, unidadId, desde, hasta, ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "sin cliente.ver no hay calendario");
    }

    /// <summary>Sin sesión tampoco se ve, como en el resto de la API.</summary>
    [Fact]
    public async Task SinSesionNoSeVeElCalendario()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anonimo = api.CrearCliente();

        (await anonimo.GetAsync(
            $"/api/v1/calendario?unidadId={Guid.NewGuid():D}&desde=2026-01-01&hasta=2026-01-03", ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}