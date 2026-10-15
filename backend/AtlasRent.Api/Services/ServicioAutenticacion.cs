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
    bool DebeCambiarPassword,
    bool MfaActivado,
    DateTimeOffset? UltimoAcceso,
    TokensEmitidos Tokens);

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
    EmisorTokens emisor,
    TimeProvider reloj,
    AuditoriaService auditoria)
{
    /// <summary>
    /// Intenta iniciar sesión. Devuelve <c>null</c> para cualquier credencial que no sirva: usuario
    /// inexistente, contraseña incorrecta, cuenta desactivada o borrada.
    /// </summary>
    public async Task<ResultadoSesion?> LoginAsync(
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
            return null;
        }

        if (!contrasenas.Verificar(contrasena, candidato.PasswordHash)
            || !candidato.Activo
            || candidato.DeletedAt is not null)
        {
            return null;
        }

        var ahora = reloj.GetUtcNow();
        var sesionId = Guid.NewGuid();
        var tokens = emisor.Emitir(candidato.Id, sesionId, Roles.Valor);

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        var usuario = await contexto.Usuario.SingleAsync(u => u.Id == candidato.Id, ct);
        usuario.UltimoAcceso = ahora;

        await sesiones.EmitirAsync(
            sesionId,
            usuario.Id,
            sesionId,
            EmisorTokens.HashToken(tokens.TokenRefresco),
            ip,
            userAgent,
            ahora.AddHours(ServicioSesiones.MaximoHoras),
            ultimaActividad: ahora,
            ct);

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = "auth.login",
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
            usuario.DebeCambiarPassword,
            usuario.MfaActivado,
            usuario.UltimoAcceso,
            tokens);
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
        var tokens = emisor.Emitir(usuario.Id, sesionId, Roles.Valor);

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
            usuario.DebeCambiarPassword,
            usuario.MfaActivado,
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
}