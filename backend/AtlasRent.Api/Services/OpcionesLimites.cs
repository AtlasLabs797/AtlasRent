namespace AtlasRent.Api.Services;

/// <summary>
/// Valores de los límites de peticiones y de los bloqueos, con los que arranca la aplicación
/// (docs/DOC_TECNICA.md §7, docs/SEGURIDAD.md A.2).
/// <para>
/// Todos son configurables en <c>Seguridad:Limites:*</c> porque un despliegue puede necesitar
/// aflojarlos, pero los de por defecto son los que fija el checklist: 5 fallos bloquean la cuenta
/// 30 minutos y 10 fallos desde una IP en 15 minutos la bloquean 1 h, con escalado a 24 h y a
/// indefinido.
/// </para>
/// <para>
/// Los contadores de peticiones viven en memoria de la instancia: con una sola instancia, como la
/// del despliegue del proyecto (Windows Server, un servicio), es lo correcto. Si algún día hubiera
/// dos o más réplicas, el límite pasaría a ser por réplica y habría que mover el rate limiting a
/// almacenamiento compartido. El bloqueo de IP no tiene ese problema: está en la base de datos, así
/// que sigue siendo global con varias instancias.
/// </para>
/// </summary>
public sealed class OpcionesLimites
{
    public const string Seccion = "Seguridad:Limites";

    /// <summary>
    /// Peticiones por minuto y por usuario para cualquier petición autenticada, y por IP cuando la
    /// petición no lo está. Es el límite global, el más ancho.
    /// </summary>
    public int GlobalPorMinuto { get; init; } = 300;

    /// <summary>
    /// Peticiones por minuto de login, por IP. Tiene que ser más estricto que
    /// <see cref="GlobalPorMinuto"/>: el login es lo que se ataca. El control por nombre de acceso
    /// no está aquí sino en el bloqueo de cuenta (<see cref="IntentosParaBloquearCuenta"/>), que es
    /// más fuerte porque persiste en la base de datos. Ver <c>ClavesLimite.Login</c>.
    /// </summary>
    public int LoginPorMinuto { get; init; } = 20;

    /// <summary>Fallos de login desde una IP que la bloquean, dentro de <see cref="VentanaIpMinutos"/>.</summary>
    public int IntentosParaBloquearIp { get; init; } = 10;

    /// <summary>Ventana, en minutos, en la que se cuentan los fallos de una IP.</summary>
    public int VentanaIpMinutos { get; init; } = 15;

    /// <summary>Fallos de login consecutivos de una cuenta que la bloquean.</summary>
    public int IntentosParaBloquearCuenta { get; init; } = 5;

    /// <summary>Duración del bloqueo de cuenta, en minutos.</summary>
    public int MinutosBloqueoCuenta { get; init; } = 30;

    /// <summary>Mínimo de la retención: no se purga lo que aún cuenta para bloqueos y avisos de IP nueva.</summary>
    public const int RetencionIntentosLoginMinimaDias = 30;

    /// <summary>
    /// Días que se conservan los intentos de login (SEC-030). Pasados, el trabajo diario los purga:
    /// guardan IP y nombres de acceso (incluidos los inventados por quien ataca) y no hay motivo
    /// para tenerlos para siempre.
    /// </summary>
    public int RetencionIntentosLoginDias { get; init; } = 90;

    public static OpcionesLimites Desde(IConfiguration configuracion) => new()
    {
        GlobalPorMinuto = Leer(configuracion, nameof(GlobalPorMinuto), 300),
        LoginPorMinuto = Leer(configuracion, nameof(LoginPorMinuto), 20),
        IntentosParaBloquearIp = Leer(configuracion, nameof(IntentosParaBloquearIp), 10),
        VentanaIpMinutos = Leer(configuracion, nameof(VentanaIpMinutos), 15),
        IntentosParaBloquearCuenta = Leer(configuracion, nameof(IntentosParaBloquearCuenta), 5),
        MinutosBloqueoCuenta = Leer(configuracion, nameof(MinutosBloqueoCuenta), 30),
        RetencionIntentosLoginDias = Math.Max(
            RetencionIntentosLoginMinimaDias,
            configuracion.GetValue($"{Seccion}:{nameof(RetencionIntentosLoginDias)}", 90)),
    };

    private static int Leer(IConfiguration configuracion, string nombre, int porDefecto) =>
        // Mínimo 1: un 0 en la configuración dejaría la API sin responder a nada.
        Math.Max(1, configuracion.GetValue($"{Seccion}:{nombre}", porDefecto));
}