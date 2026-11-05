using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Calendario de disponibilidad (docs/ESPECIFICACION.md §3.1, "Calendario de disponibilidad por
/// unidad e inmueble", F2-04).
/// <para>
/// <strong>Es una consulta y solo lee.</strong> No escribe nada, no abre transacción, no guarda
/// auditoría y no cachea el resultado: un calendario guardado en memoria o en una tabla volvería a
/// convertir la disponibilidad en un campo "ocupado", que es justo lo que la especificación
/// prohíbe (docs/ESPECIFICACION.md §3.1, AGENTS.md §6).
/// </para>
/// <para>
/// <strong>La fuente de verdad son <c>ocupacion_rango</c> y <c>ocupacion_dia</c></strong> (F2-01),
/// filtradas por <c>bloquea = true</c> para que una ocupación liberada no ocupe. Este servicio no lee
/// <c>contrato</c> ni <c>reserva</c> para decidir qué día está tomado: si lo hiciera, bastaría un
/// hueco entre dos documentos para que saliera una ocupación fantasma en pantalla, que es la carrera
/// que F2-01 cerró moviendo la comprobación a la base de datos.
/// </para>
/// <para>
/// El estado manual de la unidad (<c>unidad.estado_manual</c>) también entra, y por su propio
/// camino: <c>disponible</c> no toca ningún día, y <c>mantenimiento</c> o <c>fuera de servicio</c>
/// marcan todos los días del rango como no disponibles sin que haga falta ninguna fecha en la que
/// empezara.
/// </para>
/// <para>
/// <strong>Alcance: por unidad, con el inmueble entero como filtro.</strong> Se pide una unidad o
/// todas las de un inmueble, y la respuesta tiene la misma forma en los dos casos: una lista de
/// unidades con sus días. Es lo que cubre el criterio de aceptación ("muestra contratos, reservas,
/// medios días y mantenimiento") sin inventarse una segunda API.
/// </para>
/// <para>
/// TODO(A#): la vista por inmueble que de verdad sirve para pintar un edificio entero de un vistazo
/// es un <em>resumen</em> por unidad (días libres, ocupados y en mantenimiento), no los días uno a
/// uno de todas sus unidades. Eso hay que decidirlo antes, porque §2 dice que el empleado nunca
/// recibe totales agregados por ningún endpoint, y un recuento por unidad es justo eso.
/// </para>
/// </summary>
public sealed class ServicioCalendario(AtlasRentDbContext contexto)
{
    /// <summary>
    /// Días como máximo que se pueden pedir en una llamada: 366, que es un año natural completo
    /// (incluido el bisiesto). Es lo que cualquier vista de calendario pide de golpe —la pantalla
    /// recorre mes o trimestre, y con un año da para pintar un año sin trocear— y a la vez acota lo
    /// que barre <c>ocupacion_rango</c>: diez años de una cartera de 150 unidades son decenas de
    /// miles de filas para una pantalla que no las va a enseñar.
    /// </summary>
    public const int DiasMaximoRango = 366;

    /// <summary>
    /// El calendario de una unidad o de todas las de un inmueble, en el rango pedido
    /// <c>[desde, hasta]</c> con los dos extremos incluidos.
    /// <para>
    /// Falla con <see cref="ArgumentException"/> si el rango no tiene sentido (falta uno de los dos
    /// filtros, vienen los dos, el rango va al revés o es más largo de lo permitido) y con
    /// <see cref="KeyNotFoundException"/> si la unidad o el inmueble no existen. El controlador los
    /// traduce a 400 y 404.
    /// </para>
    /// </summary>
    public async Task<CalendarioRespuesta> LeerAsync(
        Guid? unidadId,
        Guid? inmuebleId,
        DateOnly desde,
        DateOnly hasta,
        CancellationToken ct)
    {
        ComprobarFiltros(unidadId, inmuebleId);

        var diasPedidos = hasta.DayNumber - desde.DayNumber + 1;
        if (diasPedidos < 1)
        {
            throw new ArgumentException(
                $"El rango va de {desde:yyyy-MM-dd} a {hasta:yyyy-MM-dd} y está al revés: la fecha de "
                + "inicio tiene que ser anterior a la de fin.");
        }

        if (diasPedidos > DiasMaximoRango)
        {
            throw new ArgumentException(
                $"El rango pedido son {diasPedidos} días y el máximo son {DiasMaximoRango}. Pide el "
                + "calendario por trozos (un mes, un trimestre) en vez de barrer años de una vez.");
        }

        var unidades = await LeerUnidadesAsync(unidadId, inmuebleId, ct);
        if (unidades.Count == 0)
        {
            throw new KeyNotFoundException(
                inmuebleId is { } inmueble
                    ? $"No hay ninguna unidad en el inmueble {inmueble:D}."
                    : $"No existe una unidad con el identificador {unidadId:D}.");
        }

        Guid[] ids = [.. unidades.Select(u => u.Id)];
        var rangos = await LeerRangosAsync(ids, desde, hasta, ct);
        var diasOcupados = await LeerDiasAsync(ids, desde, hasta, ct);

        return new CalendarioRespuesta(
            [.. unidades.Select(u => PintarUnidad(u, desde, hasta, rangos, diasOcupados))]);
    }

    // ---------------------------------------------------------------- consultas

    /// <summary>
    /// Las unidades pedidas y su inmueble, que es de donde sale el nombre que se pinta arriba. Se
    /// leen también las dadas de baja: su ocupación sigue siendo real, y un calendario que las
    /// escondiera daría una disponibilidad que no es la de la base.
    /// </summary>
    private async Task<List<FilaUnidad>> LeerUnidadesAsync(
        Guid? unidadId,
        Guid? inmuebleId,
        CancellationToken ct)
    {
        if (inmuebleId is { } inmueblePedido
            && !await contexto.Inmueble.AsNoTracking().AnyAsync(i => i.Id == inmueblePedido, ct))
        {
            throw new KeyNotFoundException(
                $"No existe un inmueble con el identificador {inmueblePedido:D}.");
        }

        IQueryable<Unidad> consulta = contexto.Unidad.AsNoTracking();
        consulta = unidadId is { } id
            ? consulta.Where(u => u.Id == id)
            : consulta.Where(u => u.InmuebleId == inmuebleId);

        return await (
            from unidad in consulta
            join inmueble in contexto.Inmueble.AsNoTracking() on unidad.InmuebleId equals inmueble.Id
            orderby unidad.Nombre
            select new FilaUnidad(
                unidad.Id, unidad.Nombre, unidad.InmuebleId, inmueble.Nombre, unidad.EstadoManual))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Los rangos vivos que tocan lo pedido, con el solapamiento de <c>[inicio, fin)</c>: entra un
    /// rango si empieza antes del día siguiente al pedido, y también si no acaba nunca.
    /// </summary>
    private async Task<List<OcupacionRango>> LeerRangosAsync(
        Guid[] unidades,
        DateOnly desde,
        DateOnly hasta,
        CancellationToken ct)
    {
        var siguienteAlPedido = hasta.AddDays(1);

        return await contexto.OcupacionRango.AsNoTracking()
            .Where(r => r.Bloquea
                && unidades.Contains(r.UnidadId)
                && (r.FechaFin == null || r.FechaFin > desde)
                && r.FechaInicio < siguienteAlPedido)
            .ToListAsync(ct);
    }

    /// <summary>Los días vivos del rango pedido, de medio día o de día entero.</summary>
    private async Task<List<OcupacionDia>> LeerDiasAsync(
        Guid[] unidades,
        DateOnly desde,
        DateOnly hasta,
        CancellationToken ct) =>
        await contexto.OcupacionDia.AsNoTracking()
            .Where(d => d.Bloquea && unidades.Contains(d.UnidadId) && d.Fecha >= desde && d.Fecha <= hasta)
            .ToListAsync(ct);

    // ---------------------------------------------------------------- pintado

    /// <summary>
    /// Un día por cada fecha del rango, exista lo que exista en él. Los rangos se despliegan día a
    /// día porque en la respuesta lo que viaja es el día, no el rango: la barra continua la
    /// reconstruye el frontend leyendo <see cref="CalendarioOcupacionRespuesta.FinExclusivo"/>.
    /// </summary>
    private static CalendarioUnidadRespuesta PintarUnidad(
        FilaUnidad unidad,
        DateOnly desde,
        DateOnly hasta,
        List<OcupacionRango> rangos,
        List<OcupacionDia> diasOcupados)
    {
        var ocupaciones = new Dictionary<DateOnly, List<CalendarioOcupacionRespuesta>>();

        foreach (var rango in rangos.Where(r => r.UnidadId == unidad.Id))
        {
            // El rango es [inicio, fin): el último día que ocupa es el anterior a fecha_fin, y si no
            // acaba nunca llega hasta el final de lo pedido.
            var ultimo = rango.FechaFin is { } fin ? fin.AddDays(-1) : hasta;
            if (ultimo > hasta)
            {
                ultimo = hasta;
            }

            for (var dia = rango.FechaInicio > desde ? rango.FechaInicio : desde;
                dia <= ultimo;
                dia = dia.AddDays(1))
            {
                Anotar(ocupaciones, dia, new CalendarioOcupacionRespuesta(
                    rango.Origen, rango.OrigenId, null, rango.FechaFin));
            }
        }

        foreach (var dia in diasOcupados.Where(d => d.UnidadId == unidad.Id))
        {
            Anotar(ocupaciones, dia.Fecha, new CalendarioOcupacionRespuesta(
                dia.Origen, dia.OrigenId, dia.Tipo, null));
        }

        var dias = new List<CalendarioDiaRespuesta>(hasta.DayNumber - desde.DayNumber + 1);
        for (var dia = desde; dia <= hasta; dia = dia.AddDays(1))
        {
            ocupaciones.TryGetValue(dia, out var motivos);
            var tipo = TipoDelDia(motivos, unidad.EstadoManual);

            dias.Add(new CalendarioDiaRespuesta(
                dia,
                tipo,
                tipo is TipoDiaCalendario.Libre,
                Motivo(tipo, unidad.EstadoManual),
                motivos is null
                    ? []
                    : [.. motivos.OrderBy(m => m.Origen).ThenBy(m => m.TipoDia).ThenBy(m => m.OrigenId)]));
        }

        return new CalendarioUnidadRespuesta(
            unidad.Id, unidad.Nombre, unidad.InmuebleId, unidad.NombreInmueble, unidad.EstadoManual,
            desde, hasta, dias);
    }

    private static void Anotar(
        Dictionary<DateOnly, List<CalendarioOcupacionRespuesta>> ocupaciones,
        DateOnly dia,
        CalendarioOcupacionRespuesta ocupacion)
    {
        if (!ocupaciones.TryGetValue(dia, out var delDia))
        {
            delDia = [];
            ocupaciones[dia] = delDia;
        }

        delDia.Add(ocupacion);
    }

    // ---------------------------------------------------------------- desempate

    /// <summary>
    /// Qué motivo se enseña en el día cuando hay más de uno. <strong>Contrato, reserva mensual,
    /// reserva corta y mantenimiento, en ese orden, y el estado manual de la unidad el último</strong>.
    /// <para>
    /// El orden es el de los valores de <see cref="OrigenOcupacion"/>, que es como la base numera
    /// los bloqueos, y pone el rango antes que el día suelto: el rango explica un tramo entero de
    /// días y el día suelto solo uno. El estado manual va el último porque no es un bloqueo de
    /// negocio sino una decisión permanente sobre la unidad: si el día ya tiene un contrato encima,
    /// el contrato es la respuesta a "por qué está ocupada" y el estado manual no le quita razón.
    /// </para>
    /// <para>
    /// En la práctica el desempate casi no hace falta: los triggers de F2-01 (la restricción de
    /// exclusión y la capacidad del día) no permiten que un contrato y un medio día caigan en el
    /// mismo día. La regla está escrita igual porque la respuesta tiene que ser determinista aunque
    /// llegue algo que hoy la base no deja llegar, y no porque haya un conflicto que resolver.
    /// </para>
    /// <para>
    /// Los demás motivos no se pierden: siguen todos en
    /// <see cref="CalendarioDiaRespuesta.Ocupaciones"/>, así que un día con contrato en una unidad
    /// en mantenimiento dice las dos cosas.
    /// </para>
    /// </summary>
    private static TipoDiaCalendario TipoDelDia(
        List<CalendarioOcupacionRespuesta>? motivos,
        EstadoUnidad estadoManual)
    {
        if (motivos is not null)
        {
            foreach (var origen in Prioridad)
            {
                if (motivos.Any(m => m.Origen == origen))
                {
                    return origen switch
                    {
                        OrigenOcupacion.Contrato => TipoDiaCalendario.Contrato,
                        OrigenOcupacion.ReservaMensual => TipoDiaCalendario.ReservaMensual,
                        OrigenOcupacion.ReservaCorta => TipoDiaCalendario.ReservaCorta,
                        _ => TipoDiaCalendario.Mantenimiento,
                    };
                }
            }
        }

        return estadoManual == EstadoUnidad.Disponible
            ? TipoDiaCalendario.Libre
            : TipoDiaCalendario.EstadoManual;
    }

    /// <summary>El orden del desempate, que es el orden numérico de <see cref="OrigenOcupacion"/>.</summary>
    private static readonly OrigenOcupacion[] Prioridad =
    [
        OrigenOcupacion.Contrato,
        OrigenOcupacion.ReservaMensual,
        OrigenOcupacion.ReservaCorta,
        OrigenOcupacion.Mantenimiento,
    ];

    private static string Motivo(TipoDiaCalendario tipo, EstadoUnidad estadoManual)
    {
        var texto = tipo switch
        {
            TipoDiaCalendario.Libre => "Libre",
            TipoDiaCalendario.Contrato => "Contrato vigente",
            TipoDiaCalendario.ReservaMensual => "Reserva mensual",
            TipoDiaCalendario.ReservaCorta => "Reserva corta",
            TipoDiaCalendario.Mantenimiento => "Mantenimiento",
            _ => $"Estado manual de la unidad: {Texto(estadoManual)}",
        };

        // El estado manual de la unidad no es una ocupación con fecha, así que no sale en
        // ocupaciones: si además hay un bloqueo de negocio en ese día, se dice aquí para no perderlo.
        if (tipo is not (TipoDiaCalendario.Libre or TipoDiaCalendario.EstadoManual)
            && estadoManual != EstadoUnidad.Disponible)
        {
            texto += $" · la unidad está en {Texto(estadoManual)}";
        }

        return texto;
    }

    /// <summary>
    /// El estado manual en palabras. No sale de <c>ToString()</c> porque
    /// <c>EstadoUnidad.FueraDeService</c> se leería "fueradeservicio".
    /// </summary>
    private static string Texto(EstadoUnidad estado) => estado switch
    {
        EstadoUnidad.Mantenimiento => "mantenimiento",
        EstadoUnidad.FueraDeServicio => "fuera de servicio",
        _ => "disponible",
    };

    private static void ComprobarFiltros(Guid? unidadId, Guid? inmuebleId)
    {
        if (unidadId is null && inmuebleId is null)
        {
            throw new ArgumentException(
                "Hay que decir de quién es el calendario: unidadId (una unidad) o inmuebleId (todas "
                + "las del inmueble), pero no ninguno de los dos.");
        }

        if (unidadId is not null && inmuebleId is not null)
        {
            throw new ArgumentException(
                "El calendario se pide para una unidad o para un inmueble, no para los dos a la vez.");
        }
    }

    /// <summary>La unidad con lo mínimo que hay que pintar de ella.</summary>
    private sealed record FilaUnidad(
        Guid Id,
        string Nombre,
        Guid InmuebleId,
        string NombreInmueble,
        EstadoUnidad EstadoManual);
}