using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Comprobantes;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Periodicas;

/// <summary>
/// BUG-022: cada contrato se factura entero o no deja nada, un fallo no aborta el lote, un
/// borrador huérfano de ejecuciones antiguas se reutiliza y dos ejecuciones a la vez no duplican.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class PeriodicaAtomicaTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    [Fact]
    public async Task UnContratoQueFallaAlEmitirNoDejaBorradorNiAbortaElResto()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        var mes = PeriodoFacturacion.DelMes(await AyudaFacturas.HoyAsync(admin, ct));

        var (c1, _) = await AyudaPeriodicas.ContratoActivoAsync(bd, api, ct, mes.Inicio, duracionMeses: 24);
        var (c2, _) = await AyudaPeriodicas.ContratoActivoAsync(bd, api, ct, mes.Inicio, duracionMeses: 24);
        var (c3, _) = await AyudaPeriodicas.ContratoActivoAsync(bd, api, ct, mes.Inicio, duracionMeses: 24);
        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var peticion = AyudaPeriodicas.Peticion(mes.Inicio, serieId, [c1, c2, c3]);

        var nombre = $"fallo_{Guid.NewGuid():N}";
        await EjecutarSuperAsync(
            $"""
            CREATE FUNCTION {nombre}() RETURNS trigger AS $$ BEGIN RAISE EXCEPTION 'fallo de prueba'; END $$ LANGUAGE plpgsql;
            CREATE TRIGGER {nombre} BEFORE UPDATE ON factura FOR EACH ROW
              WHEN (NEW.contrato_id = '{c2:D}' AND NEW.estado = 2) EXECUTE FUNCTION {nombre}();
            """, ct);

        try
        {
            var primera = await AyudaPeriodicas.GenerarAsync(admin, peticion, ct);

            primera.GetProperty("creadas").GetInt32().Should().Be(2);
            primera.GetProperty("fallidas").GetInt32().Should().Be(1);
            primera.GetProperty("fallidos")[0].GetProperty("contratoId").GetGuid().Should().Be(c2);
        }
        finally
        {
            await EjecutarSuperAsync($"DROP TRIGGER IF EXISTS {nombre} ON factura; DROP FUNCTION IF EXISTS {nombre}();", ct);
        }

        (await AyudaPeriodicas.FacturasDelPeriodoAsync(bd, c1, mes.Inicio, ct)).Should().ContainSingle()
            .Which.Estado.Should().Be(EstadoFactura.Emitida);
        (await AyudaPeriodicas.FacturasDelPeriodoAsync(bd, c3, mes.Inicio, ct)).Should().ContainSingle()
            .Which.Estado.Should().Be(EstadoFactura.Emitida);
        (await AyudaPeriodicas.FacturasDelPeriodoAsync(bd, c2, mes.Inicio, ct))
            .Should().BeEmpty("el contrato que falló no deja borrador huérfano");

        var clave = ServicioFacturacionPeriodica.ClaveAlertaFallo(c2, mes.Inicio);
        (await AyudaComprobante.AlertaAsync(bd, clave, ct)).Should().NotBeNull("el fallo queda como alerta");
        (await AyudaComprobante.NumerosDeSerieAsync(bd, serieId, ct)).Should().Equal(1, 2);

        // Arreglada la causa, la siguiente ejecución factura el que faltaba y cierra la alerta.
        var segunda = await AyudaPeriodicas.GenerarAsync(admin, peticion, ct);
        segunda.GetProperty("creadas").GetInt32().Should().Be(1);
        segunda.GetProperty("omitidas").GetInt32().Should().Be(2);
        segunda.GetProperty("fallidas").GetInt32().Should().Be(0);
        (await AyudaPeriodicas.FacturasDelPeriodoAsync(bd, c2, mes.Inicio, ct)).Should().ContainSingle();
        (await AyudaComprobante.AlertaAsync(bd, clave, ct)).Should().BeNull();
        (await AyudaComprobante.NumerosDeSerieAsync(bd, serieId, ct)).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task UnBorradorHuerfanoDelPeriodoSeReutilizaYSeEmite()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        var mes = PeriodoFacturacion.DelMes(await AyudaFacturas.HoyAsync(admin, ct));

        var (contratoId, clienteId) = await AyudaPeriodicas.ContratoActivoAsync(bd, api, ct, mes.Inicio, duracionMeses: 24);
        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);

        var huerfana = Guid.NewGuid();
        await using (var contexto = bd.CrearContexto(bd.CadenaSuperusuario))
        {
            contexto.Factura.Add(new Factura
            {
                Id = huerfana,
                ClienteId = clienteId,
                ContratoId = contratoId,
                SerieId = serieId,
                Estado = EstadoFactura.Borrador,
                Moneda = Moneda.USD,
                FechaVencimiento = mes.Inicio.AddDays(4),
                Concepto = ServicioFacturacionPeriodica.ConceptoRenta(mes),
                PeriodoInicio = mes.Inicio,
                PeriodoFin = mes.Fin,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await contexto.SaveChangesAsync(ct);
        }

        var resultado = await AyudaPeriodicas.GenerarAsync(
            admin, AyudaPeriodicas.Peticion(mes.Inicio, serieId, [contratoId]), ct);

        resultado.GetProperty("creadas").GetInt32().Should().Be(1);
        resultado.GetProperty("fallidas").GetInt32().Should().Be(0);
        var factura = (await AyudaPeriodicas.FacturasDelPeriodoAsync(bd, contratoId, mes.Inicio, ct)).Should().ContainSingle().Subject;
        factura.Id.Should().Be(huerfana);
        factura.Estado.Should().Be(EstadoFactura.Emitida);
        factura.Total.Should().Be(AyudaPeriodicas.RentaDePrueba);
    }

    [Fact]
    public async Task DosEjecucionesConcurrentesNoDuplicanNiFallan()
    {
        var ct = TestContext.Current.CancellationToken;
        using var a = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        using var b = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        var mes = PeriodoFacturacion.DelMes(await AyudaFacturas.HoyAsync(a, ct));

        var contratos = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            contratos.Add((await AyudaPeriodicas.ContratoActivoAsync(bd, api, ct, mes.Inicio, duracionMeses: 24)).ContratoId);
        }

        var serieId = await AyudaFacturas.CrearSerieAsync(a, TipoSerie.Normal, Moneda.USD, ct);
        var peticion = AyudaPeriodicas.Peticion(mes.Inicio, serieId, contratos);
        using var barrera = new Barrier(2);

        Task<System.Text.Json.JsonElement> Lanzar(SesionCliente s) => Task.Run(async () =>
        {
            barrera.SignalAndWait(ct);
            return await AyudaPeriodicas.GenerarAsync(s, peticion, ct);
        }, ct);

        var respuestas = await Task.WhenAll(Lanzar(a), Lanzar(b));

        respuestas.Sum(r => r.GetProperty("creadas").GetInt32()).Should().Be(3);
        respuestas.Sum(r => r.GetProperty("fallidas").GetInt32()).Should().Be(0);
        foreach (var contrato in contratos)
        {
            (await AyudaPeriodicas.FacturasDelPeriodoAsync(bd, contrato, mes.Inicio, ct)).Should().ContainSingle();
        }

        (await AyudaComprobante.NumerosDeSerieAsync(bd, serieId, ct)).Should().Equal(1, 2, 3);
    }

    private async Task EjecutarSuperAsync(string sql, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaSuperusuario);
        await contexto.Database.ExecuteSqlRawAsync(sql, ct);
    }
}
