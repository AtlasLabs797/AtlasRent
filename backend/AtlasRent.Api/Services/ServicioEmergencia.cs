using AtlasRent.Api.DTOs;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>Código de acceso de emergencia aprobado, con la fecha en que deja de valer.</summary>
public sealed record AprobacionEmergencia(string CodigoAcceso, DateTimeOffset Caduca);

/// <summary>
/// Acceso de emergencia (break-glass) para recuperar el sistema cuando un administrador pierde su
/// dispositivo y sus códigos de recuperación (docs/DOC_TECNICA.md §11, runbook de admin bloqueado).
/// <para>
/// Lo aprueban <b>dos administradores distintos</b>: el primero pide el acceso y el segundo lo
/// aprueba. Solo con la segunda aprobación existe un código de un solo uso, con el que el
/// administrador bloqueado entra sin segundo factor. Todo queda en auditoría firmada y la sesión
/// que sale nace marcada como de emergencia.
/// </para>
/// </summary>
public sealed class ServicioEmergencia(
    AtlasRentDbContext contexto,
    ServicioEmisionSesion emision,
    ServicioMfa mfa,
    ServicioBloqueos bloqueos,
    IPolicyService politica,
    AuditoriaService auditoria,
    TimeProvider reloj)
{
    /// <summary>Administradores distintos que hacen falta para aprobar un acceso de emergencia.</summary>
    public const int AprobacionesNecesarias = 2;

    /// <summary>Vida del código de acceso una vez aprobado: lo justo para usarlo en el acto.</summary>
    public const int MinutosCodigo = 10;

    private const int CaracteresPorCodigo = 10;

    /// <summary>
    /// Pide el acceso de emergencia para otro administrador. Devuelve el identificador de la
    /// solicitud, o <c>null</c> si el solicitante no es administrador o se lo pide a sí mismo
    /// (que no cuenta como dos aprobaciones).
    /// </summary>
    public async Task<Guid?> SolicitarAsync(
        Usuario solicitante,
        Guid usuarioObjetivoId,
        string motivo,
        string ip,
        CancellationToken ct)
    {
        if (!await PuedeGestionarAsync(solicitante, ct))
        {
            await AuditarDenegadoAsync("solicitud_no_admin", solicitante.Id, usuarioObjetivoId, motivo, ip, ct);
            return null;
        }

        if (solicitante.Id == usuarioObjetivoId)
        {
            await AuditarDenegadoAsync("auto_aprobacion", solicitante.Id, usuarioObjetivoId, motivo, ip, ct);
            return null;
        }

        var objetivo = await contexto.Usuario
            .SingleOrDefaultAsync(u => u.Id == usuarioObjetivoId && u.Activo && u.DeletedAt == null, ct);

        if (objetivo is null || !await PuedeGestionarAsync(objetivo, ct))
        {
            await AuditarDenegadoAsync("objetivo_no_admin", solicitante.Id, usuarioObjetivoId, motivo, ip, ct);
            return null;
        }

        var id = Guid.NewGuid();

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        contexto.AccesoEmergencia.Add(new AccesoEmergencia
        {
            Id = id,
            UsuarioId = usuarioObjetivoId,
            SolicitanteId = solicitante.Id,
            Motivo = motivo,
            Ip = ip,
            CreadoAt = reloj.GetUtcNow(),
        });
        await contexto.SaveChangesAsync(ct);

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = "seg.acceso_emergencia_solicitado",
            Ip = ip,
            UsuarioId = solicitante.Id,
            Entidad = "acceso_emergencia",
            EntidadId = id.ToString("D"),
            DatosJson = System.Text.Json.JsonSerializer.Serialize(new { usuario_objetivo = usuarioObjetivoId.ToString("D"), motivo }),
            Resultado = "permitido",
            Origen = "api",
        }, ct);

        await transaccion.CommitAsync(ct);

        return id;
    }

    /// <summary>Estados que admite el filtro de solicitudes; "todas" no filtra.</summary>
    public static readonly IReadOnlyList<string> EstadosSolicitud = ["pendiente", "aprobada", "caducada", "usada", "todas"];

    /// <summary>
    /// Solicitudes de acceso de emergencia para revisarlas. Nunca devuelve el código ni su hash.
    /// Una solicitud pendiente no caduca: solo caduca el código una vez aprobado.
    /// </summary>
    public async Task<IReadOnlyList<SolicitudEmergenciaResumen>> ListarSolicitudesAsync(string estado, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var consulta = contexto.AccesoEmergencia.AsNoTracking();
        consulta = estado switch
        {
            "pendiente" => consulta.Where(a => a.AprobadoPorId == null),
            "aprobada" => consulta.Where(a => a.AprobadoPorId != null && a.UsadoAt == null && a.CaducaAt > ahora),
            "caducada" => consulta.Where(a => a.AprobadoPorId != null && a.UsadoAt == null && a.CaducaAt <= ahora),
            "usada" => consulta.Where(a => a.UsadoAt != null),
            _ => consulta,
        };

        var filas = await (
            from a in consulta
            join objetivo in contexto.Usuario on a.UsuarioId equals objetivo.Id
            join solicitante in contexto.Usuario on a.SolicitanteId equals solicitante.Id
            join aprobador in contexto.Usuario on a.AprobadoPorId equals aprobador.Id into aprobadores
            from aprobador in aprobadores.DefaultIfEmpty()
            orderby a.CreadoAt descending
            select new
            {
                a.Id,
                a.UsuarioId,
                ObjetivoAcceso = objetivo.NombreAcceso,
                a.SolicitanteId,
                SolicitanteAcceso = solicitante.NombreAcceso,
                a.Motivo,
                a.CreadoAt,
                a.AprobadoPorId,
                AprobadorAcceso = aprobador != null ? aprobador.NombreAcceso : null,
                a.AprobadoAt,
                a.CaducaAt,
                a.UsadoAt,
            }).Take(200).ToListAsync(ct);

        return [.. filas.Select(f => new SolicitudEmergenciaResumen(
            f.Id,
            f.UsuarioId,
            f.ObjetivoAcceso,
            f.SolicitanteId,
            f.SolicitanteAcceso,
            f.Motivo,
            f.CreadoAt,
            f.AprobadoPorId,
            f.AprobadorAcceso,
            f.AprobadoAt,
            f.CaducaAt,
            f.UsadoAt,
            f.UsadoAt is not null ? "usada"
                : f.AprobadoPorId is null ? "pendiente"
                : f.CaducaAt <= ahora ? "caducada" : "aprobada"))];
    }

    /// <summary>
    /// Segunda aprobación: la del administrador que no pidió el acceso. Devuelve el código de acceso
    /// de un solo uso con su caducidad, o <c>null</c> si no se puede aprobar.
    /// </summary>
    public async Task<AprobacionEmergencia?> AprobarAsync(
        Usuario aprobador,
        Guid solicitudId,
        string ip,
        CancellationToken ct)
    {
        if (!await PuedeGestionarAsync(aprobador, ct))
        {
            await AuditarDenegadoAsync("aprobacion_no_admin", aprobador.Id, solicitudId, string.Empty, ip, ct);
            return null;
        }

        // La solicitud se lee antes de abrir la transacción: una denegación se audita en la suya
        // propia y no puede haber dos transacciones abiertas a la vez.
        var pendiente = await contexto.AccesoEmergencia
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == solicitudId, ct);

        if (pendiente is null)
        {
            await AuditarDenegadoAsync("solicitud_inexistente", aprobador.Id, solicitudId, string.Empty, ip, ct);
            return null;
        }

        // Quien pidió el acceso no lo aprueba: hacen falta dos administradores distintos.
        if (pendiente.SolicitanteId == aprobador.Id)
        {
            await AuditarDenegadoAsync("auto_aprobacion", aprobador.Id, solicitudId, pendiente.Motivo, ip, ct);
            return null;
        }

        if (pendiente.AprobadoPorId is not null)
        {
            await AuditarDenegadoAsync("ya_aprobada", aprobador.Id, solicitudId, pendiente.Motivo, ip, ct);
            return null;
        }

        var ahora = reloj.GetUtcNow();
        var codigo = GenerarCodigo();

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        // Escritura condicional: dos aprobaciones simultáneas leen "sin aprobar" a la vez, y solo la
        // que consiga marcar la fila puede fijar el código. Si el UPDATE no toca filas, otro se
        // adelantó y su código es el único válido (SEC-025).
        var caduca = ahora.AddMinutes(MinutosCodigo);
        var hashCodigo = EmisorTokens.HashToken(ServicioMfa.Normalizar(codigo));
        var afectadas = await contexto.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE acceso_emergencia
               SET aprobado_por_id = {aprobador.Id},
                   aprobado_at = {ahora},
                   caduca_at = {caduca},
                   codigo_hash = {hashCodigo}
             WHERE id = {solicitudId}
               AND aprobado_por_id IS NULL
            """, ct);

        if (afectadas != 1)
        {
            await transaccion.RollbackAsync(ct);
            await AuditarDenegadoAsync("ya_aprobada", aprobador.Id, solicitudId, pendiente.Motivo, ip, ct);
            return null;
        }

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = "seg.acceso_emergencia_aprobado",
            Ip = ip,
            UsuarioId = aprobador.Id,
            Entidad = "acceso_emergencia",
            EntidadId = solicitudId.ToString("D"),
            DatosJson = $$"""{"usuario_objetivo":"{{pendiente.UsuarioId:D}}","caduca":"{{caduca:yyyy-MM-ddTHH:mm:ssZ}}"}""",
            Resultado = "permitido",
            Origen = "api",
        }, ct);

        await transaccion.CommitAsync(ct);

        // Solo aquí y solo ahora se muestra el código: no se escribe en la base de datos ni en la
        // auditoría, y no hay forma de recuperarlo después.
        return new AprobacionEmergencia(codigo, caduca);
    }

    /// <summary>
    /// El administrador bloqueado entra con el código aprobado, sin segundo factor. La sesión que
    /// sale queda marcada como de emergencia y con su código gastado.
    /// </summary>
    public async Task<ResultadoSesion?> EntrarAsync(
        string tokenDesafio,
        string codigo,
        string ip,
        string? userAgent,
        CancellationToken ct)
    {
        var desafio = await mfa.BuscarDesafioAsync(tokenDesafio, ct);
        if (desafio is null)
        {
            return null;
        }

        var usuario = await contexto.Usuario
            .SingleOrDefaultAsync(u => u.Id == desafio.UsuarioId && u.Activo && u.DeletedAt == null, ct);

        if (usuario is null)
        {
            return null;
        }

        // SEC-025: una cuenta bloqueada no prueba códigos de emergencia, y cada código malo cuenta
        // como intento fallido igual que una contraseña incorrecta.
        if (bloqueos.CuentaBloqueada(usuario))
        {
            await bloqueos.RegistrarIntentoFallidoAsync(usuario.Id, usuario.NombreAcceso, ip, ct);
            return null;
        }

        var hash = EmisorTokens.HashToken(ServicioMfa.Normalizar(codigo));
        var ahora = reloj.GetUtcNow();

        var acceso = await contexto.AccesoEmergencia.SingleOrDefaultAsync(
            a => a.UsuarioId == desafio.UsuarioId
                && a.CodigoHash == hash
                && a.UsadoAt == null
                && a.CaducaAt != null
                && a.CaducaAt > ahora,
            ct);

        if (acceso is null)
        {
            await bloqueos.RegistrarIntentoFallidoAsync(usuario.Id, usuario.NombreAcceso, ip, ct);
            return null;
        }

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        acceso.UsadoAt = ahora;
        desafio.ConsumidoAt = ahora;
        await contexto.SaveChangesAsync(ct);

        var sesion = await emision.EmitirAsync(
            usuario, ip, userAgent, modoEmergencia: true, "auth.acceso_emergencia", ct);

        await transaccion.CommitAsync(ct);

        return sesion;
    }

    /// <summary>
    /// Quién puede pedir y aprobar un acceso de emergencia: quien tenga el permiso
    /// <c>seguridad.acceso_emergencia</c>, que hoy solo tiene el administrador.
    /// </summary>
    private Task<bool> PuedeGestionarAsync(Usuario usuario, CancellationToken ct) =>
        politica.PuedeAsync(Acciones.SeguridadAccesoEmergencia, usuario.Id, ct);

    /// <summary>
    /// Una denegación también se audita, en su propia transacción: si se escribiera dentro de la
    /// transacción de la operación denegada, el <c>ROLLBACK</c> se la llevaría por delante.
    /// </summary>
    private async Task AuditarDenegadoAsync(
        string motivo,
        Guid usuarioId,
        Guid referencia,
        string detalle,
        string ip,
        CancellationToken ct)
    {
        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = $"seg.acceso_emergencia_denegado.{motivo}",
            Ip = ip,
            UsuarioId = usuarioId,
            Entidad = "acceso_emergencia",
            EntidadId = referencia.ToString("D"),
            DatosJson = detalle.Length > 0 ? System.Text.Json.JsonSerializer.Serialize(new { motivo = detalle }) : null,
            Resultado = "denegado",
            Origen = "api",
        }, ct);

        await transaccion.CommitAsync(ct);
    }

    private static string GenerarCodigo()
    {
        var mitad = CaracteresPorCodigo / 2;
        return $"{ServicioMfa.Texto(mitad)}-{ServicioMfa.Texto(CaracteresPorCodigo - mitad)}";
    }
}
