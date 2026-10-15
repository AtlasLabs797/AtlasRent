using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Documento de propiedad de un inmueble (docs/ESPECIFICACION.md §3.1 y docs/SEGURIDAD.md
/// SEC-004): un PDF que sube una persona, con su categoría y su versión.
/// <para>
/// <strong>El archivo no está en la base de datos ni en el webroot.</strong> Se guarda en la carpeta
/// de <c>Almacen__Documentos</c> y se sirve por la API con el permiso <c>inmueble.documento</c>: un
/// título de propiedad no es un recurso público.
/// </para>
/// <para>
/// <see cref="NombreArchivo"/> lo genera el servidor y es lo único que se usa para tocar el disco.
/// <see cref="NombreOriginal"/> es solo lo que escribió quien lo subió, para mostrarlo y para
/// nombrarlo al descargar; nunca se concatena en una ruta.
/// </para>
/// </summary>
public sealed class DocumentoInmueble : IEntidadTrazada
{
    public Guid Id { get; set; }

    public Guid InmuebleId { get; set; }

    public CategoriaDocumento Categoria { get; set; }

    /// <summary>
    /// Versión dentro de la categoría: al subir un documento nuevo de la misma categoría, la
    /// siguiente. Las anteriores no se borran.
    /// </summary>
    public int Version { get; set; }

    /// <summary>Nombre que envió quien lo subió, sin ruta. Solo para mostrar y para la descarga.</summary>
    public string NombreOriginal { get; set; } = string.Empty;

    /// <summary>Nombre en disco, derivado del identificador del servidor. Es el que se usa para abrir el archivo.</summary>
    public string NombreArchivo { get; set; } = string.Empty;

    public long TamanoBytes { get; set; }

    /// <summary>Siempre <c>application/pdf</c>: es la única categoría que se acepta.</summary>
    public string TipoContenido { get; set; } = DocumentoInmueble.TipoPdf;

    /// <summary>El único tipo de contenido que se acepta en la subida (docs/SEGURIDAD.md A.4).</summary>
    public const string TipoPdf = "application/pdf";

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}