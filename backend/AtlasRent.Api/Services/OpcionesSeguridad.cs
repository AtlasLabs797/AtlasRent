using System.Net;
using Serilog;

namespace AtlasRent.Api.Services;

/// <summary>
/// Opciones de seguridad leídas de la sección <c>Seguridad</c> de la configuración.
/// La clave del JWT viene de <c>Seguridad__JwtClave</c> (base64) y no tiene valor por defecto:
/// si falta, la aplicación no arranca (AGENTS.md §6, no hay secretos en el repositorio).
/// </summary>
public sealed class OpcionesSeguridad
{
    public const string Seccion = "Seguridad";

    /// <summary>Rondas mínimas de BCrypt (docs/SEGURIDAD.md A.2).</summary>
    public const int RondasBcryptMinimas = 12;

    /// <summary>Emisor y audiencia del JWT de acceso. No es un secreto.</summary>
    public const string Emisor = "atlas-rent";

    public const string Audiencia = "atlas-rent";

    /// <summary>Clave de firma del JWT, en bytes.</summary>
    public required byte[] ClaveJwt { get; init; }

    /// <summary>
    /// IP de la oficina que nunca se bloquean por IP (docs/SEGURIDAD.md A.2). Viene de
    /// <c>Seguridad__IpListaBlanca</c>, separada por comas. Una IP mal escrita se ignora y se avisa
    /// por log: no vale la pena impedir que arranque por una variable de entorno.
    /// </summary>
    public IReadOnlySet<string> IpListaBlanca { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Si la IP está en la lista blanca: nunca se bloquea ni se le cuenta como ataque.</summary>
    public bool EsIpPermitida(string ip) => IpListaBlanca.Contains(ip);

    /// <summary>
    /// Coste de BCrypt. Configurable en <c>Seguridad:BcryptRondas</c>, mínimo 12 (docs/SEGURIDAD.md A.2).
    /// La validación está en el propio inicializador para que ninguna forma de construir las
    /// opciones se salte el mínimo.
    /// </summary>
    public int RondasBcrypt
    {
        get => _rondasBcrypt;
        init
        {
            if (value < RondasBcryptMinimas)
            {
                throw new InvalidOperationException(
                    $"El coste de BCrypt no puede ser menor que {RondasBcryptMinimas} (docs/SEGURIDAD.md A.2); se pidió {value}.");
            }

            _rondasBcrypt = value;
        }
    }

    private int _rondasBcrypt = RondasBcryptMinimas;

    /// <summary>
    /// Lee y valida las opciones. Lanza una excepción con el nombre de la variable que falta, para
    /// que un despliegue sin secreto falle al arrancar en vez de llevar una clave por defecto.
    /// </summary>
    public static OpcionesSeguridad Desde(IConfiguration configuracion)
    {
        var clave = configuracion[$"{Seccion}:JwtClave"];
        if (string.IsNullOrWhiteSpace(clave))
        {
            throw new InvalidOperationException(
                "Falta la clave de firma del JWT. Definir Seguridad__JwtClave (64 bytes en base64, "
                + "en user-secrets en desarrollo y en el servicio en producción).");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(clave);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "Seguridad__JwtClave no es base64 válido.");
        }

        if (bytes.Length < 32)
        {
            throw new InvalidOperationException(
                $"Seguridad__JwtClave necesita al menos 32 bytes para firmar HS256; tiene {bytes.Length}.");
        }

        var rondas = configuracion.GetValue($"{Seccion}:BcryptRondas", RondasBcryptMinimas);

        return new OpcionesSeguridad
        {
            ClaveJwt = bytes,
            RondasBcrypt = rondas,
            IpListaBlanca = LeerListaBlanca(configuracion[$"{Seccion}:IpListaBlanca"]),
        };
    }

    private static IReadOnlySet<string> LeerListaBlanca(string? valor)
    {
        var lista = new HashSet<string>(StringComparer.Ordinal);
        foreach (var texto in (valor ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IPAddress.TryParse(texto, out var direccion))
            {
                lista.Add(direccion.ToString());
            }
            else
            {
                Log.Warning("{Seccion}:IpListaBlanca tiene un valor que no es una IP: se ignora", texto);
            }
        }

        return lista;
    }
}

/// <summary>Nombres de las cookies de sesión. El frontend los conoce (docs/DOC_TECNICA.md §3.1).</summary>
public static class NombresCookies
{
    /// <summary>JWT de acceso. HttpOnly: solo el servidor lo lee.</summary>
    public const string Acceso = "atlas-rent-at";

    /// <summary>Token de refresco rotatorio. HttpOnly y limitado a las rutas de autenticación.</summary>
    public const string Refresco = "atlas-rent-rt";

    /// <summary>
    /// Token de CSRF. Va <strong>sin</strong> HttpOnly a propósito: es double-submit, el navegador
    /// tiene que poder leerlo para mandarlo en <c>X-CSRF-Token</c>.
    /// </summary>
    public const string Csrf = "atlas-rent-csrf";

    /// <summary>
    /// Token provisional del segundo factor (F1-06). HttpOnly y limitado a las rutas de
    /// autenticación: con él no se abre sesión, solo se pasa el segundo factor.
    /// </summary>
    public const string DesafioMfa = "atlas-rent-mfa";
}

/// <summary>Motivos de revocación de una sesión (columna <c>motivo_revocacion</c>).</summary>
public static class MotivosRevocacion
{
    /// <summary>Cierre de sesión pedido por el usuario.</summary>
    public const string Logout = "logout";

    /// <summary>Token rotado: su uso posterior es reutilización y revoca la familia.</summary>
    public const string Rotacion = "rotacion";

    /// <summary>Superada la inactividad máxima.</summary>
    public const string Inactividad = "inactividad";

    /// <summary>Superado el máximo absoluto de la sesión.</summary>
    public const string MaximoAbsoluto = "maximo_absoluto";

    /// <summary>Se detectó reutilización de un token ya rotado y se cayó la familia completa.</summary>
    public const string ReutilizacionDetectada = "reutilizacion_detectada";
}

/// <summary>
/// Rol con el que se crea la sesión mientras F1-07 no exista (docs/DOC_TECNICA.md §4.3).
/// Es un valor por defecto único: cuando llegue la tabla <c>rol</c>, se leerá de ahí.
/// </summary>
public static class Roles
{
    public const string Valor = "empleado";
}