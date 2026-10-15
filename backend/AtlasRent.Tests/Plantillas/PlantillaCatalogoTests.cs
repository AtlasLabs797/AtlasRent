using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Plantillas;

/// <summary>
/// F2-07: el catálogo de plantillas de contrato y sus datos obligatorios.
/// <para>
/// <b>Por qué no hay una prueba de "dos plantillas activas de la misma categoría".</b> Porque las
/// plantillas no tienen categoría: la especificación define categorías para los documentos del
/// inmueble (§3.1) y para el catálogo de anexos ("uno activo por tipo", §3.5), pero no las define
/// para las plantillas de contrato. Lo que ordena este catálogo es el <b>código</b>, que es único en
/// la base: no puede haber dos plantillas con el mismo nombre de uso, que es lo que la regla de "uno
/// activo por categoría" quería decir. Añadir categorías sería inventar una regla de negocio que
/// nadie ha escrito, y encima impediría tener a la vez, por ejemplo, una plantilla vigente y otra
/// para una campaña concreta.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class PlantillaCatalogoTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    /// <summary>
    /// El código identifica a la plantilla y los contratos firmados lo citan, así que no se
    /// reutiliza: el segundo alta con el mismo código es un 409 con el motivo escrito.
    /// </summary>
    [Fact]
    public async Task NoPuedeHaberDosPlantillasConElMismoCodigo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPlantilla.SesionAsync(api, bd, Roles.Empleado, ct);

        var codigo = AyudaPlantilla.Codigo();
        await AyudaPlantilla.CrearPlantillaValidaAsync(empleado, codigo, ct: ct);

        var repetida = await AyudaPlantilla.CrearPlantillaAsync(
            empleado, AyudaPlantilla.CuerpoPlantilla(codigo, "Otra con el mismo código"), ct);

        repetida.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await repetida.Cuerpo()).Should().Contain(codigo, "el conflicto dice qué código está repetido");
    }

    /// <summary>Sin nombre o sin código no hay plantilla: son los dos datos que la hacen usable.</summary>
    [Fact]
    public async Task UnaPlantillaSinNombreOSinCodigoNoSeGuarda()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPlantilla.SesionAsync(api, bd, Roles.Empleado, ct);

        (await AyudaPlantilla.CrearPlantillaAsync(
            empleado, AyudaPlantilla.CuerpoPlantilla(nombre: null), ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "el nombre es obligatorio");

        (await AyudaPlantilla.CrearPlantillaAsync(
            empleado, AyudaPlantilla.CuerpoPlantilla(codigo: string.Empty), ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "el código es obligatorio");
    }

    /// <summary>
    /// Un bloque sin texto no se guarda: la validación del modelo lo rechaza con un 400 porque un
    /// hueco en blanco en el PDF se vería como que falta algo en el contrato firmado.
    /// </summary>
    [Fact]
    public async Task UnBloqueEnBlancoNoSeGuarda()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPlantilla.SesionAsync(api, bd, Roles.Empleado, ct);

        var respuesta = await AyudaPlantilla.CrearPlantillaAsync(
            empleado,
            AyudaPlantilla.CuerpoPlantilla(
                secciones: new[]
                {
                    AyudaPlantilla.Seccion("Con texto", AyudaPlantilla.Marca("TEXTO")),
                    AyudaPlantilla.Seccion("Sin texto", "   "),
                }),
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest, "un bloque en blanco no se imprime");
    }

    /// <summary>Editar o desactivar una plantilla que no existe es un 404, no un 200 vacío.</summary>
    [Fact]
    public async Task UnaPlantillaQueNoExisteDaNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        using var gerente = await AyudaPlantilla.SesionAsync(api, bd, Roles.Gerente, ct);

        (await gerente.GetAsync($"/api/v1/plantillas-contrato/{id:D}", ct)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await AyudaPlantilla.EditarPlantillaAsync(
            gerente, id.ToString(), AyudaPlantilla.CuerpoEdicionPlantilla(), ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AyudaPlantilla.DesactivarPlantillaAsync(gerente, id.ToString(), ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}