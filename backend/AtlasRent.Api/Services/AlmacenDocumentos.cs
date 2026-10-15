using Microsoft.AspNetCore.Http;

namespace AtlasRent.Api.Services;

/// <summary>
/// Opciones del almacén de documentos, de la sección <c>Almacen</c> de la configuración
/// (<c>Almacen__Documentos</c> en el entorno).
/// <para>
/// La carpeta es un dato de configuración y no una constante del código porque en desarrollo, en
/// pruebas y en producción son tres sitios distintos. No tiene valor por defecto: arrancar sin
/// ella escribiría los documentos donde toque.
/// </para>
/// </summary>
public sealed class OpcionesAlmacen
{
    public const string Seccion = "Almacen";

    /// <summary>Carpeta donde se guardan los documentos subidos. Fuera del webroot.</summary>
    public required string RutaDocumentos { get; init; }

    public static OpcionesAlmacen Desde(IConfiguration configuracion)
    {
        var ruta = configuracion[$"{Seccion}:Documentos"];
        if (string.IsNullOrWhiteSpace(ruta))
        {
            throw new InvalidOperationException(
                "Falta la carpeta de documentos. Definir Almacen__Documentos (en user-secrets en "
                + "desarrollo y en el servicio en producción).");
        }

        return new OpcionesAlmacen { RutaDocumentos = ruta };
    }
}

/// <summary>
/// Almacén de los documentos de propiedad (docs/SEGURIDAD.md SEC-004 y A.4).
/// <para>
/// Tres reglas, y las tres están aquí porque son la diferencia entre un almacén de archivos y un
/// agujero:
/// </para>
/// <list type="number">
/// <item>
/// <strong>Fuera del webroot.</strong> Un PDF de propiedad no es un recurso público: no se sirve como
/// archivo estático, se sirve por la API con el permiso <c>inmueble.documento</c>.
/// </item>
/// <item>
/// <strong>Nombre en disco generado por el servidor.</strong> <see cref="NombreEnDisco"/> es un
/// identificador. El nombre que envía quien sube el archivo no toca el sistema de archivos: con
/// <c>..\..\</c> o una ruta absoluta escribiría fuera de la carpeta.
/// </item>
/// <item>
/// <strong>Tipo real, no extensión.</strong> Un ejecutable llamado <c>.pdf</c> pasa la extensión y
/// no pasa la firma del archivo (<see cref="EsPdf"/>).
/// </item>
/// </list>
/// </summary>
public sealed class AlmacenDocumentos(OpcionesAlmacen opciones)
{
    /// <summary>
    /// Tamaño máximo de un documento de propiedad. Un título escaneado ocupa unos pocos megas; a
    /// partir de aquí lo que llega es un archivo que no debería subirse.
    /// <para>
    /// TODO(A#): el valor es un supuesto. docs/SEGURIDAD.md A.4 exige "tamaño máximo" sin fijar la
    /// cifra; si el responsable fija otra, es este número y el mensaje que lo acompaña.
    /// </para>
    /// </summary>
    public const long TamanoMaximoBytes = 25L * 1024 * 1024;

    private const string ExtensionPdf = ".pdf";

    /// <summary>Firma de un PDF: "%PDF-". Es lo primero que dice el formato.</summary>
    private static readonly byte[] FirmaPdf = "%PDF-"u8.ToArray();

    /// <summary>Carpeta raíz, ya normalizada, que es contra la que se comprueba cualquier ruta.</summary>
    public string Carpeta => Path.GetFullPath(opciones.RutaDocumentos);

    public void AsegurarCarpeta() => Directory.CreateDirectory(Carpeta);

    /// <summary>
    /// Nombre del archivo en disco. Sale de un identificador nuevo y de nada más: no hay forma de
    /// que traiga una ruta, porque no viene de la petición.
    /// </summary>
    public static string NombreEnDisco() => $"{Guid.NewGuid():N}{ExtensionPdf}";

