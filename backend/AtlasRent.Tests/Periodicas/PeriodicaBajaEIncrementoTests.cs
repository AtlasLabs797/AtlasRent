using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Contratos;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Periodicas;

/// <summary>
/// BUG-024: la periódica factura hasta la fecha efectiva de baja (fin exclusivo: el día de la baja
/// ya no es del inquilino), la baja admite cualquier fecha futura y los incrementos de renta
/// entran en su fecha. Mes de 30 días a propósito: 3000 / 30 = 100 por día, calculado a mano.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class PeriodicaBajaEIncrementoTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    private const decimal Renta = 3_000m;

    /// <summary>Primer mes de 30 días a partir del mes que viene.</summary>
    private static DateOnly MesDe30Dias(DateOnly hoy)
    {
        var mes = new DateOnly(hoy.Year, hoy.Month, 1).AddMonths(1);
        while (DateTime.DaysInMonth(mes.Year, mes.Month) != 30)
        {
            mes = mes.AddMonths(1);
        }

        return mes;
    }

    private async Task<(SesionCliente Admin, Guid Serie, DateOnly Mes)> PrepararAsync(CancellationToken ct)
    {
        var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        var mes = MesDe30Dias(await AyudaFacturas.HoyAsync(admin, ct));
        var serie = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);

        return (admin, serie, mes);
    }

    [Fact]
    public async Task LaBajaAdmiteUnaFechaAnteriorAlFinDelBloqueSiNoEsPasadaNiAnteriorAlInicio()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await AyudaFacturas.SesionAsync(bd, api, Roles.Gerente, ct);
        var hoy = await AyudaFacturas.HoyAsync(gerente, ct);

        var (vigente, _) = await AyudaPeriodicas.ContratoActivoAsync(bd, api, ct, hoy, duracionMeses: 36);
        var ayer = await AyudaContrato.Boton(gerente, vigente, "baja", new { fechaFin = hoy.AddDays(-1).ToString("yyyy-MM-dd") }, ct);
        ayer.StatusCode.Should().Be(HttpStatusCode.Conflict, "una baja no puede ser pasada");

        var pronto = await AyudaContrato.Boton(gerente, vigente, "baja", new { fechaFin = hoy.AddDays(10).ToString("yyyy-MM-dd") }, ct);
        pronto.StatusCode.Should().Be(HttpStatusCode.OK, "la baja no tiene que esperar al fin del bloque (§3.5)");

        var (futuro, _) = await AyudaPeriodicas.ContratoActivoAsync(bd, api, ct, hoy.AddDays(30), duracionMeses: 36);
        var antesDelInicio = await AyudaContrato.Boton(gerente, futuro, "baja", new { fechaFin = hoy.AddDays(5).ToString("yyyy-MM-dd") }, ct);
        antesDelInicio.StatusCode.Should().Be(HttpStatusCode.Conflict, "no antes del inicio del contrato");
    }

    [Fact]
    public async Task LaUltimaFacturaProrrateaHastaLaBajaAunqueSeaAntesDelFinDelBloque()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, serie, mes) = await PrepararAsync(ct);
        using var _ = admin;

        var (contrato, _) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, mes.AddMonths(-1), duracionMeses: 36, renta: Renta);
        await AyudaContrato.AceptarAsync(admin, contrato, "baja", new { fechaFin = mes.AddDays(14).ToString("yyyy-MM-dd") }, ct);

        var item = AyudaPeriodicas.UnItem(await AyudaPeriodicas.SimularAsync(
            admin, AyudaPeriodicas.Peticion(mes, serie, [contrato]), ct));

        // Baja el día 15 (exclusivo): días 1..14 = 14 días x 100 = 1400,00.
        item.GetProperty("diasFacturables").GetInt32().Should().Be(14);
        item.GetProperty("importe").GetDecimal().Should().Be(1_400.00m);

        // El mes siguiente ya no es candidato.
        var siguiente = await AyudaPeriodicas.SimularAsync(
            admin, AyudaPeriodicas.Peticion(mes.AddMonths(1), serie, [contrato]), ct);
        siguiente.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task UnContratoEnBajaProgramadaSinRenovarSigueFacturandoseHastaLaBaja()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, serie, mes) = await PrepararAsync(ct);
        using var _ = admin;

        // Un solo mes de bloque: el fin vigente es el día 1 del mes de la baja y la renovación no
        // renueva una baja programada, así que el tramo 1..14 solo lo cubre la baja.
        var (contrato, _) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, mes.AddMonths(-1), duracionMeses: 1, renta: Renta);
        await AyudaContrato.AceptarAsync(admin, contrato, "baja", new { fechaFin = mes.AddDays(14).ToString("yyyy-MM-dd") }, ct);

        var item = AyudaPeriodicas.UnItem(await AyudaPeriodicas.SimularAsync(
            admin, AyudaPeriodicas.Peticion(mes, serie, [contrato]), ct));

        item.GetProperty("diasFacturables").GetInt32().Should().Be(14);
        item.GetProperty("importe").GetDecimal().Should().Be(1_400.00m);
    }

    [Fact]
    public async Task UnContratoFinalizadoFacturaSuUltimoTramoSiNoSeFacturo()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, serie, mes) = await PrepararAsync(ct);
        using var _ = admin;

        var (contrato, _) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, mes.AddMonths(-1), duracionMeses: 36, renta: Renta);

        // Finalizado el día 15 sin que el tramo se facturara. Se escribe en la base porque la fecha
        // de baja es futura y la finalización por fecha no ha llegado.
        await using (var contexto = bd.CrearContexto(bd.CadenaSuperusuario))
        {
            await contexto.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE contrato SET estado = 6, fecha_baja_programada = {mes.AddDays(14)}, fecha_fin_real = {mes.AddDays(14)} WHERE id = {contrato}", ct);
        }

        var item = AyudaPeriodicas.UnItem(await AyudaPeriodicas.SimularAsync(
            admin, AyudaPeriodicas.Peticion(mes, serie, [contrato]), ct));

        item.GetProperty("diasFacturables").GetInt32().Should().Be(14);
        item.GetProperty("importe").GetDecimal().Should().Be(1_400.00m);
    }

    [Fact]
    public async Task UnIncrementoElDia10PartePeriodoEnDosTramosYLaFacturaLoRefleja()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, serie, mes) = await PrepararAsync(ct);
        using var _ = admin;

        var (contrato, _) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, mes.AddMonths(-1), duracionMeses: 36, renta: Renta);

        var creado = await admin.PostAsync(
            $"/api/v1/contratos/{contrato:D}/incrementos",
            new { tipo = "Porcentaje", porcentaje = 10m, importe = (decimal?)null, fechaVigencia = mes.AddDays(9).ToString("yyyy-MM-dd") },
            ct);
        creado.StatusCode.Should().Be(HttpStatusCode.Created, await creado.Cuerpo());

        var item = AyudaPeriodicas.UnItem(await AyudaPeriodicas.SimularAsync(
            admin, AyudaPeriodicas.Peticion(mes, serie, [contrato]), ct));

        // Días 1..9 a 3000: 9 x 100 = 900,00. Días 10..30 a 3300: 21 x 110 = 2310,00. Total 3210,00.
        item.GetProperty("diasFacturables").GetInt32().Should().Be(30);
        item.GetProperty("prorrateado").GetBoolean().Should().BeTrue();
        item.GetProperty("importe").GetDecimal().Should().Be(3_210.00m);

        await AyudaPeriodicas.GenerarAsync(admin, AyudaPeriodicas.Peticion(mes, serie, [contrato]), ct);
        var factura = (await AyudaPeriodicas.FacturasDelPeriodoAsync(bd, contrato, mes, ct)).Should().ContainSingle().Subject;
        factura.Total.Should().Be(3_210.00m);

        await using var verificacion = bd.CrearContexto(bd.CadenaApp);
        (await verificacion.FacturaLinea.AsNoTracking().CountAsync(l => l.FacturaId == factura.Id, ct)).Should().Be(2);
    }
}
