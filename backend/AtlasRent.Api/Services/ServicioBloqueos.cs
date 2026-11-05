using AtlasRent.Api.DTOs;
using AtlasRent.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Bloqueo de cuenta y de IP por intentos fallidos de login (docs/SEGURIDAD.md A.2,
/// docs/DOC_TECNICA.md §7).
/// <para>
/// Los contadores no están en columnas de contador: salen de <see cref="IntentoLogin"/>. Los fallos
/// de una cuenta son los suyos desde el último acierto, y los de una IP son los suyos dentro de la
/// ventana. Así no hay dos fuentes de verdad que se puedan desincronizar.
/// </para>
/// </summary>
public sealed class ServicioBloqueos(
    AtlasRentDbContext contexto,
    OpcionesSeguridad seguridad,
    OpcionesLimites limites,
    TimeProvider reloj,
    AuditoriaService auditoria,
    ServicioAvisosAcceso avisosAcceso)
{
    /// <summary>Primer escalón: una hora.</summary>
    public static readonly TimeSpan PrimerNivel = TimeSpan.FromHours(1);

    /// <summary>Segundo escalón: un día.</summary>
    public static readonly TimeSpan SegundoNivel = TimeSpan.FromHours(24);

    /// <summary>Nivel a partir del cual el bloqueo no caduca y solo lo levanta el administrador.</summary>
    public const int NivelIndefinido = 3;

    /// <summary>Si la cuenta está bloqueada ahora mismo.</summary>
    public bool CuentaBloqueada(Usuario usuario) =>
        usuario.BloqueadaHasta is { } hasta && hasta > reloj.GetUtcNow();

    /// <summary>
    /// Si la IP está bloqueada. Una IP de la lista blanca nunca lo está, ni aunque tenga fila.
    /// </summary>
    public async Task<bool> IpBloqueadaAsync(string ip, CancellationToken ct)
    {
        if (seguridad.EsIpPermitida(ip))
        {
            return false;
        }

        var fila = await contexto.BloqueoIp
            .AsNoTracking()
            .SingleOrDefaultAsync(b => b.Ip == ip, ct);

        return fila is not null
            && (fila.BloqueadaHasta is null || fila.BloqueadaHasta > reloj.GetUtcNow());
    }

    /// <summary>IP con bloqueo vigente ahora (las vencidas no se listan). Solo lectura.</summary>
    public async Task<IReadOnlyList<IpBloqueadaResumen>> ListarIpsBloqueadasAsync(CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var filas = await contexto.BloqueoIp
            .AsNoTracking()
            .Where(b => b.BloqueadaHasta == null || b.BloqueadaHasta > ahora)
            .OrderByDescending(b => b.UpdatedAt)
            .Select(b => new IpBloqueadaResumen(
                b.Ip, b.Nivel, b.Fallos, b.UpdatedAt, b.BloqueadaHasta, b.BloqueadaHasta == null))
            .ToListAsync(ct);

        return [.. filas.Where(f => !seguridad.EsIpPermitida(f.Ip))];
    }

    /// <summary>
    /// Cuentas con bloqueo vigente. No hay columna de inicio: se calcula como fin menos la duración
    /// configurada, que es exacto salvo que esa duración se haya cambiado con el bloqueo en curso.
    /// </summary>
    public async Task<IReadOnlyList<CuentaBloqueadaResumen>> ListarCuentasBloqueadasAsync(CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var filas = await contexto.Usuario
            .AsNoTracking()
            .Where(u => u.DeletedAt == null && u.BloqueadaHasta > ahora)
            .OrderBy(u => u.NombreAcceso)
            .Select(u => new { u.Id, u.NombreAcceso, u.Nombre, Hasta = u.BloqueadaHasta!.Value })
            .ToListAsync(ct);

        return [.. filas.Select(u => new CuentaBloqueadaResumen(
            u.Id, u.NombreAcceso, u.Nombre, u.Hasta.AddMinutes(-limites.MinutosBloqueoCuenta), u.Hasta))];
    }

    /// <summary>Lista blanca de IP de la configuración (<c>Seguridad__IpListaBlanca</c>); no se edita por API.</summary>
    public IReadOnlyList<string> ListaBlanca() => [.. seguridad.IpListaBlanca.Order(StringComparer.Ordinal)];

    /// <summary>
    /// Anota un login fallido y aplica lo que corresponda: bloqueo de la cuenta y de la IP.
    /// Abre su propia transacción porque el login fallido no está dentro de ninguna.
    /// </summary>
    public async Task RegistrarIntentoFallidoAsync(
        Guid? usuarioId,
        string? nombreAcceso,
        string ip,
        CancellationToken ct)
    {
        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        await AnotarIntentoAsync(usuarioId, nombreAcceso, ip, exito: false, ct);
        await AplicarBloqueoCuentaAsync(usuarioId, ip, ct);
        await AplicarBloqueoIpAsync(ip, ct);

        await transaccion.CommitAsync(ct);
    }

    /// <summary>
    /// Anota un login correcto. Como el acierto es también un intento, es lo que reinicia el
    /// contador de fallos de la cuenta. Si el usuario ya había entrado y ahora entra desde una IP
    /// distinta, deja el evento de aviso al administrador.
    /// </summary>
    public async Task AnotarExitoAsync(
        Guid usuarioId,
        string nombreAcceso,
        string ip,
        CancellationToken ct)
    {
        // Antes de insertar el intento actual: es el que decide si la IP es nueva.
        var habiaEntrado = await contexto.IntentoLogin.AnyAsync(i => i.UsuarioId == usuarioId && i.Exito, ct);
        var desdeEstaIp = await contexto.IntentoLogin.AnyAsync(
            i => i.UsuarioId == usuarioId && i.Exito && i.Ip == ip, ct);

        await AnotarIntentoAsync(usuarioId, nombreAcceso, ip, exito: true, ct);

        // El primer login de un usuario no es una IP nueva: no hay con qué compararlo.
        if (!habiaEntrado || desdeEstaIp)
        {
            return;
        }

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = "auth.ip_nueva",
            Ip = ip,
            UsuarioId = usuarioId,
            Entidad = "usuario",
            EntidadId = usuarioId.ToString("D"),
            Resultado = "permitido",
            Origen = "api",
        }, ct);

        // SEC-023: alerta en la bandeja del administrador y correo encolado si hay SMTP.
        await avisosAcceso.AvisarIpNuevaAsync(usuarioId, nombreAcceso, ip, ct);
    }

    /// <summary>
    /// Quita el bloqueo de una IP. Solo lo llama quien tenga <c>ip.desbloquear</c> (el
    /// administrador): es la única vía para levantar un bloqueo indefinido, que por definición no
    /// caduca solo. Devuelve si había algo que desbloquear.
    /// </summary>
    public async Task<bool> DesbloquearIpAsync(string ip, Guid usuarioAdmin, CancellationToken ct)
    {
        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        var fila = await contexto.BloqueoIp.SingleOrDefaultAsync(b => b.Ip == ip, ct);
        if (fila is null)
        {
            await transaccion.RollbackAsync(ct);
            return false;
        }

        contexto.BloqueoIp.Remove(fila);
        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = "seg.desbloqueo_ip",
            Ip = ip,
            UsuarioId = usuarioAdmin,
            Entidad = "bloqueo_ip",
            EntidadId = fila.Id.ToString("D"),
            Resultado = "permitido",
            Origen = "api",
        }, ct);

        await transaccion.CommitAsync(ct);
        return true;
    }

    /// <summary>
    /// Levanta el bloqueo de una cuenta. También solo lo llama quien tenga
    /// <c>cuenta.desbloquear</c>: el desbloqueo automático por tiempo lo hace el propio
    /// <see cref="CuentaBloqueada"/> al comprobar que la fecha ya pasó.
    /// </summary>
    public async Task<bool> DesbloquearCuentaAsync(Guid usuarioId, Guid usuarioAdmin, string ip, CancellationToken ct)
    {
        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        var usuario = await contexto.Usuario.SingleOrDefaultAsync(u => u.Id == usuarioId, ct);
        if (usuario is null)
        {
            await transaccion.RollbackAsync(ct);
            return false;
        }

        usuario.BloqueadaHasta = null;
        usuario.DesbloqueadaAt = reloj.GetUtcNow();
        usuario.UpdatedAt = reloj.GetUtcNow();
        usuario.UpdatedBy = usuarioAdmin;
        await contexto.SaveChangesAsync(ct);

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = "seg.desbloqueo_cuenta",
            Ip = ip,
            UsuarioId = usuarioAdmin,
            Entidad = "usuario",
            EntidadId = usuarioId.ToString("D"),
            Resultado = "permitido",
            Origen = "api",
        }, ct);

        await transaccion.CommitAsync(ct);
        return true;
    }

    private async Task AnotarIntentoAsync(
        Guid? usuarioId,
        string? nombreAcceso,
        string ip,
        bool exito,
        CancellationToken ct)
    {
        contexto.IntentoLogin.Add(new IntentoLogin
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            NombreAcceso = nombreAcceso,
            Ip = ip,
            Exito = exito,
            OcurridoAt = reloj.GetUtcNow(),
        });

        await contexto.SaveChangesAsync(ct);
    }

    private async Task AplicarBloqueoCuentaAsync(Guid? usuarioId, string ip, CancellationToken ct)
    {
        if (usuarioId is not { } id)
        {
            return;
        }

        var usuario = await contexto.Usuario.SingleAsync(u => u.Id == id, ct);

        // Un bloqueo vigente no se prorroga: los fallos de mientras tanto no lo alargan.
        if (CuentaBloqueada(usuario))
        {
            return;
        }

        // El contador arranca en lo más reciente entre el último acierto, el fin del último bloqueo
        // y el desbloqueo manual: tras expirar o desbloquear hacen falta otros N fallos, no basta uno.
        var ultimoExito = await contexto.IntentoLogin
            .Where(i => i.UsuarioId == id && i.Exito)
            .MaxAsync(i => (DateTimeOffset?)i.OcurridoAt, ct);
        var desde = new[] { ultimoExito, usuario.BloqueadaHasta, usuario.DesbloqueadaAt }.Max() ?? DateTimeOffset.MinValue;

        var fallos = await contexto.IntentoLogin.CountAsync(
            i => i.UsuarioId == id && !i.Exito && i.OcurridoAt > desde,
            ct);

        if (fallos < limites.IntentosParaBloquearCuenta)
        {
            return;
        }

        var hasta = reloj.GetUtcNow().AddMinutes(limites.MinutosBloqueoCuenta);

        usuario.BloqueadaHasta = hasta;
        await contexto.SaveChangesAsync(ct);

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = "auth.bloqueo_cuenta",
            Ip = ip,
            UsuarioId = id,
            Entidad = "usuario",
            EntidadId = id.ToString("D"),
            DatosJson = $"{{\"intentos\":{fallos},\"bloqueada_hasta\":\"{hasta:yyyy-MM-ddTHH:mm:ssZ}\"}}",
            Resultado = "denegado",
            Origen = "api",
        }, ct);
    }

    private async Task AplicarBloqueoIpAsync(string ip, CancellationToken ct)
    {
        if (seguridad.EsIpPermitida(ip))
        {
            return;
        }

        var ahora = reloj.GetUtcNow();
        var fallos = await contexto.IntentoLogin.CountAsync(
            i => i.Ip == ip && !i.Exito && i.OcurridoAt >= ahora.AddMinutes(-limites.VentanaIpMinutos),
            ct);

        if (fallos < limites.IntentosParaBloquearIp)
        {
            return;
        }

        var fila = await contexto.BloqueoIp.SingleOrDefaultAsync(b => b.Ip == ip, ct);

        // Reincide si el bloqueo anterior terminó hace menos de 24 h; si pasó más de eso, el
        // atacante vuelve a empezar por el primer nivel.
        var reincide = fila?.BloqueadaHasta is { } previa && ahora - previa <= SegundoNivel;
        var nivel = reincide ? fila!.Nivel + 1 : 1;
        DateTimeOffset? hasta = nivel switch
        {
            1 => ahora.Add(PrimerNivel),
            2 => ahora.Add(SegundoNivel),
            _ => null,
        };

        if (fila is null)
        {
            fila = new BloqueoIp { Id = Guid.NewGuid(), Ip = ip, CreatedAt = ahora };
            contexto.BloqueoIp.Add(fila);
        }

        fila.Nivel = nivel;
        fila.Fallos = fallos;
        fila.BloqueadaHasta = hasta;
        fila.UpdatedAt = ahora;
        await contexto.SaveChangesAsync(ct);

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = "seg.bloqueo_ip",
            Ip = ip,
            Entidad = "bloqueo_ip",
            EntidadId = fila.Id.ToString("D"),
            DatosJson = $"{{\"nivel\":{nivel},\"fallos\":{fallos}}}",
            Resultado = "denegado",
            Origen = "api",
        }, ct);
    }
}