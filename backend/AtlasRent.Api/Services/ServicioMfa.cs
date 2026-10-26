using System.Globalization;
using System.Security.Cryptography;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using Microsoft.EntityFrameworkCore;
using OtpNet;

namespace AtlasRent.Api.Services;

/// <summary>Lo que se devuelve al empezar la activación: la URI que el frontend convierte en QR.</summary>
public sealed record DatosActivacionMfa(string Uri, int Digitos, int PeriodoSegundos);

/// <summary>
/// Resultado de confirmar la activación. Los códigos en claro se devuelven aquí y en ningún otro
/// sitio: a partir de ahora la base de datos solo tiene sus hashes.
/// </summary>
public sealed record ConfirmacionMfa(IReadOnlyList<string> CodigosRecuperacion);

/// <summary>
/// Token provisional que devuelve el login cuando falta el segundo factor.
/// <para>
/// <c>RequiereConfiguracion</c> es lo que distingue los dos casos: un administrador sin MFA todavía
/// activo solo puede configurarlo, y el resto solo puede pasar el segundo factor.
/// </para>
/// </summary>
public sealed record DesafioMfa(
    Guid UsuarioId,
    string NombreAcceso,
    string Token,
    DateTimeOffset CaducaAt,
    bool RequiereConfiguracion);

