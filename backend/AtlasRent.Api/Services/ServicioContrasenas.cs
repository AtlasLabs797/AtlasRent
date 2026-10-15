using BCrypt.Net;

namespace AtlasRent.Api.Services;

/// <summary>
/// Hash de contraseñas con BCrypt (docs/SEGURIDAD.md A.2). En la tabla <c>usuario</c> solo se
/// guarda el hash: la contraseña en claro no se escribe nunca.
/// </summary>
public sealed class ServicioContrasenas
{
    /// <summary>
    /// Longitud máxima en caracteres. BCrypt solo considera los 72 primeros bytes de la
    /// contraseña, así que se rechaza lo que pase de aquí en lugar de recortar en silencio.
    /// </summary>
    public const int LongitudMaxima = 72;

    private readonly OpcionesSeguridad _opciones;

    /// <summary>
    /// Hash de un valor inventado, con el mismo coste que el de los usuarios. Sirve para que el
    /// login de un nombre inexistente tarde lo mismo que el de un nombre real: si no, el tiempo de
    /// respuesta revelaría qué nombres de acceso existen.
    /// </summary>
    private readonly Lazy<string> _hashDeComparacion;

    public ServicioContrasenas(OpcionesSeguridad opciones)
    {
        _opciones = opciones;
        _hashDeComparacion = new Lazy<string>(
            () => BCrypt.Net.BCrypt.HashPassword("atlas-rent-contrasena-de-comparacion", opciones.RondasBcrypt));
    }

    public string Hashar(string contrasena)
    {
        if (contrasena.Length > LongitudMaxima)
        {
            throw new ArgumentException(
                $"La contraseña no puede pasar de {LongitudMaxima} caracteres (BCrypt no mira más).",
                nameof(contrasena));
        }

        return BCrypt.Net.BCrypt.HashPassword(contrasena, _opciones.RondasBcrypt);
    }

    public bool Verificar(string contrasena, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(contrasena, hash);
        }
        catch (SaltParseException)
        {
            // Hash corrupto en la base: se trata como contraseña incorrecta y no como un error 500.
            return false;
        }
    }

    /// <summary>
    /// Verifica contra el hash de comparación sin comprobar el resultado. Se llama cuando el
    /// usuario no existe para que el tiempo de respuesta no lo delate.
    /// </summary>
    public void CompararContraHashDeComparacion(string contrasena) =>
        Verificar(contrasena.Length <= LongitudMaxima ? contrasena : contrasena[..LongitudMaxima],
            _hashDeComparacion.Value);
}