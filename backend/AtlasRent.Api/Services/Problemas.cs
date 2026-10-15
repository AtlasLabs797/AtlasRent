using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Serilog;

namespace AtlasRent.Api.Services;

/// <summary>
/// Errores de la API en <c>ProblemDetails</c> con un código de referencia (docs/DOC_TECNICA.md §6).
/// El código va también al log: el usuario ve el código, el soporte busca en el log y nadie ve
/// trazas ni detalles internos.
/// </summary>
public static class Problemas
{
    /// <summary>Login con nombre de acceso inexistente o contraseña incorrecta. El mismo en ambos casos.</summary>
    public const string CredencialesInvalidas = "AUT-001";

    /// <summary>No hay sesión, o la sesión está revocada o su usuario ya no puede entrar.</summary>
    public const string SesionNoValida = "AUT-002";

    /// <summary>La sesión se pasó de inactividad o del máximo absoluto.</summary>
    public const string SesionExpirada = "AUT-003";

    /// <summary>Token de refresco desconocido, ya rotado o revocado.</summary>
    public const string RefrescoInvalido = "AUT-004";

    /// <summary>La cabecera <c>X-CSRF-Token</c> no coincide con la cookie.</summary>
    public const string CsrfInvalido = "SEG-001";

    /// <summary>Se superó el límite de peticiones de la IP o del usuario.</summary>
    public const string LimitePeticiones = "SEG-002";

    /// <summary>La IP está bloqueada por intentos fallidos de login.</summary>
    public const string IpBloqueada = "SEG-003";

    /// <summary>
    /// El token provisional del segundo factor no existe, ya se gastó o caducó, o el código no es
    /// válido. El mismo código para los tres casos: no se le dice a nadie en cuál falló.
    /// </summary>
    public const string MfaInvalido = "SEG-004";

    /// <summary>La petición no lleva con qué comprobar el segundo factor: ni sesión ni token provisional.</summary>
    public const string MfaNoVerificado = "SEG-005";

    /// <summary>El acceso de emergencia no se puede pedir ni aprobar con quien lo pide.</summary>
    public const string EmergenciaDenegada = "SEG-006";

    /// <summary>
    /// El usuario no tiene el permiso que exige el endpoint, o el endpoint no declara ninguno.
    /// Todo intento denegado queda también en la auditoría firmada.
    /// </summary>
    public const string PermisoDenegado = "SEG-007";

    /// <summary>
    /// Se ha intentado activar a un usuario un permiso que su rol no trae: los permisos por usuario
    /// recortan el techo del rol, nunca lo amplían (D17).
    /// </summary>
    public const string PermisoFueraDeTecho = "SEG-008";

    /// <summary>El usuario, el rol o el recurso al que se refiere la operación no existe.</summary>
    public const string RecursoNoEncontrado = "SEG-009";

    /// <summary>Error de validación de los datos de entrada.</summary>
    public const string Validacion = "API-001";

    /// <summary>
    /// Construye la respuesta de error y la registra. No lleva traza: solo el código, el título y
    /// una explicación que no delate datos internos.
    /// </summary>
    public static ObjectResult Construir(int estado, string codigoReferencia, string titulo, string detalle)
    {
        Log.Warning("Error {CodigoReferencia}: {Detalle}", codigoReferencia, detalle);

        var problema = new ProblemDetails
        {
            Status = estado,
            Title = titulo,
            Detail = detalle,
        };
        problema.Extensions["codigoReferencia"] = codigoReferencia;

        return new ObjectResult(problema) { StatusCode = estado };
    }

    /// <summary>Escribe una respuesta de error desde el middleware, sin pasar por el controlador.</summary>
    public static Task EscribirAsync(HttpContext contexto, ObjectResult resultado) =>
        resultado.ExecuteResultAsync(new ActionContext(contexto, contexto.GetRouteData(), new ActionDescriptor()));
}