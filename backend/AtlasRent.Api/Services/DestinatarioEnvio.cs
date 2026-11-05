using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Services;

/// <summary>
/// A quién sale un correo a un cliente, y por qué a veces no sale a nadie (docs/ESPECIFICACION.md
/// §3.2 y §3.14).
/// <para>
/// Vive aquí y no dentro de un servicio porque lo necesitan dos: el envío masivo de F4-03 y los
/// recordatorios de F4-05. «A cuál de sus tres casillas le mando esto» es una decisión de negocio, y
/// tenerla escrita dos veces es la forma de que un día una pantalla mande al principal y el otro al
/// primero de la lista.
/// </para>
/// <para>
/// El orden es el de un envío suelto sin destinatario escrito a mano: primero el correo de la ficha
/// del cliente; si no lo tiene, el contacto principal de §3.2 («el que se usa si no se elige
/// otro»), y si no hay principal, el único contacto con casilla.
/// <para>
/// <b>Con varias casillas y ningún principal no se elige uno</b>: se excluye y se dice. Mandarle la
/// factura al primero de la lista sería mandársela a alguien que nadie eligió. Ver
/// <see cref="MotivoExclusionEnvio.SinContactoElegible"/>.
/// </para>
/// </summary>
internal static class DestinatarioEnvio
{
    /// <summary>
    /// El contacto elegido, o el motivo por el que no hay ninguno. El cliente que se le pasa tiene
    /// sus contactos cargados: la elección se hace en memoria porque mira <c>EsPrincipal</c> y
    /// cuenta los que tienen casilla.
    /// </summary>
    public static Eleccion Elegir(Cliente cliente)
    {
        var nombre = cliente.Nombre;

        if (!string.IsNullOrWhiteSpace(cliente.Email))
        {
            return new(cliente.Email.Trim().ToLowerInvariant(), nombre, MotivoExclusionEnvio.Ninguno, null);
        }

        var conCorreo = cliente.Contactos
            .Where(c => !string.IsNullOrWhiteSpace(c.Email))
            .OrderBy(c => c.Nombre, StringComparer.Ordinal)
            .ToList();

        var principal = conCorreo.FirstOrDefault(c => c.EsPrincipal);

        if (principal is not null)
        {
            return Elegido(principal, nombre);
        }

        if (conCorreo.Count == 1)
        {
            return Elegido(conCorreo[0], nombre);
        }

        return conCorreo.Count == 0
            ? new(
                null,
                nombre,
                MotivoExclusionEnvio.SinDireccionCorreo,
                $"El cliente «{nombre}» no tiene correo en su ficha ni ningún contacto con casilla, "
                + "así que no hay a quién mandarle. Ponle el correo y vuelve a preparar.")
            : new(
                null,
                nombre,
                MotivoExclusionEnvio.SinContactoElegible,
                $"El cliente «{nombre}» tiene {conCorreo.Count} contactos con correo y ninguno es el "
                + "principal, así que no se sabe a cuál mandarle. Marca uno como principal.");
    }

    private static Eleccion Elegido(ContactoCliente contacto, string nombreCliente) =>
        new(contacto.Email!.Trim().ToLowerInvariant(), contacto.Nombre, MotivoExclusionEnvio.Ninguno, null);

    /// <summary>A quién sale y por qué no sale a nadie, si no sale a nadie.</summary>
    public sealed record Eleccion(
        string? Email,
        string? Nombre,
        MotivoExclusionEnvio Motivo,
        string? Detalle);
}