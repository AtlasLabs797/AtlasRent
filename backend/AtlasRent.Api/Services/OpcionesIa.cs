namespace AtlasRent.Api.Services;

/// <summary>
/// Los cuatro proveedores de IA que admite el asistente (F6-02). El nombre del enum es el que
/// viaja en <c>Ia__Proveedor</c> y el que sale en la respuesta, tal cual: sin traducir, para que el
/// logs y el frontend hablen el mismo idioma que la configuración.
/// </summary>
public enum ProveedorIa
{
    Anthropic = 1,
    Openai = 2,
    Openrouter = 3,
    Minimax = 4,
}

/// <summary>
/// Configuración de un proveedor. Una sección por proveedor (<c>Ia__Anthropic__Clave</c>,
/// <c>Ia__Openai__Clave</c>…) con la misma forma para los cuatro.
/// </summary>
public sealed class OpcionesProveedorIa
{
    /// <summary>
    /// Clave de API. <b>Nunca en el repositorio</b>: solo en variable de entorno o en secretos de
    /// usuario. Sin clave el proveedor no está disponible y el asistente responde 503.
    /// </summary>
    public string? Clave { get; init; }

    /// <summary>
    /// Raíz de la API, sin la ruta final. Cada familia la pone donde le toca: Anthropic añade
    /// <c>/v1/messages</c>, y el dialecto <c>chat/completions</c> añade <c>/chat/completions</c>.
    /// </summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>Nombre del modelo, tal y como lo llama el proveedor.</summary>
    public string Modelo { get; init; } = string.Empty;

    /// <summary>
    /// Cabeceras extra que exija el proveedor. Hoy solo OpenRouter (<c>HTTP-Referer</c> y
    /// <c>X-OpenRouter-Title</c>, que usa para atribuir la aplicación en su panel).
    /// </summary>
    public IReadOnlyDictionary<string, string> Cabeceras { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Si este proveedor puede usarse ahora mismo. Una clave vacía o en blanco es lo mismo que
    /// ninguna: es un despliegue sin configurar, no un despliegue roto.
    /// </summary>
    public bool Configurado =>
        !string.IsNullOrWhiteSpace(Clave)
        && !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(Modelo);
}

/// <summary>
/// Configuración del asistente (sección <c>Ia</c> de la configuración).
/// <para>
/// <b>Los límites van en número de consultas, no en dinero.</b> El precio por millón de tokens
/// cambia sin avisar en los cuatro proveedores y hardcodearlo en el repositorio es una deuda que
/// caduca mal: el día que sube el precio, el tope que creías silencioso deja de proteger nada. El
/// gasto se mide (<c>ia.consulta</c> guarda los tokens de cada llamada) y quien quiera puede
/// convertirlo a coste fuera del código.
/// </para>
/// </summary>
public sealed class OpcionesIa
{
    public const string Seccion = "Ia";

    /// <summary>
    /// Interruptor (<c>Ia__Activado</c>). Apagado, el asistente responde 503 con un mensaje claro:
    /// una instalación que no ha comprado cuota no es un error del servidor.
    /// </summary>
    public bool Activado { get; init; } = true;

    /// <summary>Proveedor por defecto (<c>Ia__Proveedor</c>).</summary>
    public ProveedorIa Proveedor { get; init; } = ProveedorIa.Anthropic;

    /// <summary>
    /// Tope global de consultas al mes para toda la instalación (<c>Ia__TopeConsultasMes</c>), por
    /// encima del límite diario de cada token de agente.
    /// </summary>
    public int TopeConsultasMes { get; init; } = 2000;

    /// <summary>
    /// Tope de tokens de salida por llamada (<c>Ia__MaxTokensSalida</c>). Es una frontera de coste y
    /// de latencia, no un parámetro de calidad: 1.024 tokens dan para una respuesta con cifras.
    /// </summary>
    public int MaxTokensSalida { get; init; } = 1024;

    public OpcionesProveedorIa Anthropic { get; init; } = new() { BaseUrl = "https://api.anthropic.com" };

    public OpcionesProveedorIa Openai { get; init; } = new() { BaseUrl = "https://api.openai.io/v1" };

