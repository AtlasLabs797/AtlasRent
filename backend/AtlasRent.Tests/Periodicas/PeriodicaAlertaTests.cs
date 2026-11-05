using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Jobs;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Comprobantes;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Periodicas;

/// <summary>
/// F3-05: el aviso de «periodos sin facturar» (§3.16).
/// <para>
/// Se levanta con el trabajo diario y se cierra con la factura, no cuando alguien lo marque: el
/// problema es que falte la factura, y la factura es lo que lo quita.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class PeriodicaAlertaTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    /// <summary>
    /// Un contrato activo sin factura del mes pasado levanta el aviso; al facturar ese mes, el
    /// aviso se resuelve. Se comprueba el estado de la fila, que es donde vive el hecho.
    /// </summary>
    [Fact]
    public async Task UnContratoSinFacturaLevantaElAvisoYAlFacturarSeResuelve()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var mesPasado = PeriodoFacturacion.DelMes(hoy.AddMonths(-1));

        var (contratoId, _) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, mesPasado.Inicio, duracionMeses: 24);

        var clave = TrabajoPeriodosSinFacturar.Clave(contratoId, mesPasado.Inicio);

        await EjecutarTrabajoAsync(ct);

        var viva = await AyudaComprobante.AlertaAsync(bd, clave, ct);
        viva.Should().NotBeNull("el mes ya pasó y este contrato no tiene su factura");
        viva!.Tipo.Should().Be(TrabajoPeriodosSinFacturar.TipoAlerta);
        viva.Clave.Should().Be(clave);
        viva.Mensaje.Should().Contain($"{mesPasado.Inicio:MM/yyyy}");

        // Y al facturar ese mes, el aviso se cierra solo.
        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        await AyudaPeriodicas.GenerarAsync(
            admin, AyudaPeriodicas.Peticion(mesPasado.Inicio, serieId, [contratoId]), ct);

        (await AyudaComprobante.AlertaAsync(bd, clave, ct))
            .Should().BeNull("la factura del periodo es lo que quita el problema");

        var cerrada = await AyudaComprobante.AlertaAsync(bd, clave, ct, otra: true);
        cerrada.Should().NotBeNull("cerrar una alerta no es borrarla: el rastro se queda");
        cerrada!.ResueltaAt.Should().NotBeNull();
    }

    /// <summary>
    /// Un contrato que ya está facturado no levanta el aviso nunca, y correr el trabajo dos veces el
    /// mismo día no duplica la alerta (la clave lleva contrato y periodo).
    /// </summary>
    [Fact]
    public async Task UnContratoYaFacturadoNoLevantaElAvisoNiDuplicadoNiAlRepetirElTrabajo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var mesPasado = PeriodoFacturacion.DelMes(hoy.AddMonths(-1));

        var (facturado, _) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, mesPasado.Inicio, duracionMeses: 24);

        // Un segundo contrato sin facturar, para que el aviso tenga algo que avisar y la prueba
        // pueda distinguir «no avisa de este» de «no avisa de ninguno».
        var (sinFacturar, _) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, mesPasado.Inicio, duracionMeses: 24);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        await AyudaPeriodicas.GenerarAsync(
            admin, AyudaPeriodicas.Peticion(mesPasado.Inicio, serieId, [facturado]), ct);

        await EjecutarTrabajoAsync(ct);
        await EjecutarTrabajoAsync(ct);

        (await AyudaComprobante.AlertaAsync(
            bd, TrabajoPeriodosSinFacturar.Clave(facturado, mesPasado.Inicio), ct))
            .Should().BeNull("un contrato con su factura no es un periodo sin facturar");

        var delOtro = await AyudaComprobante.AlertaAsync(
            bd, TrabajoPeriodosSinFacturar.Clave(sinFacturar, mesPasado.Inicio), ct);
        delOtro.Should().NotBeNull("este sí lo está");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Alerta.AsNoTracking().CountAsync(
            a => a.Clave == delOtro!.Clave && a.ResueltaAt == null, ct))
            .Should().Be(1, "correr el trabajo dos veces no deja dos alertas del mismo periodo");
    }

    /// <summary>Una pasada del trabajo de las 06:00 contra la base de verdad, como corre el planificador.</summary>
    private async Task EjecutarTrabajoAsync(CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var reloj = new RelojPrueba();

        await new TrabajoPeriodosSinFacturar(contexto, new ServicioAlertas(contexto, reloj), reloj)
            .EjecutarAsync(ct);
    }
}