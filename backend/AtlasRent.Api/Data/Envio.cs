namespace AtlasRent.Api.Data;

using AtlasRent.Api.Dominio;

/// <summary>
/// Un correo que salió (o que se intentó mandar) con un documento adjunto
/// (docs/ESPECIFICACION.md §3.14).
/// <para>
/// <b>El documento se apunta con una referencia lógica</b> —<see cref="TipoDocumento"/> más
/// <see cref="DocumentoId"/>— y no con una clave foránea. No es una pereza: un envío apunta a
/// facturas y proformas, recibos, estados de cuenta, cotizaciones y contratos, que están en cuatro
/// tablas distintas (el estado de cuenta no tiene ni fila propia), y no hay ninguna clave foránea
/// única que los valgue a todos. Lo que sí se guarda es lo que hace falta para poder repetir el
/// envío y para auditarlo: qué se mandó, a quién, con qué texto y con qué bytes.
/// </para>
/// <para>
/// La contrapartida de no tener clave foránea es que nada impide dejar un envío apuntando a un
/// documento borrado. No importa: un envío <b>no se borra</b> (§3.14 lo guarda como prueba de lo que
/// se le mandó al cliente) y un documento borrado no se borra tampoco (D28), así que el puntero
/// colgado no pierde nada. Si algún día hiciera falta integridad referencial real, la salida es una
/// tabla por tipo de documento con una clave foránea cada una y una vista por encima.
/// <c>TODO(A#)</c> decidir solo si aparece un caso de negocio que exija esa integridad.
/// </para>
/// </summary>
public sealed class Envio : IEntidadTrazada
{
    public Guid Id { get; set; }

    /// <summary>Qué clase de documento es el que va adjunto.</summary>
    public TipoDocumentoEnvio TipoDocumento { get; set; }

    /// <summary>
    /// Identificador del documento en su propia tabla. Para <see cref="TipoDocumentoEnvio.EstadoCuenta"/>
    /// es el identificador del cliente y el año va en <see cref="DocumentoAnio"/>.
    /// </summary>
    public Guid DocumentoId { get; set; }

    /// <summary>Año del estado de cuenta. <c>NULL</c> en todos los demás tipos.</summary>
    public int? DocumentoAnio { get; set; }

    /// <summary>Cliente al que se mandó, si el documento es de un cliente. <c>NULL</c> si no se pudo resolver.</summary>
    public Guid? ClienteId { get; set; }

    /// <summary>
    /// Contacto de <see cref="Cliente"/> al que se mandó, si el destinatario vino de la ficha del
    /// cliente. <c>NULL</c> cuando el correo se escribió a mano: ahí no hay contacto al que apuntar
    /// y no se inventa uno.
    /// </summary>
    public Guid? ContactoClienteId { get; set; }

    /// <summary>Correo al que salió. Se guarda en minúsculas para que el antidiplicado de §3.14 compare bien.</summary>
    public string DestinatarioEmail { get; set; } = string.Empty;

    /// <summary>Nombre del destinatario, si se sabe. Va al encabezado <c>To</c> del correo.</summary>
    public string? DestinatarioNombre { get; set; }

    /// <summary>Asunto con las variables ya sustituidas.</summary>
    public string Asunto { get; set; } = string.Empty;

    /// <summary>Cuerpo con las variables ya sustituidas. Texto plano: sin HTML, sin etiquetas y sin entidades.</summary>
    public string Cuerpo { get; set; } = string.Empty;

    /// <summary>Idioma de la plantilla que se usó. Es el de la fila de <c>plantilla_mensaje</c>, no el del cliente.</summary>
    public string Idioma { get; set; } = "es";

    public EstadoEnvio Estado { get; set; }

    /// <summary>
    /// Los bytes <b>exactos</b> del PDF que salió por el correo. Es el criterio de aceptación de la
    /// tarea y la razón de que <c>GET /api/v1/envios/{id}/pdf</c> devuelva esta columna y no
    /// regenere el documento: lo que se le mandó al cliente es lo que está aquí, y si se regenerara
    /// dejaría de ser la copia exacta en cuanto el documento cambiara.
    /// <para>
    /// Va <b>vacía mientras el envío está <see cref="EstadoEnvio.Pendiente"/></b> (F4-03): todavía
    /// no ha salido nada, y una fila que dice «aquí están los bytes que salieron» sin que haya salido
    /// ningún byte sería una afirmación falsa. La llena el trabajo que manda, con lo que compone.
    /// </para>
    /// </summary>
    public byte[] PdfCopia { get; set; } = [];

    /// <summary>
    /// Nombre con el que se adjuntó. Va vacío mientras el envío está <see cref="EstadoEnvio.Pendiente"/>
    /// por la misma razón que la copia: todavía no se ha compuesto el archivo.
    /// </summary>
    public string NombreArchivo { get; set; } = string.Empty;

    /// <summary>
    /// El <c>modificada_at</c> que tenía el documento <b>en el momento del envío</b>, o <c>NULL</c>
    /// si no lo tiene (un recibo, un contrato). Comparado con el <c>modificada_at</c> de ahora es lo
    /// que permite saber si alguien tocó el documento después de mandarlo (F3-08), y es lo que
    /// filtra <c>modificadasTrasEnvio</c> de §3.13.
    /// </summary>
    public DateTimeOffset? DocumentoModificadoAt { get; set; }

    /// <summary>Cuántas veces se ha intentado mandar este documento a este destinatario. Empieza en 1.</summary>
    public int Intentos { get; set; } = 1;

    /// <summary>
    /// Motivo del último fallo, con el criterio de F3-01 (DOC_TECNICA §8.0.1): solo el mensaje de una
    /// excepción propia; de cualquier otra, solo el tipo. Nunca lleva usuario, clave ni cadena de
    /// conexión.
    /// </summary>
    public string? UltimoError { get; set; }

    /// <summary>Cuándo lo aceptó el servidor de correo.</summary>
    public DateTimeOffset? EnviadoAt { get; set; }

    /// <summary>
    /// <c>NULL</c> hasta que exista la integración que lo confirme. F3-08 lo dejó escrito a
    /// propósito: SMTP no dice si un mensaje llegó a su destinatario, solo que el servidor lo aceptó.
    /// Poner «entregado» ahí sería una afirmación que nadie ha comprobado.
    /// <c>TODO(A#)</c> cuando exista el webhook de confirmación del proveedor (o el enlace de
    /// seguimiento), que es lo único que puede escribir estos dos estados.
    /// </summary>
    public DateTimeOffset? EntregadoAt { get; set; }

    /// <summary>
    /// Envío masivo al que pertenece esta fila, o <c>NULL</c> si salió de un envío suelto (F4-03).
    /// <para>
    /// <b>Un lote no es una tabla.</b> El lote es «el conjunto de envíos que comparten este
    /// identificador»: el progreso, el recuento y el reintento son agregaciones sobre
    /// <c>envio</c> filtradas por esta columna. Lo que de verdad hay que guardar del lote —quién lo
    /// pidió, con qué filtros y cuándo lo confirmó— lo escribe la auditoría
    /// (<c>envio.masivo.confirmado</c>), que es append-only y ya existe para eso (AGENTS.md §6). Una
    /// tabla de lotes sería una segunda fuente de lo mismo, y este repo ya pagó por ese problema en
    /// BUG-007.
    /// </para>
    /// </summary>
    public Guid? LoteId { get; set; }

    /// <summary>Cuándo se confirmó la apertura. <c>NULL</c> por el mismo motivo que <see cref="EntregadoAt"/>.</summary>
    public DateTimeOffset? LeidoAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}