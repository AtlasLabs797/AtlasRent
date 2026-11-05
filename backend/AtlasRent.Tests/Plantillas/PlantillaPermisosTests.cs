using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Plantillas;

/// <summary>
/// F2-07: los permisos de plantillas y anexos son los de la matriz de docs/ESPECIFICACION.md §2, sin
/// crear ninguna acción nueva.
/// <list type="bullet">
/// <item>El empleado prepara el catálogo (crear y editar va con <c>contrato.borrador</c>) y lo lee.</item>
/// <item>Desactivar va con <c>contrato.estado</c>, que es de gerente y administrador: es la misma
/// decisión de negocio que aprobar o dar de baja.</item>
/// <item>Sin las acciones recortadas (D17) no hace nada, y sin sesión tampoco.</item>
/// </list>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class PlantillaPermisosTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    /// <summary>
    /// El empleado puede preparar una plantilla y un anexo, los puede leer y no los puede desactivar.
    /// </summary>
    [Fact]
    public async Task ElEmpleadoPuedePrepararUnaPlantillaYNoDesactivarla()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPlantilla.SesionAsync(api, bd, Roles.Empleado, ct);

        var codigo = AyudaPlantilla.Codigo();
        var alta = await AyudaPlantilla.CrearPlantillaAsync(
            empleado, AyudaPlantilla.CuerpoPlantilla(codigo), ct);
        alta.StatusCode.Should().Be(
            HttpStatusCode.Created,
            "contrato.borrador incluye preparar el texto que va al contrato que firma el cliente");
        var plantillaId = (await AyudaPlantilla.Id(alta, ct)).ToString();

        var anexo = await AyudaPlantilla.CrearAnexoAsync(empleado, AyudaPlantilla.CuerpoAnexo(), ct);
        anexo.StatusCode.Should().Be(HttpStatusCode.Created, await anexo.Cuerpo());
        var anexoId = (await AyudaPlantilla.Id(anexo, ct)).ToString();

        // Lectura: cliente.ver, que es la fila 2 de §2 ("Ver clientes, unidades, reservas y contratos").
        (await empleado.GetAsync($"/api/v1/plantillas-contrato/{plantillaId}", ct)).StatusCode
            .Should().Be(HttpStatusCode.OK, "leer una plantilla va con cliente.ver");
        (await empleado.GetAsync("/api/v1/plantillas-anexo", ct)).StatusCode
            .Should().Be(HttpStatusCode.OK, "leer el catálogo de anexos también va con cliente.ver");

        // Edición: sí, es texto que el empleado prepara.
        (await AyudaPlantilla.EditarPlantillaAsync(
            empleado, plantillaId, AyudaPlantilla.CuerpoEdicionPlantilla(), ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Desactivación: no. Retirar del catálogo es decisión de gerente (contrato.estado, fila 6).
        (await AyudaPlantilla.DesactivarPlantillaAsync(empleado, plantillaId, ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "contrato.estado no le viene al empleado en §2");
        (await AyudaPlantilla.DesactivarAnexoAsync(empleado, anexoId, ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        // Y nada se desactivó por el camino de dentro.
        var plantilla = await AyudaPlantilla.Json(
            await empleado.GetAsync($"/api/v1/plantillas-contrato/{plantillaId}", ct), ct);
        plantilla.GetProperty("activa").GetBoolean().Should().BeTrue();
    }

    /// <summary>El gerente desactiva: es la parte del catálogo que decide qué se puede firmar.</summary>
    [Fact]
    public async Task ElGerenteDesactivaLaPlantillaYElAnexo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await AyudaPlantilla.SesionAsync(api, bd, Roles.Gerente, ct);

        await AyudaPlantilla.LiberarTipoAsync(gerente, "Mantenimiento", ct);
        var plantillaId = await AyudaPlantilla.CrearPlantillaValidaAsync(gerente, AyudaPlantilla.Codigo(), ct: ct);
        var anexoId = await AyudaPlantilla.CrearAnexoValidoAsync(gerente, "Mantenimiento", ct: ct);

        (await AyudaPlantilla.DesactivarPlantillaAsync(gerente, plantillaId, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await AyudaPlantilla.DesactivarAnexoAsync(gerente, anexoId, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Sin las dos acciones no se prepara ni se lee nada del catálogo.</summary>
    [Fact]
    public async Task SinLasAccionesNoSePreparaNiSeLeeElCatalogo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaPlantilla.SesionAsync(api, bd, Roles.Admin, ct);
        var usuario = await AyudaAuth.CrearUsuarioAsync(
            bd, AyudaPlantilla.Contrasena, rol: Roles.Empleado, ct: ct);

        foreach (var accion in new[] { Acciones.ContratoBorrador, Acciones.ClienteVer })
        {
            (await admin.PutAsync(
                $"/api/v1/usuarios/{usuario.Id:D}/permisos",
                new { accion, permitido = false },
                ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using var sesion = await AyudaPermisos.SesionAsync(api, usuario, AyudaPlantilla.Contrasena, ct);

        (await sesion.PostAsync("/api/v1/plantillas-contrato", AyudaPlantilla.CuerpoPlantilla(), ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sesion.PostAsync("/api/v1/plantillas-anexo", AyudaPlantilla.CuerpoAnexo(), ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sesion.GetAsync("/api/v1/plantillas-contrato", ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await sesion.GetAsync("/api/v1/plantillas-anexo", ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Sin sesión no hay catálogo ni documento de contrato.</summary>
    [Fact]
    public async Task SinSesionNoSeVeElCatalogoNiElPdf()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await AyudaPlantilla.SesionAsync(api, bd, Roles.Gerente, ct);
        using var empleado = await AyudaPlantilla.SesionAsync(api, bd, Roles.Empleado, ct);
        var codigo = AyudaPlantilla.Codigo();
        await AyudaPlantilla.CrearPlantillaValidaAsync(empleado, codigo, ct: ct);
        var contratoId = await AyudaPlantilla.ContratoAprobadoAsync(empleado, gerente, bd, codigo, ct);

        using var anonimo = api.CrearCliente();

        (await anonimo.GetAsync("/api/v1/plantillas-contrato", ct)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await anonimo.GetAsync("/api/v1/plantillas-anexo", ct)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await anonimo.GetAsync($"/api/v1/contratos/{contratoId:D}/pdf", ct)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }
}