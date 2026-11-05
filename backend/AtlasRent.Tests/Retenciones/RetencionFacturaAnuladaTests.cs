using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Jobs;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Retenciones;

/// <summary>
/// F3-08 combinado con F3-04: una factura anulada no puede dejar un aviso perpetuo.
/// <para>
/// El comprobante de retención de una factura anulada se queda pendiente para siempre —a una factura
/// anulada no hay que pedirle el <c>607</c> a nadie— así que si el trabajo de retenciones siguiera
/// mirándola, la bandeja llenaría de avisos que nadie puede cerrar. Lo que se comprueba es que el
/// trabajo resuelve esa alerta en vez de levantarla.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class RetencionFacturaAnuladaTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    [Fact]
    public async Task UnaFacturaAnuladaNoDejaElAvisoDeRetencionLevantado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        const decimal porcentaje = 7m;

        var tipoRetencion = await AyudaRetenciones.CrearTipoAsync(sesion, BaseRetencion.Total, porcentaje, ct);
        var facturaId = await AyudaRetenciones.BorradorConRetencionAsync(sesion, tipoRetencion, ct);

        await AyudaRetenciones.EmitirAsync(sesion, facturaId, ct);

        await EjecutarTrabajoAsync(ct);
        (await AyudaRetenciones.AlertaAsync(bd, facturaId, ct))
            .Should().NotBeNull("la factura retiene y su comprobante sigue pendiente: hay que avisar");

        var anulada = await sesion.PostAsync(
            $"/api/v1/facturas/{facturaId:D}/anular",
            new { motivo = "Factura que no debía retener" },
            ct);

        anulada.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, await anulada.Cuerpo());

        await EjecutarTrabajoAsync(ct);

        // La alerta resuelve, no desaparece: cerrar no es borrar (AGENTS.md §6), y si el problema
        // volviera, la misma fila se reabre (BUG-008).
        (await AyudaRetenciones.AlertaAsync(bd, facturaId, ct))
            .Should().BeNull("una factura anulada no deja un aviso que nadie puede cerrar");
        (await AyudaRetenciones.AlertaAsync(bd, facturaId, ct, otra: true))
            .Should().NotBeNull("la alerta sigue en la base, cerrada");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var comprobante = await contexto.ComprobanteRetencion.AsNoTracking()
            .SingleAsync(c => c.FacturaId == facturaId, ct);

        comprobante.Estado.Should().Be(EstadoComprobanteRetencion.Pendiente,
            "el comprobante no se borra: se queda pendiente de un documento que ya no vale");
    }

    /// <summary>Una pasada del trabajo diario contra la base de verdad, como corre el planificador.</summary>
    private async Task EjecutarTrabajoAsync(CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var reloj = new RelojPrueba();

        await new TrabajoRetenciones(contexto, new ServicioAlertas(contexto, reloj), reloj)
            .EjecutarAsync(ct);
    }
}