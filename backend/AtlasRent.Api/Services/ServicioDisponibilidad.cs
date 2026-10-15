using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AtlasRent.Api.Services;

/// <summary>
/// Escribe y libera los bloqueos de disponibilidad de una unidad (F2-01, docs/ESPECIFICACION.md
/// §3.4): rangos para contratos y reservas mensuales, días sueltos para reservas cortas.
/// <para>
/// Es una pieza de llamador, no una puerta: el servicio de reservas (F2-03) y el de contratos
/// (F2-05) lo llaman desde su propia transacción, que es la que guarda a la vez la fila de negocio
/// y estos bloqueos. Por eso este servicio <strong>no abre transacción ni escribe auditoría</strong>:
/// abrirla aquí partiría la unidad de su reserva, y la auditoría la pone quien llama una sola vez
/// por la mutación (AGENTS.md §6).
/// </para>
/// <para>
/// La comprobación de verdad no está en este código sino en la base de datos: la restricción de
/// exclusión de <c>ocupacion_rango</c> y el trigger de capacidad de <c>ocupacion_dia</c>. Aquí solo
/// se traduce el fallo a un error que el controlador sabe convertir en un 409.
/// </para>
/// </summary>
public sealed class ServicioDisponibilidad(AtlasRentDbContext contexto, TimeProvider reloj)
{
    /// <summary>
    /// SQLSTATE de los dos triggers de ocupación: la unidad está bloqueada por un rango, o el día ya
    /// está completo o con sus dos medios días. Lo pone la migración <c>DisponibilidadOcupacion</c>.
    /// </summary>
    public const string SqlstateOcupacion = "23001";

    /// <summary>SQLSTATE de la restricción de exclusión de <c>ocupacion_rango</c>.</summary>
    public const string SqlstateSolapamiento = "23P01";

    /// <summary>
    /// Bloquea la unidad en un rango por un contrato o una reserva mensual.
    /// <para>
    /// Reintentar la misma fila de origen no duplica el bloqueo: lo impide el índice único
    /// (origen, origen_id).
    /// </para>
    /// </summary>
    public async Task OcuparRangoAsync(
        Guid unidadId,
        OrigenOcupacion origen,
        Guid origenId,
        DateOnly fechaInicio,
        DateOnly? fechaFin,
        CancellationToken ct)
    {
        contexto.OcupacionRango.Add(new OcupacionRango
        {
            Id = Guid.NewGuid(),
            UnidadId = unidadId,
            Origen = origen,
            OrigenId = origenId,
            FechaInicio = fechaInicio,
            FechaFin = fechaFin,
            Bloquea = true,
            CreadoAt = reloj.GetUtcNow(),
        });

        await GuardarAsync(ct);
    }

    /// <summary>
    /// Libera el rango de una fila de origen. No borra la fila: la deja con <c>bloquea = false</c>,
    /// porque los registros con efecto económico no se borran (AGENTS.md §6).
    /// </summary>
    public async Task LiberarRangoAsync(OrigenOcupacion origen, Guid origenId, CancellationToken ct)
    {
        var filas = await contexto.OcupacionRango
            .Where(r => r.Origen == origen && r.OrigenId == origenId && r.Bloquea)
            .ToListAsync(ct);

        foreach (var fila in filas)
        {
            fila.Bloquea = false;
        }

        await GuardarAsync(ct);
    }

    /// <summary>
    /// Bloquea días sueltos de una reserva corta o de un mantenimiento. Se insertan todos juntos: si
    /// uno de los días no cabe, no se guarda ninguno, porque van en la misma transacción.
    /// </summary>
    public async Task OcuparDiasAsync(
        Guid unidadId,
        OrigenOcupacion origen,
        Guid origenId,
        IReadOnlyList<(DateOnly Fecha, TipoOcupacionDia Tipo)> dias,
        CancellationToken ct)
    {
        var creado = reloj.GetUtcNow();

        foreach (var (fecha, tipo) in dias)
        {
            contexto.OcupacionDia.Add(new OcupacionDia
            {
                Id = Guid.NewGuid(),
                UnidadId = unidadId,
                Origen = origen,
                OrigenId = origenId,
                Fecha = fecha,
                Tipo = tipo,
                Bloquea = true,
                CreadoAt = creado,
            });
        }

        await GuardarAsync(ct);
    }

    /// <summary>Libera los días de una fila de origen, sin borrarlos.</summary>
    public async Task LiberarDiasAsync(OrigenOcupacion origen, Guid origenId, CancellationToken ct)
    {
        var filas = await contexto.OcupacionDia
            .Where(d => d.Origen == origen && d.OrigenId == origenId && d.Bloquea)
            .ToListAsync(ct);

        foreach (var fila in filas)
        {
            fila.Bloquea = false;
        }

        await GuardarAsync(ct);
    }

