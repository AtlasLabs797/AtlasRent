namespace AtlasRent.Api.Data;

using AtlasRent.Api.Dominio;

/// <summary>
/// Texto de un correo por tipo de mensaje y por idioma (docs/ESPECIFICACION.md §3.14). Lo edita
/// solo el administrador (<c>plantilla.editar</c>) desde <c>/api/v1/config/plantillas-mensaje</c>.
/// <para>
/// El cuerpo es <b>texto plano</b>, igual que el PDF se genera por código y no con HTML y navegador
/// (SEC-001, docs/SEGURIDAD.md): el texto del cliente se pone literal, sin escapar, y un correo en
/// texto plano no puede ejecutar nada ni pedir nada por su cuenta. La sustitución de las variables
/// es textual y no interpreta nada.
/// </para>
/// <para>
/// Las variables admitidas son <c>{cliente}</c>, <c>{numero}</c>, <c>{total}</c>, <c>{pendiente}</c>,
/// <c>{vencimiento}</c> y <c>{unidad}</c>. Una variable que el documento no tiene se sustituye por
/// una cadena vacía: es preferible un hueco visible a un marcador literal como «{total}» en un
/// correo que llega al cliente.
/// </para>
/// </summary>
public sealed class PlantillaMensaje : IEntidadTrazada
{
    public Guid Id { get; set; }

    public TipoMensajeEnvio Tipo { get; set; }

    /// <summary>Código de idioma tal y como va en <c>idioma</c> de <c>envio</c>. La siembra escribe «es».</summary>
    public string Idioma { get; set; } = "es";

    public string Asunto { get; set; } = string.Empty;

    public string Cuerpo { get; set; } = string.Empty;

    /// <summary>
    /// Desactivada sigue existiendo pero no se usa. No se borra nunca: un envío viejo dice qué
    /// plantilla encontró al salir, y si la plantilla desaparece ese envío queda sin explicación.
    /// </summary>
    public bool Activa { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}