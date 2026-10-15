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