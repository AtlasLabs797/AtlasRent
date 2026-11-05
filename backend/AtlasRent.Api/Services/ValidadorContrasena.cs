namespace AtlasRent.Api.Services;

/// <summary>La contraseña propuesta no se acepta. El mensaje es apto para mostrarlo al usuario.</summary>
public sealed class ContrasenaRechazadaException(string mensaje) : Exception(mensaje);

/// <summary>
/// Reglas al fijar cualquier contraseña (alta, cambio propio y restablecimiento): entre 12 y 72
/// caracteres y que no aparezca en HaveIBeenPwned (docs/ESPECIFICACION.md §7, SEC-015).
/// <para>
/// Si HaveIBeenPwned no responde, la contraseña se acepta (<see cref="ServicioComplicidad"/> ya lo
/// registra en el log) y el método devuelve <c>false</c>: el llamante lo deja en la auditoría de su
/// mutación y avisa en la respuesta. Se prefiere así a bloquear el cambio: una caída de un tercero
/// no debe dejar a nadie sin poder cambiar una contraseña comprometida.
/// </para>
/// </summary>
public sealed class ValidadorContrasena(ServicioComplicidad complicidad)
{
    /// <returns>Si se pudo comprobar contra HaveIBeenPwned.</returns>
    /// <exception cref="ContrasenaRechazadaException">Longitud incorrecta o contraseña filtrada.</exception>
    public async Task<bool> ValidarAsync(string contrasena, CancellationToken ct)
    {
        if (contrasena.Length is < ServicioContrasenas.LongitudMinima or > ServicioContrasenas.LongitudMaxima)
        {
            throw new ContrasenaRechazadaException(
                $"La contraseña debe tener entre {ServicioContrasenas.LongitudMinima} y "
                + $"{ServicioContrasenas.LongitudMaxima} caracteres.");
        }

        var resultado = await complicidad.ComprobarAsync(contrasena, ct);
        if (resultado.ApareceEnFiltraciones)
        {
            throw new ContrasenaRechazadaException(
                "Esa contraseña aparece en filtraciones de datos conocidas. Elige otra.");
        }

        return resultado.Comprobada;
    }
}
