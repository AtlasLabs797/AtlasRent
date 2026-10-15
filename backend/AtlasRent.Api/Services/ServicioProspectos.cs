using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Prospectos (docs/ESPECIFICACION.md §3.2): gente a la que se le quiere cotizar y que todavía no
/// es cliente. El empleado los ve y los gestiona (<c>cliente.ver</c> y <c>cliente.editar</c>).
/// <para>
/// El documento se cifra y se indexa igual que en el cliente. La tabla es aparte a propósito: un
/// prospecto no tiene número de cliente, se lo dan el día que se convierte y ese número no se
/// reutiliza después.
/// </para>
/// <para>
/// TODO(A#): convertir un prospecto en cliente. La especificación §3.2 dice que al pasarse recibe
/// el número de cliente, y ese número tiene que salir de la misma secuencia para no repetirse. Es
/// trabajo de la tarea de cotizaciones, que es la que sabe qué ocurre con el prospecto cuando deja
/// de serlo.
/// </para>
/// </summary>
public sealed class ServicioProspectos(
    AtlasRentDbContext contexto,
    AuditoriaService auditoria,
    ServicioIdempotencia idempotencia,
    ServicioCifrado cifrado,
    IndiceCiegoDocumento indice,
    TimeProvider reloj)
{
    private static readonly Dictionary<string, Func<IQueryable<Prospecto>, bool, IOrderedQueryable<Prospecto>>>
        Ordenes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["nombre"] = (q, desc) => desc ? q.OrderByDescending(p => p.Nombre) : q.OrderBy(p => p.Nombre),
            ["tipo"] = (q, desc) => desc ? q.OrderByDescending(p => p.Tipo) : q.OrderBy(p => p.Tipo),
            ["createdAt"] = (q, desc) => desc ? q.OrderByDescending(p => p.CreatedAt) : q.OrderBy(p => p.CreatedAt),
        };

    public Task<Pagina<ProspectoResumen>> ListarAsync(
        ConsultaLista paginacion,
        string? busqueda,
        CancellationToken ct)
    {
        var consulta = contexto.Prospecto.AsNoTracking().Where(p => p.Activo);

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            var termino = busqueda.Trim().ToLowerInvariant();
            consulta = consulta.Where(p => p.Nombre.ToLower().Contains(termino));
        }

        return ConsultaPaginada.PaginarAsync(
            consulta, Ordenes, "nombre", paginacion, AResumen, ct);
    }

    /// <summary>Ficha del prospecto con el documento descifrado (decisión D16).</summary>
    public async Task<ProspectoRespuesta> LeerAsync(Guid id, CancellationToken ct)
    {
        var prospecto = await CargarAsync(id, ct);

        return new ProspectoRespuesta(
            prospecto.Id,
            prospecto.Nombre,
            prospecto.Tipo,
            prospecto.Documento is null ? null : cifrado.Descifrar(prospecto.Documento),
            prospecto.Email,
            prospecto.Telefono,
            prospecto.Notas,
            prospecto.Activo,
            prospecto.CreatedAt);
    }

    public async Task<ProspectoResumen> CrearAsync(
        ProspectoPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var huella = indice.Huella(peticion.Documento);
        await ExigirDocumentoLibreAsync(huella, null, ct);

        var prospecto = new Prospecto();
        Aplicar(prospecto, peticion, huella);
        prospecto.Activo = true;

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Prospecto, prospecto,
            esAlta: true,
            accion: "prospecto.creado",
            entidad: "prospecto",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: DatosAuditoria,
            ct: ct);
    }

    public async Task<ProspectoResumen> ActualizarAsync(
        Guid id,
        ProspectoPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var prospecto = await CargarAsync(id, ct);

        var huella = indice.Huella(peticion.Documento);
        await ExigirDocumentoLibreAsync(huella, id, ct);

        Aplicar(prospecto, peticion, huella);

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Prospecto, prospecto,
            esAlta: false,
            accion: "prospecto.actualizado",
            entidad: "prospecto",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: DatosAuditoria,
            ct: ct);
    }

    /// <summary>Baja lógica: un prospecto que ya se convirtió no se borra, porque tiene cotizaciones.</summary>
    public async Task DesactivarAsync(
        Guid id,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var prospecto = await CargarAsync(id, ct);
        prospecto.Activo = false;

        await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Prospecto, prospecto,
            esAlta: false,
            accion: "prospecto.desactivado",
            entidad: "prospecto",
            usuario,
            ip,
            mutacion,
            proyectar: p => AResumen(p),
            datosAuditoria: DatosAuditoria,
            ct: ct);
    }

    // ---------------------------------------------------------------- internos

    private static ProspectoResumen AResumen(Prospecto prospecto) =>
        new(
            prospecto.Id,
            prospecto.Nombre,
            prospecto.Tipo,
            prospecto.Email,
            prospecto.Telefono,
            prospecto.Activo,
            prospecto.CreatedAt);

    /// <summary>Lo que sí puede ir a la auditoría: el documento nunca, ni cifrado ni en claro.</summary>
    private static object DatosAuditoria(Prospecto prospecto) => new
    {
        prospecto.Nombre,
        Tipo = prospecto.Tipo.ToString(),
        prospecto.Activo,
    };

    private async Task<Prospecto> CargarAsync(Guid id, CancellationToken ct) =>
        await contexto.Prospecto.FirstOrDefaultAsync(p => p.Id == id, ct)
        ?? throw new KeyNotFoundException($"No existe el prospecto '{id:D}'.");

    private void Aplicar(Prospecto prospecto, ProspectoPeticion peticion, string? huella)
    {
        prospecto.Nombre = peticion.Nombre.Trim();
        prospecto.Tipo = peticion.Tipo;
        prospecto.Documento = string.IsNullOrWhiteSpace(peticion.Documento)
            ? null
            : cifrado.Cifrar(peticion.Documento.Trim());
        prospecto.DocumentoHmac = huella;
        prospecto.Email = Limpiar(peticion.Email);
        prospecto.Telefono = Limpiar(peticion.Telefono);
        prospecto.Notas = Limpiar(peticion.Notas);
    }

    private static string? Limpiar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private async Task ExigirDocumentoLibreAsync(string? huella, Guid? idActual, CancellationToken ct)
    {
        if (huella is null)
        {
            return;
        }

        var consulta = contexto.Prospecto.Where(p => p.DocumentoHmac == huella);
        if (idActual is { } id)
        {
            consulta = consulta.Where(p => p.Id != id);
        }

        if (await consulta.AnyAsync(ct))
        {
            throw new InvalidOperationException(
                "Ya hay un prospecto con ese documento de identidad.");
        }
    }
}