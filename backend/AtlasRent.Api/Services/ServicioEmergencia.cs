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
        if (!EsAdmin(solicitante))
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

        if (objetivo is null || !objetivo.EsAdmin)
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
            DatosJson = $$"""{"usuario_objetivo":"{{usuarioObjetivoId:D}}","motivo":"{{Escapar(motivo)}}"}""",
            Resultado = "permitido",
            Origen = "api",
        }, ct);

        await transaccion.CommitAsync(ct);

        return id;
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
        if (!EsAdmin(aprobador))
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

        var solicitud = await contexto.AccesoEmergencia
            .SingleAsync(a => a.Id == solicitudId, ct);

        solicitud.AprobadoPorId = aprobador.Id;
        solicitud.AprobadoAt = ahora;
        solicitud.CaducaAt = ahora.AddMinutes(MinutosCodigo);
        solicitud.CodigoHash = EmisorTokens.HashToken(ServicioMfa.Normalizar(codigo));
        await contexto.SaveChangesAsync(ct);

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = "seg.acceso_emergencia_aprobado",
            Ip = ip,
            UsuarioId = aprobador.Id,
            Entidad = "acceso_emergencia",
            EntidadId = solicitud.Id.ToString("D"),
            DatosJson = $$"""{"usuario_objetivo":"{{solicitud.UsuarioId:D}}","caduca":"{{solicitud.CaducaAt:yyyy-MM-ddTHH:mm:ssZ}}"}""",
            Resultado = "permitido",
            Origen = "api",
        }, ct);

        await transaccion.CommitAsync(ct);

        // Solo aquí y solo ahora se muestra el código: no se escribe en la base de datos ni en la
        // auditoría, y no hay forma de recuperarlo después.
        return new AprobacionEmergencia(codigo, solicitud.CaducaAt!.Value);
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
            return null;
        }

        var usuario = await contexto.Usuario
            .SingleOrDefaultAsync(u => u.Id == desafio.UsuarioId && u.Activo && u.DeletedAt == null, ct);

        if (usuario is null)
        {
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
    /// Quién puede aprobar un acceso de emergencia.
    /// <para>
    /// TODO(A#) F1-07: sustituir por la comprobación de permisos de <c>IPolicyService</c>. Está
    /// aislada aquí a propósito para que ese cambio no toque los controladores ni el resto.
    /// </para>
    /// </summary>
    public static bool EsAdmin(Usuario usuario) => usuario.EsAdmin;

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
            DatosJson = detalle.Length > 0 ? $$"""{"motivo":"{{Escapar(detalle)}}"}""" : null,
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

    /// <summary>
    /// Los motivos van dentro del JSON de auditoría y los escribe quien llama, así que se escapan
    /// antes de meterlos: unas comillas o un salto de línea romperían el JSON, y con él la firma
    /// de la cadena.
    /// </summary>
    private static string Escapar(string texto)
    {
        var escapado = texto.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

        // Caracteres de control (un salto de línea entre otros): dentro de una cadena JSON solo
        // valen escapados, y aquí no merece la pena escribir la secuencia.
        return new string(escapado.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
    }
}