using System.Data.Common;
using AtlasRent.Api.Services;
using Serilog;

namespace AtlasRent.Api.Middleware;

/// <summary>
/// Bloquea las peticiones que llegan desde una IP bloqueada por intentos fallidos de login
/// (docs/SEGURIDAD.md A.2, docs/DOC_TECNICA.md §7).
/// <para>
/// Va antes que el limitador de peticiones y que la autenticación: una IP bloqueada no gasta ni un
/// solo ciclo de BCrypt ni de validación de JWT, y el bloqueo vale para <strong>todas</strong> las
/// rutas, no solo para el login.
/// </para>
/// </summary>
public sealed class IpBanMiddleware(RequestDelegate siguiente)
{
    public async Task InvokeAsync(HttpContext http, ServicioBloqueos bloqueos, IResolutorIpPeticion resolutor)
    {
        var ip = resolutor.Resolver(http);

        bool bloqueada;
        try
        {
            bloqueada = await bloqueos.IpBloqueadaAsync(ip, http.RequestAborted);
        }
        catch (Exception excepcion) when (EsFalloDeBaseDeDatos(excepcion))
        {
            // Si no se puede consultar la base de datos no se bloquea a nadie: /health/live tiene que
            // seguir respondiendo sin base de datos y, en cualquier caso, toda ruta real necesita la
            // base de datos para validar sesión. Se registra el tipo de fallo, nada más.
            Log.Warning("No se pudo comprobar el bloqueo de IP {Ip}: {TipoError}", ip, excepcion.GetType().Name);
            await siguiente(http);
            return;
        }

        if (bloqueada)
        {
            await Problemas.EscribirAsync(http, Problemas.Construir(
                StatusCodes.Status403Forbidden,
                Problemas.IpBloqueada,
                "Petición rechazada",
                "La IP está bloqueada por intentos fallidos de acceso."));
            return;
        }

        await siguiente(http);
    }

    /// <summary>
    /// EF envuelve el fallo de conexión de Npgsql en una <see cref="InvalidOperationException"/> ("transient
    /// failure"), así que no basta con capturar <see cref="DbException"/>: se mira también la causa.
    /// </summary>
    private static bool EsFalloDeBaseDeDatos(Exception excepcion)
    {
        for (var e = excepcion; e is not null; e = e.InnerException)
        {
            if (e is DbException)
            {
                return true;
            }
        }

        return false;
    }
}