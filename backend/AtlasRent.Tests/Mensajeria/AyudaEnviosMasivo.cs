using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Jobs;
using AtlasRent.Tests.CicloFactura;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtlasRent.Tests.Mensajeria;

/// <summary>
/// Apoyo común de las pruebas de envío masivo (F4-03, docs/ESPECIFICACION.md §3.14).
/// <para>
/// Los datos salen de los mismos ayudantes que usan las pruebas de F4-02 y de la vista de F3-10: un
/// cliente, un contrato y facturas de verdad. Un lote montado a mano sobre la tabla probaría un envío
/// que el sistema no tiene.
/// </para>
/// </summary>
internal static class AyudaEnviosMasivo
{
    /// <summary>
    /// Sesión de un rol con su identificador de usuario. El identificador hace falta porque los
    /// trabajos de la cola no tienen sesión HTTP: el permiso se le comprueba a quien pidió el envío,
    /// y es el mismo usuario que confirmó.
    /// </summary>
    public static async Task<(SesionCliente Sesion, Guid Usuario)> SesionAsync(
        ContenedorBdFixture bd,
        ApiTestFactory api,
        string rol,
        CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, AyudaFacturas.Contrasena, rol: rol, ct: ct);

        return (await AyudaPermisos.SesionAsync(api, usuario, AyudaFacturas.Contrasena, ct), usuario.Id);
    }

    /// <summary>
    /// El cuerpo de <c>/masivo/preparar</c> y de <c>/masivo/confirmar</c>: los mismos filtros que
    /// acepta <c>GET /api/v1/cuentas</c>, con el idioma y el <c>forzar</c> del envío masivo.
    /// <para>
    /// Se filtra siempre por cliente. La base es compartida por toda la colección de pruebas, así que
    /// un filtro sin cliente traería facturas de las demás pruebas y los recuentos no dirían nada.
    /// </para>
    /// </summary>
    public static object Peticion(
        Guid clienteId,
        string? idioma = null,
        bool forzar = false,
        Guid? unidadId = null) =>
        new
        {
            filtro = new
            {
                clienteId,
                unidadId,
                incluirProformas = false,
                incluirFianzas = true,
            },
            idioma,
            forzar,
        };

    /// <summary>
    /// Prepara el lote y devuelve el resumen. <b>No manda nada</b>: esa es la mitad de lo que
    /// comprueba la prueba que lo llama.
    /// </summary>
    public static async Task<JsonElement> PrepararAsync(
        SesionCliente sesion,
        object peticion,
        CancellationToken ct,
        string? claveIdempotencia = null)
    {
        var respuesta = claveIdempotencia is null
            ? await sesion.PostAsync("/api/v1/envios/masivo/preparar", peticion, ct)
            : await sesion.PostAsync("/api/v1/envios/masivo/preparar", peticion, claveIdempotencia, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());

        return await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    /// <summary>Confirma el lote y devuelve el identificador del lote creado.</summary>
    public static async Task<Guid> ConfirmarAsync(
        SesionCliente sesion,
        object peticion,
        CancellationToken ct,
        string? claveIdempotencia = null)
    {
        var respuesta = claveIdempotencia is null
            ? await sesion.PostAsync("/api/v1/envios/masivo/confirmar", peticion, ct)
            : await sesion.PostAsync("/api/v1/envios/masivo/confirmar", peticion, claveIdempotencia, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());

        return (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("loteId").GetGuid();
    }

    /// <summary>El progreso del lote.</summary>
    public static async Task<JsonElement> ProgresoAsync(
        SesionCliente sesion,
        Guid loteId,
        CancellationToken ct)
    {
        var leido = await sesion.GetAsync($"/api/v1/envios/masivo/{loteId:D}", ct);
        leido.StatusCode.Should().Be(HttpStatusCode.OK, await leido.Cuerpo());

        return await leido.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    /// <summary>Reintenta los fallidos del lote y devuelve el cuerpo de la respuesta.</summary>
    public static async Task<JsonElement> ReintentarAsync(
        SesionCliente sesion,
        Guid loteId,
        CancellationToken ct,
        string? claveIdempotencia = null)
    {
        var respuesta = claveIdempotencia is null
            ? await sesion.PostAsync($"/api/v1/envios/masivo/{loteId:D}/reintentar", new { }, ct)
            : await sesion.PostAsync($"/api/v1/envios/masivo/{loteId:D}/reintentar", new { }, claveIdempotencia, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());

        return await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    /// <summary>
    /// Ejecuta el trabajo de un envío encolado, igual que haría el servidor de jobs. Se pide el
    /// trabajo al contenedor para que use el mismo <see cref="IServicioCorreo"/> —el doble— que el
    /// resto de la prueba.
    /// </summary>
    public static async Task EjecutarTrabajoAsync(
        ApiTestFactoryMasivo api,
        Guid envioId,
        Guid usuario,
        CancellationToken ct)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var trabajo = scope.ServiceProvider.GetRequiredService<TrabajoEnvio>();

        await trabajo.EjecutarAsync(envioId, usuario, "198.51.100.7", ct);
    }

    /// <summary>Las filas de <c>envio</c> del lote, leídas de la base.</summary>
    public static async Task<List<Envio>> FilasDelLoteAsync(
        ContenedorBdFixture bd,
        Guid loteId,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        return CopiaEnvio.EnClaro(await contexto.Envio.AsNoTracking()
            .Where(e => e.LoteId == loteId)
            .OrderBy(e => e.DocumentoId)
            .ToListAsync(ct));
    }

    /// <summary>Las filas de <c>envio</c> de un documento, leídas de la base.</summary>
    public static async Task<List<Envio>> FilasDelDocumentoAsync(
        ContenedorBdFixture bd,
        Guid documentoId,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        return CopiaEnvio.EnClaro(await contexto.Envio.AsNoTracking()
            .Where(e => e.DocumentoId == documentoId)
            .ToListAsync(ct));
    }

    /// <summary>Cuántas filas de <c>envio</c> hay en toda la base.</summary>
    public static async Task<int> TotalEnviosAsync(ContenedorBdFixture bd, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        return await contexto.Envio.AsNoTracking().CountAsync(ct);
    }

    /// <summary>
    /// Le quita el correo al cliente del escenario. Es como se prueba «sin dirección de correo»: un
    /// cliente puede dejar de tener casilla, y entonces no hay a quién mandarle.
    /// </summary>
    public static async Task QuitarCorreoAlClienteAsync(
        SesionCliente sesion,
        Guid clienteId,
        CancellationToken ct)
    {
        var ficha = await sesion.GetAsync($"/api/v1/clientes/{clienteId:D}", ct);
        ficha.StatusCode.Should().Be(HttpStatusCode.OK, await ficha.Cuerpo());

        var cuerpo = await ficha.Content.ReadFromJsonAsync<JsonElement>(ct);

        var actualizado = await sesion.PutAsync(
            $"/api/v1/clientes/{clienteId:D}",
            new
            {
                nombre = cuerpo.GetProperty("nombre").GetString(),
                tipo = cuerpo.GetProperty("tipo").GetString(),
                documento = cuerpo.GetProperty("documento").ValueKind == JsonValueKind.Null
                    ? null
                    : cuerpo.GetProperty("documento").GetString(),
                email = (string?)null,
            },
            ct);

        actualizado.StatusCode.Should().Be(HttpStatusCode.OK, await actualizado.Cuerpo());
    }

    /// <summary>
    /// Si los bytes empiezan por la firma de un PDF. Los bytes no son texto y no hay un
    /// <c>StartWith</c> para ellos, pero la firma es lo que decide si lo adjunto es un papel o un
    /// archivo cualquiera.
    /// </summary>
    public static bool EsPdf(byte[] contenido) =>
        contenido.Length >= 4
        && contenido[0] == (byte)'%'
        && contenido[1] == (byte)'P'
        && contenido[2] == (byte)'D'
        && contenido[3] == (byte)'F';

    /// <summary>Las filas del array de la respuesta, en el orden en el que las pone el servidor.</summary>
    public static JsonElement[] Lista(JsonElement respuesta, string propiedad) =>
        [.. respuesta.GetProperty(propiedad).EnumerateArray()];

    /// <summary>El excluido de un documento, que es como se comprueba el motivo.</summary>
    public static JsonElement ExcluidoDe(JsonElement resumen, Guid documentoId) =>
        Lista(resumen, "excluidos").Should().ContainSingle(e => e.GetProperty("documentoId").GetGuid() == documentoId).Which;

    /// <summary>Anula una factura emitida por el camino de §3.7.</summary>
    public static Task AnularAsync(SesionCliente sesion, Guid facturaId, CancellationToken ct) =>
        AyudaCiclo.AnularAsync(sesion, facturaId, "Prueba de envío masivo.", ct);
}