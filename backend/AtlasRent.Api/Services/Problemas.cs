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

    /// <summary>Token de agente desconocido, revocado, caducado o de un usuario que ya no puede entrar (F6-01).</summary>
    public const string TokenAgenteInvalido = "AUT-005";

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

    /// <summary>La clave de idempotencia ya se usó con una petición distinta.</summary>
    public const string IdempotenciaEnConflicto = "API-002";

    /// <summary>
    /// La serie apunta a una plantilla de PDF que este catálogo no tiene (F4-01). Va aparte de
    /// <see cref="Validacion"/> porque no es un dato de la petición que se pueda corregir en el
    /// formulario: la plantilla la eligió el administrador en la configuración de la serie.
    /// </summary>
    public const string PlantillaDesconocida = "API-004";

    /// <summary>
    /// Ya existe un registro con ese valor único (número o prefijo de serie, tipo de retención por
    /// defecto), o el registro cambió entre que se leyó y se guardó.
    /// </summary>
    public const string ValorDuplicado = "API-003";

    /// <summary>
    /// No hay servidor de correo configurado y por eso no se puede mandar nada (F4-02). Va aparte
    /// porque el arreglo no es corregir la petición: es escribir una variable de entorno del
    /// despliegue, y quien lo lee necesita ese dato para saber a quién llamar. Mismo criterio que
    /// BUG-007 con la tolerancia de cambio.
    /// </summary>
    public const string CorreoNoConfigurado = "API-005";

    /// <summary>
    /// La unidad no está disponible para esas fechas: la chocan con un contrato, con otra reserva o
    /// con la capacidad del día (F2-01, docs/ESPECIFICACION.md §3.4).
    /// </summary>
    public const string UnidadNoDisponible = "DIS-001";

    /// <summary>
    /// El asistente no puede contestar ahora mismo: la IA está desactivada, el proveedor elegido
    /// no tiene clave, o el proveedor falló (F6-02). <b>Un 503 y no un 500</b>: un proveedor caído no
    /// es un error de este servidor, y el frontend ya lo trata como una situación que se pasa sola.
    /// </summary>
    public const string ProveedorIaNoDisponible = "IA-001";

    /// <summary>
    /// Se agotó el límite de consultas al asistente: el diario del token de agente o el tope global
    /// del mes (F6-02). Se rechaza antes de llamar al proveedor, así que no se ha gastado nada con
    /// esta petición.
    /// </summary>
    public const string LimiteConsultasIa = "IA-002";

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