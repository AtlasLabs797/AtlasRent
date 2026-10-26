using AtlasRent.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AtlasRent.Api.Services;

/// <summary>Estado de una sesión tal y como se necesita para validar una petición.</summary>
/// <param name="Activo">Falso si el usuario está desactivado o borrado lógicamente.</param>
public sealed record EstadoSesion(
    Guid UsuarioId,
    bool Activo,
    DateTimeOffset UltimaActividad,
    DateTimeOffset CaducaAbsoluta,
    DateTimeOffset? RevocadaAt,
    bool DebeCambiarPassword);

/// <summary>Resultado de validar una sesión: si vale y, si no, el código de error con el que responde.</summary>
/// <param name="DebeCambiarPassword">Sesión válida, pero con el cambio de contraseña pendiente (SEC-015).</param>
public sealed record ResultadoValidacionSesion(
    bool Valida,
    string? CodigoReferencia = null,
    string? Detalle = null,
    bool DebeCambiarPassword = false)
{
    public static ResultadoValidacionSesion Correcta => new(true);

    public static ResultadoValidacionSesion Rechazada(string codigo, string detalle) => new(false, codigo, detalle);
}

/// <summary>
/// Sesiones abiertas: emisión, búsqueda, revocación y validación.
/// <para>
/// La validación se hace contra la tabla <c>sesion</c> en cada petición, con una caché de 30
/// segundos: es lo que hace que desactivar un usuario corte el acceso en la siguiente petición en
/// lugar de esperar a que caduque el JWT (docs/SEGURIDAD.md SEC-003).
/// </para>
/// </summary>
public sealed class ServicioSesiones(
    AtlasRentDbContext contexto,
    TimeProvider reloj,
    IMemoryCache cache,
    AuditoriaService auditoria)
{
    /// <summary>Inactividad máxima (docs/SEGURIDAD.md A.2).</summary>
    public const int InactividadMinutos = 30;

    /// <summary>Máximo absoluto de una sesión, heredado por los tokens rotados.</summary>
    public const int MaximoHoras = 12;

    /// <summary>Margen máximo de la caché de sesión, en segundos.</summary>
    public const int CacheSegundos = 30;

    private const string PrefijoCache = "sesion:";

    private static string ClaveCache(Guid sesionId) => $"{PrefijoCache}{sesionId:D}";

    /// <summary>
    /// Crea la fila de sesión. El identificador lo fija el llamante porque es el <c>sid</c> del JWT,
    /// que se emite antes de insertar.
    /// </summary>
    /// <param name="ultimaActividad">
    /// Se hereda de la sesión anterior al rotar: refrescar el token no es actividad del usuario, y
    /// si lo fuera, una sesión inactiva se mantendría viva solo refrescando.
    /// </param>
    public async Task<Sesion> EmitirAsync(
        Guid sesionId,
        Guid usuarioId,
        Guid familiaId,
        string hashToken,
        string ip,
        string? userAgent,
        DateTimeOffset caducaAbsoluta,
        DateTimeOffset ultimaActividad,
        bool modoEmergencia = false,
        CancellationToken ct = default)
    {
        var ahora = reloj.GetUtcNow();
        var sesion = new Sesion
        {
            Id = sesionId,
            UsuarioId = usuarioId,
            TokenHash = hashToken,
            FamiliaId = familiaId,
            Ip = ip,
            UserAgent = userAgent,
            EmitidaAt = ahora,
            UltimaActividad = ultimaActividad,
            CaducaAbsoluta = caducaAbsoluta,
            ModoEmergencia = modoEmergencia,
        };

        contexto.Sesion.Add(sesion);
        await contexto.SaveChangesAsync(ct);

        return sesion;
    }

    /// <summary>
    /// Valida la sesión de una petición. La caché solo evita ir a la base: las reglas de tiempo se
    /// evalúan siempre con el reloj actual, de modo que una entrada en caché no puede alargar la
    /// sesión más allá de la inactividad o del máximo absoluto.
    /// </summary>
    public async Task<ResultadoValidacionSesion> ValidarAsync(Guid sesionId, CancellationToken ct)
    {
        var estado = await cache.GetOrCreateAsync<EstadoSesion?>(
            ClaveCache(sesionId),
            async _ => await LeerAsync(sesionId, ct),
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(CacheSegundos) });

        if (estado is null)
        {
            return ResultadoValidacionSesion.Rechazada(Problemas.SesionNoValida, "La sesión no existe.");
        }

        if (estado.RevocadaAt is not null)
        {
            return ResultadoValidacionSesion.Rechazada(Problemas.SesionNoValida, "La sesión está revocada.");
        }

        if (!estado.Activo)
        {
            return ResultadoValidacionSesion.Rechazada(Problemas.SesionNoValida, "El usuario está desactivado.");
        }

        var ahora = reloj.GetUtcNow();
        if (ahora > estado.CaducaAbsoluta)
        {
            await RevocarAsync(sesionId, MotivosRevocacion.MaximoAbsoluto, estado.UsuarioId, ahora, ct);
            return ResultadoValidacionSesion.Rechazada(
                Problemas.SesionExpirada, "La sesión superó el máximo absoluto de 12 horas.");
        }

        if (ahora - estado.UltimaActividad > TimeSpan.FromMinutes(InactividadMinutos))
        {
            await RevocarAsync(sesionId, MotivosRevocacion.Inactividad, estado.UsuarioId, ahora, ct);
            return ResultadoValidacionSesion.Rechazada(
                Problemas.SesionExpirada, "La sesión superó los 30 minutos de inactividad.");
        }

        return new ResultadoValidacionSesion(true, DebeCambiarPassword: estado.DebeCambiarPassword);
    }

    /// <summary>Busca la fila de sesión de un token de refresco (por el hash SHA-256 del token).</summary>
    public Task<Sesion?> BuscarPorTokenHashAsync(string hashToken, CancellationToken ct) =>
        contexto.Sesion.Include(s => s.Usuario).SingleOrDefaultAsync(s => s.TokenHash == hashToken, ct);

    public Task<Sesion?> BuscarAsync(Guid sesionId, CancellationToken ct) =>
        contexto.Sesion.Include(s => s.Usuario).SingleOrDefaultAsync(s => s.Id == sesionId, ct);

    public void InvalidarCache(Guid sesionId) => cache.Remove(ClaveCache(sesionId));

    /// <summary>
    /// Marca como revocadas todas las sesiones vivas del usuario (SEC-019) dentro de la transacción
    /// del llamante, que es quien audita. Devuelve sus identificadores: el llamante debe pasarlos a
    /// <see cref="InvalidarCache(IEnumerable{Guid})"/> <b>después</b> del commit, porque invalidar
    /// antes dejaría que una petición concurrente volviera a cachear el estado sin revocar.
    /// </summary>
    /// <param name="excepto">Sesión que se conserva (la desde la que el usuario cambia su contraseña).</param>
    public async Task<IReadOnlyList<Guid>> RevocarTodasDelUsuarioAsync(
        Guid usuarioId,
        string motivo,
        Guid? excepto,
        CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var vivas = await contexto.Sesion
            .Where(s => s.UsuarioId == usuarioId && s.RevocadaAt == null && s.Id != excepto)
            .ToListAsync(ct);

        foreach (var sesion in vivas)
        {
            sesion.RevocadaAt = ahora;
            sesion.MotivoRevocacion = motivo;
        }

        await contexto.SaveChangesAsync(ct);

        return [.. vivas.Select(s => s.Id)];
    }

    public void InvalidarCache(IEnumerable<Guid> sesionIds)
    {
        foreach (var id in sesionIds)
        {
            InvalidarCache(id);
        }
    }

    /// <summary>Revoca la familia entera: el token robado y todos los que salieron de él.</summary>
    public async Task RevocarFamiliaAsync(Guid familiaId, string motivo, Guid usuarioId, string ip, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        var deLaFamilia = await contexto.Sesion
            .Where(s => s.FamiliaId == familiaId && s.RevocadaAt == null)
            .ToListAsync(ct);

        foreach (var sesion in deLaFamilia)
        {
            sesion.RevocadaAt = ahora;
            sesion.MotivoRevocacion = motivo;
            cache.Remove(ClaveCache(sesion.Id));
        }

        await contexto.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = $"sesion.revocada.{motivo}",
            Ip = ip,
            UsuarioId = usuarioId,
            Entidad = "sesion",
            EntidadId = familiaId.ToString("D"),
            DatosJson = $$"""{"sesiones_revocadas":{{deLaFamilia.Count}}}""",
            Resultado = "permitido",
            Origen = "api",
        }, ct);
        await transaccion.CommitAsync(ct);
    }

    /// <summary>Revoca una sesión concreta y deja constancia en auditoría, en la misma transacción.</summary>
    public async Task RevocarAsync(
        Guid sesionId,
        string motivo,
        Guid usuarioId,
        DateTimeOffset ahora,
        CancellationToken ct)
    {
        cache.Remove(ClaveCache(sesionId));

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);
        var sesion = await BuscarAsync(sesionId, ct);
        if (sesion is null)
        {
            await transaccion.RollbackAsync(ct);
            return;
        }

        if (sesion.RevocadaAt is null)
        {
            sesion.RevocadaAt = ahora;
            sesion.MotivoRevocacion = motivo;
            await contexto.SaveChangesAsync(ct);
        }

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = $"sesion.revocada.{motivo}",
            Ip = sesion.Ip,
            UsuarioId = usuarioId,
            Entidad = "sesion",
            EntidadId = sesion.Id.ToString("D"),
            Resultado = "permitido",
            Origen = "api",
        }, ct);
        await transaccion.CommitAsync(ct);
    }

    private async Task<EstadoSesion?> LeerAsync(Guid sesionId, CancellationToken ct)
    {
        var sesion = await BuscarAsync(sesionId, ct);
        if (sesion is null)
        {
            return null;
        }

        var usuario = sesion.Usuario;
        var activo = usuario is not null && usuario.Activo && usuario.DeletedAt is null;

        // La escritura de última actividad va aquí y no en cada petición: la caché la limita a una
        // vez cada 30 segundos por sesión.
        if (sesion.RevocadaAt is null && activo)
        {
            var ahora = reloj.GetUtcNow();
            if (ahora - sesion.UltimaActividad > TimeSpan.FromSeconds(CacheSegundos))
            {
                sesion.UltimaActividad = ahora;
                await contexto.SaveChangesAsync(ct);
            }
        }

        return new EstadoSesion(
            sesion.UsuarioId,
            activo,
            sesion.UltimaActividad,
            sesion.CaducaAbsoluta,
            sesion.RevocadaAt,
            usuario?.DebeCambiarPassword ?? false);
    }
}