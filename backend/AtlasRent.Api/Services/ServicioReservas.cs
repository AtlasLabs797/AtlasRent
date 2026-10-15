using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AtlasRent.Api.Services;

/// <summary>
/// Reservas (docs/ESPECIFICACION.md §3.4): mensual antes del contrato y corta de medio día o día
/// completo.
/// <list type="bullet">
/// <item>
/// <b>La fila de negocio y su bloqueo se escriben en la misma transacción.</b> Si el bloqueo fuera
/// una segunda transacción, un fallo al guardar la reserva dejaría una unidad ocupada por una reserva
/// que no existe, y nadie la vería en la lista. Por eso todo pasa por
/// <see cref="GuardadoAuditado"/>, que es el que abre la transacción y la cierra: el bloqueo lo
/// escribe <see cref="ServicioDisponibilidad"/> desde <c>antesDeGuardar</c> y, si algo falla después,
/// se deshace con ella.
/// </item>
/// <item>
/// <b>El bloqueo no lo decide este servicio.</b> Las reglas del documento (mismo día, 4 horas o
/// jornada, dos personas como mucho) se comprueban aquí para que el error sea de negocio y llegue
/// como un 409 con su explicación; la garantía de que la unidad está libre la dan la restricción de
/// exclusión de <c>ocupacion_rango</c> y el trigger de capacidad de <c>ocupacion_dia</c> (F2-01),
/// que son los que lo impiden sin carreras. Por eso una falta del trigger se traduce a un 409 con
/// <see cref="Problemas.UnidadNoDisponible"/> y no se intenta adivinar por qué.
/// </item>
/// <item>
/// <b>El anticipo no es un cobro (D10).</b> Se guardan importe y nota y no se crea ninguna fila de
/// cobro, factura ni asiento: la especificación dice que no hay pasarela de pago y que el anticipo
/// queda anotado en la reserva, y el cobro se registra cuando se paga la factura, que es F3-01.
/// Escribir algo aquí sería inventar un movimiento de dinero que nadie ha hecho.
/// </item>
/// </list>
/// </summary>
public sealed class ServicioReservas(
    AtlasRentDbContext contexto,
    AuditoriaService auditoria,
    ServicioIdempotencia idempotencia,
    ServicioDisponibilidad disponibilidad,
    IRegistroAlertas alertas,
    TimeProvider reloj)
{
    /// <summary>
    /// Jornada de una reserva corta: día completo de 08:00 a 16:00 y medio día de 4 horas exactas.
    /// <para>
    /// DECISIÓN DEL ORQUESTADOR: el medio día son 4 horas exactas. Para que eso no fuera arbitrario,
    /// la jornada entera son las 8 horas que van de las 08:00 a las 16:00, de modo que el medio día
    /// es exactamente la mitad. Son las horas que se piden en el formulario, y no un alquiler por
    /// horas: entre medias no hay nada (D11).
    /// </para>
    /// </summary>
    private static readonly TimeOnly InicioJornada = new(8, 0);

    private static readonly TimeOnly FinJornada = new(16, 0);

    private static readonly TimeSpan HorasMedioDia = TimeSpan.FromHours(4);

    private static readonly Dictionary<string, Func<IQueryable<Reserva>, bool, IOrderedQueryable<Reserva>>>
        Ordenes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["createdAt"] = (q, desc) => desc ? q.OrderByDescending(r => r.CreatedAt) : q.OrderBy(r => r.CreatedAt),
            ["entradaPrevista"] = (q, desc) =>
                desc ? q.OrderByDescending(r => r.EntradaPrevista) : q.OrderBy(r => r.EntradaPrevista),
            ["estado"] = (q, desc) => desc ? q.OrderByDescending(r => r.Estado) : q.OrderBy(r => r.Estado),
            ["importe"] = (q, desc) => desc ? q.OrderByDescending(r => r.Importe) : q.OrderBy(r => r.Importe),
        };

    public Task<Pagina<ReservaResumen>> ListarAsync(
        ConsultaLista paginacion,
        ReservaFiltro filtro,
        CancellationToken ct)
    {
        IQueryable<Reserva> consulta = contexto.Reserva.AsNoTracking()
            .Include(r => r.Cliente)
            .Include(r => r.Prospecto)
            .Include(r => r.Unidad)
            .Include(r => r.Dias);

        if (!filtro.IncluirInactivas)
        {
            consulta = consulta.Where(r => r.Activo);
        }

        if (filtro.Estado is { } estado)
        {
            consulta = consulta.Where(r => r.Estado == estado);
        }

        if (filtro.Tipo is { } tipo)
        {
            consulta = consulta.Where(r => r.Tipo == tipo);
        }

        if (filtro.UnidadId is { } unidadId)
        {
            consulta = consulta.Where(r => r.UnidadId == unidadId);
        }

        if (filtro.ClienteId is { } clienteId)
        {
            consulta = consulta.Where(r => r.ClienteId == clienteId);
        }

        if (filtro.ProspectoId is { } prospectoId)
        {
            consulta = consulta.Where(r => r.ProspectoId == prospectoId);
        }

        if (filtro.Desde is { } desde)
        {
            consulta = consulta.Where(r => r.EntradaPrevista >= desde);
        }

        if (filtro.Hasta is { } hasta)
        {
            consulta = consulta.Where(r => r.EntradaPrevista <= hasta);
        }

        if (!string.IsNullOrWhiteSpace(filtro.Texto))
        {
            var termino = filtro.Texto.Trim().ToLowerInvariant();
            consulta = consulta.Where(r =>
                (r.Cliente != null && r.Cliente.Nombre.ToLower().Contains(termino)) ||
                (r.Prospecto != null && r.Prospecto.Nombre.ToLower().Contains(termino)) ||
                (r.Unidad != null && r.Unidad.Nombre.ToLower().Contains(termino)));
        }

        return ConsultaPaginada.PaginarAsync(
            consulta, Ordenes, "entradaPrevista", paginacion, AResumen, ct);
    }

    public async Task<ReservaRespuesta> LeerAsync(Guid id, CancellationToken ct)
    {
        var reserva = await CargarAsync(id, ct);

        return ARespuesta(reserva);
    }

    /// <summary>
    /// Alta de una reserva. La fila y su bloqueo salen de la misma transacción, como está escrito en
    /// la clase: si la unidad está ocupada, el trigger salta y no queda ni la reserva ni el bloqueo.
    /// </summary>
    public async Task<ReservaResumen> CrearAsync(
        ReservaPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        Validar(peticion);
        await ExigirUnidadYDestinatarioAsync(peticion, ct);

        var reserva = new Reserva
        {
            Estado = EstadoReserva.Activa,
            Activo = true,
            // Fecha de negocio (D16), no la hora del servidor: una reserva de las 23:30 es de ese día en Santo Domingo,
            // día que es en Santo Domingo, no al siguiente.
            FechaReserva = TiempoNegocio.FechaEnZonaNegocio(reloj.GetUtcNow()),
        };
        Aplicar(reserva, peticion);

        var dias = ConstruirDias(peticion);

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Reserva, reserva,
            esAlta: true,
            accion: "reserva.creada",
            entidad: "reserva",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: c => DatosAuditoria(c, dias.Count),
            antesDeGuardar: () => EscribirYBloquearAsync(reserva, dias, usuario, ct),
            ct: ct);
    }

    /// <summary>
    /// Edición de una reserva activa. Cambiar los días o la unidad cambia el bloqueo, y para eso
    /// hace falta soltar lo viejo antes de escribir lo nuevo: por eso aquí no se edita nada
    /// directamente, sino que se reescribe la ocupación de la fila de origen. Todo en la misma
    /// transacción, así que una edición que choca no deja la unidad medio bloqueada.
    /// </summary>
    public async Task<ReservaResumen> ActualizarAsync(
        Guid id,
        ReservaPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var reserva = await CargarAsync(id, ct);
        ExigirActiva(reserva, "editar");

        Validar(peticion);
        await ExigirUnidadYDestinatarioAsync(peticion, ct);

        var anteriores = reserva.Dias.ToList();
        Aplicar(reserva, peticion);
        var dias = ConstruirDias(peticion);

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Reserva, reserva,
            esAlta: false,
            accion: "reserva.actualizada",
            entidad: "reserva",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: c => DatosAuditoria(c, dias.Count, anteriores.Select(d => d.Fecha)),
            antesDeGuardar: () => ReescribirOcupacionAsync(reserva, anteriores, dias, usuario, ct),
            ct: ct);
    }

    /// <summary>
    /// Baja lógica: la reserva queda <c>Cancelada</c>, con <c>activo = false</c>, y su bloqueo se
    /// suelta. La fila y sus días se quedan, porque una reserva cancelada es historial (AGENTS.md §6).
    /// <para>
    /// <b>También cancela la reserva en conflicto, y esa es la salida que le faltaba al estado.</b>
    /// docs/ESPECIFICACION.md §3.4 dice que una reserva en conflicto "sigue bloqueando hasta que el
    /// gerente decida" y no enumera entre quelles son esas decisiones. La decisión que se toma aquí es
    /// del orquestador y es una sola: <b>cancelar la reserva</b>. Sin ella, una reserva que pasó a
    /// <see cref="EstadoReserva.EnConflicto"/> se quedaba <c>Activo = true</c> para siempre —porque
    /// <c>ExigirActiva</c> le impedía cancelarse a sí misma—, el candado no lo soltaba nadie, y el
    /// aviso de conflicto de F2-10 no se podía cerrar. No se inventan aquí otras salidas (convertir a
    /// otra unidad, reabrir la reserva): el mínimo para que el estado tenga salida es poder cancelar.
    /// <para>
    /// Al cancelarse en conflicto se cierra también su aviso de conflicto, con el mismo
    /// <c>ResolverAsync</c> que usa el barrido de F2-10, y en la misma transacción que la baja: si
    /// no, la bandeja se quedaría con un aviso que ya no aplica y que nadie puede cerrar.
    /// </para>
    /// </summary>
    public async Task<ReservaResumen> CancelarAsync(
        Guid id,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var reserva = await CargarAsync(id, ct);
        ExigirCancelable(reserva);

        var estabaEnConflicto = reserva.Estado == EstadoReserva.EnConflicto;

        reserva.Estado = EstadoReserva.Cancelada;
        reserva.Activo = false;

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Reserva, reserva,
            esAlta: false,
            accion: "reserva.cancelada",
            entidad: "reserva",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: c => DatosAuditoria(c, c.Dias.Count),
            antesDeGuardar: () => LiberarAsync(reserva, ct),
            // El aviso se cierra después de guardar la baja y antes del commit, para que la baja y el
            // cierre del aviso salgan juntos o no salga ninguno de los dos.
            despuesDeGuardar: estabaEnConflicto
                ? () => alertas.ResolverAsync(
                    ServicioAvisosOperacion.ClaveReservaConflicto(reserva.Id), ct)
                : null,
            ct: ct);
    }

    /// <summary>
    /// Pasa una reserva activa a <see cref="EstadoReserva.EnConflicto"/>: la unidad ya está
    /// comprometida con un contrato y quien reservó tiene que buscar otra.
    /// <para>
    /// <b>No libera el bloqueo.</b> La especificación dice que en conflicto la reserva sigue
    /// bloqueando hasta que el gerente decida, y el que conserva la unidad no puede ser el que está
    /// buscando sitio para el cliente.
    /// </para>
    /// <para>
    /// Esto lo llama el servicio de contratos (F2-05) al activar un contrato. Si lo llama desde su
    /// propia transacción, el cambio y el contrato salen en un solo commit; si no, abre la suya.
    /// No lleva <c>Idempotency-Key</c> porque no es una petición de API sino un efecto de otra
    /// mutación.
    /// <para>
    /// Que aquí no se suelte el candado no significa que la unidad pueda quedarse bloqueada por una
    /// reserva que ya lo perdió: en cuanto hay un contrato vigente encima, quien suelta es el
    /// servicio de contratos, porque el candado pasa a ser suyo. Hasta entonces, lo de arriba.
    /// </para>
    /// </para>
    /// </summary>
    public async Task MarcarEnConflictoAsync(
        Guid id,
        Guid usuario,
        string ip,
        CancellationToken ct)
    {
        var reserva = await CargarAsync(id, ct);
        ExigirActiva(reserva, "marcar en conflicto");

        reserva.Estado = EstadoReserva.EnConflicto;

        await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Reserva, reserva,
            esAlta: false,
            accion: "reserva.en_conflicto",
            entidad: "reserva",
            usuario,
            ip,
            mutacion: null,
            proyectar: AResumen,
            datosAuditoria: c => DatosAuditoria(c, c.Dias.Count),
            enTransaccionDelLlamador: contexto.Database.CurrentTransaction is not null,
            ct: ct);
    }

    // ---------------------------------------------------------------- internos

    /// <summary>
    /// Las reglas del documento, antes de tocar la base de datos. Devuelven 409, como el resto de
    /// servicios: no son un error de formato sino una reserva que no se puede guardar.
    /// </summary>
    private static void Validar(ReservaPeticion peticion)
    {
        if (peticion.ClienteId is null && peticion.ProspectoId is null)
        {
            throw new InvalidOperationException(
                "La reserva necesita un cliente o un prospecto: se reserva a alguien, no en el vacío.");
        }

        if (peticion.ClienteId is not null && peticion.ProspectoId is not null)
        {
            throw new InvalidOperationException(
                "La reserva es para un cliente o para un prospecto, no para los dos.");
        }

        // Los enums llegan del JSON: un número que no existe se rechaza aquí, no en la base.
        if (peticion.Tipo is not (TipoReserva.Mensual or TipoReserva.Corta))
        {
            throw new InvalidOperationException(
                $"El tipo de reserva '{peticion.Tipo}' no existe: solo mensual o corta.");
        }

        if (peticion.Moneda is not (Moneda.USD or Moneda.DOP))
        {
            throw new InvalidOperationException(
                $"La moneda '{peticion.Moneda}' no existe. Las reservas van en USD o en DOP.");
        }

        if (peticion.Importe < 0)
        {
            throw new InvalidOperationException("El importe de la reserva no puede ser negativo.");
        }

        if (peticion.AnticipoImporte < 0)
        {
            throw new InvalidOperationException("El importe del anticipo no puede ser negativo.");
        }

        if (peticion.Tipo == TipoReserva.Mensual)
        {
            ValidarMensual(peticion);
        }
        else
        {
            ValidarCorta(peticion);
        }
    }

    private static void ValidarMensual(ReservaPeticion peticion)
    {
        if (peticion.Modalidad != ModalidadAlquiler.Mensual)
        {
            throw new InvalidOperationException(
                "Una reserva mensual es de modalidad mensual.");
        }

        if (peticion.SalidaPrevista is not null)
        {
            throw new InvalidOperationException(
                "Una reserva mensual no tiene fecha de salida: bloquea la unidad desde la entrada "
                + "prevista hasta que haya contrato.");
        }

        if (peticion.Dias.Count > 0)
        {
            throw new InvalidOperationException(
                "Una reserva mensual no lleva días: bloquea un rango. Los días son de la corta.");
        }
    }

    private static void ValidarCorta(ReservaPeticion peticion)
    {
        if (peticion.Modalidad is ModalidadAlquiler.Mensual)
        {
            throw new InvalidOperationException(
                "Una reserva corta es de medio día o de día completo, no mensual.");
        }

        if (peticion.Dias.Count == 0)
        {
            throw new InvalidOperationException("Una reserva corta necesita al menos un día.");
        }

        if (peticion.SalidaPrevista is { } salida && salida != peticion.EntradaPrevista)
        {
            throw new InvalidOperationException(
                "Una reserva corta entra y sale el mismo día. Si el cliente necesita dormir fuera, "
                + "eso es una reserva mensual.");
        }

        var fechas = new HashSet<DateOnly>();

        foreach (var dia in peticion.Dias)
        {
            var tipo = TipoDeJornada(dia);

            if (tipo is TipoOcupacionDia.MedioDia && peticion.Modalidad != ModalidadAlquiler.MedioDia)
            {
                throw new InvalidOperationException(
                    $"El día {dia.Fecha:dd/MM/yyyy} son 4 horas, que es un medio día, y la reserva "
                    + "es de día completo. Cada día va con su tipo.");
            }

            if (tipo is TipoOcupacionDia.DiaCompleto && peticion.Modalidad != ModalidadAlquiler.DiaCompleto)
            {
                throw new InvalidOperationException(
                    $"El día {dia.Fecha:dd/MM/yyyy} es la jornada entera, y la reserva es de medio "
                    + "día. Cada día va con su tipo.");
            }

            // Dos personas el mismo día son dos medios días, y eso son dos reservas. Meter las dos
            // en una sola chocaría con el bloqueo por unidad, día y fila de origen, que solo admite
            // un día por reserva: el trigger contaría uno y dejaría pasar a un tercero. Aquí se
            // rechaza con un error de negocio, que es lo que la especificación pide para el tercer
            // medio día, en vez de dejar que lo diga PostgreSQL.
            if (!fechas.Add(dia.Fecha))
            {
                throw new InvalidOperationException(
                    $"La reserva repite el día {dia.Fecha:dd/MM/yyyy}. Si van dos personas, son dos "
                    + "reservas: una unidad y un día caben en dos medios días, no en más.");
            }
        }
    }

    /// <summary>
    /// El tipo de un día sale de las horas que se piden, nunca al revés: 4 horas exactas son medio
    /// día y la jornada de 08:00 a 16:00 es día completo. Cualquier otra duración no existe (D11).
    /// </summary>
    private static TipoOcupacionDia TipoDeJornada(ReservaDiaPeticion dia)
    {
        if (dia.HoraSalida <= dia.HoraEntrada)
        {
            throw new InvalidOperationException(
                $"El día {dia.Fecha:dd/MM/yyyy} sale antes de entrar: la entrada y la salida son el "
                + "mismo día.");
        }

        var duracion = dia.HoraSalida.ToTimeSpan() - dia.HoraEntrada.ToTimeSpan();

        if (duracion == HorasMedioDia)
        {
            return TipoOcupacionDia.MedioDia;
        }

        if (dia.HoraEntrada == InicioJornada && dia.HoraSalida == FinJornada)
        {
            return TipoOcupacionDia.DiaCompleto;
        }

        throw new InvalidOperationException(
            $"El día {dia.Fecha:dd/MM/yyyy} dura {duracion.TotalHours:0.##} h y no se puede "
            + "reservar: los días son de medio día (4 h exactas) o de la jornada completa "
            + $"({InicioJornada:hh\\:mm} a {FinJornada:hh\\:mm}). No hay alquiler por horas.");
    }

    private async Task ExigirUnidadYDestinatarioAsync(ReservaPeticion peticion, CancellationToken ct)
    {
        if (!await contexto.Unidad.AnyAsync(u => u.Id == peticion.UnidadId, ct))
        {
            throw new KeyNotFoundException($"No existe la unidad '{peticion.UnidadId:D}'.");
        }

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
    }

    private static void ExigirActiva(Reserva reserva, string accion)
    {
        if (reserva.Estado != EstadoReserva.Activa)
        {
            throw new InvalidOperationException(
                $"Una reserva en estado {Texto(reserva.Estado)} ya no se puede {accion}.");
        }
    }

    /// <summary>
    /// Estados desde los que se puede dar de baja. <c>Activa</c> es el normal y <c>EnConflicto</c>
    /// es la salida del conflicto que decidió el orquestador (ver <see cref="CancelarAsync"/>):
    /// una reserva que ya no puede tener la unidad tampoco puede quedarse bloqueándola. Todo lo
    /// demás —convertida, completada o cancelada— sigue sin salida, porque el candado ya lo soltó
    /// alguien o no lo tiene.
    /// </summary>
    private static void ExigirCancelable(Reserva reserva)
    {
        if (reserva.Estado is not (EstadoReserva.Activa or EstadoReserva.EnConflicto))
        {
            throw new InvalidOperationException(
                $"Una reserva en estado {Texto(reserva.Estado)} ya no se puede cancelar.");
        }
    }

    private static void Aplicar(Reserva reserva, ReservaPeticion peticion)
    {
        reserva.UnidadId = peticion.UnidadId;
        reserva.ClienteId = peticion.ClienteId;
        reserva.ProspectoId = peticion.ProspectoId;
        reserva.Tipo = peticion.Tipo;
        reserva.Modalidad = peticion.Modalidad;
        reserva.Moneda = peticion.Moneda;
        reserva.Importe = Redondeo.Importe(peticion.Importe);
        reserva.AnticipoImporte = peticion.AnticipoImporte is { } anticipo ? Redondeo.Importe(anticipo) : null;
        reserva.AnticipoNota = peticion.AnticipoNota?.Trim();
        reserva.Observaciones = peticion.Observaciones?.Trim();
        reserva.EntradaPrevista = peticion.EntradaPrevista;
        // En la corta la salida es el mismo día de la entrada, así que se guarda tal cual vino: si
        // no vino, se deja en nula en vez de inventar un valor.
        reserva.SalidaPrevista = peticion.Tipo == TipoReserva.Corta
            ? peticion.SalidaPrevista ?? peticion.EntradaPrevista
            : null;
    }

    /// <summary>
    /// Los días de la corta con su tipo ya deducido de las horas. Se validan en
    /// <see cref="Validar"/> antes, así que aquí el tipo ya está comprobado.
    /// <para>
    /// La clave foránea se pone después, en <see cref="AnadirDias"/>: en el momento de construir
    /// estas filas la reserva todavía no tiene <c>Id</c>, porque lo asigna
    /// <see cref="GuardadoAuditado"/> al empezar a guardar.
    /// </para>
    /// </summary>
    private static List<ReservaDia> ConstruirDias(ReservaPeticion peticion)
    {
        if (peticion.Tipo == TipoReserva.Mensual)
        {
            return [];
        }

        return
        [
            .. peticion.Dias.Select(dia => new ReservaDia
            {
                Id = Guid.NewGuid(),
                Fecha = dia.Fecha,
                HoraEntrada = dia.HoraEntrada,
                HoraSalida = dia.HoraSalida,
                Tipo = TipoDeJornada(dia),
            }),
        ];
    }

    /// <summary>
    /// Los días de negocio y el bloqueo, en la misma transacción que abre
    /// <see cref="GuardadoAuditado"/>. Los días van por el conjunto y no por la navegación para no
    /// tocar la navegación de una entidad que esa misma llamada está guardando.
    /// </summary>
    private async Task EscribirYBloquearAsync(
        Reserva reserva,
        List<ReservaDia> dias,
        Guid usuario,
        CancellationToken ct)
    {
        AnadirDias(reserva, dias, usuario);
        await BloquearAsync(reserva, dias, ct);
    }

    /// <summary>
    /// Cambiar los días o la unidad reescribe el bloqueo entero de la reserva: suelta lo que ya no
    /// está y ocupa lo nuevo. El borrado de los días de negocio se confirma antes de insertar los
    /// nuevos, como en las líneas de una cotización, y todo sigue dentro de la transacción.
    /// </summary>
    private async Task ReescribirOcupacionAsync(
        Reserva reserva,
        List<ReservaDia> anteriores,
        List<ReservaDia> nuevas,
        Guid usuario,
        CancellationToken ct)
    {
        if (anteriores.Count > 0)
        {
            contexto.ReservaDia.RemoveRange(anteriores);
            await contexto.SaveChangesAsync(ct);
        }

        await EscribirYBloquearAsync(reserva, nuevas, usuario, ct);
    }

    /// <summary>Suelta el bloqueo de la reserva sin tocar su negocio.</summary>
    private Task LiberarAsync(Reserva reserva, CancellationToken ct) =>
        disponibilidad.SincronizarAsync(
            Origen(reserva), reserva.Id, reserva.UnidadId, rangoDesde: null, dias: null, ct);

    /// <summary>
    /// El bloqueo que corresponde a la reserva. El rango va en <c>ocupacion_rango</c> y los días
    /// sueltos en <c>ocupacion_dia</c>; una mensual no pasa a tener días ni al revés.
    /// </summary>
    private Task BloquearAsync(Reserva reserva, List<ReservaDia> dias, CancellationToken ct) =>
        reserva.Tipo == TipoReserva.Mensual
            ? disponibilidad.SincronizarAsync(
                Origen(reserva), reserva.Id, reserva.UnidadId, reserva.EntradaPrevista, dias: null, ct)
            : disponibilidad.SincronizarAsync(
                Origen(reserva),
                reserva.Id,
                reserva.UnidadId,
                rangoDesde: null,
                [.. dias.OrderBy(d => d.Fecha).Select(d => (d.Fecha, d.Tipo))],
                ct);

    /// <summary>El origen de la ocupación: el que le toca a la base de datos, separado por clase.</summary>
    private static OrigenOcupacion Origen(Reserva reserva) =>
        reserva.Tipo == TipoReserva.Mensual ? OrigenOcupacion.ReservaMensual : OrigenOcupacion.ReservaCorta;

    private void AnadirDias(Reserva reserva, List<ReservaDia> dias, Guid usuario)
    {
        if (dias.Count == 0)
        {
            return;
        }

        var ahora = reloj.GetUtcNow();

        foreach (var dia in dias)
        {
            dia.ReservaId = reserva.Id;
            dia.CreatedAt = ahora;
            dia.CreatedBy = usuario;
        }

        contexto.ReservaDia.AddRange(dias);
    }

    private static ReservaResumen AResumen(Reserva reserva) =>
        new(
            reserva.Id,
            reserva.UnidadId,
            reserva.Unidad?.Nombre ?? string.Empty,
            reserva.ClienteId,
            reserva.ProspectoId,
            reserva.Cliente?.Nombre ?? reserva.Prospecto?.Nombre,
            reserva.Tipo,
            reserva.Estado,
            reserva.Modalidad,
            reserva.Moneda,
            reserva.Importe,
            reserva.AnticipoImporte,
            reserva.FechaReserva,
            reserva.EntradaPrevista,
            reserva.SalidaPrevista,
            reserva.EntradaReal,
            reserva.SalidaReal,
            reserva.Activo,
            reserva.Dias.Count,
            reserva.AnticipoImporte is not null,
            reserva.CreatedAt);

    private static ReservaRespuesta ARespuesta(Reserva reserva) =>
        new(
            reserva.Id,
            reserva.UnidadId,
            reserva.Unidad?.Nombre ?? string.Empty,
            reserva.ClienteId,
            reserva.ProspectoId,
            reserva.Cliente?.Nombre ?? reserva.Prospecto?.Nombre,
            reserva.Tipo,
            reserva.Estado,
            reserva.Modalidad,
            reserva.Moneda,
            reserva.Importe,
            reserva.AnticipoImporte,
            reserva.AnticipoNota,
            reserva.Observaciones,
            reserva.FechaReserva,
            reserva.EntradaPrevista,
            reserva.SalidaPrevista,
            reserva.EntradaReal,
            reserva.SalidaReal,
            reserva.Activo,
            [.. reserva.Dias
                .OrderBy(d => d.Fecha)
                .ThenBy(d => d.HoraEntrada)
                .Select(d => new ReservaDiaRespuesta(d.Id, d.Fecha, d.HoraEntrada, d.HoraSalida, d.Tipo))],
            reserva.CreatedAt);

    /// <summary>
    /// Qué va a la auditoría. Ni el anticipo con su nota ni el texto del cliente: importes y
    /// referencias de documentos, nunca contenido (AGENTS.md §6).
    /// <para>
    /// Los días que la reserva tenía antes del cambio van también: al editar, sus filas antiguas se
    /// borran de <c>reserva_dia</c> (la reserva no se borra, su composición sí), y sin esto el
    /// registro de "la reserva estaba el día 12" se perdería.
    /// </para>
    /// </summary>
    private static object DatosAuditoria(Reserva reserva, int dias, IEnumerable<DateOnly>? diasAnteriores = null) => new
    {
        reserva.UnidadId,
        reserva.ClienteId,
        reserva.ProspectoId,
        Tipo = reserva.Tipo.ToString(),
        Estado = reserva.Estado.ToString(),
        Modalidad = reserva.Modalidad.ToString(),
        Moneda = reserva.Moneda.ToString(),
        reserva.Importe,
        reserva.AnticipoImporte,
        Dias = dias,
        DiasAnteriores = diasAnteriores is null ? null : (IReadOnlyCollection<DateOnly>)[.. diasAnteriores],
        reserva.EntradaPrevista,
        reserva.Activo,
    };

    private async Task<Reserva> CargarAsync(Guid id, CancellationToken ct) =>
        await contexto.Reserva
            .Include(r => r.Cliente)
            .Include(r => r.Prospecto)
            .Include(r => r.Unidad)
            .Include(r => r.Dias)
            .FirstOrDefaultAsync(r => r.Id == id, ct)
        ?? throw new KeyNotFoundException($"No existe la reserva '{id:D}'.");

    /// <summary>El nombre del estado para los mensajes de la API, en el idioma de quien la lee.</summary>
    private static string Texto(EstadoReserva estado) => estado switch
    {
        EstadoReserva.Activa => "activa",
        EstadoReserva.Convertida => "convertida en contrato",
        EstadoReserva.Completada => "completada",
        EstadoReserva.Cancelada => "cancelada",
        EstadoReserva.EnConflicto => "en conflicto",
        _ => estado.ToString(),
    };
}