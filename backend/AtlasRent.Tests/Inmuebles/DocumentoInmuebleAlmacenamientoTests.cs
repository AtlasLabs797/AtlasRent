using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AtlasRent.Tests.Inmuebles;

/// <summary>
/// F1-10, almacenamiento de documentos (docs/SEGURIDAD.md SEC-004 y A.4): el PDF se guarda fuera del
/// webroot y se sirve por la API, el nombre en disco lo pone el servidor, y la extensión no es
/// prueba de nada.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class DocumentoInmuebleAlmacenamientoTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    [Fact]
    public async Task ElArchivoSeGuardaFueraDelWebrootYSeSirvePorLaApi()
    {
        var ct = TestContext.Current.CancellationToken;
        VaciarCarpeta();
        using var gerente = await SesionAsync(Roles.Gerente, ct);
        var inmueble = await DocumentoInmueblePermisosTests.CrearInmuebleAsync(gerente, ct);
        var pdf = SesionCliente.PdfDePrueba();

        var documento = await SubirAsync(gerente, inmueble, "titulo-de-propiedad.pdf", pdf, "Titulo", ct);

        // 1) El archivo está en la carpeta de Almacen__Documentos, que en pruebas es una temporal.
        var carpeta = Path.GetFullPath(api.CarpetaDocumentos);
        var enDisco = ArchivosEnDisco();
        enDisco.Should().HaveCount(1, "una subida, un archivo");
        Path.GetFullPath(enDisco[0]).Should().StartWith(carpeta + Path.DirectorySeparatorChar);
        (await File.ReadAllBytesAsync(enDisco[0], ct)).Should().Equal(pdf);

        // 2) Y no está en el webroot: un PDF de propiedad no es un archivo estático.
        var webroot = Path.Combine(
            api.Services.GetRequiredService<IHostEnvironment>().ContentRootPath,
            "wwwroot");
        Directory
            .Exists(webroot)
            .Should()
            .BeFalse($"{webroot} no debe existir en pruebas, y por eso no hay nada que servir desde ahí");

        // 3) La API lo sirve byte a byte.
        var descarga = await gerente.GetAsync(
            $"/api/v1/inmuebles/{inmueble:D}/documentos/{documento:D}/archivo",
            ct);
        descarga.StatusCode.Should().Be(HttpStatusCode.OK);
        descarga.Content.Headers.ContentType?.MediaType.Should().Be(DocumentoInmueble.TipoPdf);
        (await descarga.Content.ReadAsByteArrayAsync(ct)).Should().Equal(pdf);
    }

    [Theory]
    [InlineData("../../fuera.pdf")]
    [InlineData(@"..\..\fuera.pdf")]
    [InlineData(@"C:\Windows\Temp\fuera.pdf")]
    public async Task UnNombreConSaltoDeCarpetaNoEscribeFuera(string nombreEnviado)
    {
        var ct = TestContext.Current.CancellationToken;
        VaciarCarpeta();
        using var gerente = await SesionAsync(Roles.Gerente, ct);
        var inmueble = await DocumentoInmueblePermisosTests.CrearInmuebleAsync(gerente, ct);

        var subida = await gerente.SubirArchivoAsync(
            $"/api/v1/inmuebles/{inmueble:D}/documentos",
            "archivo",
            nombreEnviado,
            SesionCliente.PdfDePrueba(),
            DocumentoInmueble.TipoPdf,
            [("categoria", "Compraventa")],
            ct);
        subida.StatusCode.Should().Be(HttpStatusCode.OK, await DocumentoInmueblePermisosTests.textoDe(subida, ct));

        var carpeta = Path.GetFullPath(api.CarpetaDocumentos);
        var enDisco = ArchivosEnDisco();
        enDisco.Should().HaveCount(1, "el archivo queda dentro de la carpeta del almacén");

        // El nombre en disco es un identificador: no tiene nada que ver con lo que envió el cliente.
        Path.GetFileName(enDisco[0]).Should().NotContain("fuera");
        Path.GetFileNameWithoutExtension(enDisco[0])
            .Should()
            .HaveLength(32, "sale de un Guid con formato N");

        // Y las rutas que el nombre enviado podría haber apuntado no existen.
        File.Exists(Path.GetFullPath(Path.Combine(carpeta, "..", "..", "fuera.pdf")))
            .Should()
            .BeFalse("el nombre enviado no saca el archivo de la carpeta");
        File.Exists(Path.GetFullPath(nombreEnviado)).Should().BeFalse("una ruta absoluta tampoco");
    }

    /// <summary>
    /// La extensión no vale como prueba: un ejecutable (o un script) con nombre <c>.pdf</c> se
    /// rechaza por su contenido (docs/SEGURIDAD.md A.4).
    /// </summary>
    [Theory]
    [InlineData("MZ\u0093\u0000\u0000", "ejecutable de Windows")]
    [InlineData("#!/bin/sh\nrm -rf /\n", "guion de shell")]
    [InlineData("<html><body>no soy un pdf</body></html>", "página web")]
    public async Task SeRechazaUnArchivoQueNoEsPdfAunqueSeLlamePdf(string contenido, string descripcion)
    {
        var ct = TestContext.Current.CancellationToken;
        VaciarCarpeta();
        using var gerente = await SesionAsync(Roles.Gerente, ct);
        var inmueble = await DocumentoInmueblePermisosTests.CrearInmuebleAsync(gerente, ct);

        var subida = await gerente.SubirArchivoAsync(
            $"/api/v1/inmuebles/{inmueble:D}/documentos",
            "archivo",
            "titulo.pdf",
            System.Text.Encoding.ASCII.GetBytes(contenido),
            DocumentoInmueble.TipoPdf,
            [("categoria", "Titulo")],
            ct);

        subida.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"un {descripcion} no es un documento de propiedad");
        ArchivosEnDisco().Should().BeEmpty("un archivo rechazado no deja nada en el almacén");
    }

    [Fact]
    public async Task SeRechazaUnArchivoQueNoTieneExtensionPdf()
    {
        var ct = TestContext.Current.CancellationToken;
        VaciarCarpeta();
        using var gerente = await SesionAsync(Roles.Gerente, ct);
        var inmueble = await DocumentoInmueblePermisosTests.CrearInmuebleAsync(gerente, ct);

        // Un PDF de verdad con otro nombre: el contenido sirve, pero la regla es PDF y nada más.
        var subida = await gerente.SubirArchivoAsync(
            $"/api/v1/inmuebles/{inmueble:D}/documentos",
            "archivo",
            "titulo.pdf.exe",
            SesionCliente.PdfDePrueba(),
            DocumentoInmueble.TipoPdf,
            [("categoria", "Titulo")],
            ct);

        subida.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ArchivosEnDisco().Should().BeEmpty();
    }

    [Fact]
    public async Task SeRespetaElTamanoMaximo()
    {
        var ct = TestContext.Current.CancellationToken;
        VaciarCarpeta();
        using var gerente = await SesionAsync(Roles.Gerente, ct);
        var inmueble = await DocumentoInmueblePermisosTests.CrearInmuebleAsync(gerente, ct);

        var justo = SesionCliente.PdfDePrueba((int)AlmacenDocumentos.TamanoMaximoBytes);
        (await gerente.SubirArchivoAsync(
            $"/api/v1/inmuebles/{inmueble:D}/documentos",
            "archivo",
            "grande.pdf",
            justo,
            DocumentoInmueble.TipoPdf,
            [("categoria", "Titulo")],
            ct)).StatusCode.Should().Be(HttpStatusCode.OK, "el tamaño máximo se admite");

        var mayor = SesionCliente.PdfDePrueba((int)AlmacenDocumentos.TamanoMaximoBytes + 1);
        (await gerente.SubirArchivoAsync(
            $"/api/v1/inmuebles/{inmueble:D}/documentos",
            "archivo",
            "demasiado.pdf",
            mayor,
            DocumentoInmueble.TipoPdf,
            [("categoria", "Titulo")],
            ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "un byte de más ya no entra");

        ArchivosEnDisco().Should().HaveCount(1, "solo se quedó el que estaba dentro del límite");
    }

    [Fact]
    public async Task CadaSubidaDeLaMismaCategoriaEsUnaVersionMas()
    {
        var ct = TestContext.Current.CancellationToken;
        VaciarCarpeta();
        using var gerente = await SesionAsync(Roles.Gerente, ct);
        var inmueble = await DocumentoInmueblePermisosTests.CrearInmuebleAsync(gerente, ct);

        await SubirAsync(gerente, inmueble, "titulo.pdf", SesionCliente.PdfDePrueba(), "Titulo", ct);
        await SubirAsync(gerente, inmueble, "titulo-rectificado.pdf", SesionCliente.PdfDePrueba(), "Titulo", ct);
        await SubirAsync(gerente, inmueble, "compraventa.pdf", SesionCliente.PdfDePrueba(), "Compraventa", ct);

        var lista = await (await gerente.GetAsync($"/api/v1/inmuebles/{inmueble:D}/documentos", ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);

        var documentos = lista.GetProperty("data").EnumerateArray().ToList();
        documentos.Should().HaveCount(3);

        var titulos = documentos.Where(d => d.GetProperty("categoria").GetString() == "Titulo").ToList();
        titulos.Should().HaveCount(2);
        titulos.Select(d => d.GetProperty("version").GetInt32()).Should().BeEquivalentTo([1, 2]);

        // Los archivos anteriores no se tocan: un documento de propiedad no se borra ni se sobrescribe.
        ArchivosEnDisco().Should().HaveCount(3);
    }

    private static async Task<Guid> SubirAsync(
        SesionCliente sesion,
        Guid inmueble,
        string nombreArchivo,
        byte[] pdf,
        string categoria,
        CancellationToken ct)
    {
        var subida = await sesion.SubirArchivoAsync(
            $"/api/v1/inmuebles/{inmueble:D}/documentos",
            "archivo",
            nombreArchivo,
            pdf,
            DocumentoInmueble.TipoPdf,
            [("categoria", categoria)],
            ct);
        subida.StatusCode.Should().Be(HttpStatusCode.OK, await DocumentoInmueblePermisosTests.textoDe(subida, ct));

        return (await subida.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Los PDF que hay ahora mismo en el almacén. La carpeta puede no existir todavía: ninguna
    /// subida aceptada significa ninguna carpeta creada.
    /// </summary>
    private string[] ArchivosEnDisco() =>
        Directory.Exists(api.CarpetaDocumentos)
            ? Directory.GetFiles(api.CarpetaDocumentos, "*.pdf", SearchOption.AllDirectories)
            : [];
    /// <summary>
    /// La carpeta de documentos la comparte toda la clase (una sola instancia de la fábrica), así que
    /// cada prueba la deja vacía al empezar. Si no, el archivo de la prueba anterior contaría como
    /// si fuera suyo y las comprobaciones sobre lo que queda en disco no dirían nada.
    /// </summary>
    private void VaciarCarpeta()
    {
        if (Directory.Exists(api.CarpetaDocumentos))
        {
            Directory.Delete(api.CarpetaDocumentos, recursive: true);
        }
    }

    private async Task<SesionCliente> SesionAsync(string rol, CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: rol, ct: ct);
        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }
}