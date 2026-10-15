using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Plantillas;

/// <summary>
/// F2-07: el catálogo de anexos es de uno activo por tipo (docs/ESPECIFICACION.md §3.5: "Anexos:
/// catálogo, uno activo por tipo"), y es un catálogo: desactivar no borra.
/// <list type="bullet">
/// <item>Un segundo anexo activo del mismo tipo no entra; al desactivar el primero, el siguiente sí.</item>
/// <item>El anexo desactivado sigue ahí, con su texto, y se puede ver en el catálogo con todos=true.</item>
/// <item>Los datos que no valen (sin nombre, sin tipo, con un tipo que no existe) se rechazan.</item>
/// </list>
/// <para>
/// Las pruebas comparten la base de la colección y el catálogo es de uno activo por tipo, así que
/// cada una deja libre el tipo que va a usar antes de crear el suyo.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class AnexoCatalogoTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    [Fact]
    public async Task NoPuedeHaberDosAnexosActivosDelMismoTipo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPlantilla.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaPlantilla.SesionAsync(api, bd, Roles.Gerente, ct);

        await AyudaPlantilla.LiberarTipoAsync(gerente, "Mantenimiento", ct);
        var primero = await AyudaPlantilla.CrearAnexoValidoAsync(empleado, "Mantenimiento", ct: ct);

        var segundo = await AyudaPlantilla.CrearAnexoAsync(
            empleado, AyudaPlantilla.CuerpoAnexo("Mantenimiento", "Otro mantenimiento"), ct);
        segundo.StatusCode.Should().Be(
            HttpStatusCode.Conflict,
            "el catálogo es de uno activo por tipo, no de dos");

        // Desactivar el primero abre sitio: el catálogo cambia de estado, no se vacía.
        (await AyudaPlantilla.DesactivarAnexoAsync(gerente, primero, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await AyudaPlantilla.CrearAnexoAsync(
            empleado, AyudaPlantilla.CuerpoAnexo("Mantenimiento", "Mantenimiento nuevo"), ct))
            .StatusCode.Should().Be(HttpStatusCode.Created, "desactivado el anterior, entra el siguiente");
    }

    /// <summary>Desactivar no borra: el anexo sigue en la base, con su texto, y se puede consultar.</summary>
    [Fact]
    public async Task DesactivarUnAnexoNoLoBorra()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPlantilla.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaPlantilla.SesionAsync(api, bd, Roles.Gerente, ct);

        await AyudaPlantilla.LiberarTipoAsync(gerente, "Normas", ct);
        var texto = AyudaPlantilla.Marca("REGLAS");
        var id = await AyudaPlantilla.CrearAnexoValidoAsync(
            empleado, "Normas", new[] { AyudaPlantilla.Seccion("Regla", texto) }, ct);

        (await AyudaPlantilla.DesactivarAnexoAsync(gerente, id, ct)).StatusCode
            .Should().Be(HttpStatusCode.OK);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var fila = await contexto.PlantillaAnexo.AsNoTracking().SingleAsync(a => a.Id == Guid.Parse(id), ct);
        fila.Activa.Should().BeFalse();
        fila.Secciones.Select(s => s.Texto).Should().Contain(texto, "desactivar es poner un estado, no borrar");
    }

    /// <summary>
    /// El catálogo enseña los anexos activos, que son los que se pueden elegir al firmar; los
    /// desactivados siguen ahí, pero solo si se piden con todos=true.
    /// </summary>
    [Fact]
    public async Task ElCatalogoSoloEnseñaLosAnexosActivos()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPlantilla.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaPlantilla.SesionAsync(api, bd, Roles.Gerente, ct);

        await AyudaPlantilla.LiberarTipoAsync(gerente, "Otros", ct);
        var nombre = $"Otros de prueba {Guid.NewGuid():N}"[..32];
        var alta = await AyudaPlantilla.CrearAnexoAsync(
            empleado, AyudaPlantilla.CuerpoAnexo("Otros", nombre), ct);
        alta.StatusCode.Should().Be(HttpStatusCode.Created, await alta.Cuerpo());
        var id = (await AyudaPlantilla.Id(alta, ct)).ToString();

        var activos = await AyudaPlantilla.Json(
            await empleado.GetAsync("/api/v1/plantillas-anexo", ct), ct);
        activos.GetProperty("data").EnumerateArray()
            .Should().Contain(a => a.GetProperty("nombre").GetString() == nombre,
                "mientras está activo es elegible");

        (await AyudaPlantilla.DesactivarAnexoAsync(gerente, id, ct)).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var trasDesactivar = await AyudaPlantilla.Json(
            await empleado.GetAsync("/api/v1/plantillas-anexo", ct), ct);
        trasDesactivar.GetProperty("data").EnumerateArray()
            .Should().NotContain(a => a.GetProperty("nombre").GetString() == nombre,
                "un anexo desactivado no se ofrece para firmar");

        var todos = await AyudaPlantilla.Json(
            await empleado.GetAsync("/api/v1/plantillas-anexo?todos=true", ct), ct);
        todos.GetProperty("data").EnumerateArray()
            .Should().Contain(a => a.GetProperty("nombre").GetString() == nombre,
                "pero sigue en el catálogo, porque no se borra");
    }

    /// <summary>Un anexo sin tipo no entra: el catálogo se ordena por tipo.</summary>
    [Fact]
    public async Task UnAnexoSinTipoNoSeGuarda()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPlantilla.SesionAsync(api, bd, Roles.Empleado, ct);

        var respuesta = await AyudaPlantilla.CrearAnexoAsync(
            empleado,
            new { nombre = "Sin tipo", secciones = Array.Empty<object>() },
            ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest, "el tipo es obligatorio");
    }

    /// <summary>Un anexo sin nombre tampoco: es lo que lo separa de los demás en el catálogo.</summary>
    [Fact]
    public async Task UnAnexoSinNombreNoSeGuarda()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPlantilla.SesionAsync(api, bd, Roles.Empleado, ct);

        (await AyudaPlantilla.CrearAnexoAsync(empleado, new { tipo = "Normas" }, ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Un tipo que no existe en el catálogo no se inventa: es un 400 de enlace de modelo.</summary>
    [Fact]
    public async Task UnAnexoDeUnTipoDesconocidoSeRechaza()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPlantilla.SesionAsync(api, bd, Roles.Empleado, ct);

        var respuesta = await AyudaPlantilla.CrearAnexoAsync(
            empleado, AyudaPlantilla.CuerpoAnexo("TipoQueNoExiste"), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}