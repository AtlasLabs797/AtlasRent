using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Inmuebles;

/// <summary>
/// F1-10, permisos: los documentos de propiedad van detrás de <c>inmueble.documento</c>, que es de
/// gerente y administrador (docs/ESPECIFICACION.md §2, docs/SEGURIDAD.md SEC-004).
/// <para>
/// Es el criterio de aceptación de la tarea: <strong>el empleado recibe 403</strong> al listarlos y
/// al descargarlos. Los inmuebles y las unidades sí son suyos: es lo que le toca.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class DocumentoInmueblePermisosTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    [Fact]
    public async Task ElEmpleadoRecibe403AlListarYAlDescargarLosDocumentosDePropiedad()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await SesionAsync(Roles.Gerente, ct);
        var inmueble = await CrearInmuebleAsync(gerente, ct);
        var documento = await SubirAsync(gerente, inmueble, "titulo.pdf", SesionCliente.PdfDePrueba(), ct);

        // El gerente sí entra: sin esto, el 403 del empleado no probaría nada.
        (await gerente.GetAsync($"/api/v1/inmuebles/{inmueble:D}/documentos", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using var empleado = await SesionAsync(Roles.Empleado, ct);

        // El empleado gestiona inmuebles y unidades sin problema.
        (await empleado.GetAsync($"/api/v1/inmuebles/{inmueble:D}/unidades", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var listado = await empleado.GetAsync($"/api/v1/inmuebles/{inmueble:D}/documentos", ct);
        listado.StatusCode.Should().Be(HttpStatusCode.Forbidden, "un título de propiedad no lo ve el empleado");

        var descarga = await empleado.GetAsync(
            $"/api/v1/inmuebles/{inmueble:D}/documentos/{documento:D}/archivo",
            ct);
        descarga.StatusCode.Should().Be(HttpStatusCode.Forbidden, "descargar es lo mismo que ver");
    }

    [Fact]
    public async Task ElGerenteYElAdministradorSiPuedenListarYDescargar()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await SesionAsync(Roles.Gerente, ct);
        var inmueble = await CrearInmuebleAsync(gerente, ct);
        var documento = await SubirAsync(gerente, inmueble, "titulo.pdf", SesionCliente.PdfDePrueba(), ct);

        using var admin = await SesionAsync(Roles.Admin, ct);

        (await admin.GetAsync($"/api/v1/inmuebles/{inmueble:D}/documentos", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var descarga = await admin.GetAsync(
            $"/api/v1/inmuebles/{inmueble:D}/documentos/{documento:D}/archivo",
            ct);
        descarga.StatusCode.Should().Be(HttpStatusCode.OK);
        descarga.Content.Headers.ContentType?.MediaType.Should().Be(DocumentoInmueble.TipoPdf);
    }

    [Fact]
    public async Task SinSesionNoSeListanLosDocumentosDePropiedad()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anonimo = api.CrearCliente();

        (await anonimo.GetAsync($"/api/v1/inmuebles/{Guid.NewGuid():D}/documentos", ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Segunda barrera, por si la API dejara pasar a alguien: la tabla tiene RLS y con el rol
    /// <c>empleado</c> no sale ninguna fila (docs/SEGURIDAD.md A.4).
    /// </summary>
    [Fact]
    public async Task LaBaseDeDatosTampocoDejaLeerLosDocumentosConRolEmpleado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await SesionAsync(Roles.Gerente, ct);
        var inmueble = await CrearInmuebleAsync(gerente, ct);
        await SubirAsync(gerente, inmueble, "titulo.pdf", SesionCliente.PdfDePrueba(), ct);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var conEmpleado = await CuentaFilasAsync(contexto, inmueble, Roles.Empleado, ct);
        var conGerente = await CuentaFilasAsync(contexto, inmueble, Roles.Gerente, ct);

        conEmpleado.Should().Be(0, "la política de documento_inmueble solo admite admin y gerente");
        conGerente.Should().Be(1, "el gerente sí ve el documento de su inmueble");
    }

    private static async Task<int> CuentaFilasAsync(
        AtlasRentDbContext contexto,
        Guid inmueble,
        string rol,
        CancellationToken ct)
    {
        // El rol tiene que estar puesto en app.rol dentro de una transacción: la política lo lee de
        // ahí, igual que hace la aplicación. Va como parámetro de PostgreSQL, no pegado en el SQL.
        // Se cuentan solo los documentos de este inmueble porque la base la comparten todas las
        // pruebas de la clase.
        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);
        await contexto.Database.ExecuteSqlAsync($"SELECT set_config('app.rol', {rol}, true)", ct);
        var cuenta = await contexto.DocumentoInmueble
            .AsNoTracking()
            .CountAsync(d => d.InmuebleId == inmueble, ct);
        await transaccion.RollbackAsync(ct);

        return cuenta;
    }

    internal static async Task<Guid> CrearInmuebleAsync(SesionCliente sesion, CancellationToken ct)
    {
        var alta = await sesion.PostAsync(
            "/api/v1/inmuebles",
            new { nombre = $"Edificio {Guid.NewGuid():N}"[..40], tipo = "Edificio" },
            ct);
        alta.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await alta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    internal static async Task<Guid> SubirAsync(
        SesionCliente sesion,
        Guid inmueble,
        string nombreArchivo,
        byte[] contenido,
        CancellationToken ct)
    {
        var subida = await sesion.SubirArchivoAsync(
            $"/api/v1/inmuebles/{inmueble:D}/documentos",
            "archivo",
            nombreArchivo,
            contenido,
            DocumentoInmueble.TipoPdf,
            [("categoria", nameof(CategoriaDocumento.Compraventa))],
            ct);
        subida.StatusCode.Should().Be(HttpStatusCode.OK, await textoDe(subida, ct));

        return (await subida.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    internal static async Task<string> textoDe(HttpResponseMessage respuesta, CancellationToken ct) =>
        await respuesta.Content.ReadAsStringAsync(ct);

    private async Task<SesionCliente> SesionAsync(string rol, CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: rol, ct: ct);
        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }
}