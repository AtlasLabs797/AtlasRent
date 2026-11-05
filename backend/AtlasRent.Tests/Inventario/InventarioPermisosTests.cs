using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Inventario;

/// <summary>
/// F2-08: los permisos del inventario son los de la matriz de docs/ESPECIFICACION.md §2, sin crear
/// ninguna acción nueva.
/// <list type="bullet">
/// <item>
/// El empleado prepara el inventario base con <c>cliente.editar</c> (fila 3, donde §2 agrupa unidades y
/// reservas) y lo lee con <c>cliente.ver</c> (fila 2).
/// </item>
/// <item>
/// Firmar la revisión de salida va con <c>contrato.estado</c> (fila 6), que es de gerente y
/// administrador: la firma de esa revisión es la que desbloquea la liquidación de F2-09 y la que va
/// al cliente.
/// </item>
/// <item>
/// Sin las acciones recortadas (D17) no hace nada, y sin sesión tampoco.
/// </item>
/// </list>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class InventarioPermisosTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    private static readonly (string Nombre, decimal Cantidad, string? Notas)[] TresElementos =
    [
        ("Colchón", 2, null),
        ("Nevera", 1, null),
        ("Silla de oficina", 4, null),
    ];

    /// <summary>
    /// El empleado prepara el inventario de la unidad igual que prepara el contrato, pero no firma la
    /// revisión de salida: esa firma no le llega en §2.
    /// </summary>
    [Fact]
    public async Task ElEmpleadoPreparaElInventarioBaseYNoFirmaLaRevision()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaInventario.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaInventario.SesionAsync(api, bd, Roles.Gerente, ct);

        var (unidadId, contratoId) = await AyudaInventario.ContratoActivoConInventarioAsync(
            empleado, gerente, bd, TresElementos, desplazamientoInicio: 320, ct: ct);

        // Crear un elemento del base sí: cliente.editar es la fila donde §2 mete las unidades.
        var alta = await empleado.PostAsync(
            $"/api/v1/unidades/{unidadId:D}/inventario-base",
            AyudaInventario.Elemento("Aire acondicionado", 1, "Instalado en 2024"),
            ct);
        alta.StatusCode.Should().Be(
            HttpStatusCode.Created,
            "el inventario base lo prepara el empleado, igual que el contrato que usa esa unidad");
        var nuevoId = await AyudaReserva.Id(alta, ct);

        // Y lo puede corregir y retirar: desactivar no es borrar.
        (await empleado.PutAsync(
            $"/api/v1/unidades/{unidadId:D}/inventario-base/{nuevoId:D}",
            AyudaInventario.Elemento("Aire acondicionado", 2),
            ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await empleado.PostAsync(
            $"/api/v1/unidades/{unidadId:D}/inventario-base/{nuevoId:D}/desactivacion",
            new { unidadId, nuevoId },
            ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var baseActual = await empleado.GetAsync($"/api/v1/unidades/{unidadId:D}/inventario-base", ct);
        baseActual.StatusCode.Should().Be(HttpStatusCode.OK, "leer va con cliente.ver");
        (await Items(baseActual, ct)).Should().HaveCount(3, "el elemento desactivado sale del listado");

        // Firmar la revisión, no. Es la decisión que desbloquea la liquidación.
        var congelado = await AyudaInventario.InventarioContratoAsync(empleado, contratoId, ct);
        var firma = await AyudaInventario.FirmarRawAsync(
            empleado,
            contratoId,
            AyudaInventario.CuerpoDeFirma(
                congelado,
                [("Presente", null), ("Presente", null), ("Presente", null)]),
            ct);
        firma.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "contrato.estado no le viene al empleado en §2, y la firma de la revisión es de gerente");

        // Y por dentro tampoco se firmó nada.
        var revision = await gerente.GetAsync($"/api/v1/contratos/{contratoId:D}/revision-inventario", ct);
        revision.StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "un 403 no deja la revisión escrita por el camino de dentro");
    }

    /// <summary>El gerente firma la revisión: es el mismo permiso con el que aprueba y liquida.</summary>
    [Fact]
    public async Task ElGerenteFirmaLaRevisionDeSalida()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaInventario.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaInventario.SesionAsync(api, bd, Roles.Gerente, ct);

        var (_, contratoId) = await AyudaInventario.ContratoActivoConInventarioAsync(
            empleado, gerente, bd, TresElementos, desplazamientoInicio: 321, ct: ct);

        var congelado = await AyudaInventario.InventarioContratoAsync(gerente, contratoId, ct);
        var revision = await AyudaInventario.FirmarAsync(
            gerente,
            contratoId,
            congelado,
            [("Presente", null), ("Presente", null), ("Danado", "La nevera enfría mal")],
            importePropuesto: 8_000m,
            ct: ct);

        revision.GetProperty("contratoId").GetGuid().Should().Be(contratoId);
        revision.GetProperty("firmadaPorId").GetGuid().Should().NotBe(Guid.Empty, "la firma dice quién firmó");
    }

    /// <summary>Sin las dos acciones no se lee ni se escribe nada del inventario.</summary>
    [Fact]
    public async Task SinLasAccionesNoSePreparaNiSeFirmaElInventario()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaInventario.SesionAsync(api, bd, Roles.Admin, ct);
        var usuario = await AyudaAuth.CrearUsuarioAsync(
            bd, AyudaInventario.Contrasena, rol: Roles.Gerente, ct: ct);

        foreach (var accion in new[]
                 {
                     Acciones.ClienteEditar,
                     Acciones.ContratoEstado,
                     Acciones.ClienteVer,
                 })
        {
            (await admin.PutAsync(
                $"/api/v1/usuarios/{usuario.Id:D}/permisos",
                new { accion, permitido = false },
                ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using var sesion = await AyudaPermisos.SesionAsync(api, usuario, AyudaInventario.Contrasena, ct);
        var unidadId = Guid.NewGuid();

        (await sesion.PostAsync(
            $"/api/v1/unidades/{unidadId:D}/inventario-base",
            AyudaInventario.Elemento(),
            ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "sin cliente.editar no se prepara el base");
        (await sesion.GetAsync($"/api/v1/unidades/{unidadId:D}/inventario-base", ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "sin cliente.ver no se lee");
        (await sesion.GetAsync($"/api/v1/contratos/{Guid.NewGuid():D}/inventario", ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await sesion.GetAsync($"/api/v1/contratos/{Guid.NewGuid():D}/revision-inventario", ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await sesion.PostAsync(
            $"/api/v1/contratos/{Guid.NewGuid():D}/revision-inventario",
            new { lineas = Array.Empty<object>() },
            ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "sin contrato.estado no se firma la revisión");
    }

    /// <summary>Sin sesión no hay inventario ni revisión de salida.</summary>
    [Fact]
    public async Task SinSesionNoSeVeNiSeFirmaElInventario()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaInventario.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaInventario.SesionAsync(api, bd, Roles.Gerente, ct);

        var (unidadId, contratoId) = await AyudaInventario.ContratoActivoConInventarioAsync(
            empleado, gerente, bd, TresElementos, desplazamientoInicio: 322, ct: ct);

        using var anonimo = api.CrearCliente();
        var cuerpoVacio = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        (await anonimo.GetAsync($"/api/v1/unidades/{unidadId:D}/inventario-base", ct)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await anonimo.GetAsync($"/api/v1/contratos/{contratoId:D}/inventario", ct)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await anonimo.GetAsync($"/api/v1/contratos/{contratoId:D}/revision-inventario", ct)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await anonimo.PostAsync(
            $"/api/v1/contratos/{contratoId:D}/revision-inventario", cuerpoVacio, ct)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "sin sesión no se firma nada, ni aunque el cuerpo valiera");
    }

    private static async Task<List<JsonElement>> Items(
        HttpResponseMessage respuesta,
        CancellationToken ct) =>
        [.. (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).EnumerateArray()];
}