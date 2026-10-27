using System.Text.Json;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Un documento listo para servirse: dónde está el archivo y con qué nombre se descarga.
/// <see cref="Ruta"/> es un caminho dentro de la carpeta de <c>Almacen__Documentos</c>;
/// <see cref="Nombre"/> es el que escribió quien lo subió y solo llega a la cabecera de descarga.
/// </summary>
public sealed record ArchivoDocumento(string Ruta, string Nombre, string TipoContenido);

/// <summary>
/// Documentos PDF de propiedad (docs/ESPECIFICACION.md §3.1 y docs/SEGURIDAD.md SEC-004).
/// <para>
/// Un documento es una fila de metadatos más un archivo que vive en la carpeta de
/// <c>Almacen__Documentos</c>, <strong>fuera del webroot</strong>. Aquí solo se sube y se da la ruta
/// del archivo; servirlo es cosa del endpoint, que va detrás del permiso <c>inmueble.documento</c>.
/// </para>
/// <para>
/// Las versiones se acumulan: subir un segundo documento de la misma categoría crea la versión
/// siguiente y no toca la anterior. Un documento de propiedad no se borra ni se sobrescribe.
/// </para>
/// </summary>
public sealed class ServicioDocumentosInmueble(
    AtlasRentDbContext contexto,
    AuditoriaService auditoria,
    ServicioIdempotencia idempotencia,
    AlmacenDocumentos almacen,
    TimeProvider reloj)
{
    /// <summary>
    /// Documentos de un inmueble.
    /// <para>
    /// La lectura va dentro de una transacción propia porque la tabla tiene RLS
    /// (docs/SEGURIDAD.md A.4) y la política lee <c>app.rol</c>, que el interceptor de transacción
    /// escribe al abrirla. Sin ella, la consulta vería la política con el rol vacío y no saldría
    /// ninguna fila. Se hace rollback porque es una lectura.
    /// </para>
    /// </summary>
    public async Task<Pagina<DocumentoInmuebleRespuesta>> ListarAsync(
        Guid inmuebleId,
        ConsultaLista paginacion,
        CategoriaDocumento? categoria,
        CancellationToken ct)
    {
        var consulta = contexto.DocumentoInmueble
            .AsNoTracking()
            .Where(d => d.InmuebleId == inmuebleId);

        if (categoria is not null)
        {
            consulta = consulta.Where(d => d.Categoria == categoria);
        }

        var ordenes = new Dictionary<string, Func<IQueryable<DocumentoInmueble>, bool, IOrderedQueryable<DocumentoInmueble>>>
        {
            ["categoria"] = (q, desc) => desc ? q.OrderByDescending(d => d.Categoria) : q.OrderBy(d => d.Categoria),
            ["version"] = (q, desc) => desc ? q.OrderByDescending(d => d.Version) : q.OrderBy(d => d.Version),
            ["createdAt"] = (q, desc) => desc ? q.OrderByDescending(d => d.CreatedAt) : q.OrderBy(d => d.CreatedAt),
        };

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);
        var pagina = await ConsultaPaginada.PaginarAsync(
            consulta, ordenes, "createdAt", paginacion, ARespuesta, ct);
        await transaccion.RollbackAsync(ct);

        return pagina;
    }

    /// <summary>
    /// Sube un PDF de propiedad. El nombre en disco lo genera el servidor y la extensión no es
    /// prueba de nada: lo que decide es la firma del archivo (<see cref="AlmacenDocumentos.EsPdf"/>).
    /// </summary>
    public async Task<DocumentoInmuebleRespuesta> SubirAsync(
        Guid inmuebleId,
        IFormFile archivo,
        CategoriaDocumento categoria,
        Guid usuario,
        string ip,
        CancellationToken ct)
    {
        // Antes de tocar el disco: un inmueble que no existe no debe dejar ni un archivo (BUG-052).
        if (!await contexto.Inmueble.AsNoTracking().AnyAsync(i => i.Id == inmuebleId, ct))
        {
            throw new KeyNotFoundException($"No existe el inmueble '{inmuebleId:D}'.");
        }

        var nombreArchivo = await almacen.ValidarYGuardarAsync(archivo, ct);

        try
        {
            return await GuardarFilaAsync(
                inmuebleId, archivo, categoria, nombreArchivo, usuario, ip, ct);
        }
        catch
        {
            // El archivo ya está en la carpeta pero la fila no: si se deja ahí, queda un PDF de
            // propiedad en disco sin registro que lo explique.
            almacen.BorrarSiExiste(nombreArchivo);
            throw;
        }
    }

    /// <summary>
    /// Ruta del archivo de un documento, comprobando antes que el documento es del inmueble que se
    /// pide. La ruta se construye con el nombre que generó el servidor, nunca con el nombre que
    /// envió el cliente.
    /// </summary>
    public async Task<ArchivoDocumento> ArchivoAsync(
        Guid inmuebleId,
        Guid documentoId,
        Guid usuario,
        string ip,
        CancellationToken ct)
    {
        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);
        var documento = await contexto.DocumentoInmueble
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == documentoId && d.InmuebleId == inmuebleId, ct);

        if (documento is null)
        {
            await transaccion.RollbackAsync(ct);
            throw new KeyNotFoundException($"No existe el documento '{documentoId:D}'.");
        }

        // SEC-030: bajar un PDF de propiedad es un acceso a datos sensibles y deja rastro.
        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = "documento_inmueble.descargado",
            Ip = ip,
            UsuarioId = usuario,
            Entidad = "documento_inmueble",
            EntidadId = documento.Id.ToString("D"),
            DatosJson = JsonSerializer.Serialize(new { documento.InmuebleId, documento.NombreOriginal }),
            Resultado = "permitido",
            Origen = "api",
        }, ct);
        await transaccion.CommitAsync(ct);

        return new ArchivoDocumento(
            almacen.RutaDe(documento.NombreArchivo),
            documento.NombreOriginal,
            documento.TipoContenido);
    }

    // ---------------------------------------------------------------- internos

    private async Task<DocumentoInmuebleRespuesta> GuardarFilaAsync(
        Guid inmuebleId,
        IFormFile archivo,
        CategoriaDocumento categoria,
        string nombreArchivo,
        Guid usuario,
        string ip,
        CancellationToken ct)
    {
        var documento = new DocumentoInmueble
        {
            InmuebleId = inmuebleId,
            Categoria = categoria,
            NombreOriginal = NombreOriginal(archivo.FileName),
            NombreArchivo = nombreArchivo,
            TamanoBytes = archivo.Length,
            TipoContenido = DocumentoInmueble.TipoPdf,
        };

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.DocumentoInmueble, documento,
            esAlta: true,
            accion: "documento_inmueble.subido",
            entidad: "documento_inmueble",
            usuario,
            ip,
            mutacion: null,
            proyectar: ARespuesta,
            datosAuditoria: DatosAuditoria,
            antesDeGuardar: () => FijarVersionAsync(documento, ct),
            ct: ct);
    }

    /// <summary>
    /// Versión siguiente dentro de la categoría. Se calcula dentro de la misma transacción que
    /// inserta la fila, con un cerrojo por (inmueble, categoría) para que dos subidas simultáneas no
    /// cojan la misma; el índice único <c>(inmueble_id, categoria, version)</c> es la red.
    /// </summary>
    private async Task FijarVersionAsync(DocumentoInmueble documento, CancellationToken ct)
    {
        // Cerrojo de transacción por (inmueble, categoría): dos subidas simultáneas se ponen en fila
        // en vez de calcular la misma versión y dar un 409 al segundo (BUG-052). El índice único
        // sigue siendo la red.
        await contexto.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({"documento_inmueble:" + documento.InmuebleId.ToString("D") + ":" + (int)documento.Categoria}))",
            ct);

        var ultima = await contexto.DocumentoInmueble
            .Where(d => d.InmuebleId == documento.InmuebleId && d.Categoria == documento.Categoria)
            .MaxAsync(d => (int?)d.Version, ct);

        documento.Version = (ultima ?? 0) + 1;
    }

    /// <summary>
    /// El nombre que envió el cliente, sin ruta y sin caracteres de control. Se guarda para
    /// mostrarlo y para nombrarlo en la descarga; no se usa nunca para tocar el disco.
    /// </summary>
    private static string NombreOriginal(string? nombreArchivo)
    {
        var nombre = Path.GetFileName(nombreArchivo ?? string.Empty);
        nombre = new string(nombre.Where(c => !char.IsControl(c)).ToArray()).Trim();

        return nombre.Length > 200 ? nombre[..200] : nombre;
    }

    private static DocumentoInmuebleRespuesta ARespuesta(DocumentoInmueble documento) =>
        new(
            documento.Id,
            documento.InmuebleId,
            documento.Categoria,
            documento.Version,
            documento.NombreOriginal,
            documento.TamanoBytes,
            documento.TipoContenido,
            documento.CreatedAt);

    private static object DatosAuditoria(DocumentoInmueble documento) => new
    {
        documento.InmuebleId,
        Categoria = documento.Categoria.ToString(),
        documento.Version,
        documento.NombreOriginal,
        documento.TamanoBytes,
    };
}