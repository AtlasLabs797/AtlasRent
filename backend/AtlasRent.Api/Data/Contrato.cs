using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Contrato de alquiler (docs/ESPECIFICACION.md §3.5).
/// <para>
/// Los obligatorios que pone la especificación, y que <c>ServicioContratos</c> rechaza con 409 si
/// faltan: cliente, unidad, facturadora, inicio, duración inicial, renta, moneda, día de pago,
/// fianza, regla y duración de renovación y plantilla.
/// <para>
/// El flujo es <c>Borrador → PendienteAprobacion → Aprobado → Activo → BajaProgramada → Finalizado
/// → Liquidado</c>, con el rechazo volviendo a borrador con comentario obligatorio.
/// <para>
/// <b>Nada de esto se borra nunca</b> (AGENTS.md §6): el historial de cambios está en
/// <see cref="ContratoVersion"/> y las solicitudes en <see cref="SolicitudAprobacion"/>.
/// </para>
/// </summary>
public sealed class Contrato : IEntidadTrazada
{
    /// <summary>Cliente del contrato. Obligatorio (docs/ESPECIFICACION.md §3.5).</summary>
    public Guid ClienteId { get; set; }

    public Cliente? Cliente { get; set; }

    /// <summary>
    /// Unidad que se alquila. Obligatoria: el contrato cuelga de la unidad, y al activarse es lo
    /// que bloquea en <c>ocupacion_rango</c>.
    /// </summary>
    public Guid UnidadId { get; set; }

    public Unidad? Unidad { get; set; }

    /// <summary>
    /// Subempresa que factura. Obligatoria (docs/ESPECIFICACION.md §3.5: "la entidad que factura
    /// es la subempresa, nunca el hotel"), porque el contrato escribe en la cadena de facturación de
    /// F3 y sin esto la factura no sabría a nombre de quién va.
    /// </summary>
    public Guid FacturadoraId { get; set; }

    public Subempresa? Facturadora { get; set; }

    /// <summary>Inicio de la relación. Obligatorio, en día de negocio (D16).</summary>
    public DateOnly FechaInicio { get; set; }

    /// <summary>
    /// Duración del primer bloque, en meses. Obligatoria (docs/ESPECIFICACION.md §3.5). El primer
    /// bloque casi siempre es un año porque el alquiler empieza en la temporada, pero el dato va
    /// escrito aparte porque de ahí sale <see cref="FechaFinInicial"/>.
    /// </summary>
    public int DuracionInicialMeses { get; set; }

    /// <summary>
    /// Fin del primer bloque, derivado de <see cref="FechaInicio"/> + <see cref="DuracionInicialMeses"/>.
    /// Lo calcula el servicio; no viene en la petición.
    /// </summary>
    public DateOnly FechaFinInicial { get; set; }

    /// <summary>
    /// Fin del bloque que está en curso. Empieza valiendo <see cref="FechaFinInicial"/> y lo mueve la
    /// renovación automática (F2-06).
    /// <para>
    /// Es lo que decide si a un contrato hay que renovarlo: cuando ya es anterior a hoy en Santo
    /// Domingo. <see cref="FechaFinInicial"/> no lo mueve nadie nunca, porque es la duración que se
    /// firmó; los bloques que van después quedan en <see cref="Bloques"/>.
    /// </para>
    /// </summary>
    public DateOnly FechaFinVigente { get; set; }

    /// <summary>
    /// Bloques que abrió la renovación automática. El primero no está aquí: es
    /// <see cref="FechaInicio"/>–<see cref="FechaFinInicial"/>.
    /// </summary>
    public List<ContratoBloque> Bloques { get; set; } = [];

    /// <summary>Incrementos de renta pactados con fecha, aplicados o no.</summary>
    public List<ContratoIncremento> Incrementos { get; set; } = [];

    /// <summary>
    /// Renta del periodo, en la moneda del contrato (<c>numeric(18,2)</c>, redondeo medio hacia
    /// arriba de <see cref="Redondeo"/>).
    /// </summary>
    public decimal Renta { get; set; }

    /// <summary>Moneda de la renta y de la fianza (D7: una moneda por documento).</summary>
    public Moneda Moneda { get; set; }

