namespace AtlasRent.Api.Services;

/// <summary>
/// Plantilla de PDF de un documento. Lo mínimo que necesita el compositor es el tamaño de la
/// página, y el nombre es lo que la serie guarda (F4-01, <c>docs/ESPECIFICACION.md</c> §3.7).
/// </summary>
/// <param name="Nombre">Identificador que guarda <c>Serie.PlantillaPdf</c>.</param>
/// <param name="Version">Versión del diseño. Sube cuando el documento cambia de forma: así una
/// factura emitida con una versión se puede distinguir de una emitida con otra.</param>
/// <param name="Ancho">Ancho de la página en puntos (612 = carta, <c>docs/DESIGN.md</c> §7).</param>
/// <param name="Alto">Alto de la página en puntos (792 = carta).</param>
internal sealed record PlantillaPdf(string Nombre, string Version, float Ancho, float Alto);

/// <summary>
/// Catálogo de plantillas de PDF conocidas (F4-01).
/// <para>
/// <b>Hoy hay una sola</b>, la <c>estandar</c> de <c>docs/DESIGN.md §7</c>. El registro existe para
/// que las plantillas por serie que aporte el responsable en F0-06 (los ejemplos actuales por serie)
/// quepan aquí sin volver a tocar el compositor.
/// </para>
/// <para>
/// <b>Un nombre que no está en el catálogo no cae en la de por defecto.</b> Rejectarlo al configurar
/// la serie es la diferencia entre un documento que se imprimió con el diseño que alguien pidió y
/// uno que se imprimió con otro sin que nadie se enterara. Por eso <see cref="Resolver"/> lanza y
/// <see cref="ServicioConfiguracion"/> lo traduce a un 400 que lista las válidas.
/// </para>
/// <para>
/// TODO(A#): F0-06 (A2) trae los ejemplos de PDF actuales por serie. Cuando lleguen, cada ejemplo es
/// una entrada más de este catálogo con su versión, y el compositor decide con ella.
/// </para>
/// </summary>
internal static class RegistroPlantillasPdf
{
    /// <summary>La de <c>docs/DESIGN.md §7</c>. La que usa toda serie que no diga otra cosa.</summary>
    private static readonly PlantillaPdf Estandar = new("estandar", "1", 612f, 792f);

    /// <summary>Las plantillas del catálogo, en el orden en que se ofrecen al administrador.</summary>
    public static IReadOnlyList<PlantillaPdf> Conocidas { get; } = [Estandar];

    /// <summary>Los nombres válidos, para el mensaje del 400 y para el <c>GET</c> de la serie.</summary>
    public static IReadOnlyList<string> Nombres { get; } = [.. Conocidas.Select(p => p.Nombre)];

    /// <summary>Nombre de la plantilla de por defecto, que es la que lleva una serie sin <c>PlantillaPdf</c>.</summary>
    public static string NombrePorDefecto => Estandar.Nombre;

    /// <summary>
    /// Resuelve el nombre de una serie. Nulo o vacío da la de por defecto; cualquier otro nombre
    /// tiene que existir en el catálogo.
    /// </summary>
    /// <exception cref="ArgumentException">El nombre no está en el catálogo. El mensaje lleva la
    /// lista de los válidos, que es lo que el 400 enseña.</exception>
    public static PlantillaPdf Resolver(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            return Estandar;
        }

        return Conocidas.FirstOrDefault(p => string.Equals(p.Nombre, nombre.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException(
                $"La plantilla de PDF «{nombre.Trim()}» no existe. Las válidas son: {string.Join(", ", Nombres)}.");
    }
}
