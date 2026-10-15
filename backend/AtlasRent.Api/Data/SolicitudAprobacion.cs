using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>En qué está la solicitud de aprobación de un contrato.</summary>
public enum ResultadoSolicitud
{
    /// <summary>Esperando decisión del gerente o del administrador.</summary>
    Pendiente = 1,

    /// <summary>Aprobada. El contrato pasó a aprobado con la plantilla congelada.</summary>
    Aprobada = 2,

    /// <summary>Rechazada. El contrato volvió a borrador con el comentario obligatorio.</summary>
    Rechazada = 3,

    /// <summary>
    /// Caducada sin que nadie decidiera: el contrato cambió mientras esperaba. Hay que enviarla otra
    /// vez, porque lo que el gerente iba a aprobar ya no es lo que el contrato dice.
    /// </summary>
    Caducada = 4,
}

/// <summary>
/// Solicitud de aprobación de un contrato (docs/ESPECIFICACION.md §3.5).
/// <para>
/// Es lo que hace que "el empleado lo manda y lo aprueba un gerente": al enviar un borrador queda una
/// fila aquí con <b>una huella del contenido que se pide aprobar</b>. Si el contrato cambia mientras
/// espera, esa huella deja de valer, la solicitud <see cref="ResultadoSolicitud.Caducada"/> y hay que
/// enviarla de nuevo. Sin esto, un gerente aprobaría a ciegas un contrato que el empleado cambió por
/// debajo, que es exactamente el agujero que la especificación está taponando.
/// <para>
/// Nunca se borra: hay que poder responder "¿qué se pidió aprobar y qué se aprobó?". Cada envío es
/// una fila nueva; la anterior queda caducada o resuelta.
/// <para>
/// <b>Los campos están puestos para la bandeja de aprobación (F2-10)</b>, que todavía no existe. Lo
/// que la bandeja necesita son las solicitudes vivas (<see cref="Resultado"/> pendiente) con su
/// contrato, su huella y su solicitante, ordenadas por <see cref="SolicitadoAt"/>, y qué se les hizo
/// a las que ya no están vivas (<see cref="ResueltoPorId"/>, <see cref="ResueltoAt"/>,
/// <see cref="Comentario"/> y <see cref="MotivoCaducidad"/>). Si le hacen falta el cliente, la unidad
/// y la renta desnormalizados para no hacer join, se añaden aquí.
/// </para>
/// </summary>
public sealed class SolicitudAprobacion : IEntidadTrazada
{
    /// <summary>Contrato cuya aprobación se pide.</summary>
    public Guid ContratoId { get; set; }

    public Contrato? Contrato { get; set; }

    /// <summary>Estado de la solicitud.</summary>
    public ResultadoSolicitud Resultado { get; set; }

    /// <summary>
    /// Huella SHA-256 del contenido que se envía a aprobar (cliente, unidad, facturadora, inicio,
    /// duración, renta, moneda, día de pago, fianza, regla y duración de renovación, plantilla y
    /// observaciones). No lleva clave: no es un secreto sino un checksum para detectar el cambio, y
    /// con él se responde "¿esto es lo que te mandé a aprobar?".
    /// <para>
    /// Deliberadamente <b>no</b> incluye el estado ni las marcas del cambio de estado: si los
    /// incluyera, el propio paso a pendiente invalidaría su propia solicitud y el contrato se
    /// caducaría solo al enviarlo, sin que nadie hubiera tocado nada.
    /// </para>
    /// </summary>
    public string HuellaContenido { get; set; } = string.Empty;

    /// <summary>Quién pidió la aprobación (el empleado).</summary>
    public Guid? SolicitadoPorId { get; set; }

    /// <summary>Cuándo se pidió.</summary>
    public DateTimeOffset SolicitadoAt { get; set; }

    /// <summary>
    /// Quién resolvió la solicitud: el gerente o el administrador que aprobó o rechazó. Nula
    /// mientras está pendiente o si caducó sola.
    /// </summary>
    public Guid? ResueltoPorId { get; set; }

    /// <summary>Cuándo se resolvió.</summary>
    public DateTimeOffset? ResueltoAt { get; set; }

    /// <summary>
    /// Comentario del rechazo (obligatorio por §3.5) o nota con la que se envió a aprobación.
    /// </summary>
    public string? Comentario { get; set; }

    /// <summary>
    /// Por qué caducó, si caducó. Con "contenido distinto" queda claro en la bandeja que hay que
    /// volver a enviar, y no que alguien la cerró.
    /// </summary>
    public string? MotivoCaducidad { get; set; }

    /// <inheritdoc />
    public Guid Id { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}