    /// <summary>
    /// Día del mes en que se paga (docs/ESPECIFICACION.md §3.5). Entre 1 y 31; lo que pase en meses
    /// cortos lo resuelve el prorrateo de F3-01.
    /// </summary>
    public int DiaPago { get; set; }

    /// <summary>
    /// Importe de la fianza, en la moneda del contrato. Obligatorio en el contrato (la pide §3.5):
    /// aquí queda pactado; su cobro y su devolución son de las tablas <c>fianza</c> y
    /// <c>fianza_devolucion</c> de F3, no de aquí.
    /// TODO(A#) F3: el movimiento de la fianza se registra contra <c>fianza</c> y
    /// <c>fianza_devolucion</c>; este importe es solo lo pactado.
    /// </summary>
    public decimal Fianza { get; set; }

    /// <summary>Regla de renovación automática.</summary>
    public ReglaRenovacion ReglaRenovacion { get; set; }

    /// <summary>
    /// Duración del bloque de renovación, en meses. Obligatoria (docs/ESPECIFICACION.md §3.5). Con
    /// las reglas fijas tiene que valer lo que la regla dice; con
    /// <see cref="Dominio.ReglaRenovacion.Personalizada"/> vale lo pactado.
    /// </summary>
    public int RenovacionMeses { get; set; }

    /// <summary>
    /// Plantilla con la que se redacta el contrato, como identificador. <b>No es una clave ajena:</b>
    /// la tabla de plantillas es de F2-07, así que hasta que exista esta columna guarda el
    /// identificador que el front le pase.
    /// TODO(A#) F2-07: cuando exista <c>plantilla_contrato</c>, esta columna pasa a ser la clave
    /// ajena real y el PDF se compone desde ella.
    /// </summary>
    public string PlantillaContratoId { get; set; } = string.Empty;

    /// <summary>
    /// Copia de <see cref="PlantillaContratoId"/> del momento en que se aprobó, y marca de cuándo.
    /// Es la congelación que pide la especificación (§3.5: "la plantilla del contrato se congela al
    /// aprobarse"): a partir de ahí, editar la plantilla del contrato no toca lo que el contrato
    /// firmado usa. No lo vuelve a escribir nadie.
    /// </summary>
    public string? PlantillaContratoCongeladaId { get; set; }

    /// <summary>Cuándo se congeló la plantilla, si ya está congelada.</summary>
    public DateTimeOffset? PlantillaCongeladaAt { get; set; }

    /// <summary>Estado del flujo.</summary>
    public EstadoContrato Estado { get; set; }

    /// <summary>
    /// Desde cuándo está en <see cref="Estado"/>: el histórico de estados se lee de aquí y del
    /// historial de la auditoría, sin tabla aparte.
    /// </summary>
    public DateOnly? EstadoActualDesde { get; set; }

    /// <summary>Quién dejó el contrato en <see cref="Estado"/>.</summary>
    public Guid? EstadoActualPorId { get; set; }

    /// <summary>
    /// Comentario del cambio de estado: el motivo del rechazo, la nota de la aprobación, la
    /// observación de la baja. El rechazo lo exige (docs/ESPECIFICACION.md §3.5: "rechazo con
    /// comentario obligatorio").
    /// </summary>
    public string? EstadoActualComentario { get; set; }

    /// <summary>
    /// Fecha efectiva de fin elegida al dar de baja. Nula mientras el contrato siga vigente.
    /// docs/ESPECIFICACION.md §3.5: preaviso recomendado de 30 días, sin penalización.
    /// </summary>
    public DateOnly? FechaBajaProgramada { get; set; }

    /// <summary>Fecha real en que terminó la relación, al finalizar.</summary>
    public DateOnly? FechaFinReal { get; set; }

    /// <summary>Observaciones de negocio.</summary>
    public string? Observaciones { get; set; }

    /// <summary>
    /// Historial de cambios de un contrato ya activo. Se escribe, nunca se modifica ni se borra
    /// (docs/ESPECIFICACION.md §3.5: "un cambio en contrato activo genera versión nueva").
    /// </summary>
    public List<ContratoVersion> Versiones { get; set; } = [];

    /// <summary>Solicitudes de aprobación que ha tenido este contrato, incluidas las caducadas.</summary>
    public List<SolicitudAprobacion> Solicitudes { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid Id { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}