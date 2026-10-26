using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Reservas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Tests.Mensajeria;

/// <summary>
/// Apoyo común de las pruebas de envío de correo (F4-02, docs/ESPECIFICACION.md §3.14).
/// <para>
/// Las facturas, los clientes y los contratos salen de los mismos ayudantes que usan las pruebas de
/// F3 —<see cref="AyudaCuentas"/> y <see cref="AyudaCobros"/>— porque son las mismas piezas que la
/// vista única y que los PDF de F4-01. Montar aquí un camino paralelo sería probar una factura que
/// el sistema no tiene.
/// </para>
/// </summary>
internal static class AyudaEnvios
{
    /// <summary>Correo de ejemplo del cliente que crea <c>AyudaFacturas.CrearClienteAsync</c>.</summary>
    public const string CorreoEjemplo = "cliente@example.test";

    /// <summary>
    /// Destinatario de ejemplo, con una constante propia para no dejar que una prueba mande al
    /// <see cref="CorreoEjemplo"/> del cliente sin querer y se monte un antidiplicado que no espera.
    /// </summary>
    public static string CorreoDePrueba() => $"destinatario-{Guid.NewGuid():N}@example.test";

    public static Task<SesionCliente> SesionAsync(
        ContenedorBdFixture bd,
        ApiTestFactory api,
        string rol,
        CancellationToken ct) =>
        AyudaCuentas.SesionAsync(bd, api, rol, ct);

    /// <summary>
    /// Manda un documento a un destinatario y exige que salga. Devuelve la fila de la respuesta, que
    /// es la misma que sale en <c>GET /api/v1/envios</c>.
    /// </summary>
    public static async Task<JsonElement> EnviarAsync(
        SesionCliente sesion,
        object peticion,
        CancellationToken ct,
        string? claveIdempotencia = null)
    {
        var respuesta = claveIdempotencia is null
            ? await sesion.PostAsync("/api/v1/envios", peticion, ct)
            : await sesion.PostAsync("/api/v1/envios", peticion, claveIdempotencia, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());

        return await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    /// <summary>Petición de envío de una factura o proforma a un destinatario escrito a mano.</summary>
    public static object PeticionFactura(
        Guid facturaId,
        string email,
        bool forzar = false,
        string? idioma = null) =>
        new
        {
            tipoDocumento = nameof(TipoDocumentoEnvio.Factura),
            documentoId = facturaId,
            email,
            idioma,
            forzar,
        };

    /// <summary>Petición de envío de una cotización.</summary>
    public static object PeticionCotizacion(
        Guid cotizacionId,
        string email,
        bool forzar = false) =>
        new
        {
            tipoDocumento = nameof(TipoDocumentoEnvio.Cotizacion),
            documentoId = cotizacionId,
            email,
            forzar,
        };

    /// <summary>
    /// Petición de envío del estado de cuenta de un cliente. El <c>documentoId</c> es el del cliente
    /// y el año va aparte, porque el estado de cuenta es por cliente y por año (§3.14).
    /// </summary>
    public static object PeticionEstadoCuenta(
        Guid clienteId,
        int anio,
        string email,
        bool forzar = false) =>
        new
        {
            tipoDocumento = nameof(TipoDocumentoEnvio.EstadoCuenta),
            documentoId = clienteId,
            documentoAnio = anio,
            email,
            forzar,
        };

    /// <summary>
    /// La fila de <c>envio</c> leída de la base de datos, que es donde está la copia del PDF. Se lee
    /// de la base y no de la API porque <c>GET /api/v1/envios</c> no lleva los bytes, y el criterio de
    /// aceptación es que lo guardado sea exactamente lo que salió.
    /// </summary>
    public static async Task<Envio> FilaAsync(
        ContenedorBdFixture bd,
        Guid envioId,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        return CopiaEnvio.EnClaro(await contexto.Envio.AsNoTracking()
            .SingleAsync(e => e.Id == envioId, ct));
    }

    /// <summary>El envío más reciente de un documento, leído de la base.</summary>
    public static async Task<Envio?> UltimoEnvioAsync(
        ContenedorBdFixture bd,
        Guid documentoId,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        return CopiaEnvio.EnClaro(await contexto.Envio.AsNoTracking()
            .Where(e => e.DocumentoId == documentoId)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync(ct));
    }

    /// <summary>Los bytes de <c>GET /api/v1/facturas/{id}/pdf</c>, que es el papel de referencia.</summary>
    public static async Task<byte[]> PdfDeFacturaAsync(
        SesionCliente sesion,
        Guid facturaId,
        CancellationToken ct)
    {
        var descargado = await sesion.GetAsync($"/api/v1/facturas/{facturaId:D}/pdf", ct);
        descargado.StatusCode.Should().Be(HttpStatusCode.OK, await descargado.Cuerpo());

        return await descargado.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>Los bytes de <c>GET /api/v1/envios/{id}/pdf</c>, que es la copia guardada.</summary>
    public static async Task<byte[]> PdfDeEnvioAsync(
        SesionCliente sesion,
        Guid envioId,
        CancellationToken ct)
    {
        var descargado = await sesion.GetAsync($"/api/v1/envios/{envioId:D}/pdf", ct);
        descargado.StatusCode.Should().Be(HttpStatusCode.OK, await descargado.Cuerpo());

        return await descargado.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>
    /// Las líneas de una factura emitida, que es lo que hay que mandar a <c>/modificar</c> para que
    /// F3-08 la marque como modificada.
    /// </summary>
    public static async Task<Guid> PrimeraLineaAsync(
        SesionCliente sesion,
        Guid facturaId,
        CancellationToken ct)
    {
        var detalle = await sesion.GetAsync($"/api/v1/facturas/{facturaId:D}", ct);
        detalle.StatusCode.Should().Be(HttpStatusCode.OK, await detalle.Cuerpo());

        return (await detalle.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("lineas")[0].GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Modifica las líneas de una factura ya emitida (F3-08). Es lo que pone
    /// <c>modificada_at</c> y por tanto lo que hace que una factura enviada aparezca en
    /// <c>modificadasTrasEnvio</c>.
    /// </summary>
    public static async Task ModificarFacturaAsync(
        SesionCliente sesion,
        Guid facturaId,
        CancellationToken ct)
    {
        var lineaId = await PrimeraLineaAsync(sesion, facturaId, ct);

        var modificada = await sesion.PutAsync(
            $"/api/v1/facturas/{facturaId:D}/modificar",
            new
            {
                lineas = new[]
                {
                    new
                    {
                        lineaId,
                        tipo = nameof(TipoLineaFactura.Renta),
                        concepto = "Línea corregida tras el envío",
                        cantidad = 1m,
                        precioUnitario = 1m,
                    },
                },
            },
            ct);

        modificada.StatusCode.Should().Be(HttpStatusCode.OK, await modificada.Cuerpo());
    }

    /// <summary>
    /// Cuenta los envíos de un documento a un destinatario, leyendo la base. Es el «solo se mandó una
    /// vez» de la prueba de idempotencia.
    /// </summary>
    public static async Task<int> EnviosAsync(
        ContenedorBdFixture bd,
        Guid documentoId,
        string email,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        return await contexto.Envio.AsNoTracking()
            .CountAsync(e => e.DocumentoId == documentoId && e.DestinatarioEmail == email, ct);
    }
}