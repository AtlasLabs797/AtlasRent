using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>Datos de la sesión abierta por un login o un refresco correctos.</summary>
public sealed record ResultadoSesion(
    Guid UsuarioId,
    Guid SesionId,
    string NombreAcceso,
    string Nombre,
    string? Email,
    string Rol,
    bool DebeCambiarPassword,
    bool MfaActivado,
    bool MfaPendiente,
    bool AccesoEmergencia,
    DateTimeOffset? UltimoAcceso,
    TokensEmitidos Tokens);

/// <summary>
/// Lo que devuelve el login cuando la contraseña es correcta.
/// <para>
/// O bien hay <see cref="Sesion"/> (login completo), o bien <c>RequiereMfa</c>: la contraseña era
/// correcta pero falta el segundo factor y todavía no se abre ninguna sesión.
/// </para>
/// </summary>
public sealed record ResultadoAutenticacion(Usuario Usuario, ResultadoSesion? Sesion, bool RequiereMfa);

/// <summary>
/// Login, refresco y cierre de sesión (docs/DOC_TECNICA.md §7).
/// <para>
/// El login nunca distingue entre "usuario no existe" y "contraseña incorrecta": misma respuesta,
/// mismo código de referencia y, además, el mismo trabajo de verificación para que el tiempo de
/// respuesta tampoco lo distinga.
/// </para>
/// </summary>
public sealed class ServicioAutenticacion(
    AtlasRentDbContext contexto,
    ServicioContrasenas contrasenas,
    ServicioSesiones sesiones,
    ServicioEmisionSesion emision,
    ServicioMfa mfa,
    EmisorTokens emisor,
    IPolicyService politica,
    TimeProvider reloj,
    AuditoriaService auditoria,
    ServicioBloqueos bloqueos)
{
    /// <summary>
    /// Intenta iniciar sesión. Devuelve <c>null</c> para cualquier credencial que no sirva: usuario
    /// inexistente, contraseña incorrecta, cuenta desactivada o borrada.
    /// <para>
    /// Con la contraseña correcta y segundo factor pendiente devuelve <see cref="ResultadoAutenticacion"/>
    /// sin sesión: el token provisional lo emite <see cref="ServicioMfa.EmitirDesafioAsync"/>.
    /// </para>
    /// </summary>
    public async Task<ResultadoAutenticacion?> LoginAsync(
        string nombreAcceso,
        string contrasena,
        string ip,
        string? userAgent,
        CancellationToken ct)
    {
        var normalizado = Usuario.NormalizarNombreAcceso(nombreAcceso);

        // La verificación de la contraseña va fuera de la transacción: BCrypt tarda y no hace falta
        // tener bloqueada la fila de usuario mientras tanto.
        var candidato = await contexto.Usuario
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.NombreAcceso == normalizado, ct);

        if (candidato is null)
        {
            contrasenas.CompararContraHashDeComparacion(contrasena);
            // Sin usuario no hay cuenta que bloquear, pero el intento cuenta para la IP.
            await bloqueos.RegistrarIntentoFallidoAsync(null, normalizado, ip, ct);
            return null;
        }

        // Cuenta bloqueada: mismo trabajo de verificación y misma respuesta que una contraseña
        // incorrecta, para que ni el tiempo de respuesta ni el código digan que está bloqueada.
        if (bloqueos.CuentaBloqueada(candidato))
        {
            contrasenas.Verificar(contrasena, candidato.PasswordHash);
            await bloqueos.RegistrarIntentoFallidoAsync(candidato.Id, normalizado, ip, ct);
            return null;
        }

        if (!contrasenas.Verificar(contrasena, candidato.PasswordHash)
            || !candidato.Activo
            || candidato.DeletedAt is not null)
        {
            await bloqueos.RegistrarIntentoFallidoAsync(candidato.Id, normalizado, ip, ct);
            return null;
        }

        // Segundo factor obligatorio: la contraseña no abre sesión por sí sola. Ni siquiera se
        // anota el intento como acertado: el login todavía no está completo, y anotarlo como
        // acierto reiniciaría el contador de fallos y dejaría probar códigos TOTP sin límite.
        if (await mfa.NecesitaMfaAsync(candidato, ct))
        {
            return new ResultadoAutenticacion(candidato, Sesion: null, RequiereMfa: true);
        }

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        var usuario = await contexto.Usuario.SingleAsync(u => u.Id == candidato.Id, ct);

        var resultado = await emision.EmitirAsync(usuario, ip, userAgent, modoEmergencia: false, "auth.login", ct);

        await transaccion.CommitAsync(ct);

        return new ResultadoAutenticacion(usuario, resultado, RequiereMfa: false);
    }

    /// <summary>
    /// Rota el token de refresco. Si llega un token que ya se rotó, es que alguien copió uno de los
    /// tokens de la cadena: se revoca la familia completa y se responde con el mismo error que un
    /// token desconocido (docs/SEGURIDAD.md A.2).
    /// </summary>
    public async Task<ResultadoSesion?> RefrescarAsync(
        string tokenRefresco,
        string ip,
        string? userAgent,
        CancellationToken ct)
    {
        var hash = EmisorTokens.HashToken(tokenRefresco);
        var anterior = await sesiones.BuscarPorTokenHashAsync(hash, ct);
        if (anterior is null)
        {
            return null;
        }

        var usuario = anterior.Usuario;

        // Token ya rotado: es reutilización de un token de la familia. Se cae la familia entera.
        if (anterior.RevocadaAt is not null && anterior.MotivoRevocacion == MotivosRevocacion.Rotacion)
        {
            await sesiones.RevocarFamiliaAsync(
                anterior.FamiliaId,
                MotivosRevocacion.ReutilizacionDetectada,
                anterior.UsuarioId,
                ip,
                ct);
            return null;
        }

        var ahora = reloj.GetUtcNow();
        if (anterior.RevocadaAt is not null
            || usuario is null
            || !usuario.Activo
            || usuario.DeletedAt is not null)
        {
            return null;
        }

        // Mismo orden que en la validación de cada petición: primero el máximo absoluto y luego la
        // inactividad, para que el motivo de la revocación diga cuál de las dos se superó.
        var caducada = ahora > anterior.CaducaAbsoluta
            ? MotivosRevocacion.MaximoAbsoluto
            : ahora - anterior.UltimaActividad > TimeSpan.FromMinutes(ServicioSesiones.InactividadMinutos)
                ? MotivosRevocacion.Inactividad
                : null;

        if (caducada is not null)
        {
            await sesiones.RevocarFamiliaAsync(
                anterior.FamiliaId, caducada, anterior.UsuarioId, ip, ct);
            return null;
        }

        var sesionId = Guid.NewGuid();
        var rol = await politica.RolDeAsync(usuario.Id, ct) ?? string.Empty;
        var tokens = emisor.Emitir(usuario.Id, sesionId, rol);

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        // Rotar invalida el token anterior, y esa marca es la que permite detectar su reutilización.
        anterior.RevocadaAt = ahora;
        anterior.MotivoRevocacion = MotivosRevocacion.Rotacion;
        await contexto.SaveChangesAsync(ct);
        sesiones.InvalidarCache(anterior.Id);

        // El máximo absoluto lo hereda la sesión rotada: refrescar no alarga los 12 horas.
        await sesiones.EmitirAsync(
            sesionId,
            usuario.Id,
            anterior.FamiliaId,
            EmisorTokens.HashToken(tokens.TokenRefresco),
            ip,
            userAgent,
            anterior.CaducaAbsoluta,
            // Y la inactividad también: rotar el token no cuenta como actividad del usuario.
            anterior.UltimaActividad,
            // Refrescar no borra el rastro: una sesión abierta por emergencia sigue siéndolo.
            anterior.ModoEmergencia,
            ct);

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = "auth.refresco",
            Ip = ip,
            UsuarioId = usuario.Id,
            Entidad = "sesion",
            EntidadId = sesionId.ToString("D"),
            Resultado = "permitido",
            Origen = "api",
        }, ct);
        await transaccion.CommitAsync(ct);

        return new ResultadoSesion(
            usuario.Id,
            sesionId,
            usuario.NombreAcceso,
            usuario.Nombre,
            usuario.Email,
            rol,
            usuario.DebeCambiarPassword,
            usuario.MfaActivado,
            usuario.MfaPendiente,
            anterior.ModoEmergencia,
            usuario.UltimoAcceso,
            tokens);
    }

    /// <summary>Cierre de sesión: revoca la sesión actual y las que salieron de ella.</summary>
    public async Task CerrarAsync(Guid sesionId, CancellationToken ct)
    {
        var sesion = await sesiones.BuscarAsync(sesionId, ct);
        if (sesion is null)
        {
            return;
        }

        await sesiones.RevocarFamiliaAsync(
            sesion.FamiliaId, MotivosRevocacion.Logout, sesion.UsuarioId, sesion.Ip, ct);
    }

    /// <summary>Identidad del usuario de la petición, para <c>/api/v1/auth/yo</c>.</summary>
    public async Task<Usuario?> IdentidadAsync(Guid usuarioId, CancellationToken ct) =>
        await contexto.Usuario.AsNoTracking().SingleOrDefaultAsync(u => u.Id == usuarioId, ct);

    /// <summary>
    /// Si la sesión se abrió por un acceso de emergencia (F1-06). Va en la fila de sesión, no en el
    /// usuario: el rastro es de aquella sesión concreta.
    /// </summary>
    public async Task<bool> AccesoEmergenciaDeLaSesionAsync(Guid sesionId, CancellationToken ct) =>
        await contexto.Sesion
            .AsNoTracking()
            .Where(s => s.Id == sesionId)
            .Select(s => s.ModoEmergencia)
            .SingleOrDefaultAsync(ct);
}