    public OpcionesProveedorIa Openrouter { get; init; } = new() { BaseUrl = "https://openrouter.ai/api/v1" };

    public OpcionesProveedorIa Minimax { get; init; } = new() { BaseUrl = "https://api.minimax.io/v1" };

    /// <summary>Configuración del proveedor pedido.</summary>
    public OpcionesProveedorIa Del(ProveedorIa proveedor) => proveedor switch
    {
        ProveedorIa.Anthropic => Anthropic,
        ProveedorIa.Openai => Openai,
        ProveedorIa.Openrouter => Openrouter,
        ProveedorIa.Minimax => Minimax,
        _ => Anthropic,
    };

    /// <summary>
    /// Si el asistente puede contestar ahora mismo. Si no puede, el motivo va en el mensaje del
    /// 503 para que quien lo lee sepa si tiene que corregir una clave o encender el interruptor.
    /// </summary>
    public bool Disponible => Activado && Del(Proveedor).Configurado;

    /// <summary>Nombre corto del proveedor, tal y como sale en la respuesta y en la auditoría.</summary>
    public static string Nombre(ProveedorIa proveedor) => proveedor.ToString().ToLowerInvariant();

    public static OpcionesIa Desde(IConfiguration configuracion) => new()
    {
        Activado = configuracion.GetValue($"{Seccion}:Activado", true),
        Proveedor = LeerProveedor(configuracion.GetValue<string>($"{Seccion}:Proveedor")),
        TopeConsultasMes = configuracion.GetValue($"{Seccion}:TopeConsultasMes", 2000),
        MaxTokensSalida = configuracion.GetValue($"{Seccion}:MaxTokensSalida", 1024),
        Anthropic = LeerProveedor(configuracion, nameof(ProveedorIa.Anthropic), "https://api.anthropic.com"),
        Openai = LeerProveedor(configuracion, nameof(ProveedorIa.Openai), "https://api.openai.io/v1"),
        Openrouter = LeerProveedor(
            configuracion,
            nameof(ProveedorIa.Openrouter),
            "https://openrouter.ai/api/v1",
            OpenRouter(configuracion)),
        Minimax = LeerProveedor(configuracion, nameof(ProveedorIa.Minimax), "https://api.minimax.io/v1"),
    };

    /// <summary>
    /// Un proveedor mal escrito no es un 500 al arrancar: es la instalación que se queda con
    /// Anthropic, que es el que está por defecto. La clave se cuela en el mensaje del 503 y en el
    /// log de Serilog como propiedad suelta, nunca dentro del texto: «clave» no es una clave.
    /// </summary>
    private static ProveedorIa LeerProveedor(string? nombre) =>
        Enum.TryParse<ProveedorIa>(nombre, ignoreCase: true, out var proveedor)
            ? proveedor
            : ProveedorIa.Anthropic;

    private static OpcionesProveedorIa LeerProveedor(
        IConfiguration configuracion,
        string seccion,
        string basePorDefecto,
        IReadOnlyDictionary<string, string>? cabeceras = null) => new()
    {
        Clave = configuracion.GetValue<string>($"{Seccion}:{seccion}:Clave"),
        BaseUrl = configuracion.GetValue($"{Seccion}:{seccion}:BaseUrl", basePorDefecto),
        Modelo = configuracion.GetValue($"{Seccion}:{seccion}:Modelo", string.Empty),
        Cabeceras = cabeceras ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
    };

    /// <summary>
    /// OpenRouter atribuye las llamadas en su panel con estas dos cabeceras. Si el despliegue no las
    /// pone, se mandan vacías y las omite el cliente: OpenRouter las acepta igual.
    /// </summary>
    private static IReadOnlyDictionary<string, string> OpenRouter(IConfiguration configuracion)
    {
        var cabeceras = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var referer = configuracion.GetValue<string>($"{Seccion}:Openrouter:Referer");
        if (!string.IsNullOrWhiteSpace(referer))
        {
            cabeceras["HTTP-Referer"] = referer;
        }

        var titulo = configuracion.GetValue<string>($"{Seccion}:Openrouter:Titulo");
        if (!string.IsNullOrWhiteSpace(titulo))
        {
            cabeceras["X-OpenRouter-Title"] = titulo;
        }

        return cabeceras;
    }
}