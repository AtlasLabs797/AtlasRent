using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Comprobantes;

/// <summary>
/// F3-03 sobre la referencia de e-factura externa (D5, §3.7).
/// <para>
/// El campo es un texto que teclea gerente o administrador y que luego se imprime en el PDF, sin
/// validación ni lógica. Lo que se comprueba aquí es lo que sí tiene reglas: quién puede escribirlo,
/// que quede auditado con IP, que se vea en la respuesta y que una factura anulada no lo acepte.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ComprobanteReferenciaEfacturaTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    /// <summary>
    /// Gerente la teclea, queda en la factura, sale en la respuesta y su escritura está auditada con
    /// su IP (AGENTS.md §6).
    /// </summary>
    [Fact]
    public async Task GerenteTecleaLaReferenciaYQuedaAuditadaConSuIp()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        using var gerente = await AyudaFacturas.SesionAsync(bd, api, Roles.Gerente, ct);

        var serieId = await AyudaComprobante.SerieFiscalAsync(admin, ct);
        var clienteId = await AyudaFacturas.CrearClienteAsync(admin, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        await AyudaComprobante.NumeradorAsync(admin, serieId, hoy, ct);
        var facturaId = await AyudaComprobante.BorradorAsync(admin, clienteId, serieId, ct);

        await admin.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);

        // La referencia inventada en la prueba: no significa nada fuera de esta base.
        var referencia = $"E{Guid.NewGuid():N}"[..12];
        var guardada = await gerente.PutAsync(
            $"/api/v1/facturas/{facturaId:D}/referencia-efactura",
            new { referencia },
            ct);

        guardada.StatusCode.Should().Be(HttpStatusCode.OK);
        (await guardada.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("referenciaEfactura").GetString().Should().Be(referencia);

        // Y se ve al leer la factura, no solo en la respuesta del propio cambio.
        var leida = await gerente.GetAsync($"/api/v1/facturas/{facturaId:D}", ct);
        (await leida.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("referenciaEfactura").GetString().Should().Be(referencia);

        await using var contextoSinRls = bd.CrearContexto(bd.CadenaSuperusuario);
        var auditoria = await contextoSinRls.Auditoria.AsNoTracking()
            .SingleAsync(a => a.Accion == "factura.referencia_efactura"
                && a.EntidadId == facturaId.ToString("D"), ct);

        auditoria.Ip.Should().NotBeNullOrWhiteSpace("toda mutación se audita con su IP");
        auditoria.Entidad.Should().Be("factura");
        auditoria.Resultado.Should().Be("permitido");
    }

    /// <summary>
    /// El empleado no puede: `factura.emitir` está desactivado para su rol (D15) y esta referencia es
    /// parte del documento fiscal, aunque solo sea un texto.
    /// </summary>
    [Fact]
    public async Task ElEmpleadoNoPuedeTeclearLaReferencia()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        using var empleado = await AyudaFacturas.SesionAsync(bd, api, Roles.Empleado, ct);

        var serieId = await AyudaComprobante.SerieFiscalAsync(admin, ct);
        var clienteId = await AyudaFacturas.CrearClienteAsync(admin, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        await AyudaComprobante.NumeradorAsync(admin, serieId, hoy, ct);
        var facturaId = await AyudaComprobante.BorradorAsync(admin, clienteId, serieId, ct);

        var respuesta = await empleado.PutAsync(
            $"/api/v1/facturas/{facturaId:D}/referencia-efactura",
            new { referencia = $"E{Guid.NewGuid():N}"[..12] },
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Una factura anulada no admite referencia: lo que salió no se reescribe, y poner una
    /// e-factura sobre un documento anulado dejaría una factura con dos documentos fiscales detrás.
    /// La anulación en sí es de F3-08, así que aquí la factura se anula por debajo, que es
    /// exactamente el estado en el que se la va a encontrar esa tarea.
    /// </summary>
    [Fact]
    public async Task UnaFacturaAnuladaNoAceptaReferencia()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var serieId = await AyudaComprobante.SerieFiscalAsync(admin, ct);
        var clienteId = await AyudaFacturas.CrearClienteAsync(admin, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        await AyudaComprobante.NumeradorAsync(admin, serieId, hoy, ct);
        var facturaId = await AyudaComprobante.BorradorAsync(admin, clienteId, serieId, ct);

        await admin.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var fila = await contexto.Factura.SingleAsync(f => f.Id == facturaId, ct);
            fila.Estado = EstadoFactura.Anulada;
            await contexto.SaveChangesAsync(ct);
        }

        var respuesta = await admin.PutAsync(
            $"/api/v1/facturas/{facturaId:D}/referencia-efactura",
            new { referencia = $"E{Guid.NewGuid():N}"[..12] },
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}