using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.DTOs;

/// <summary>
/// Lo que se manda en masa y con qué criterio (F4-03, docs/ESPECIFICACION.md §3.14).
/// <para>
/// <b>Los filtros no se reescriben aquí.</b> <see cref="Filtro"/> es el mismo tipo que devuelve
/// <c>GET /api/v1/cuentas</c> y el mismo que filtra la vista única, y el lote se arma con
/// <c>ServicioCuentas.ListarAsync</c>. Un filtro paralelo sería el segundo sitio donde vive «qué
/// documentos entran», que es exactamente cómo una pantalla y un correo acaban mandando cosas
/// distintas.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EnvioMasivoPeticion
{
    /// <summary>Los filtros de la vista única de cuentas (§3.13), tal cual.</summary>
    [Required]
    public required FiltroCuentas Filtro { get; init; }

    /// <summary>
    /// Idioma de la plantilla. Si no viene, <c>es</c>. Un idioma sin plantilla excluye los
    /// documentos, en vez de mandarles en español sin avisar: ver <see cref="MotivoExclusionEnvio.SinPlantilla"/>.
    /// </summary>
    [MaxLength(10)]
    public string? Idioma { get; init; }

    /// <summary>
    /// Manda también a los documentos que ya se enviaron a ese destinatario (§3.14). Es el único
    /// motivo de exclusión que se puede saltar, y solo lo salta quien lo pide a propósito.
    /// </summary>
    public bool Forzar { get; init; }
}

/// <summary>
/// Un documento que entra en el lote, con su destinatario y <b>la vista previa de lo que va a
/// salir</b> (F4-03, §3.14: «pantalla de resumen (cuántos, excluidos con motivo, vista previa)»).
/// <para>
/// <see cref="Asunto"/> y <see cref="Cuerpo"/> llevan las variables ya sustituidas y son
/// exactamente los que van en la fila de <c>envio</c> que se va a encolar, no una aproximación: el
/// resumen es el punto en el que alguien lee lo que va a salir antes de que salga.
/// </para>
/// </summary>
public sealed record EnvioMasivoDocumento(
    Guid DocumentoId,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoDocumentoEnvio>))] TipoDocumentoEnvio TipoDocumento,
    string Numero,
    Guid ClienteId,
    string? Cliente,
    string DestinatarioEmail,
    string? DestinatarioNombre,
    string Asunto,
    string Cuerpo,
    string NombreArchivo,
    string Idioma);

/// <summary>
/// Un documento que el filtro trae y el lote no manda, con el motivo por el que no entra (§3.14).
/// El motivo va como código para que la interfaz pueda agrupar, y <see cref="Detalle"/> lleva el
/// texto que se puede leer tal cual, sin traducir nada.
/// <para>
/// <para name="Tipo">Es el <b>tipo de fila de la vista</b> (§3.13) y no un tipo de envío: una fianza
/// no es un documento que se pueda mandar y aquí aparece como fianza, que es lo que es.
/// </para>
/// <para name="Numero">El número sin el prefijo de serie, que es el que trae la fila de la vista. El
/// número completo con la serie lo compone la vista de cuentas, que es de donde salió esta fila.
/// </para>
/// </summary>
public sealed record EnvioMasivoExcluido(
    Guid DocumentoId,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoFilaCuenta>))] TipoFilaCuenta Tipo,
    string? Numero,
    string? Cliente,
    [property: JsonConverter(typeof(JsonStringEnumConverter<MotivoExclusionEnvio>))] MotivoExclusionEnvio Motivo,
    string Detalle);

/// <summary>
/// El resumen de lo que se va a mandar, <b>sin mandar nada</b> (F4-03, §3.14).
/// <para>
/// <see cref="Aviso"/> lo dice en la respuesta y no solo en el nombre del endpoint: preparar es
/// inocuo, pero quien lo llama desde código puede no saberlo, y una pantalla que manda mil correos
/// sin que nadie lo haya pedido es el peor resultado posible de esta tarea.
/// </para>
/// </summary>
public sealed record EnvioMasivoResumen(
    int Total,
    int TotalExcluidos,
    IReadOnlyList<EnvioMasivoDocumento> Documentos,
    IReadOnlyList<EnvioMasivoExcluido> Excluidos,
    FiltroCuentas Filtro,
    string Idioma,
    string Aviso);

/// <summary>
/// Lo que responde la confirmación: qué lote se ha creado y cuántos envíos quedan en la cola.
/// <b>Confirmar no manda nada</b>: manda el trabajo de la cola, uno por documento.
/// </summary>
public sealed record LoteEnvio(
    Guid LoteId,
    int Total,
    int Excluidos,
    string Idioma,
    string Aviso);

/// <summary>
/// Un envío del lote que falló, con el motivo que se guarda en <c>ultimo_error</c> y el contador de
/// intentos. Es lo que hay que ver para decidir si reintentar tiene sentido.
/// </summary>
public sealed record EnvioMasivoFallo(
    Guid EnvioId,
    Guid DocumentoId,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TipoDocumentoEnvio>))] TipoDocumentoEnvio TipoDocumento,
    string? Cliente,
    string DestinatarioEmail,
    int Intentos,
    string? Motivo);

/// <summary>
/// El progreso de un lote (F4-03, §3.14).
/// <para>
/// Los tres recuentos son sobre las filas que comparten <c>lote_id</c>: un envío del lote cuenta
/// una vez, aunque el mismo documento se haya reintentado, porque el reintento sube <c>intentos</c>
/// en la fila que ya estaba y no crea otra.
/// </para>
/// <para>
/// <b><c>Enviados</c> cuenta también <c>Entregado</c> y <c>Leido</c></b> si algún día los escribe el
/// proveedor: son estados de «sí salió», solo que con más información.
/// </para>
/// </summary>
public sealed record ProgresoLote(
    Guid LoteId,
    int Total,
    int Enviados,
    int Fallidos,
    int Pendientes,
    IReadOnlyList<EnvioMasivoFallo> FallidosDetalle);

/// <summary>
/// Lo que responde un reintento: cuántos envíos del lote se han vuelto a encolar.
/// <see cref="Reintentados"/> es 0 cuando no había ninguno fallido, y eso no es un error: el criterio
/// de aceptación es «reintenta solo fallidos», y no había fallidos.
/// </summary>
public sealed record ReintentoLote(
    Guid LoteId,
    int Reintentados,
    int Pendientes,
    string Aviso);