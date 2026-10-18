using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>Qué se está pidiendo aprobar: la fila no es de contratos sino de aprobaciones (F6-03).</summary>
public enum TipoSolicitud
{
    /// <summary>Lo de siempre: aprobar un contrato con su huella de contenido (§3.5).</summary>
    Contrato = 1,

    /// <summary>
    /// Una acción que propone el asistente y que solo ocurre si alguien con permiso la aprueba. El
    /// contenido que hay que aprobar vive en <c>payload_json</c> y el aviso al que apunta se comprueba
    /// con la misma <see cref="SolicitudAprobacion.HuellaContenido"/>.
    /// </summary>
    AccionAgente = 2,
}

/// <summary>En qué está la solicitud de aprobación.</summary>
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
/// Solicitud de aprobación. De un contrato (docs/ESPECIFICACION.md §3.5) o de una acción del
/// asistente (F6-03), porque el mecanismo —huella, resultado, solicitante, resolutor y motivo de
/// caducidad— es el mismo y duplicarlo sería tener dos reglas donde caduca una aprobación.
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
/// <b>F6-03: <see cref="ContratoId"/> es nulo cuando <see cref="Tipo"/> es
/// <see cref="TipoSolicitud.AccionAgente"/></b> y lo que hay en <see cref="PayloadJson"/> es el aviso
/// de recordatorio que se propone mandar. Sigue siendo la misma fila y la misma caducidad por huella:
/// lo que caduca es el saldo que el aprobador vio, no el contrato.
/// <para>
/// <b>Los campos están puestos para la bandeja de aprobación (F2-10)</b>, que existe desde esa
/// historia. <see cref="Services.ServicioAprobaciones"/> lista las vivas (<see cref="Resultado"/>
/// pendiente) con su contrato, su huella y su solicitante, ordenadas por <see cref="SolicitadoAt"/>;
/// y la ficha del contrato (<see cref="Services.ServicioContratos"/>) devuelve todas, incluidas las
/// caducadas y las resueltas, que son el historial de por qué un contrato volvió a enviarse.
/// <para>
/// <b>No se desnormalizó nada aquí, a propósito.</b> La bandeja junta por <see cref="ContratoId"/>
/// con <c>contrato</c> para el cliente, la unidad y la renta propuesta, y con <c>contrato_version</c>
/// para la renta vigente (la <c>renta_nueva</c> de la última versión). Copiar aquí solicitante,
/// cliente, unidad o renta sería guardar dos copias de un dato que ya está en otra tabla y que se
/// desactualizarían solas en cuanto alguien corrigiera el contrato por el otro camino. Lo que sí
/// vive aquí y no en ningún otro sitio —<see cref="HuellaContenido"/>, <see cref="MotivoCaducidad"/> y
/// <see cref="Comentario"/>— es lo que no se puede derivar de otra fila, y por eso existe.
/// </para>
/// </summary>
public sealed class SolicitudAprobacion : IEntidadTrazada
{
    /// <summary>Qué se pide aprobar. Decide qué significa <see cref="ContratoId"/> y quién resuelve.</summary>
    public TipoSolicitud Tipo { get; set; } = TipoSolicitud.Contrato;

    /// <summary>
    /// Contrato cuya aprobación se pide. <c>null</c> en las propuestas del asistente: la referencia a
    /// lo que hay que decidir va en <see cref="PayloadJson"/>.
    /// </summary>
    public Guid? ContratoId { get; set; }

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
    /// <para>
    /// F6-03: en una propuesta del asistente es la huella del aviso de recordatorio y del saldo
    /// vencido del cliente en el momento de proponer. Mismo mecanismo, otra pregunta: "¿el saldo que
    /// se pidió recordar sigue siendo el de hoy?".
    /// </para>
    /// </summary>
    public string HuellaContenido { get; set; } = string.Empty;

    /// <summary>
    /// Qué se propone hacer, solo para <see cref="TipoSolicitud.AccionAgente"/> (<c>null</c> en los
    /// contratos, que ya se describen con las columnas del contrato).
    /// <para>
    /// <b>Lleva el seudónimo del cliente, nunca su nombre</b>: este JSON va al <c>datos_json</c> de la
    /// auditoría y a ninguna parte más, y un dato personal guardado con otro nombre sigue siendo un
    /// dato personal guardado. La pantalla de aprobación sí enseña el nombre real, porque se arma
    /// leyendo <c>cliente</c> por el identificador que sí va aquí.
    /// </para>
    /// </summary>
    public string? PayloadJson { get; set; }

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