    /// <summary>
    /// Deja la ocupación de una fila de origen exactamente en el estado pedido, en un solo
    /// <c>SaveChanges</c> y sin borrar ni duplicar filas.
    /// <para>
    /// Existe porque <see cref="OcuparRangoAsync"/> y <see cref="LiberarRangoAsync"/> no sirven para
    /// <i>editar</i> el bloqueo: liberar y volver a ocupar el mismo día choca con el índice único
    /// (origen, origen_id, fecha) y deja un rango viejo bloqueando la unidad para siempre. Aquí se
    /// hace la diferencia: lo que sigue se deja como estaba, lo que se va se libera y lo que es
    /// nuevo se inserta.
    /// </para>
    /// <para>
    /// Un <c>null</c> en cada lado significa "no hay nada de esto": un <c>null</c> en
    /// <paramref name="rangoDesde"/> libera el rango de esa fila de origen (y por eso también sirve
    /// para cambiar una reserva de mensual a corta), y un <c>null</c> en <paramref name="dias"/>
    /// libera sus días.
    /// </para>
    /// </summary>
    public async Task SincronizarAsync(
        OrigenOcupacion origen,
        Guid origenId,
        Guid unidadId,
        DateOnly? rangoDesde,
        IReadOnlyList<(DateOnly Fecha, TipoOcupacionDia Tipo)>? dias,
        CancellationToken ct)
    {
        var rango = await contexto.OcupacionRango
            .SingleOrDefaultAsync(r => r.Origen == origen && r.OrigenId == origenId, ct);

        if (rangoDesde is { } desde)
        {
            if (rango is null)
            {
                contexto.OcupacionRango.Add(new OcupacionRango
                {
                    Id = Guid.NewGuid(),
                    UnidadId = unidadId,
                    Origen = origen,
                    OrigenId = origenId,
                    FechaInicio = desde,
                    FechaFin = null,
                    Bloquea = true,
                    CreadoAt = reloj.GetUtcNow(),
                });
            }
            else
            {
                rango.UnidadId = unidadId;
                rango.FechaInicio = desde;
                rango.Bloquea = true;
            }
        }
        else if (rango is { Bloquea: true })
        {
            rango.Bloquea = false;
        }

        var existentes = await contexto.OcupacionDia
            .Where(d => d.Origen == origen && d.OrigenId == origenId)
            .ToListAsync(ct);

        // Las fechas del llamante llegan sin repetir: una reserva no lleva dos franjas el mismo día
        // (lo rechaza el servicio de reservas), que es justo lo que hace único el índice
        // (origen, origen_id, fecha).
        var pedidas = dias?.ToDictionary(d => d.Fecha, d => d.Tipo) ?? [];

        foreach (var existente in existentes.Where(d => !pedidas.ContainsKey(d.Fecha)))
        {
            existente.Bloquea = false;
        }

        foreach (var (fecha, tipo) in pedidas)
        {
            var fila = existentes.SingleOrDefault(d => d.Fecha == fecha);

            if (fila is null)
            {
                contexto.OcupacionDia.Add(new OcupacionDia
                {
                    Id = Guid.NewGuid(),
                    UnidadId = unidadId,
                    Origen = origen,
                    OrigenId = origenId,
                    Fecha = fecha,
                    Tipo = tipo,
                    Bloquea = true,
                    CreadoAt = reloj.GetUtcNow(),
                });
            }
            else
            {
                fila.UnidadId = unidadId;
                fila.Tipo = tipo;
                fila.Bloquea = true;
            }
        }

        await GuardarAsync(ct);
    }

    /// <summary>
    /// Si la unidad está libre en todo el rango <c>[desde, hasta)</c>. Es una consulta para pintar el
    /// calendario; la garantía de que el alta luego sea posible la dan los bloqueos de la base, no
    /// esta respuesta.
    /// </summary>
    public async Task<bool> EstaLibreAsync(
        Guid unidadId,
        DateOnly desde,
        DateOnly hasta,
        CancellationToken ct)
    {
        // Solapamiento de [inicio, fin): choca si el rango empieza antes de que acabe lo pedido y
        // acaba después de que empiece. Un fecha_fin null está abierto, así que nunca acaba.
        var hayRango = await contexto.OcupacionRango.AnyAsync(
            r => r.Bloquea
                && r.UnidadId == unidadId
                && (r.FechaFin == null || r.FechaFin > desde)
                && r.FechaInicio < hasta,
            ct);

        if (hayRango)
        {
            return false;
        }

        var hayDia = await contexto.OcupacionDia.AnyAsync(
            d => d.Bloquea
                && d.UnidadId == unidadId
                && d.Fecha >= desde
                && d.Fecha < hasta,
            ct);

        return !hayDia;
    }

    /// <summary>
    /// Traduce la violación de la base a un error de dominio. Los dos SQLSTATE que llegan aquí
    /// significan lo mismo para quien llama: esa unidad no está libre, y el controlador lo responde
    /// con <see cref="Problemas.UnidadNoDisponible"/>.
    /// </summary>
    private async Task GuardarAsync(CancellationToken ct)
    {
        try
        {
            await contexto.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e)
            when (e.InnerException is PostgresException pg
                && pg.SqlState is SqlstateOcupacion or SqlstateSolapamiento)
        {
            // Se guarda el PostgresException, no el DbUpdateException: es el que lleva el SQLSTATE
            // y el mensaje de la base, que es lo que sirve para el registro cuando algo falla.
            throw new InvalidOperationException("La unidad no está disponible para esas fechas.", pg);
        }
    }
}