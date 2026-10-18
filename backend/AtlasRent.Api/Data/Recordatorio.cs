using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Un aviso de pago por cliente y unidad, que es como §3.14 los quiere: «un mensaje por cliente y
/// unidad». La fila es la <b>decisión</b> del trabajo del día, no el correo.
/// <para>
/// <b>Por qué esta tabla y por qué no cabe en <see cref="Envio"/>.</b> El envío de F4-02 es un
/// documento con su número, y su antidiplicado es «(documento, año, destinatario)». Un recordatorio
/// no es un documento —no se numera ni se archiva— y su clave de deduplicación es otra:
/// <c>(cliente_id, unidad_id, fecha)</c>. Meterlo en <c>envio</c> obligaría a inventar un
/// «documento» que no existe solo para poder guardar una terna que tampoco cabe en su clave.
/// </para>
/// <para>
/// <b>Ese único es la idempotencia del trabajo.</b> Correr el trabajo dos veces el mismo día no
/// puede mandar dos avisos (§8.0), igual que en F3-05 y F2-06: la fila existe o no existe, y por
/// eso el trabajo escribe primero y solo manda después. Es también la razón de que la columna
/// <c>unidad_id</c> pueda ser nula y de que el índice sea <c>NULLS NOT DISTINCT</c>: un cliente sin
/// contrato con unidad tiene avisos sin unidad, y sin eso dos avisos del mismo día a ese cliente
/// entrarían los dos.
/// </para>
/// <para>
/// No lleva importe. Lo que se dice al cliente va escrito en el cuerpo del <see cref="Envio"/> al que
/// dio lugar, y lo que se le attacha va en <c>envio.pdf_copia</c>: dos copias del mismo dato que se
/// pueden desincronizar y que nadie lee.
/// </para>
/// </summary>
public sealed class Recordatorio : IEntidadTrazada
{
    public Guid Id { get; set; }

    public Guid ClienteId { get; set; }

    public Cliente? Cliente { get; set; }

    /// <summary>
    /// Nula si el cliente no tiene contrato con unidad (§3.14 habla de cliente y unidad, y hay
    /// clientes que no tienen ninguna).
    /// </summary>
    public Guid? UnidadId { get; set; }

    public Unidad? Unidad { get; set; }

    /// <summary>
    /// La fecha de negocio del aviso, no la del instante en que se escribió: es parte de la clave de
    /// deduplicación y por eso no cambia aunque el trabajo corra a las 09:05 y luego a las 23:30.
    /// </summary>
    public DateOnly Fecha { get; set; }

    public EstadoRecordatorio Estado { get; set; }

    /// <summary>El envío que este aviso produjo, o <c>null</c> si no llegó a componerse.</summary>
    public Guid? EnvioId { get; set; }

    public Envio? Envio { get; set; }

    /// <summary>
    /// Por qué no se mandó, si no se mandó. No es el error del correo: el fallo del envío ya está en
    /// <c>envio.ultimo_error</c>. Aquí va el motivo de la decisión («ya está pagado», «el cliente tiene
    /// los recordatorios pausados»), que es de este servicio.
    /// </summary>
    public string? MotivoOmision { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}