/// <summary>
/// Segundo factor (F1-06): activación, códigos de recuperación, token provisional del login en dos
/// pasos y verificación del TOTP.
/// <para>
/// El secreto TOTP y los códigos de recuperación nunca salen de aquí más que en la respuesta que los
/// muestra, y nunca van a un log ni a la auditoría: el log solo recibe el código de referencia del
/// error (AGENTS.md §6).
/// </para>
/// <para>
/// <strong>F1-09 (SEC-007 cerrado):</strong> <c>usuario.mfa_secreto</c> se guarda cifrado en reposo con
/// AES-256-GCM, con el mismo <see cref="ServicioCifrado"/> que el documento de identidad. F1-06 lo
/// dejó en claro y aquí ya no está. Un secreto TOTP en claro sirve para suplantar a alguien hasta
/// que se roba el teléfono.
/// </para>
/// </summary>
public sealed class ServicioMfa(
    AtlasRentDbContext contexto,
    ServicioEmisionSesion emision,
    ServicioBloqueos bloqueos,
    IPolicyService politica,
    AuditoriaService auditoria,
    ServicioCifrado cifrado,
    OpcionesSeguridad opciones,
    TimeProvider reloj)
{
    /// <summary>Issuer de la URI <c>otpauth://</c>: el nombre que ve el usuario en su aplicación.</summary>
    public const string Emisor = "Atlas Rent";

    /// <summary>Marcador de los hashes de recuperación con HMAC. Los anteriores (SHA-256 sin sal) no lo llevan.</summary>
    public const string PrefijoHashCodigo = "h1:";

    /// <summary>
    /// Lo que se guarda de un código de recuperación (SEC-030): HMAC-SHA256 con la clave del servidor
    /// (<c>Seguridad__HmacDocumentoClave</c>), separado por dominio. Con SHA-256 a secas, quien se
    /// llevara la tabla podía probar candidatos sin límite; con la clave fuera de la base, no.
    /// </summary>
    public static string HashCodigoRecuperacion(byte[] clave, string codigoNormalizado) =>
        PrefijoHashCodigo + Convert.ToHexStringLower(HMACSHA256.HashData(
            clave, System.Text.Encoding.UTF8.GetBytes("codigo-recuperacion:" + codigoNormalizado)));

    /// <summary>Longitud del código TOTP.</summary>
    public const int Digitos = 6;

    /// <summary>
    /// Periodo del código TOTP en segundos. Con 30 el usuario tiene medio minuto para escribirlo,
    /// que es lo estándar en Google Authenticator y Microsoft Authenticator.
    /// </summary>
    public const int PeriodoSegundos = 30;

    /// <summary>
    /// Ventana de verificación: el paso actual, el anterior y el siguiente. El anterior y el
    /// siguiente cubren el desfase del reloj entre el teléfono y el servidor (RFC 6238 §5.2).
    /// </summary>
    public static readonly VerificationWindow Ventana = VerificationWindow.RfcSpecifiedNetworkDelay;

    /// <summary>Vida del token provisional del login en dos pasos.</summary>
    public const int MinutosDesafio = 5;

    /// <summary>Códigos de recuperación que se generan al activar el MFA.</summary>
    public const int CodigosPorUsuario = 10;

    private const int BytesSecreto = 20;
    private const int CaracteresPorCodigo = 10;

    // Sin I, O, 0 ni 1: son los caracteres que se confunden al leerlos o dictarlos de un papel.
    private const string AlfabetoCodigo = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>
    /// Si a este usuario le toca segundo factor. Para quien tiene el permiso <c>mfa.obligatorio</c>
    /// es obligatorio aunque no lo tenga configurado (docs/ESPECIFICACION.md D4): sin MFA no hay
    /// sesión, solo el token provisional para configurarlo. Hoy ese permiso es del administrador y
    /// solo del administrador (D4).
    /// </summary>
    public async Task<bool> NecesitaMfaAsync(Usuario usuario, CancellationToken ct) =>
        usuario.MfaActivado
        || await politica.PuedeAsync(Acciones.MfaObligatorio, usuario.Id, ct)
        || await GerenteConInterruptorAsync(usuario, ct);

    /// <summary>
    /// SEC-024: el interruptor "MFA para gerente" (D4) de <c>ajuste_general</c>. Es un ajuste y no
    /// una fila de <c>permiso_rol</c> para que el administrador lo active sin migración y sin tocar
    /// la matriz: <c>mfa.obligatorio</c> sigue siendo solo del administrador.
    /// </summary>
    private async Task<bool> GerenteConInterruptorAsync(Usuario usuario, CancellationToken ct) =>
        await politica.RolDeAsync(usuario.Id, ct) == Roles.Gerente
        && await contexto.AjusteGeneral.AsNoTracking().AnyAsync(a => a.MfaGerente, ct);

    /// <summary>
    /// Emite el token provisional de un login con la contraseña correcta. Es opaco, dura
    /// <see cref="MinutosDesafio"/> y es de un solo uso: no habilita más que los endpoints del
    /// segundo factor.
    /// </summary>
    public async Task<DesafioMfa> EmitirDesafioAsync(Usuario usuario, string ip, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var token = EmisorTokens.TokenAleatorio();

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        contexto.MfaDesafio.Add(new MfaDesafio
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            TokenHash = EmisorTokens.HashToken(token),
            Ip = ip,
            EmitidoAt = ahora,
            CaducaAt = ahora.AddMinutes(MinutosDesafio),
        });
        await contexto.SaveChangesAsync(ct);

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = "auth.mfa_desafio",
            Ip = ip,
            UsuarioId = usuario.Id,
            Entidad = "mfa_desafio",
            EntidadId = usuario.Id.ToString("D"),
            DatosJson = usuario.MfaActivado
                ? """{"motivo":"segundo_factor"}"""
                : """{"motivo":"alta_mfa"}""",
            Resultado = "permitido",
            Origen = "api",
        }, ct);

        await transaccion.CommitAsync(ct);

        return new DesafioMfa(
            usuario.Id,
            usuario.NombreAcceso,
            token,
            ahora.AddMinutes(MinutosDesafio),
            RequiereConfiguracion: !usuario.MfaActivado);
    }

    /// <summary>
    /// Empieza la activación: genera el secreto y devuelve la URI <c>otpauth://</c> para que el
    /// frontend dibuje el QR. Devuelve <c>null</c> si el MFA ya está activo.
    /// <para>
    /// El MFA no queda activo todavía: hace falta <see cref="ConfirmarAsync"/> con un código válido,
    /// que es lo que demuestra que el usuario guardó el secreto de verdad.
    /// </para>
    /// </summary>
    public async Task<DatosActivacionMfa?> IniciarAsync(Usuario usuario, string ip, CancellationToken ct)
    {
        if (usuario.MfaActivado)
        {
            return null;
        }

        var secreto = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(BytesSecreto));
        var uri = new OtpUri(OtpType.Totp, secreto, usuario.NombreAcceso, Emisor).ToString();

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        // F1-09: cifrado en reposo, nunca en claro (docs/SEGURIDAD.md SEC-007).
        usuario.MfaSecreto = cifrado.Cifrar(secreto);
        await contexto.SaveChangesAsync(ct);

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = "auth.mfa_inicio",
            Ip = ip,
            UsuarioId = usuario.Id,
            Entidad = "usuario",
            EntidadId = usuario.Id.ToString("D"),
            Resultado = "permitido",
            Origen = "api",
        }, ct);

        await transaccion.CommitAsync(ct);

        return new DatosActivacionMfa(uri, Digitos, PeriodoSegundos);
    }

    /// <summary>
    /// Confirma la activación con un código TOTP válido del secreto pendiente. Si el código no
    /// vale, el MFA sigue sin activar y se devuelve <c>null</c>.
    /// <para>
    /// Al confirmar se generan los códigos de recuperación, que solo se devuelven aquí y en ningún
    /// log: a partir de ahora la tabla solo guarda sus hashes.
    /// </para>
    /// </summary>
    public async Task<ConfirmacionMfa?> ConfirmarAsync(Usuario usuario, string codigo, string ip, CancellationToken ct)
    {
        if (usuario.MfaActivado || usuario.MfaSecreto is not { } secretoCifrado)
        {
            return null;
        }

        // SEC-025: una cuenta bloqueada no prueba códigos, y cada código malo cuenta como fallo.
        if (bloqueos.CuentaBloqueada(usuario))
        {
            await RegistrarFalloAsync(usuario.Id, usuario.NombreAcceso, ip, ct);
            return null;
        }

        // F1-09: el secreto está cifrado en reposo; hay que descifrarlo para comprobar el código.
        var secreto = cifrado.Descifrar(secretoCifrado);

        if (!CodigoValido(secreto, codigo))
        {
            await RegistrarFalloAsync(usuario.Id, usuario.NombreAcceso, ip, ct);
            return null;
        }

        var ahora = reloj.GetUtcNow();
        var codigos = GenerarCodigos();

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        usuario.MfaActivado = true;
        await contexto.SaveChangesAsync(ct);

        // Los códigos que hubiera de una activación anterior dejan de servir: se marcan gastados en vez
// de borrarse, para que la API no necesite permiso de borrado en una tabla de credenciales.
await contexto.CodigoRecuperacion
            .Where(c => c.UsuarioId == usuario.Id && c.UsadoAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsadoAt, ahora), ct);

        foreach (var hash in codigos.Select(c => HashCodigoRecuperacion(opciones.ClaveHmacDocumento, Normalizar(c))))
        {
            contexto.CodigoRecuperacion.Add(new CodigoRecuperacion
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuario.Id,
                CodigoHash = hash,
                CreadoAt = ahora,
            });
        }

        await contexto.SaveChangesAsync(ct);

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = "auth.mfa_activado",
            Ip = ip,
            UsuarioId = usuario.Id,
            Entidad = "usuario",
            EntidadId = usuario.Id.ToString("D"),
            // Solo cuántos códigos se emitieron: los códigos en claro no se auditan ni se registran.
            DatosJson = $$"""{"codigos_recuperacion":{{CodigosPorUsuario}}}""",
            Resultado = "permitido",
            Origen = "api",
        }, ct);

        await transaccion.CommitAsync(ct);

        return new ConfirmacionMfa(codigos);
    }

    /// <summary>
    /// Login en dos pasos: verifica el TOTP del desafío y, si vale, abre la sesión completa.
    /// <para>
    /// Un código incorrecto se anota como intento fallido más: cuenta para el bloqueo de cuenta y
    /// para el de IP igual que una contraseña incorrecta (docs/SEGURIDAD.md A.2).
    /// </para>
    /// </summary>
    public async Task<ResultadoSesion?> VerificarSegundoFactorAsync(
        string tokenDesafio,
        string codigo,
        string ip,
        string? userAgent,
        CancellationToken ct)
    {
        var desafio = await BuscarDesafioAsync(tokenDesafio, ct);
        if (desafio is null)
        {
            return null;
        }

        var usuario = await UsuarioDelDesafioAsync(desafio, ct);

        // SEC-025: con la cuenta bloqueada no se prueban códigos; el intento cuenta como fallo.
        if (usuario is not null && bloqueos.CuentaBloqueada(usuario))
        {
            await RegistrarFalloAsync(usuario.Id, usuario.NombreAcceso, ip, ct);
            return null;
        }

        if (usuario is null
            || !usuario.MfaActivado
            || usuario.MfaSecreto is not { } secretoCifrado
            || !CodigoValido(cifrado.Descifrar(secretoCifrado), codigo))
        {
            await RegistrarFalloAsync(desafio.UsuarioId, usuario?.NombreAcceso, ip, ct);
            return null;
        }

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        // Gastar el desafío es una escritura condicional, no una asignación en memoria: dos peticiones
        // simultáneas con el mismo token leen "no gastado" a la vez, y solo la que consiga marcar la
        // fila puede emitir sesión. La comprobación del resultado de la actualización es la que
        // decide; si el UPDATE no toca filas, otro ya lo gastó.
        if (!await ConsumirDesafioAsync(desafio, ct))
        {
            await transaccion.RollbackAsync(ct);
            return null;
        }

        var sesion = await emision.EmitirAsync(usuario, ip, userAgent, modoEmergencia: false, "auth.login_mfa", ct);

        await transaccion.CommitAsync(ct);

        return sesion;
    }

    /// <summary>
    /// Entra con un código de recuperación en vez de con el TOTP. El código se marca usado dentro de
    /// la misma transacción que abre la sesión: o se gasta y entra, o no pasa nada.
    /// </summary>
    public async Task<ResultadoSesion?> VerificarCodigoRecuperacionAsync(
        string tokenDesafio,
        string codigo,
        string ip,
        string? userAgent,
        CancellationToken ct)
    {
        var desafio = await BuscarDesafioAsync(tokenDesafio, ct);
        if (desafio is null)
        {
            return null;
        }

        var usuario = await UsuarioDelDesafioAsync(desafio, ct);
        if (usuario is null)
        {
            await RegistrarFalloAsync(desafio.UsuarioId, null, ip, ct);
            return null;
        }

        if (bloqueos.CuentaBloqueada(usuario))
        {
            await RegistrarFalloAsync(usuario.Id, usuario.NombreAcceso, ip, ct);
            return null;
        }

        // Los códigos emitidos antes de SEC-030 siguen guardados como SHA-256 sin marcador y valen
        // hasta que se gasten o se regeneren; los nuevos llevan el marcador de versión.
        var normalizado = Normalizar(codigo);
        var hash = HashCodigoRecuperacion(opciones.ClaveHmacDocumento, normalizado);
        var hashAnterior = EmisorTokens.HashToken(normalizado);

        var fila = await contexto.CodigoRecuperacion.SingleOrDefaultAsync(
            c => c.UsuarioId == usuario.Id
                && (c.CodigoHash == hash || c.CodigoHash == hashAnterior)
                && c.UsadoAt == null, ct);

        if (fila is null)
        {
            await RegistrarFalloAsync(desafio.UsuarioId, usuario.NombreAcceso, ip, ct);
            return null;
        }

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        // Igual que en el segundo factor: gastar el código y el desafío son escrituras
        // condicionales. Si otro intento los gastó mientras este validaba, no se abre sesión.
        var codigosUsados = await MarcarCodigoUsadoAsync(fila.Id, ct);
        var desafioUsado = await ConsumirDesafioAsync(desafio, ct);
        if (codigosUsados != 1 || !desafioUsado)
        {
            await transaccion.RollbackAsync(ct);
            return null;
        }

        var sesion = await emision.EmitirAsync(usuario, ip, userAgent, modoEmergencia: false, "auth.login_recuperacion", ct);

        await transaccion.CommitAsync(ct);

        return sesion;
    }

    /// <summary>
    /// Marca un código de recuperación como usado, solo si seguía sin usar. Devuelve las filas
    /// afectadas: 0 significa que otro intento se adelantó.
    /// </summary>
    private async Task<int> MarcarCodigoUsadoAsync(Guid codigoRecuperacionId, CancellationToken ct) =>
        await contexto.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE codigo_recuperacion
               SET usado_at = {reloj.GetUtcNow()}
             WHERE id = {codigoRecuperacionId}
               AND usado_at IS NULL
            """, ct);

    /// <summary>
    /// Busca el desafío de un token provisional. Devuelve <c>null</c> si no existe, ya se gastó o
    /// caducó. Lo usa también <see cref="ServicioEmergencia"/> para el acceso de emergencia.
    /// <para>
    /// Se trae sin <c>AsNoTracking</c> a propósito: quien lo gasta tiene que marcarlo como
    /// consumido, y una entidad sin seguimiento no guarda ese cambio.
    /// </para>
    /// </summary>
    public async Task<MfaDesafio?> BuscarDesafioAsync(string tokenDesafio, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(tokenDesafio))
        {
            return null;
        }

        var hash = EmisorTokens.HashToken(tokenDesafio);
        var ahora = reloj.GetUtcNow();

        return await contexto.MfaDesafio
            .SingleOrDefaultAsync(d => d.TokenHash == hash && d.ConsumidoAt == null && d.CaducaAt > ahora, ct);
    }

    /// <summary>Normalización de un código escrito por una persona: mayúsculas y sin guiones.</summary>
    public static string Normalizar(string codigo) =>
        codigo.Trim().Replace("-", string.Empty, StringComparison.Ordinal)
            .ToUpper(CultureInfo.InvariantCulture);

    /// <summary>Código TOTP válido para el secreto, con la ventana de reloj de <see cref="Ventana"/>.</summary>
    private bool CodigoValido(string secretoBase32, string codigo)
    {
        if (codigo.Length != Digitos)
        {
            return false;
        }

        var totp = new Totp(
            Base32Encoding.ToBytes(secretoBase32),
            totpSize: Digitos,
            mode: OtpHashMode.Sha1,
            step: PeriodoSegundos);

        // TODO(A#) F1-06: RFC 6238 §5.2 pide que un mismo código no se acepte dos veces. Aquí el
        // token provisional es de un solo uso, así que repetir un código exige volver a pasar la
        // contraseña; guardar el paso de tiempo usado en el usuario lo cerraría del todo.
        return totp.VerifyTotp(reloj.GetUtcNow().UtcDateTime, codigo, out _, Ventana);
    }

    private static List<string> GenerarCodigos()
    {
        var codigos = new List<string>(CodigosPorUsuario);
        for (var i = 0; i < CodigosPorUsuario; i++)
        {
            var mitad = CaracteresPorCodigo / 2;
            codigos.Add(string.Concat(
                Texto(mitad), '-', Texto(CaracteresPorCodigo - mitad)));
        }

        return codigos;
    }

    /// <summary>
    /// Caracteres aleatorios del alfabeto sin caracteres que se confunden al leerlos. Se usa
    /// <see cref="RandomNumberGenerator"/> y no <c>Random</c>: el código es una credencial.
    /// </summary>
    internal static string Texto(int longitud)
    {
        var buffer = new char[longitud];
        for (var i = 0; i < longitud; i++)
        {
            buffer[i] = AlfabetoCodigo[RandomNumberGenerator.GetInt32(AlfabetoCodigo.Length)];
        }

        return new string(buffer);
    }

    private Task<Usuario?> UsuarioDelDesafioAsync(MfaDesafio desafio, CancellationToken ct) =>
        contexto.Usuario.SingleOrDefaultAsync(u => u.Id == desafio.UsuarioId && u.Activo && u.DeletedAt == null, ct);

    /// <summary>
    /// Anota un segundo factor fallido. El intento es la traza del fallo: la misma tabla y el mismo
    /// bloqueo que cuentan las contraseñas. No se escribe nada más, y menos el código recibido.
    /// </summary>
    private Task RegistrarFalloAsync(Guid usuarioId, string? nombreAcceso, string ip, CancellationToken ct) =>
        bloqueos.RegistrarIntentoFallidoAsync(usuarioId, nombreAcceso, ip, ct);

    /// <summary>
    /// Gasta el desafío con una escritura condicional. Devuelve <c>false</c> si ya estaba gastado,
    /// que es lo que impide que dos peticiones simultáneas abran dos sesiones con el mismo token.
    /// </summary>
    private async Task<bool> ConsumirDesafioAsync(MfaDesafio desafio, CancellationToken ct)
    {
        var afectadas = await contexto.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE mfa_desafio
               SET consumido_at = {reloj.GetUtcNow()}
             WHERE id = {desafio.Id}
               AND consumido_at IS NULL
            """, ct);

        return afectadas == 1;
    }
}