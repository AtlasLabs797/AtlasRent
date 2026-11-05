using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Contratos;

/// <summary>
/// F2-05: los permisos de los contratos no son nuevos (docs/ESPECIFICACION.md §2).
/// <list type="bullet">
/// <item>
/// El empleado crea borradores y los manda a aprobación: es la fila 5 de la matriz.
/// </item>
/// <item>
/// Sin acciones recortadas (D17) no crea nada ni ve nada: 403.
/// </item>
/// <item>
/// Y sin sesión tampoco, como en el resto de la API.
/// </item>
/// </list>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ContratoPermisosTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// El empleado hace su parte del flujo entero: crear el borrador, editarlo y mandarlo a
    /// aprobación. Lo que viene después no es suyo.
    /// </summary>
    [Fact]
    public async Task ElEmpleadoCreaEditaYMandaAProbacion()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct), Contrasena, ct);
        using var gerente = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct), Contrasena, ct);

        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);
        var inicio = AyudaContrato.Dia(400);

        var id = await AyudaContrato.CrearAsync(
            empleado, AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, inicio), ct);

        (await empleado.GetAsync("/api/v1/contratos", ct)).StatusCode
            .Should().Be(HttpStatusCode.OK, "cliente.ver incluye los contratos: es la misma fila de §2");
        (await empleado.GetAsync($"/api/v1/contratos/{id:D}", ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var editado = await empleado.PutAsync(
            $"/api/v1/contratos/{id:D}",
            AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, inicio, renta: 65_000m),
            ct);
        editado.StatusCode.Should().Be(HttpStatusCode.OK, "contrato.borrador incluye editar el borrador");

        await AyudaContrato.AceptarAsync(empleado, id, "solicitud", new { }, ct);

        // Y a partir de ahí el flujo es del gerente: el empleado no lo retoma.
        (await AyudaContrato.Boton(empleado, id, "anular-baja", ct: ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await AyudaContrato.Boton(empleado, id, "finalizacion", ct: ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await AyudaContrato.Boton(empleado, id, "rechazo", new { comentario = "No." }, ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Sin los permisos de contrato (D17) no se crea ni se ve. Se quitan las dos acciones, leer y
    /// escribir, porque las dos las necesita para trabajar con contratos.
    /// </summary>
    [Fact]
    public async Task SinLasAccionesDeContratoNoSeCreanNiSeVen()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct), Contrasena, ct);
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);

        foreach (var accion in new[] { Acciones.ContratoBorrador, Acciones.ClienteVer })
        {
            (await admin.PutAsync(
                $"/api/v1/usuarios/{usuario.Id:D}/permisos",
                new { accion, permitido = false },
                ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using var sesion = await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);

        (await sesion.GetAsync("/api/v1/contratos", ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        var cuerpo = AyudaContrato.Peticion(
            await AyudaReserva.ClienteAsync(admin, ct),
            await AyudaReserva.UnidadAsync(admin, ct),
            await AyudaContrato.SubempresaAsync(bd, ct));

        (await sesion.PostAsync("/api/v1/contratos", cuerpo, ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Sin sesión no hay contratos, como en el resto de la API.</summary>
    [Fact]
    public async Task SinSesionNoSeVenLosContratos()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anonimo = api.CrearCliente();

        (await anonimo.GetAsync("/api/v1/contratos", ct)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }
}