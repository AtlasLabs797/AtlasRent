namespace AtlasRent.Api.Data;

/// <summary>
/// Token de un agente externo: la credencial con la que un asistente consulta la API sin sesión de
/// navegador (F6-01, docs/DOC_TECNICA.md §7).
/// <para>
/// Del token solo se guarda el hash SHA-256, igual que con el token de refresco y con el de
/// recuperación. El en claro se entrega una sola vez, en la respuesta del alta, y no vuelve a salir
/// de ahí (docs/SEGURIDAD.md A.2).
/// </para>
/// <para>
/// El token no concede permisos por sí mismo: corre <b>como su usuario propietario</b>, con su rol y
/// con los permisos que ese usuario tenga recortados. <see cref="Acciones"/> es la lista de
/// operaciones que el agente puede pedir, y siempre dentro del techo del propietario.
/// </para>
/// <para>
/// No se borra nunca: revocar es poner <see cref="Revocado"/>, para que la auditoría siga explicando
/// qué token hubo y desde cuándo dejó servir (AGENTS.md §6).
/// </para>
/// </summary>
public sealed class AgenteToken
{
    public Guid Id { get; set; }

    /// <summary>Usuario cuyo rol y cuyos límites son los de este token.</summary>
    public Guid UsuarioId { get; set; }

    public Usuario? Usuario { get; set; }

    /// <summary>Nombre con el que el administrador reconoce el token en el listado.</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>SHA-256 en hexadecimal del token. Único: es la búsqueda en cada petición.</summary>
    public string HashToken { get; set; } = string.Empty;

    /// <summary>
    /// Acciones que este token puede pedir. Es un recorte, nunca una ampliación: si una de estas
    /// acciones no está en el techo del propietario, la petición sale denegada.
    /// </summary>
    public string[] Acciones { get; set; } = [];

    public DateTimeOffset Creado { get; set; }

    /// <summary>
    /// Cuándo deja de valer. Nula significa que no caduca por tiempo: entonces lo decide el
    /// administrador revocándolo.
    /// </summary>
    public DateTimeOffset? Caduca { get; set; }

    /// <summary>Cuándo se revocó. Con este valor el token no vale para nada más.</summary>
    public DateTimeOffset? Revocado { get; set; }

    /// <summary>Última petición atendida con este token, aunque saliera denegada.</summary>
    public DateTimeOffset? UltimoUso { get; set; }

    /// <summary>
    /// Consultas por día permitidas. Lo fija el administrador al emitir el token.
    /// <para>
    /// TODO(F6-02): hoy la columna se guarda y se muestra, pero el corte por día lo implementa el
    /// trabajo de la base de consultas de solo lectura.
    /// </para>
    /// </summary>
    public int LimiteConsultasDia { get; set; }
}