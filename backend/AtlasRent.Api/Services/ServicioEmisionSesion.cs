using AtlasRent.Api.Data;

namespace AtlasRent.Api.Services;

/// <summary>
/// Abre una sesión completa: cookie de acceso, fila de <c>sesion</c>, auditoría y reinicio de los
/// contadores de bloqueo.
/// <para>
/// No abre transacción propia: se llama dentro de la transacción del llamante, para que la sesión
/// y su auditoría se escriban juntas (AGENTS.md §6). Los tres caminos que abren sesión (login
/// normal, segundo factor y acceso de emergencia) pasan por aquí para que ninguno se olvide de
/// auditar o de reiniciar el contador de fallos.
/// </para>
/// </summary>
public sealed class ServicioEmisionSesion(
    AtlasRentDbContext contexto,
    ServicioSesiones sesiones,
    EmisorTokens emisor,
    ServicioBloqueos bloqueos,
    AuditoriaService auditoria,
    TimeProvider reloj)
{
    /// <param name="modoEmergencia">
    /// La sesión no viene del segundo factor normal sino de un acceso de emergencia. Queda marcada
    /// en la fila de sesión y en la auditoría (F1-06).
    /// </param>
    /// <param name="accion">Acción de auditoría que explica por qué se abre esta sesión.</param>
    public async Task<ResultadoSesion> EmitirAsync(
        Usuario usuario,
        string ip,
        string? userAgent,
        bool modoEmergencia,
        string accion,
        CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var sesionId = Guid.NewGuid();
        var tokens = emisor.Emitir(usuario.Id, sesionId, Roles.Valor);

        usuario.UltimoAcceso = ahora;
        await contexto.SaveChangesAsync(ct);

        await sesiones.EmitirAsync(
            sesionId,
            usuario.Id,
            sesionId,
            EmisorTokens.HashToken(tokens.TokenRefresco),
            ip,
            userAgent,
            ahora.AddHours(ServicioSesiones.MaximoHoras),
            ultimaActividad: ahora,
            modoEmergencia: modoEmergencia,
            ct);

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = accion,
            Ip = ip,
            UsuarioId = usuario.Id,
            Entidad = "sesion",
            EntidadId = sesionId.ToString("D"),
            DatosJson = modoEmergencia ? """{"modo":"emergencia"}""" : null,
            Resultado = "permitido",
            Origen = "api",
        }, ct);

        // También es la fuente de verdad de los contadores de bloqueo, y va en la misma
        // transacción que la sesión.
        await bloqueos.AnotarExitoAsync(usuario.Id, usuario.NombreAcceso, ip, ct);

        return new ResultadoSesion(
            usuario.Id,
            sesionId,
            usuario.NombreAcceso,
            usuario.Nombre,
            usuario.Email,
            usuario.DebeCambiarPassword,
            usuario.MfaActivado,
            usuario.MfaPendiente,
            modoEmergencia,
            usuario.UltimoAcceso,
            tokens);
    }
}