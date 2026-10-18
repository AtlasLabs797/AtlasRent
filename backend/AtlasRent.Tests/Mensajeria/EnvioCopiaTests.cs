using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Mensajeria;

/// <summary>
/// F4-02: la copia del PDF que salió por correo es <b>exactamente</b> lo que se adjuntó, y
/// <c>GET /api/v1/envios/{id}/pdf</c> devuelve esa copia y no una regeneración.
/// <para>
/// La comparación byte a byte no puede hacerse contra <c>GET /api/v1/facturas/{id}/pdf</c> sin más
/// contexto: QuestPDF escribe una marca de tiempo propia en cada composición, así que dos generaciones
/// del mismo documento solo son el mismo archivo si no ha cambiado nada entre medias.
/// </para>
/// <para>
/// Por eso lo que se comprueba aquí es lo que importa: lo que se mandaron al cliente es exactamente lo
/// que quedó guardado, y el endpoint de la copia devuelve esa columna y no vuelve a componer nada.
/// <b>Eso se demuestra cambiando el documento</b> —se le toca una línea (F3-08) después de mandar— y
/// comprobando que la copia descargada sigue siendo la que se adjuntó. Si el endpoint regenerara, el
/// papel cambiaría en cuanto el documento cambiara, y dejaría de ser la copia de lo que el cliente
/// recibió.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class EnvioCopiaTests(ContenedorBdFixture bd, ApiTestFactoryCorreo api)
    : IClassFixture<ApiTestFactoryCorreo>
{
    /// <summary>
    /// <c>envio.pdf_copia</c> es byte a byte el PDF que se adjuntó, y el endpoint de la copia devuelve
    /// esa misma columna.
    /// </summary>
    [Fact]
    public async Task LaCopiaDelPdfEnviadoEsExacta()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaEnvios.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaId = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        var destino = AyudaEnvios.CorreoDePrueba();
        var envio = await AyudaEnvios.EnviarAsync(admin, AyudaEnvios.PeticionFactura(facturaId, destino), ct);
        var envioId = envio.GetProperty("id").GetGuid();

        var adjunto = api.Doble.Ultimo!.Adjunto;
        adjunto.Take(5).Should().Equal("%PDF-"u8.ToArray(), "lo que salió es el PDF de F4-01, no otra cosa");

        // La fila guardada tiene exactamente los mismos bytes que se mandaron.
        var fila = await AyudaEnvios.FilaAsync(bd, envioId, ct);
        fila.PdfCopia.Should().Equal(adjunto, "envio.pdf_copia guarda los bytes exactos que salieron");

        // Y el endpoint de la copia devuelve esa misma columna.
        (await AyudaEnvios.PdfDeEnvioAsync(admin, envioId, ct))
            .Should().Equal(adjunto, "la copia guardada es la que se devuelve, no un PDF nuevo");

        // Se cambia la factura DESPUÉS de mandarla. La copia descargada tiene que seguir siendo la que
        // se adjuntó: si el endpoint regenerara el documento, el papel saldría distinto y dejaría de
        // ser la copia de lo que el cliente recibió.
        await AyudaEnvios.ModificarFacturaAsync(admin, facturaId, ct);

        (await AyudaEnvios.PdfDeEnvioAsync(admin, envioId, ct))
            .Should().Equal(
                adjunto,
                "la copia es la que salió, no el documento de ahora: por eso GET /envios/{id}/pdf no "
                + "vuelve a componerlo");

        fila.NombreArchivo.Should().EndWith(".pdf");
        fila.Estado.Should().Be(EstadoEnvio.Enviado);
        fila.UltimoError.Should().BeNull();
    }
}