using System.Net;
using System.Text;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Cotizaciones;

/// <summary>
/// F2-02: lo que se puede hacer con una cotización aceptada (docs/ESPECIFICACION.md §3.3) y el PDF
/// del documento.
/// <para>
/// Ni la reserva ni el contrato se crean aquí: la especificación dice que el botón copia los datos al
/// formulario y que no se crea nada automáticamente. Estos endpoints son los que F2-03 y F2-05 van a
/// consumir.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class CotizacionDesdeAceptadaTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    [Theory]
    [InlineData("para-reserva")]
    [InlineData("para-contrato")]
    public async Task LosDatosCopiadosSoloEstanEnUnaCotizacionAceptada(string destino)
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await SesionAsync(ct);

        var clienteId = await AyudaCotizacion.ClienteAsync(gerente, ct);
        var unidadId = await AyudaCotizacion.UnidadAsync(gerente, ct);
        var id = await AyudaCotizacion.CrearAsync(
            gerente,
            clienteId,
            unidadId: unidadId,
            lineas: new[] { AyudaCotizacion.Linea("Mensualidad", 1m, 45000m) },
            ct: ct);

        // En borrador todavía no: hay que enviarla y aceptarla primero.
        (await gerente.GetAsync($"/api/v1/cotizaciones/{id:D}/{destino}", ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await AyudaCotizacion.CambiarEstadoAsync(gerente, id, "Enviada", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await gerente.GetAsync($"/api/v1/cotizaciones/{id:D}/{destino}", ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "enviada tampoco: solo la aceptada");

        (await AyudaCotizacion.CambiarEstadoAsync(gerente, id, "Aceptada", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var copiados = await AyudaCotizacion.Json(
            await gerente.GetAsync($"/api/v1/cotizaciones/{id:D}/{destino}", ct), ct);

        copiados.GetProperty("cotizacionId").GetGuid().Should().Be(id);
        copiados.GetProperty("clienteId").GetGuid().Should().Be(clienteId);
        copiados.GetProperty("unidadId").GetGuid().Should().Be(unidadId, "la unidad cotizada viene copiada");
        copiados.GetProperty("moneda").GetString().Should().Be("DOP");
        copiados.GetProperty("total").GetDecimal().Should().Be(45000m);

        var linea = copiados.GetProperty("lineas")[0];
        linea.GetProperty("descripcion").GetString().Should().Be("Mensualidad");
        linea.GetProperty("cantidad").GetDecimal().Should().Be(1m);
        linea.GetProperty("precioUnitario").GetDecimal().Should().Be(45000m);
        linea.GetProperty("importe").GetDecimal().Should().Be(45000m);
    }

    /// <summary>
    /// Nada se crea solo: leer los datos copiados no deja rastro. La especificación (docs/
    /// ESPECIFICACION.md §3.3) dice que el botón abre el formulario con los datos puestos y que no se
    /// crea nada automáticamente.
    /// </summary>
    [Fact]
    public async Task LosDatosCopiadosNoCreanNada()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await SesionAsync(ct);

        var id = await AceptadaAsync(gerente, ct);

        await using var contexto = bd.CrearContexto(
            bd.CadenaApp, new ContextoSesion { Rol = Roles.Gerente });

        var antes = await contexto.Auditoria.AsNoTracking()
            .CountAsync(a => a.Entidad == "cotizacion", ct);

        (await gerente.GetAsync($"/api/v1/cotizaciones/{id:D}/para-reserva", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await gerente.GetAsync($"/api/v1/cotizaciones/{id:D}/para-contrato", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var despues = await contexto.Auditoria.AsNoTracking()
            .CountAsync(a => a.Entidad == "cotizacion", ct);

        despues.Should().Be(antes, "leer los datos copiados no escribe nada, ni de cotizaciones ni de nada más");
    }

    /// <summary>
    /// El PDF sale con la firma del formato y con contenido. La descripción que se usa aquí lleva
    /// etiquetas y acentos a propósito: lo que escribe el cliente viaja como texto plano y no como
    /// formato, así que el documento se genera igual.
    /// </summary>
    [Fact]
    public async Task ElPdfSeGeneraConLasLineasDelDocumento()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await SesionAsync(ct);

        var id = await AceptadaAsync(
            gerente, ct, descripcion: "Alquiler <b>con</b> acentos: año y niño", precio: 1234.56m);

        var respuesta = await gerente.GetAsync($"/api/v1/cotizaciones/{id:D}/pdf", ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        respuesta.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");

        var pdf = await respuesta.Content.ReadAsByteArrayAsync(ct);
        Encoding.ASCII.GetString(pdf, 0, Math.Min(pdf.Length, 5))
            .Should().Be("%PDF-", "un PDF de verdad empieza así");
        pdf.Length.Should().BeGreaterThan(1000, "un documento con contenido, no un archivo vacío");
    }

    /// <summary>Sin sesión no hay PDF, aunque la cotización exista.</summary>
    [Fact]
    public async Task SinSesionNoHayPdf()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await SesionAsync(ct);
        var id = await AceptadaAsync(gerente, ct);

        using var anonimo = api.CrearCliente();

        (await anonimo.GetAsync($"/api/v1/cotizaciones/{id:D}/pdf", ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------------------------------------------------------------- ayudantes

    private async Task<Guid> AceptadaAsync(
        SesionCliente sesion,
        CancellationToken ct,
        string descripcion = "Mensualidad",
        decimal precio = 25000m)
    {
        var id = await AyudaCotizacion.CrearAsync(
            sesion,
            await AyudaCotizacion.ClienteAsync(sesion, ct),
            lineas: new[] { AyudaCotizacion.Linea(descripcion, 1m, precio) },
            ct: ct);

        (await AyudaCotizacion.CambiarEstadoAsync(sesion, id, "Enviada", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await AyudaCotizacion.CambiarEstadoAsync(sesion, id, "Aceptada", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        return id;
    }

    private async Task<SesionCliente> SesionAsync(CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }
}