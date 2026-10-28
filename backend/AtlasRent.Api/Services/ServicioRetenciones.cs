using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Comprobantes de retención de las facturas de cliente agente de retención (F3-04,
/// docs/ESPECIFICACION.md §3.9, D14).
/// <para>
/// <b>El comprobante se crea al emitir, no cuando lo pida alguien.</b> Una factura que retiene y no
/// tiene comprobante es un papel que hay que perseguir; si la fila nace pendiente en la misma
/// transacción que la emisión, entonces el trabajo diario solo tiene que buscar lo pendiente y no lo
/// que se olvidó de crear. Y si la emisión se deshace, la fila tampoco existe.
/// </para>
/// <para>
/// <b>Recibir un comprobante no es validarlo.</b> La aplicación no habla con la DGII y §3.9 no pide
/// más, así que el número se guarda tal cual llega. Lo que sí hace es cerrar el aviso: mientras el
/// comprobante está pendiente hay una alerta viva, y en cuanto llega se resuelve (F2-09, BUG-008:
/// si vuelve a quedarse sin comprobante, la misma alerta vuelve a abrirse).
/// </para>
/// </summary>
public sealed class ServicioRetenciones(
    AtlasRentDbContext contexto,
    AuditoriaService auditoria,
    ServicioIdempotencia idempotencia,
    IPolicyService politica,
    IRegistroAlertas alertas,
    TimeProvider reloj)
{
    /// <summary>
    /// Tipo de alerta, el mismo que la tabla ya preveía y que ya usa el numerador de comprobante
    /// fiscal (F3-03): los dos avisos son del mismo asunto, un comprobante que hay que pedir.
    /// </summary>
    public const string TipoAlerta = "comprobante";

    /// <summary>
    /// Clave de la alerta de una factura que retiene y no ha recibido su comprobante.
    /// <para>
    /// Lleva la factura y no la fecha: el aviso no es «hoy falta el papel», es «falta el papel», y la
    /// fila se refresca cada día mientras siga en esa situación (§3.16).
    /// </para>
    /// </summary>
    public static string ClaveAlerta(Guid facturaId) => $"retencion:{facturaId:D}:sin-comprobante";

    /// <summary>
    /// Deja la fila del comprobante en el grafo de EF, sin guardarla, para que salga en el mismo
    /// <c>SaveChanges</c> y en la misma transacción que la emisión que la llama (F3-04, D28).
    /// <para>
    /// <b>Tipo, base, porcentaje e importe se copian aquí y no se releen nunca más.</b> El tipo y el
    /// porcentaje se leen del tipo configurado en el momento de emitir, igual que hizo F3-03 con el
    /// tipo del comprobante fiscal: si el administrador cambia el tipo mañana, el comprobante que ya
    /// se emitió sigue diciendo lo que se retuvo de verdad.
    /// </para>
    /// </summary>
    /// <param name="factura">Factura ya emitida, con sus líneas de retención cargadas.</param>
    /// <param name="usuario">Quien la emite, que es quien queda como creador del comprobante.</param>
    public async Task ReservarEnEmisionAsync(Factura factura, Guid usuario, CancellationToken ct)
    {
        if (factura.Retencion <= 0m)
        {
            return;
        }

        var comprobante = await ConstruirAsync(factura, usuario, ct);
        contexto.ComprobanteRetencion.Add(comprobante);
    }

    /// <summary>
    /// BUG-036: al modificar una factura emitida, el comprobante de retención pendiente se pone al día
    /// (tipo, base, porcentaje e importe). Si ya se recibió, el cliente entregó un papel con el importe
    /// anterior y cambiarlo debajo lo dejaría contradiciendo al documento: la modificación se rechaza.
    /// </summary>
    public async Task ActualizarEnModificacionAsync(Factura factura, Guid usuario, CancellationToken ct)
    {
        var comprobante = await contexto.ComprobanteRetencion
            .SingleOrDefaultAsync(c => c.FacturaId == factura.Id, ct);

        if (comprobante is null)
        {
            return;
        }

        var nuevo = await ConstruirAsync(factura, usuario, ct);

        if (comprobante.Tipo == nuevo.Tipo
            && comprobante.Base == nuevo.Base
            && comprobante.Porcentaje == nuevo.Porcentaje
            && comprobante.Importe == nuevo.Importe)
        {
            return;
        }

        if (comprobante.Estado != EstadoComprobanteRetencion.Pendiente)
        {
            throw new InvalidOperationException(
                $"La factura '{factura.Id:D}' tiene un comprobante de retención ya recibido y la "
                    + "modificación cambiaría la retención: anula la factura y emite otra.");
        }

        comprobante.Tipo = nuevo.Tipo;
        comprobante.Base = nuevo.Base;
        comprobante.Porcentaje = nuevo.Porcentaje;
        comprobante.Importe = nuevo.Importe;
        comprobante.UpdatedAt = reloj.GetUtcNow();
        comprobante.UpdatedBy = usuario;
    }

    private async Task<ComprobanteRetencion> ConstruirAsync(Factura factura, Guid usuario, CancellationToken ct)
    {
        var lineas = factura.Lineas
            .Where(l => l.Tipo == TipoLineaFactura.Retencion)
            .OrderBy(l => l.Posicion)
            .ToList();

        var ids = lineas.Select(l => l.TipoRetencionId).OfType<Guid>().ToArray();

        // El nombre del tipo sale de la configuración y no de la línea: la línea guarda la clave y
        // el porcentaje, no el texto, y el texto es justo lo que hay que imprimir en el papel.
        Dictionary<Guid, string> nombres = ids.Length == 0
            ? []
            : await contexto.TipoRetencion
                .AsNoTracking()
                .Where(t => ids.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Nombre, ct);

        // TODO(A#) §3.9 habla de "el comprobante de retención" en singular y no dice qué declara un
        // comprobante de una factura con dos retenciones de bases distintas. Se copia la base de la
        // primera línea y el porcentaje sale como suma de las aplicadas. Si en algún caso aparece, es
        // esta línea y las dos siguientes las que hay que cambiar, no el modelo entero.
        var tipo = lineas.Count == 0
            ? "Retención"
            : string.Join(", ", lineas.Select(l => l.TipoRetencionId is { } id && nombres.TryGetValue(id, out var nombre)
                ? nombre
                : "Retención"));

        return new ComprobanteRetencion
        {
            Id = Guid.NewGuid(),
            FacturaId = factura.Id,
            Tipo = tipo,
            Base = lineas.FirstOrDefault()?.BaseRetencion ?? BaseRetencion.Total,
            Porcentaje = Redondeo.Porcentaje(lineas.Sum(l => l.PorcentajeRetencion ?? 0m)),
            Importe = factura.Retencion,
            Estado = EstadoComprobanteRetencion.Pendiente,
            CreatedAt = reloj.GetUtcNow(),
            CreatedBy = usuario,
        };
    }

    /// <summary>
    /// Facturas de la consulta cuyo comprobante sigue pendiente, que es lo que enciende el indicador
    /// <c>retencionSinComprobante</c> del DTO de factura (§3.9).
    /// <para>
    /// Va como una consulta agregada sobre la página y no una por factura, por el mismo motivo que
    /// el cobrado de F3-06: una vista de cuentas de veinticinco filas no puede costar veintiséis
    /// viajes.
    /// </para>
    /// </summary>
    public async Task<IReadOnlySet<Guid>> SinComprobanteAsync(
        IReadOnlyCollection<Guid> facturaIds,
        CancellationToken ct)
    {
        if (facturaIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var pendientes = await contexto.ComprobanteRetencion
            .AsNoTracking()
            .Where(c => c.Estado == EstadoComprobanteRetencion.Pendiente
                && facturaIds.Contains(c.FacturaId))
            .Select(c => c.FacturaId)
            .ToListAsync(ct);

        return pendientes.ToHashSet();
    }

    /// <summary>
    /// Marca el comprobante como recibido con su número y su fecha, y resuelve el aviso que estaba
    /// vivo (§3.9, F2-09).
    /// <para>
    /// La resolución va <b>dentro de la misma transacción</b>: si la recepción se deshace, el aviso
    /// sigue vivo, porque un comprobante que no se guardó es un papel que no ha llegado.
    /// </para>
    /// <para>
    /// Solo gerente y administrador: es el mismo techo que emitir la factura
    /// (<see cref="Acciones.FacturaEmitir"/>), porque quien anota lo que el cliente entregó está
    /// haciendo el mismo tipo de gestión sobre un documento ya emitido.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, Func<IQueryable<ComprobanteRetencion>, bool, IOrderedQueryable<ComprobanteRetencion>>>
        Ordenes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["createdAt"] = (q, desc) => desc ? q.OrderByDescending(c => c.CreatedAt) : q.OrderBy(c => c.CreatedAt),
        };

    /// <summary>Lista de comprobantes, por factura y/o solo los pendientes de recibir. Lo más nuevo primero.</summary>
    public Task<Pagina<ComprobanteRetencionRespuesta>> ListarAsync(
        ConsultaLista paginacion,
        Guid? facturaId,
        bool? pendiente,
        CancellationToken ct)
    {
        var consulta = contexto.ComprobanteRetencion.AsNoTracking().AsQueryable();

        if (facturaId is { } factura)
        {
            consulta = consulta.Where(c => c.FacturaId == factura);
        }

        if (pendiente is { } soloPendientes)
        {
            consulta = soloPendientes
                ? consulta.Where(c => c.Estado == EstadoComprobanteRetencion.Pendiente)
                : consulta.Where(c => c.Estado != EstadoComprobanteRetencion.Pendiente);
        }

        // Orden por defecto descendente salvo que el cliente pida otro sentido.
        var pag = paginacion.SortBy is null && paginacion.SortDir == ConsultaLista.OrdenAscendente
            ? paginacion with { SortDir = ConsultaLista.OrdenDescendente }
            : paginacion;

        return ConsultaPaginada.PaginarAsync(consulta, Ordenes, "createdAt", pag, ARespuesta, ct);
    }

    public async Task<ComprobanteRetencionRespuesta> LeerAsync(Guid id, CancellationToken ct)
    {
        var comprobante = await contexto.ComprobanteRetencion.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException($"El comprobante de retención '{id:D}' no existe.");

        return ARespuesta(comprobante);
    }

    public async Task<ComprobanteRetencionRespuesta> RecibirAsync(
        Guid id,
        ComprobanteRetencionPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirAsync(Acciones.FacturaEmitir, $"comprobante-retencion/{id:D}", usuario, ct);

        var comprobante = await contexto.ComprobanteRetencion
            .SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException($"El comprobante de retención '{id:D}' no existe.");

        if (comprobante.Estado == EstadoComprobanteRetencion.Recibido)
        {
            throw new InvalidOperationException(
                $"El comprobante de retención '{id:D}' ya se había recibido con el número "
                    + $"'{comprobante.Numero}' el {comprobante.FechaRecepcion:yyyy-MM-dd}. No se "
                    + "reescribe lo que ya llegó: si el cliente entrega otro, la factura se anula y se "
                    + "emite otra (F3-08).");
        }

        var numero = peticion.Numero.Trim();

        comprobante.Estado = EstadoComprobanteRetencion.Recibido;
        comprobante.Numero = numero;
        comprobante.FechaRecepcion = peticion.FechaRecepcion;
        // El instante y el responsable los pone el reloj del servidor, no quien lo teclea: la fecha
        // del documento es la del papel, y esta es la de cuando la aplicación lo vio.
        comprobante.RecibidoAt = reloj.GetUtcNow();
        comprobante.RecibidoPor = usuario;

        var facturaId = comprobante.FacturaId;

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.ComprobanteRetencion, comprobante,
            esAlta: false,
            accion: "comprobante_retencion.recibido",
            entidad: "comprobante_retencion",
            usuario,
            ip,
            mutacion,
            proyectar: ARespuesta,
            datosAuditoria: c => new
            {
                Comprobante = c.Id.ToString("D"),
                Factura = c.FacturaId.ToString("D"),
                Numero = c.Numero,
                FechaRecepcion = c.FechaRecepcion?.ToString("yyyy-MM-dd"),
            },
            despuesDeGuardar: () => alertas.ResolverAsync(ClaveAlerta(facturaId), ct),
            ct: ct);
    }

    /// <summary>
    /// Segunda puerta del permiso, igual que en <c>ServicioFacturas</c>: el endpoint pide la acción
    /// con <c>[Accion]</c>, pero el atributo protege la entrada por HTTP y nada más.
    /// </summary>
    private async Task ExigirAsync(string accion, string recurso, Guid usuario, CancellationToken ct)
    {
        if (await politica.PuedeAsync(accion, usuario, ct))
        {
            return;
        }

        await politica.AuditarDenegadoAsync(accion, recurso, ct);

        throw new PermisoDenegadoException(accion, recurso);
    }

    private static ComprobanteRetencionRespuesta ARespuesta(ComprobanteRetencion c) => new(
        c.Id,
        c.FacturaId,
        c.Tipo,
        c.Base,
        c.Porcentaje,
        c.Importe,
        c.Estado,
        c.Numero,
        c.FechaRecepcion,
        c.RecibidoAt,
        c.RecibidoPor,
        c.CreatedAt);
}