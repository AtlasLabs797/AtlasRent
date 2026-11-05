using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Contratos;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Plantillas;

/// <summary>
/// F2-07: la plantilla se congela al aprobar (docs/ESPECIFICACION.md §3.5). Este es el criterio de
/// aceptación de la tarea, y es una afirmación negativa: después de aprobar, ni editar ni
/// desactivar la plantilla pueden cambiar lo que dice un contrato ya firmado.
/// <list type="bullet">
/// <item>La copia congelada guarda el texto del día de la firma, y el PDF sale de ella.</item>
/// <item>Desactivar la plantilla del catálogo no toca ni la copia ni el documento del contrato.</item>
/// <item>Sin aprobar no hay documento: no hay texto congelado que imprimir.</item>
/// </list>
/// <para>
/// El PDF se compara byte a byte y no por su texto: QuestPDF escribe los caracteres como índices de
/// glifo de una fuente incrustada con subconjunto, así que el texto no se puede leer del archivo sin
/// una fuente inversora. Dos generaciones del mismo contenido dan los mismos bytes (comprobado),
/// y eso convierte "el documento no ha cambiado" en algo verificable.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class PlantillaCongeladaTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    /// <summary>
    /// El criterio de aceptación: tras aprobar, editar la plantilla no cambia el contrato aprobado,
    /// ni su copia congelada ni el PDF que sale de ella.
    /// </summary>
    [Fact]
    public async Task LaPlantillaQuedaCongeladaAlAprobar()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPlantilla.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaPlantilla.SesionAsync(api, bd, Roles.Gerente, ct);

        var codigo = AyudaPlantilla.Codigo();
        var textoFirmado = AyudaPlantilla.Marca("FIRMADO");
        var plantillaId = await AyudaPlantilla.CrearPlantillaValidaAsync(
            empleado,
            codigo,
            new[] { AyudaPlantilla.Seccion("Objeto del contrato", textoFirmado) },
            ct: ct);

        var contratoId = await AyudaPlantilla.ContratoAprobadoAsync(empleado, gerente, bd, codigo, ct);
        var pdfAlFirmar = SinFecha(await AyudaPlantilla.PdfAsync(gerente, contratoId, ct));

        var congelada = await AyudaPlantilla.CongeladaAsync(bd, contratoId, ct);
        congelada.Should().NotBeNull("aprobar deja una copia congelada, no solo un código");
        congelada!.Codigo.Should().Be(codigo);
        congelada.Secciones.Select(s => s.Texto).Should()
            .Contain(textoFirmado, "la copia guarda el texto del día de la firma");

        // Se edita la plantilla, que es exactamente lo que no puede cambiar este contrato.
        var textoNuevo = AyudaPlantilla.Marca("REVISADO");
        (await AyudaPlantilla.EditarPlantillaAsync(
            empleado,
            plantillaId,
            AyudaPlantilla.CuerpoEdicionPlantilla(
                secciones: new[] { AyudaPlantilla.Seccion("Objeto del contrato", textoNuevo) }),
            ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var trasEditar = await AyudaPlantilla.CongeladaAsync(bd, contratoId, ct);
        trasEditar!.Secciones.Select(s => s.Texto).Should()
            .Contain(textoFirmado, "editar la plantilla no reescribe lo que ya se firmó");
        trasEditar.Secciones.Select(s => s.Texto).Should()
            .NotContain(textoNuevo, "y el texto nuevo no aparece por la puerta de atrás");

        // Y el PDF sigue siendo el mismo archivo, porque sale de la copia.
        SinFecha(await AyudaPlantilla.PdfAsync(gerente, contratoId, ct))
            .Should().Equal(pdfAlFirmar, "el PDF sale de la plantilla congelada, no de la viva");

        // Y la edición sí se guardó: no es que no se pueda editar, es que no alcanza a lo firmado.
        var plantilla = await AyudaPlantilla.Json(
            await empleado.GetAsync($"/api/v1/plantillas-contrato/{plantillaId}", ct), ct);
        plantilla.GetProperty("secciones")[0].GetProperty("texto").GetString()
            .Should().Be(textoNuevo, "el catálogo sí tiene el texto nuevo");

        var ficha = await AyudaContrato.FichaAsync(gerente, contratoId, ct);
        ficha.GetProperty("plantillaContratoCongeladaId").GetString()
            .Should().Be(codigo, "el contrato sigue nombrando la plantilla con la que se firmó");
    }

    /// <summary>
    /// Desactivar la plantilla de un contrato vivo deja su contenido y su PDF intactos: desactivar
    /// es una decisión de catálogo sobre lo que se firmará después, no un borrado de lo ya firmado.
    /// </summary>
    [Fact]
    public async Task DesactivarNoAfectaAUnContratoYaAprobado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPlantilla.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaPlantilla.SesionAsync(api, bd, Roles.Gerente, ct);

        var codigo = AyudaPlantilla.Codigo();
        var textoFirmado = AyudaPlantilla.Marca("FIRMADO");
        var plantillaId = await AyudaPlantilla.CrearPlantillaValidaAsync(
            empleado,
            codigo,
            new[] { AyudaPlantilla.Seccion("Objeto del contrato", textoFirmado) },
            ct: ct);

        var contratoId = await AyudaPlantilla.ContratoAprobadoAsync(empleado, gerente, bd, codigo, ct);
        var antes = SinFecha(await AyudaPlantilla.PdfAsync(gerente, contratoId, ct));

        (await AyudaPlantilla.DesactivarPlantillaAsync(gerente, plantillaId, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var congelada = await AyudaPlantilla.CongeladaAsync(bd, contratoId, ct);
        congelada.Should().NotBeNull("desactivar la plantilla no borra la copia congelada");
        congelada!.Secciones.Select(s => s.Texto).Should().Contain(textoFirmado);

        SinFecha(await AyudaPlantilla.PdfAsync(gerente, contratoId, ct))
            .Should().Equal(antes, "el documento sale igual antes y después de desactivar la plantilla");

        var plantilla = await AyudaPlantilla.Json(
            await gerente.GetAsync($"/api/v1/plantillas-contrato/{plantillaId}", ct), ct);
        plantilla.GetProperty("activa").GetBoolean().Should().BeFalse("la plantilla sí está desactivada");

        // Desactivar dos veces la misma plantilla es un 409, no un 200 que no hace nada.
        (await AyudaPlantilla.DesactivarPlantillaAsync(gerente, plantillaId, ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Y con la plantilla desactivada no se puede seguir approving con ella.
        var otro = await AyudaPlantilla.ContratoEnBorradorAsync(empleado, bd, codigo, ct);
        var respuesta = await AyudaPlantilla.AprobarAsync(gerente, otro, ct);
        respuesta.StatusCode.Should().Be(
            HttpStatusCode.Conflict,
            "un contrato no se aprueba con una plantilla que ya no está en el catálogo");
    }

    /// <summary>
    /// El PDF se compone desde la copia congelada: lleva la plantilla con sus secciones y el anexo
    /// que la plantilla declara, y cambia con ellos y solo con ellos.
    /// </summary>
    [Fact]
    public async Task ElPdfDelContratoSaleDeLaCopiaCongelada()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPlantilla.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaPlantilla.SesionAsync(api, bd, Roles.Gerente, ct);

        await AyudaPlantilla.LiberarTipoAsync(gerente, "Normas", ct);
        var textoAnexo = AyudaPlantilla.Marca("ANEXO");
        await AyudaPlantilla.CrearAnexoValidoAsync(
            empleado, "Normas", new[] { AyudaPlantilla.Seccion("Horario de silencio", textoAnexo) }, ct);

        var codigo = AyudaPlantilla.Codigo();
        var textoPlantilla = AyudaPlantilla.Marca("PLANTILLA");
        var plantillaId = await AyudaPlantilla.CrearPlantillaValidaAsync(
            empleado,
            codigo,
            new[] { AyudaPlantilla.Seccion("Objeto del contrato", textoPlantilla) },
            new[] { "Normas" },
            ct);

        var contratoId = await AyudaPlantilla.ContratoAprobadoAsync(empleado, gerente, bd, codigo, ct);
        var pdf = SinFecha(await AyudaPlantilla.PdfAsync(gerente, contratoId, ct));
        pdf.Length.Should().BeGreaterThan(1000, "un documento con contenido, no un archivo vacío");

        var congelada = await AyudaPlantilla.CongeladaAsync(bd, contratoId, ct);
        congelada!.Secciones.Select(s => s.Texto).Should().Contain(textoPlantilla);
        congelada.Anexos.Select(a => a.Nombre).Should().Contain("Normas de convivencia");
        congelada.Anexos.SelectMany(a => a.Secciones).Select(s => s.Texto).Should().Contain(textoAnexo);

        // Con el mismo contenido congelado, el mismo archivo.
        AyudaPlantilla.SinFechaDeGeneracion(await AyudaPlantilla.PdfAsync(gerente, contratoId, ct)).Should().Equal(pdf);

        // Y con otro contenido congelado, otro archivo: el PDF no es una constante.
        var textoOtro = AyudaPlantilla.Marca("OTRA");
        (await AyudaPlantilla.EditarPlantillaAsync(
            empleado,
            plantillaId,
            AyudaPlantilla.CuerpoEdicionPlantilla(
                secciones: new[] { AyudaPlantilla.Seccion("Objeto del contrato", textoOtro) }),
            ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var otroContrato = await AyudaPlantilla.ContratoAprobadoAsync(empleado, gerente, bd, codigo, ct);
        var pdfOtro = SinFecha(await AyudaPlantilla.PdfAsync(gerente, otroContrato, ct));
        pdfOtro.Should().NotEqual(pdf, "el documento cambia porque cambió su contenido congelado");
    }

    /// <summary>
    /// Sin aprobar no hay documento: la plantilla se congela al aprobar, y un PDF sin texto congelado
    /// sería un contrato en blanco con formato de contrato.
    /// </summary>
    [Fact]
    public async Task SinAprobarNoHayPdfDeContrato()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPlantilla.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaPlantilla.SesionAsync(api, bd, Roles.Gerente, ct);

        var codigo = AyudaPlantilla.Codigo();
        await AyudaPlantilla.CrearPlantillaValidaAsync(empleado, codigo, ct: ct);
        var contratoId = await AyudaPlantilla.ContratoEnBorradorAsync(empleado, bd, codigo, ct);

        (await gerente.GetAsync($"/api/v1/contratos/{contratoId:D}/pdf", ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "sin plantilla congelada no hay documento");

        // Al aprobarlo, el mismo contrato ya sí tiene documento.
        (await AyudaPlantilla.AprobarAsync(gerente, contratoId, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await AyudaPlantilla.PdfAsync(gerente, contratoId, ct)).Length
            .Should().BeGreaterThan(1000);
    }

    /// <summary>
    /// El PDF sin las fechas de generación, para poder compararlo con el de otra descarga del mismo
    /// contrato: el contenido es idéntico y lo único que cambia es el momento en que se generó.
    /// </summary>
    private static byte[] SinFecha(byte[] pdf) => AyudaPlantilla.SinFechaDeGeneracion(pdf);
}