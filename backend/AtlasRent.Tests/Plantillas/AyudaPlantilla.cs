using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Tests.Contratos;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Plantillas;

/// <summary>
/// Atajos de las pruebas de plantillas y anexos (F2-07): el alta del catálogo por la API y el
/// recorrido completo de un contrato hasta aprobado con su plantilla congelada.
/// <para>
/// Cada prueba crea su plantilla con un código único, así que no se pisan entre sí aunque compartan
/// la base de datos de la colección.
/// </para>
/// </summary>
internal static class AyudaPlantilla
{
    public const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>Sesión de un rol, lista para llamar por HTTP.</summary>
    public static async Task<SesionCliente> SesionAsync(
        ApiTestFactoryBloqueos api,
        ContenedorBdFixture bd,
        string rol,
        CancellationToken ct) =>
        await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: rol, ct: ct), Contrasena, ct);

    /// <summary>Código de plantilla que no se repite en la prueba.</summary>
    public static string Codigo(string prefijo = "plantilla") => $"{prefijo}-{Guid.NewGuid():N}"[..40];

    /// <summary>Un bloque de texto de plantilla o anexo.</summary>
    public static object Seccion(string titulo, string texto) => new { titulo, texto };

    /// <summary>
    /// Un texto de marca que se busca dentro del PDF. Sin espacios y con guion: el PDF lo parte
    /// donde le conviene y una búsqueda con espacios no encontraría nada.
    /// </summary>
    public static string Marca(string nombre) => $"{nombre}-{Guid.NewGuid():N}"[..24];

    /// <summary>
    /// Cuerpo de alta de plantilla. Se llama sin argumentos para crear una plantilla de prueba
    /// normal; los parámetros están para las pruebas de validación.
    /// </summary>
    public static object CuerpoPlantilla(
        string? codigo = null,
        string? nombre = "Estándar",
        object? secciones = null,
        object? anexosTipos = null) =>
        new
        {
            codigo = codigo ?? Codigo(),
            nombre,
            secciones = secciones ?? new[] { Seccion("Objeto", Marca("CLAUSULA")) },
            anexosTipos = anexosTipos ?? new string[0],
        };

    /// <summary>Cuerpo de edición del texto de una plantilla.</summary>
    public static object CuerpoEdicionPlantilla(
        string? nombre = "Estándar editada",
        object? secciones = null,
        object? anexosTipos = null) =>
        new
        {
            nombre,
            secciones = secciones ?? new[] { Seccion("Objeto", Marca("CLAUSULA")) },
            anexosTipos = anexosTipos ?? new string[0],
        };

    /// <summary>Cuerpo de alta de anexo del catálogo.</summary>
    public static object CuerpoAnexo(
        string tipo = "Normas",
        string? nombre = "Normas de convivencia",
        object? secciones = null) =>
        new
        {
            nombre,
            tipo,
            secciones = secciones ?? new[] { Seccion("Horario", Marca("ANEXO")) },
        };

    public static Task<HttpResponseMessage> CrearPlantillaAsync(
        SesionCliente sesion,
        object cuerpo,
        CancellationToken ct) =>
        sesion.PostAsync("/api/v1/plantillas-contrato", cuerpo, ct);

    public static Task<HttpResponseMessage> EditarPlantillaAsync(
        SesionCliente sesion,
        string plantillaId,
        object cuerpo,
        CancellationToken ct) =>
        sesion.PutAsync($"/api/v1/plantillas-contrato/{plantillaId}", cuerpo, ct);

    public static Task<HttpResponseMessage> DesactivarPlantillaAsync(
        SesionCliente sesion,
        string plantillaId,
        CancellationToken ct) =>
        sesion.PostAsync($"/api/v1/plantillas-contrato/{plantillaId}/desactivacion", new { }, ct);

    public static Task<HttpResponseMessage> CrearAnexoAsync(
        SesionCliente sesion,
        object cuerpo,
        CancellationToken ct) =>
        sesion.PostAsync("/api/v1/plantillas-anexo", cuerpo, ct);

    public static Task<HttpResponseMessage> EditarAnexoAsync(
        SesionCliente sesion,
        string anexoId,
        object cuerpo,
        CancellationToken ct) =>
        sesion.PutAsync($"/api/v1/plantillas-anexo/{anexoId}", cuerpo, ct);

    public static Task<HttpResponseMessage> DesactivarAnexoAsync(
        SesionCliente sesion,
        string anexoId,
        CancellationToken ct) =>
        sesion.PostAsync($"/api/v1/plantillas-anexo/{anexoId}/desactivacion", new { }, ct);

    /// <summary>Alta de plantilla que tiene que salir bien, y su identificador.</summary>
    public static async Task<string> CrearPlantillaValidaAsync(
        SesionCliente sesion,
        string codigo,
        object? secciones = null,
        object? anexosTipos = null,
        CancellationToken ct = default)
    {
        var respuesta = await CrearPlantillaAsync(
            sesion,
            CuerpoPlantilla(codigo, secciones: secciones, anexosTipos: anexosTipos),
            ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Created, await respuesta.Cuerpo());

        return (await Id(respuesta, ct)).ToString();
    }

    /// <summary>Alta de anexo que tiene que salir bien, y su identificador.</summary>
    public static async Task<string> CrearAnexoValidoAsync(
        SesionCliente sesion,
        string tipo,
        object? secciones = null,
        CancellationToken ct = default)
    {
        var respuesta = await CrearAnexoAsync(sesion, CuerpoAnexo(tipo, secciones: secciones), ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Created, await respuesta.Cuerpo());

        return (await Id(respuesta, ct)).ToString();
    }

    /// <summary>
    /// Contrato aprobado y listo para documentos: el empleado lo crea con la plantilla dada y lo
    /// manda, y el gerente lo aprueba. Devuelve su identificador.
    /// </summary>
    public static async Task<Guid> ContratoAprobadoAsync(
        SesionCliente empleado,
        SesionCliente gerente,
        ContenedorBdFixture bd,
        string codigoPlantilla,
        CancellationToken ct)
    {
        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);

        var id = await AyudaContrato.CrearAsync(
            empleado,
            AyudaContrato.Peticion(
                clienteId, unidadId, facturadoraId, AyudaContrato.Dia(40), plantilla: codigoPlantilla),
            ct);

        await AyudaContrato.AceptarAsync(empleado, id, "solicitud", new { }, ct);
        await AyudaContrato.AceptarAsync(gerente, id, "aprobacion", null, ct);

        return id;
    }

    /// <summary>
    /// Contrato en borrador con la plantilla dada, sin aprobar. Sirve para probar que aprobar es lo
    /// que congela y lo que hace posible el documento.
    /// </summary>
    public static async Task<Guid> ContratoEnBorradorAsync(
        SesionCliente empleado,
        ContenedorBdFixture bd,
        string codigoPlantilla,
        CancellationToken ct)
    {
        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);

        return await AyudaContrato.CrearAsync(
            empleado,
            AyudaContrato.Peticion(
                clienteId, unidadId, facturadoraId, AyudaContrato.Dia(50), plantilla: codigoPlantilla),
            ct);
    }

    /// <summary>
    /// El gerente aprueba un contrato en borrador, con su flujo de solicitud. Devuelve la respuesta
    /// de la aprobación, que es un 409 cuando la plantilla ya no sirve para congelar.
    /// </summary>
    public static async Task<HttpResponseMessage> AprobarAsync(
        SesionCliente gerente,
        Guid contratoId,
        CancellationToken ct)
    {
        await AyudaContrato.AceptarAsync(gerente, contratoId, "solicitud", new { }, ct);

        return await AyudaContrato.Boton(gerente, contratoId, "aprobacion", null, ct);
    }

    /// <summary>La copia congelada del contrato, leída de la base.</summary>
    public static async Task<ContratoPlantillaCongelada?> CongeladaAsync(
        ContenedorBdFixture bd,
        Guid contratoId,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        return await contexto.ContratoPlantillaCongelada.AsNoTracking()
            .SingleOrDefaultAsync(c => c.ContratoId == contratoId, ct);
    }

    /// <summary>
    /// El PDF del contrato, tal y como lo devuelve la API.
    /// <para>
    /// Las pruebas comparan el archivo entero, no su texto: QuestPDF incrusta las fuentes con
    /// subconjunto y escribe los caracteres como índices de glifo, así que el texto no se puede
    /// leer del PDF sin una fuente inversora. Lo que sí es estable es el archivo: dos generaciones
    /// del mismo contenido dan los mismos bytes, y eso es justo lo que demuestra que el documento
    /// sale de la copia congelada.
    /// </para>
    /// </summary>
    public static async Task<byte[]> PdfAsync(SesionCliente sesion, Guid contratoId, CancellationToken ct)
    {
        var respuesta = await sesion.GetAsync($"/api/v1/contratos/{contratoId:D}/pdf", ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());
        respuesta.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");

        var pdf = await respuesta.Content.ReadAsByteArrayAsync(ct);
        Encoding.ASCII.GetString(pdf, 0, Math.Min(pdf.Length, 5))
            .Should().Be("%PDF-", "un PDF de verdad empieza así");

        return pdf;
    }

    /// <summary>
    /// El PDF sin las fechas de generación. QuestPDF escribe <c>/CreationDate</c> y <c>/ModDate</c>
    /// con el momento en que se genera, así que dos descargas del mismo documento nunca son idénticas
    /// byte a byte; el resto del archivo sí lo es (comprobado). Se sustituyen por una fecha fija de
    /// la misma longitud para poder comparar el contenido sin tocar los desplazamientos.
    /// </summary>
    public static byte[] SinFechaDeGeneracion(byte[] pdf) =>
        Encoding.Latin1.GetBytes(FechasDelDocumento.Replace(
            Encoding.Latin1.GetString(pdf), "(D:00000000000000+00'00')"));

    private static readonly Regex FechasDelDocumento = new(
        @"\(D:\d{14}[+-]\d{2}'\d{2}'\)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// El catálogo de anexos entero, página a página. El endpoint no filtra por tipo ni por nombre y
    /// la base es la de la colección, así que leer solo la primera página haría que cualquier
    /// afirmación sobre un anexo concreto dependiera de cuántas filas hubieran dejado las demás
    /// pruebas.
    /// </summary>
    public static async Task<List<JsonElement>> CatalogoAsync(
        SesionCliente sesion,
        bool todos,
        CancellationToken ct)
    {
        var filas = new List<JsonElement>();
        for (var pagina = 1; ; pagina++)
        {
            var respuesta = await sesion.GetAsync(
                $"/api/v1/plantillas-anexo?todos={(todos ? "true" : "false")}&page={pagina}&pageSize=200&sortBy=tipo",
                ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());

            var cuerpo = await Json(respuesta, ct);
            filas.AddRange(cuerpo.GetProperty("data").EnumerateArray());

            if (pagina >= cuerpo.GetProperty("totalPages").GetInt32())
            {
                return filas;
            }
        }
    }

    /// <summary>
    /// Deja libre un tipo de anexo en el catálogo. Las pruebas comparten la base de la colección y
    /// el catálogo es de uno activo por tipo, así que una prueba que usa "Normas" tiene que
    /// despejarlo antes.
    /// <para>
    /// Se recogen todos los identificadores antes de desactivar ninguno: si se desactivara al
    /// recorrer las páginas, el catálogo menguaría mientras se avanza y el salto de páginas se
    /// saltaría filas.
    /// </para>
    /// </summary>
    public static async Task LiberarTipoAsync(SesionCliente gerente, string tipo, CancellationToken ct)
    {
        var aDesactivar = (await CatalogoAsync(gerente, todos: true, ct))
            .Where(a => a.GetProperty("activa").GetBoolean()
                && a.GetProperty("tipo").GetString() == tipo)
            .Select(a => a.GetProperty("id").GetString()!)
            .ToList();

        foreach (var anexo in aDesactivar)
        {
            (await DesactivarAnexoAsync(gerente, anexo, ct))
                .StatusCode.Should().Be(HttpStatusCode.OK, $"desactivar el anexo anterior de tipo {tipo}");
        }
    }

    public static async Task<JsonElement> Json(HttpResponseMessage respuesta, CancellationToken ct) =>
        await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);

    public static async Task<Guid> Id(HttpResponseMessage respuesta, CancellationToken ct) =>
        (await Json(respuesta, ct)).GetProperty("id").GetGuid();
}