    /// <summary>Si el nombre que envió el cliente tiene extensión PDF (sin distinguir mayúsculas).</summary>
    public static bool TieneExtensionPdf(string? nombre) =>
        Path.GetExtension(nombre ?? string.Empty).Equals(ExtensionPdf, StringComparison.OrdinalIgnoreCase);

    /// <summary>Si los primeros bytes son los de un PDF. Esta es la comprobación del tipo real.</summary>
    public static bool EsPdf(ReadOnlySpan<byte> cabecera) => cabecera.StartsWith(FirmaPdf);

    /// <summary>
    /// Ruta completa de un archivo del almacén. Además de usar un nombre generado, <strong>comprueba
    /// que la ruta final cae dentro de la carpeta</strong>: es la defensa que cierra el caso de que
    /// un nombre viniera de fuera del almacén.
    /// </summary>
    public string RutaDe(string nombreArchivo)
    {
        var carpeta = Carpeta;
        var ruta = Path.GetFullPath(Path.Combine(carpeta, nombreArchivo));
        var prefijo = carpeta.EndsWith(Path.DirectorySeparatorChar)
            ? carpeta
            : carpeta + Path.DirectorySeparatorChar;

        if (!ruta.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase))
        {
            throw new DocumentoInvalidoException(
                "El documento queda fuera de la carpeta de almacenamiento.");
        }

        return ruta;
    }

    /// <summary>
    /// Valida lo que se va a subir y devuelve el nombre en disco. Falla si el archivo no es un PDF
    /// de verdad o si excede el tamaño máximo.
    /// </summary>
    public async Task<string> ValidarYGuardarAsync(IFormFile archivo, CancellationToken ct)
    {
        if (!TieneExtensionPdf(archivo.FileName))
        {
            throw new DocumentoInvalidoException("Solo se admiten documentos PDF.");
        }

        if (archivo.Length is 0 or > TamanoMaximoBytes)
        {
            throw new DocumentoInvalidoException(
                $"El documento debe pesar entre 1 byte y {TamanoMaximoBytes / (1024 * 1024)} MB.");
        }

        await using var origen = archivo.OpenReadStream();
        var cabecera = new byte[FirmaPdf.Length];

        // ReadAtLeast y no Read: una lectura suelta puede devolver menos bytes de los pedidos
        // aunque el archivo tenga la firma al principio.
        var leidos = await origen.ReadAtLeastAsync(cabecera, FirmaPdf.Length, false, ct);
        if (leidos < FirmaPdf.Length || !EsPdf(cabecera))
        {
            throw new DocumentoInvalidoException(
                "El archivo no es un PDF: la extensión no coincide con su contenido.");
        }

        AsegurarCarpeta();

        var nombre = NombreEnDisco();
        var ruta = RutaDe(nombre);
        await using (var destino = File.Create(ruta))
        {
            // La cabecera que se acaba de leer también es parte del archivo: se vuelve a escribir
            // antes de copiar el resto, o el PDF se guardaría sin su "%PDF-".
            await destino.WriteAsync(cabecera, ct);
            await origen.CopyToAsync(destino, ct);
        }

        return nombre;
    }

    /// <summary>
    /// Borra el archivo del disco sin que falle la operación que lo está limpiando: se usa cuando
    /// la escritura de la fila falla después de que el archivo ya estaba en la carpeta.
    /// </summary>
    public void BorrarSiExiste(string nombreArchivo)
    {
        try
        {
            var ruta = RutaDe(nombreArchivo);
            if (File.Exists(ruta))
            {
                File.Delete(ruta);
            }
        }
        catch (DocumentoInvalidoException)
        {
            // El nombre no es del almacén: no hay nada nuestro que borrar.
        }
        catch (IOException)
        {
            // Un archivo que no se puede borrar no puede tumbar la petición que ya va a fallar.
        }
    }
}

/// <summary>
/// Lo que el cliente subió no cumple lo que se admite (docs/SEGURIDAD.md A.4). Se traduce a un 400
/// con el motivo, que es lo que necesita quien está delante para corregirlo.
/// </summary>
public sealed class DocumentoInvalidoException(string mensaje) : Exception(mensaje);