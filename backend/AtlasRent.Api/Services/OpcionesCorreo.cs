namespace AtlasRent.Api.Services;

/// <summary>
/// Configuración del servidor de correo (F4-02, docs/ESPECIFICACION.md §3.14), de la sección
/// <c>Correo</c> de la configuración (<c>Correo__Host</c>, <c>Correo__Puerto</c>,
/// <c>Correo__Usuario</c>, <c>Correo__Clave</c>, <c>Correo__Ssl</c>, <c>Correo__Remitente</c> y
/// <c>Correo__NombreRemitente</c>).
/// <para>
/// <b>La clave vive en variables de entorno o en <c>dotnet user-secrets</c>, nunca en la base de
/// datos ni en un fichero del repositorio</b> (docs/SEGURIDAD.md A.7). Aquí sale de
/// <c>IConfiguration</c> y no hay ninguna columna donde escribirla: si el correo se configurara en
/// la base, la clave acabaría en los backups y en la tabla, que es justo lo que no se quiere.
/// </para>
/// <para>
/// <b>Sin configurar, el envío falla.</b> No es que no se pueda, es que no se deja pasar en silencio:
/// un ajuste que se permite sin que nadie haya escrito un valor es una pérdida silenciosa (el mismo
/// criterio que fijó BUG-007 con la tolerancia de diferencia de cambio).
/// </para>
/// </summary>
public sealed class OpcionesCorreo
{
    public const string Seccion = "Correo";

    public string? Host { get; init; }

    public int Puerto { get; init; } = 587;

    public string? Usuario { get; init; }

    public string? Clave { get; init; }

    /// <summary>
    /// <c>true</c> para STARTTLS sobre el puerto 587 (lo de siempre), <c>false</c> para TLS
    /// implícito, que es el 465. No hay «sin cifrar»: un correo con datos de factura no viaja en
    /// claro ni contra el servidor del proveedor.
    /// </summary>
    public bool Ssl { get; init; } = true;

    public string? Remitente { get; init; }

    /// <summary>Nombre que sale junto a la dirección en el encabezado <c>From</c>. Opcional.</summary>
    public string? NombreRemitente { get; init; }

    /// <summary>
    /// Si está lo mínimo para hablar con el servidor: host, remitente y, cuando hay usuario, la
    /// clave. Un servidor sin autenticación es posible (el de una red local), así que la clave solo
    /// es obligatoria si hay usuario.
    /// </summary>
    public bool Configurado =>
        !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(Remitente)
        && (string.IsNullOrWhiteSpace(Usuario) || !string.IsNullOrWhiteSpace(Clave));

    public static OpcionesCorreo Desde(IConfiguration configuracion)
    {
        var puerto = configuracion[$"{Seccion}:Puerto"];
        var ssl = configuracion[$"{Seccion}:Ssl"];

        return new OpcionesCorreo
        {
            Host = Limpio(configuracion[$"{Seccion}:Host"]),
            Puerto = int.TryParse(puerto, out var valor) ? valor : 587,
            Usuario = Limpio(configuracion[$"{Seccion}:Usuario"]),
            Clave = configuracion[$"{Seccion}:Clave"],
            Ssl = ssl is null ? true : bool.TryParse(ssl, out var activado) ? activado : true,
            Remitente = Limpio(configuracion[$"{Seccion}:Remitente"]),
            NombreRemitente = Limpio(configuracion[$"{Seccion}:NombreRemitente"]),
        };
    }

    private static string? Limpio(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}