using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Cotizaciones (docs/ESPECIFICACION.md §3.3, D18).
/// <list type="bullet">
/// <item>
/// Los estados son <c>Borrador → Enviada → Aceptada / Rechazada</c> y no hay ningún otro camino: una
/// transición que no esté en la tabla se rechaza con 409.
/// </item>
/// <item>
/// Solo se edita en borrador. Enviada, Aceptada y Rechazada quedan congeladas, que es lo mismo que
/// hace el resto del sistema con lo que ya salió.
/// </item>
/// <item>
/// El importe de cada línea y el total los calcula aquí, con el redondeo medio hacia arriba de
/// <see cref="Redondeo"/>, nunca la base de datos.
/// </item>
/// <item>
/// Una cotización lleva una sola moneda y no lleva ITBIS ni retenciones: la especificación solo las
/// pide en facturas y proformas (§3.8). Con eso, el subtotal y el total son la misma suma.
/// </item>
/// </list>
/// <para>
/// Nada se borra: la baja es lógica, porque una cotización aceptada es la origem de una reserva o de
/// un contrato.
/// </para>
/// </summary>
public sealed class ServicioCotizaciones(
    AtlasRentDbContext contexto,
    AuditoriaService auditoria,
    ServicioIdempotencia idempotencia,
    ServicioCifrado cifrado,
    TimeProvider reloj)
{
    /// <summary>
    /// Los únicos cambios de estado permitidos (D18). Lo que no esté en esta tabla se rechaza: el
    /// flujo es <c>Borrador → Enviada → Aceptada / Rechazada</c> y desde Aceptada o Rechazada no se
    /// vuelve atrás.
    /// </summary>
    private static readonly Dictionary<EstadoCotizacion, EstadoCotizacion[]> Transiciones = new()
    {
        [EstadoCotizacion.Borrador] = [EstadoCotizacion.Enviada],
        [EstadoCotizacion.Enviada] = [EstadoCotizacion.Aceptada, EstadoCotizacion.Rechazada],
        [EstadoCotizacion.Aceptada] = [],
        [EstadoCotizacion.Rechazada] = [],
    };

    private static readonly Dictionary<string, Func<IQueryable<Cotizacion>, bool, IOrderedQueryable<Cotizacion>>>
        Ordenes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["createdAt"] = (q, desc) => desc ? q.OrderByDescending(c => c.CreatedAt) : q.OrderBy(c => c.CreatedAt),
            ["estado"] = (q, desc) => desc ? q.OrderByDescending(c => c.Estado) : q.OrderBy(c => c.Estado),
            ["total"] = (q, desc) => desc ? q.OrderByDescending(c => c.Total) : q.OrderBy(c => c.Total),
        };

    public Task<Pagina<CotizacionResumen>> ListarAsync(
        ConsultaLista paginacion,
        string? busqueda,
        EstadoCotizacion? estado,
        bool incluirInactivas,
        CancellationToken ct)
    {
        IQueryable<Cotizacion> consulta = contexto.Cotizacion.AsNoTracking()
            .Include(c => c.Cliente)
            .Include(c => c.Prospecto)
            .Include(c => c.Unidad)
            .Include(c => c.Lineas);

        if (!incluirInactivas)
        {
            consulta = consulta.Where(c => c.Activo);
        }

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            var termino = busqueda.Trim().ToLowerInvariant();
            consulta = consulta.Where(c =>
                (c.Cliente != null && c.Cliente.Nombre.ToLower().Contains(termino)) ||
                (c.Prospecto != null && c.Prospecto.Nombre.ToLower().Contains(termino)) ||
                (c.Unidad != null && c.Unidad.Nombre.ToLower().Contains(termino)));
        }

        if (estado is { } filtro)
        {
            consulta = consulta.Where(c => c.Estado == filtro);
        }

        return ConsultaPaginada.PaginarAsync(
            consulta, Ordenes, "createdAt", paginacion, AResumen, ct);
    }

    public async Task<CotizacionRespuesta> LeerAsync(Guid id, CancellationToken ct)
    {
        var cotizacion = await CargarAsync(id, ct);

        return new CotizacionRespuesta(
            cotizacion.Id,
            cotizacion.ClienteId,
            cotizacion.Cliente?.Nombre,
            cotizacion.ProspectoId,
            cotizacion.Prospecto?.Nombre,
            cotizacion.UnidadId,
            cotizacion.Unidad?.Nombre,
            cotizacion.Estado,
            cotizacion.Moneda,
            cotizacion.Total,
            cotizacion.Total,
            cotizacion.Activo,
            cotizacion.CreatedAt,
            [.. cotizacion.Lineas.OrderBy(l => l.Posicion).Select(ALinea)]);
    }

    public async Task<CotizacionResumen> CrearAsync(
        CotizacionPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        Validar(peticion);
        await ExigirDestinatarioExistenteAsync(peticion, ct);

        var cotizacion = new Cotizacion
        {
            // Una cotización siempre nace en borrador: el primer botón del flujo es "enviar", y ese
            // es el que exige cotizacion.enviar.
            Estado = EstadoCotizacion.Borrador,
            Activo = true,
        };
        Aplicar(cotizacion, peticion);

        var lineas = ConstruirLineas(peticion);

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Cotizacion, cotizacion,
            esAlta: true,
            accion: "cotizacion.creada",
            entidad: "cotizacion",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: c => DatosAuditoria(c, lineas.Count),
            antesDeGuardar: () =>
            {
                AnadirLineas(cotizacion, lineas, usuario);
                return Task.CompletedTask;
            },
            ct: ct);
    }

    public async Task<CotizacionResumen> ActualizarAsync(
        Guid id,
        CotizacionPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var cotizacion = await CargarAsync(id, ct);
        ExigirBorrador(cotizacion);

        Validar(peticion);
        await ExigirDestinatarioExistenteAsync(peticion, ct);

        var anteriores = cotizacion.Lineas.ToList();
        Aplicar(cotizacion, peticion);
        var lineas = ConstruirLineas(peticion);

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Cotizacion, cotizacion,
            esAlta: false,
            accion: "cotizacion.actualizada",
            entidad: "cotizacion",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: c => DatosAuditoria(c, lineas.Count),
            antesDeGuardar: () => ReemplazarLineasAsync(cotizacion, anteriores, lineas, usuario, ct),
            ct: ct);
    }

    /// <summary>
    /// Baja lógica. Solo mientras está en borrador: lo que ya se envió a un cliente no se retira, y una
    /// cotización aceptada es el origen de la reserva o del contrato.
    /// </summary>
    public async Task DesactivarAsync(
        Guid id,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var cotizacion = await CargarAsync(id, ct);
        ExigirBorrador(cotizacion);
        cotizacion.Activo = false;

        await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Cotizacion, cotizacion,
            esAlta: false,
            accion: "cotizacion.desactivada",
            entidad: "cotizacion",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: c => DatosAuditoria(c, c.Lineas.Count),
            ct: ct);
    }

    /// <summary>
    /// Mueve la cotización por el flujo <c>Borrador → Enviada → Aceptada / Rechazada</c>.
    /// <para>
    /// DECISIÓN DEL ORQUESTADOR: este cambio de estado lo exige <c>cotizacion.enviar</c>, la misma
    /// acción que protege el envío. La matriz de docs/ESPECIFICACION.md §2 no tiene una fila propia
    /// para "aceptar una cotización" y la más cercana es "Enviar cotizaciones"; además D15 la hace
    /// llegar desactivada al empleado, así que sin activar el permiso a mano el empleado no acepta
    /// cotizaciones. Si más adelante el negocio quiere que el empleado acepte, es una fila nueva
    /// en la matriz y un permiso nuevo, no un cambio aquí.
    /// </para>
    /// </summary>
    public async Task<CotizacionResumen> CambiarEstadoAsync(
        Guid id,
        EstadoCotizacion destino,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var cotizacion = await CargarAsync(id, ct);

        if (!Transiciones[cotizacion.Estado].Contains(destino))
        {
            throw new InvalidOperationException(
                $"Una cotización en estado {Texto(cotizacion.Estado)} no puede pasar a " +
                $"{Texto(destino)}. El flujo es borrador → enviada → aceptada o rechazada.");
        }

        cotizacion.Estado = destino;

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Cotizacion, cotizacion,
            esAlta: false,
            accion: $"cotizacion.{Texto(destino)}",
            entidad: "cotizacion",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: c => DatosAuditoria(c, c.Lineas.Count),
            ct: ct);
    }

    /// <summary>
    /// Datos copiados para el formulario de reserva (F2-03). No crea nada: la especificación (§3.3)
    /// dice que el botón abre el formulario con los datos ya puestos y que es la persona quien
    /// confirma.
    /// </summary>
    public Task<CotizacionTransferenciaRespuesta> ParaReservaAsync(Guid id, CancellationToken ct) =>
        ParaTransferenciaAsync(id, ct);

    /// <summary>
    /// Datos copiados para el formulario de contrato (F2-05). Tampoco crea nada, por lo mismo que
    /// <see cref="ParaReservaAsync"/>.
    /// </summary>
    public Task<CotizacionTransferenciaRespuesta> ParaContratoAsync(Guid id, CancellationToken ct) =>
        ParaTransferenciaAsync(id, ct);

    /// <summary>Genera el PDF de la cotización (docs/DESIGN.md §7).</summary>
    public async Task<byte[]> PdfAsync(Guid id, CancellationToken ct)
    {
        var cotizacion = await CargarAsync(id, ct);

        return DocumentoCotizacionPdf.Generar(
            cotizacion,
            Descifrar(cotizacion.Cliente?.Documento),
            Descifrar(cotizacion.Prospecto?.Documento));
    }

    // ---------------------------------------------------------------- internos

    /// <summary>El documento de identidad se descifra para el PDF y solo para el PDF (D16).</summary>
    private string? Descifrar(string? documentoCifrado) =>
        documentoCifrado is null ? null : cifrado.Descifrar(documentoCifrado);

    private async Task<CotizacionTransferenciaRespuesta> ParaTransferenciaAsync(Guid id, CancellationToken ct)
    {
        var cotizacion = await CargarAsync(id, ct);

        if (cotizacion.Estado != EstadoCotizacion.Aceptada)
        {
            throw new InvalidOperationException(
                "Solo una cotización aceptada se puede copiar a una reserva o a un contrato. " +
                $"Esta está en estado {Texto(cotizacion.Estado)}.");
        }

        return new CotizacionTransferenciaRespuesta(
            cotizacion.Id,
            cotizacion.ClienteId,
            cotizacion.Cliente?.Nombre,
            cotizacion.ProspectoId,
            cotizacion.Prospecto?.Nombre,
            cotizacion.UnidadId,
            cotizacion.Unidad?.Nombre,
            cotizacion.Moneda,
            cotizacion.Total,
            [.. cotizacion.Lineas.OrderBy(l => l.Posicion).Select(ALinea)]);
    }

    /// <summary>
    /// Las reglas del documento, antes de tocar la base de datos. Todas devuelven 409, como las del
    /// resto de servicios: no son un error de formato sino un documento que no se puede guardar.
    /// </summary>
    private static void Validar(CotizacionPeticion peticion)
    {
        if (peticion.ClienteId is null && peticion.ProspectoId is null)
        {
            throw new InvalidOperationException(
                "La cotización necesita un cliente o un prospecto: se cotiza a alguien, no en el vacío.");
        }

        if (peticion.ClienteId is not null && peticion.ProspectoId is not null)
        {
            throw new InvalidOperationException(
                "La cotización es para un cliente o para un prospecto, no para los dos.");
        }

        // El enum llega del JSON: si el cliente manda un número que no existe, se rechaza aquí.
        if (peticion.Moneda is not (Moneda.USD or Moneda.DOP))
        {
            throw new InvalidOperationException(
                $"La moneda '{peticion.Moneda}' no existe. Las cotizaciones van en USD o en DOP.");
        }

        if (peticion.Lineas.Count == 0)
        {
            throw new InvalidOperationException("La cotización necesita al menos una línea.");
        }

        foreach (var linea in peticion.Lineas)
        {
            if (string.IsNullOrWhiteSpace(linea.Descripcion))
            {
                throw new InvalidOperationException("Toda línea necesita decir qué se ofrece.");
            }

            if (linea.Cantidad < 0)
            {
                throw new InvalidOperationException("La cantidad de una línea no puede ser negativa.");
            }

            if (linea.PrecioUnitario < 0)
            {
                throw new InvalidOperationException("El precio unitario de una línea no puede ser negativo.");
            }
        }
    }

    private async Task ExigirDestinatarioExistenteAsync(CotizacionPeticion peticion, CancellationToken ct)
    {
        if (peticion.ClienteId is { } clienteId &&
            !await contexto.Cliente.AnyAsync(c => c.Id == clienteId, ct))
        {
            throw new KeyNotFoundException($"No existe el cliente '{clienteId:D}'.");
        }

        if (peticion.ProspectoId is { } prospectoId &&
            !await contexto.Prospecto.AnyAsync(p => p.Id == prospectoId, ct))
        {
            throw new KeyNotFoundException($"No existe el prospecto '{prospectoId:D}'.");
        }

        if (peticion.UnidadId is { } unidadId &&
            !await contexto.Unidad.AnyAsync(u => u.Id == unidadId, ct))
        {
            throw new KeyNotFoundException($"No existe la unidad '{unidadId:D}'.");
        }
    }

    private static void ExigirBorrador(Cotizacion cotizacion)
    {
        if (cotizacion.Estado != EstadoCotizacion.Borrador)
        {
            throw new InvalidOperationException(
                $"Una cotización en estado {Texto(cotizacion.Estado)} ya no se cambia. " +
                "Se edita una nueva.");
        }
    }

    private void Aplicar(Cotizacion cotizacion, CotizacionPeticion peticion)
    {
        cotizacion.ClienteId = peticion.ClienteId;
        cotizacion.ProspectoId = peticion.ProspectoId;
        cotizacion.UnidadId = peticion.UnidadId;
        cotizacion.Moneda = peticion.Moneda;
        cotizacion.Total = Redondeo.Importe(peticion.Lineas.Sum(l => l.Cantidad * l.PrecioUnitario));
    }

    /// <summary>
    /// Las líneas del documento con su importe ya redondeado. <c>decimal</c> multiplica exacto, así
    /// que el total guardado y la suma de las líneas siempre dan lo mismo. La clave foránea se pone
    /// al añadirlas, cuando la cotización ya tiene <c>Id</c>.
    /// </summary>
    private static List<CotizacionLinea> ConstruirLineas(CotizacionPeticion peticion)
    {
        var lineas = new List<CotizacionLinea>(peticion.Lineas.Count);
        var posicion = 1;

        foreach (var linea in peticion.Lineas)
        {
            var cantidad = Redondeo.Importe(linea.Cantidad);
            var precio = Redondeo.Importe(linea.PrecioUnitario);

            lineas.Add(new CotizacionLinea
            {
                Id = Guid.NewGuid(),
                Posicion = posicion++,
                Descripcion = linea.Descripcion.Trim(),
                Cantidad = cantidad,
                PrecioUnitario = precio,
                Importe = Redondeo.Importe(cantidad * precio),
            });
        }

        return lineas;
    }

    /// <summary>
    /// Las líneas se añaden por el conjunto y no por la navegación de la cotización, para no tocar la
    /// navegación de una entidad que <see cref="GuardadoAuditado"/> está guardando.
    /// </summary>
    private void AnadirLineas(Cotizacion cotizacion, List<CotizacionLinea> lineas, Guid usuario)
    {
        var ahora = reloj.GetUtcNow();

        foreach (var linea in lineas)
        {
            linea.CotizacionId = cotizacion.Id;
            linea.CreatedAt = ahora;
            linea.CreatedBy = usuario;
        }

        contexto.CotizacionLinea.AddRange(lineas);
    }

    /// <summary>
    /// Cambia el juego entero de líneas. El borrado se confirma antes de insertar las nuevas porque
    /// <c>(cotizacion_id, posicion)</c> es único y el juego nuevo reutiliza las mismas posiciones:
    /// borrando e insertando en el mismo <c>SaveChanges</c>, el orden de los comandos no está
    /// garantizado. Todo sigue dentro de la transacción de <see cref="GuardadoAuditado"/>.
    /// </summary>
    private async Task ReemplazarLineasAsync(
        Cotizacion cotizacion,
        List<CotizacionLinea> anteriores,
        List<CotizacionLinea> nuevas,
        Guid usuario,
        CancellationToken ct)
    {
        if (anteriores.Count > 0)
        {
            contexto.CotizacionLinea.RemoveRange(anteriores);
            await contexto.SaveChangesAsync(ct);
        }

        AnadirLineas(cotizacion, nuevas, usuario);
    }

    private static CotizacionResumen AResumen(Cotizacion cotizacion) =>
        new(
            cotizacion.Id,
            cotizacion.ClienteId,
            cotizacion.Cliente?.Nombre,
            cotizacion.ProspectoId,
            cotizacion.Prospecto?.Nombre,
            cotizacion.UnidadId,
            cotizacion.Unidad?.Nombre,
            cotizacion.Estado,
            cotizacion.Moneda,
            cotizacion.Total,
            cotizacion.Total,
            cotizacion.Lineas.Count,
            cotizacion.Activo,
            cotizacion.CreatedAt);

    private static CotizacionLineaRespuesta ALinea(CotizacionLinea linea) =>
        new(linea.Posicion, linea.Descripcion, linea.Cantidad, linea.PrecioUnitario, linea.Importe);

    private static object DatosAuditoria(Cotizacion cotizacion, int lineas) => new
    {
        cotizacion.ClienteId,
        cotizacion.ProspectoId,
        cotizacion.UnidadId,
        Moneda = cotizacion.Moneda.ToString(),
        Estado = cotizacion.Estado.ToString(),
        cotizacion.Total,
        Lineas = lineas,
        cotizacion.Activo,
    };

    private async Task<Cotizacion> CargarAsync(Guid id, CancellationToken ct) =>
        await contexto.Cotizacion
            .Include(c => c.Cliente)
            .Include(c => c.Prospecto)
            .Include(c => c.Unidad)
            .Include(c => c.Lineas)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
        ?? throw new KeyNotFoundException($"No existe la cotización '{id:D}'.");

    /// <summary>El nombre del estado para los mensajes de la API, en el idioma de quien la lee.</summary>
    private static string Texto(EstadoCotizacion estado) => estado switch
    {
        EstadoCotizacion.Borrador => "borrador",
        EstadoCotizacion.Enviada => "enviada",
        EstadoCotizacion.Aceptada => "aceptada",
        EstadoCotizacion.Rechazada => "rechazada",
        _ => estado.ToString(),
    };
}