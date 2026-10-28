using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Periodicas;

/// <summary>
/// Los items de la simulación traen cliente, unidad e inmueble ya resueltos, para que la pantalla
/// no tenga que pedirlos aparte. Los esperados se leen de la base, no de la respuesta.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class PeriodicaSimulacionDatosTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    [Fact]
    public async Task LaSimulacionTraeNombresDeClienteUnidadEInmueble()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var mes = PeriodoFacturacion.DelMes(hoy);

        var (contratoId, clienteId) = await AyudaPeriodicas.ContratoActivoAsync(bd, api, ct, mes.Inicio);
        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var contrato = await contexto.Contrato.AsNoTracking().SingleAsync(c => c.Id == contratoId, ct);
        var cliente = await contexto.Cliente.AsNoTracking().SingleAsync(c => c.Id == clienteId, ct);
        var unidad = await contexto.Unidad.AsNoTracking().SingleAsync(u => u.Id == contrato.UnidadId, ct);
        var inmueble = await contexto.Inmueble.AsNoTracking().SingleAsync(i => i.Id == unidad.InmuebleId, ct);

        var simulacion = await AyudaPeriodicas.SimularAsync(
            admin, AyudaPeriodicas.Peticion(mes.Inicio, serieId, [contratoId]), ct);
        var item = AyudaPeriodicas.UnItem(simulacion);

        item.GetProperty("clienteNombre").GetString().Should().Be(cliente.Nombre);
        item.GetProperty("unidadNombre").GetString().Should().Be(unidad.Nombre);
        item.GetProperty("inmuebleId").GetGuid().Should().Be(inmueble.Id);
        item.GetProperty("inmuebleNombre").GetString().Should().Be(inmueble.Nombre);
        item.GetProperty("contratoFechaInicio").GetString().Should().Be(contrato.FechaInicio.ToString("yyyy-MM-dd"));
    }
}
