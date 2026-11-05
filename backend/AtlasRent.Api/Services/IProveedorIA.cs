namespace AtlasRent.Api.Services;

/// <summary>
/// Lo que se le pide a un proveedor. <b>No hay herramientas ni funciones que definir</b>: el modelo
/// solo puede contestar con texto. Ver <see cref="ServicioConsultaIa"/> para por qué eso es la
/// defensa contra la inyección de prompt.
/// </summary>
/// <param name="Sistema">Instrucciones de sistema, en español.</param>
/// <param name="Pregunta">La pregunta del usuario, ya seudonimizada.</param>
/// <param name="Datos">Los datos del alcance, ya seudonimizados, en JSON.</param>
public sealed record PeticionProveedor(string Sistema, string Pregunta, string Datos);

/// <summary>
/// Respuesta normalizada de un proveedor. Los cuatro se reducen a esto, así que el resto del
/// sistema no depende de cuál contestó.
/// </summary>
/// <param name="Texto">Respuesta en texto plano.</param>
/// <param name="TokensEntrada">Tokens de la petición, para medir el gasto.</param>
/// <param name="TokensSalida">Tokens de la respuesta, para medir el gasto.</param>
public sealed record RespuestaProveedor(string Texto, int TokensEntrada, int TokensSalida);

/// <summary>
/// Un proveedor concreto de IA. Hay <b>dos implementaciones</b>, no cuatro: la API de mensajes de
/// Anthropic y el dialecto <c>chat/completions</c> que OpenAI, OpenRouter y MiniMax ya comparten.
/// Tres clases que solo cambian la URL base y la clave son tres sitios donde arreglar un cambio.
/// </summary>
public interface IProveedorIA
{
    /// <summary>
    /// Si esta implementación atiende a ese proveedor. No es «qué proveedor soy» porque
    /// <see cref="ProveedorChatCompletions"/> atiende a tres a la vez y no puede decir cuál es sin
    /// mirar la configuración.
    /// </summary>
    bool Atiende(ProveedorIa proveedor);

    /// <summary>
    /// Hace la consulta. <b>Sin reintentos</b>: un reintento automático duplica el gasto cada vez
    /// que el proveedor contesta y nadie lo ve.
    /// </summary>
    /// <exception cref="ProveedorIaException">El proveedor no respondió o respondió algo que no se entiende.</exception>
    Task<RespuestaProveedor> ConsultarAsync(PeticionProveedor peticion, CancellationToken ct);
}

/// <summary>
/// Fallo del proveedor. Se traduce a <b>503</b>, nunca a 500: un proveedor caído no es un error del
/// servidor de Atlas Rent, y el frontend ya distingue los dos.
/// <para>
/// El mensaje lleva el motivo (timeout, clave rechazada, respuesta ilegible) porque no lleva ni la
/// clave ni la respuesta del proveedor, que volverían a subir a la base de datos de un tercero.
/// </para>
/// </summary>
public sealed class ProveedorIaException(string mensaje, Exception? causa = null)
    : Exception(mensaje, causa);

/// <summary>
/// Se pasó el límite de consultas: el diario del token de agente o el tope global del mes. Se
/// traduce a <b>429</b> y queda auditado, igual que cualquier otro intento rechazado.
/// </summary>
public sealed class LimiteIaExcedidoException(string mensaje) : Exception(mensaje);