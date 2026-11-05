namespace AtlasRent.Api.Dominio;

/// <summary>
/// Estado de un aviso de pago (docs/ESPECIFICACION.md §3.14, D12).
/// <para>
/// Son tres y no cinco como en <see cref="EstadoEnvio"/>: un recordatorio no es un documento que se
/// mande y del que haya que saber qué pasó, sino una <b>decisión</b> del trabajo del día. No hay
/// «entregado» ni «leído» porque nadie va a mirar si el cliente abrió el aviso antes de pagar, y no
/// hay «fallido» porque el fallo es del <see cref="Envio"/> que salió de él, que ya tiene su estado.
/// </para>
/// <para>
/// <c>Omitido</c> no es un fallo: es el recordatorio que se decidió <b>no</b> mandar, y por eso
/// guarda el motivo (<c>motivo_omision</c>) y la fila se queda para que se pueda ver qué pasó.
/// </para>
/// </summary>
public enum EstadoRecordatorio
{
    /// <summary>Preparado y en la cola. En modo manual, así se queda hasta que alguien lo manda.</summary>
    Pendiente = 1,

    /// <summary>Salió. El envío al que dio lugar está en <c>Enviado</c> o posterior.</summary>
    Enviado = 2,

    /// <summary>Se decidió no mandarlo, y <c>motivo_omision</c> dice por qué.</summary>
    Omitido = 3,
}