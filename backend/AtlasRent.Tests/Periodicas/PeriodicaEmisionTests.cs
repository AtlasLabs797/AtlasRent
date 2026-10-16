using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Cobros;
using AtlasRent.Tests.Comprobantes;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Periodicas;

/// <summary>
/// F3-05: la factura periódica sale por el camino de F3-02 y queda auditada.
/// <para>
/// Lo que se comprueba es que la generación periódica no es una puerta trasera que se salta la
/// emisión: número de serie, cambio congelado, comprobante fiscal y auditoría con IP.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class PeriodicaEmisionTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    /// <summary>
    /// Serie fiscal con numerador: la factura periódica sale con número, con el tipo de cambio del
    /// día congelado y con comprobante, exactamente como una tecleada a mano. El cambio del día se
    /// carga aquí y se devuelve al final, porque la base es compartida (BUG-006).
    /// </summary>
    [Fact]
    public async Task LaFacturaPeriodicaSeEmiteConNumeroCambioCongeladoYComprobante()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var mes = PeriodoFacturacion.DelMes(hoy);

        await using (var limpieza = AyudaCobros.SinRastroDeCambio(bd, hoy, Moneda.DOP))
        {
            var serieId = await AyudaComprobante.SerieFiscalAsync(admin, ct);
            await AyudaComprobante.NumeradorAsync(admin, serieId, hoy, ct);
            var tipoCambioId = await AyudaFacturas.CargarTipoCambioAsync(
                admin, Moneda.DOP, AyudaCobros.CambioDelDia, ct);

            var (contratoId, _) = await AyudaPeriodicas.ContratoActivoAsync(
                bd, api, ct, mes.Inicio, duracionMeses: 24, moneda: Moneda.DOP);

            var resultado = await AyudaPeriodicas.GenerarAsync(
                admin, AyudaPeriodicas.Peticion(mes.Inicio, serieId, [contratoId]), ct);

            var emitida = resultado.GetProperty("facturas")[0];
            var facturaId = emitida.GetProperty("facturaId").GetGuid();

            emitida.GetProperty("numero").GetInt32().Should().Be(1, "es la primera emisión de la serie");
            // El número de comprobante sale del numerador que la prueba acaba de crear, que es
            // inventado: lo que se comprueba es que hay uno y que es el que la factura lleva.
            emitida.GetProperty("comprobanteNumero").GetInt32().Should().BeGreaterThan(0);

            await using var contexto = bd.CrearContexto(bd.CadenaApp);
            var fila = await contexto.Factura.AsNoTracking().SingleAsync(f => f.Id == facturaId, ct);

            fila.Estado.Should().Be(EstadoFactura.Emitida);
            fila.Numero.Should().Be(1);
            fila.FechaEmision.Should().Be(hoy);
            fila.TipoCambioValor.Should().Be(AyudaCobros.CambioDelDia, "el cambio queda congelado en el documento");
            fila.TipoCambioFecha.Should().Be(hoy);
            fila.TipoCambioId.Should().Be(tipoCambioId);
            fila.PeriodoInicio.Should().Be(mes.Inicio);
            fila.PeriodoFin.Should().Be(mes.Fin);

            var comprobante = await contexto.ComprobanteFiscal.AsNoTracking()
                .SingleAsync(c => c.FacturaId == facturaId, ct);
            comprobante.SerieId.Should().Be(serieId);
            comprobante.Numero.Should().Be(emitida.GetProperty("comprobanteNumero").GetInt32(),
                "el comprobante es el que dice el numerador de la serie (F3-03)");

            // Y el número del comprobante sale también en la auditoría de la emisión, como en F3-02.
            await using var contextoSinRls = bd.CrearContexto(bd.CadenaSuperusuario);
            var auditoria = await contextoSinRls.Auditoria.AsNoTracking()
                .SingleAsync(a => a.Accion == "factura.emitida" && a.EntidadId == facturaId.ToString("D"), ct);

            auditoria.Ip.Should().NotBeNullOrWhiteSpace("toda mutación se audita con su IP (AGENTS.md §6)");
            auditoria.Datos!.RootElement.GetProperty("NumeroComprobante").GetInt32().Should().Be(comprobante.Numero);
        }
    }

    /// <summary>
    /// La ejecución es un lote y se audita como tal: quién la hizo, de qué mes, con qué serie, cuántas
    /// facturas salieron, cuáles se saltaron y por qué, y si los contratos venían de una lista.
    /// </summary>
    [Fact]
    public async Task LaEjecucionQuedaAuditadaConQuienQuePeriodoYCuantasFacturas()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var mes = PeriodoFacturacion.DelMes(hoy);

        var (facturado, _) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, mes.Inicio, duracionMeses: 24);

        // Un segundo contrato en pesos, que es candidato del mes pero que la serie fijada a dólares
        // no puede facturar: sale en los omitidos con su motivo y en la auditoría de la ejecución.
        var (fuera, _) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, mes.Inicio, duracionMeses: 24, moneda: Moneda.DOP);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);

        await AyudaPeriodicas.GenerarAsync(
            admin, AyudaPeriodicas.Peticion(mes.Inicio, serieId, [facturado, fuera]), ct);

        await using var contexto = bd.CrearContexto(bd.CadenaSuperusuario);
        // La base es compartida y otras pruebas también facturan: se toma la última ejecución, que
        // es la que acaba de hacer esta prueba.
        var auditoria = await contexto.Auditoria.AsNoTracking()
            .Where(a => a.Accion == ServicioFacturacionPeriodica.AccionEjecucion)
            .OrderByDescending(a => a.CreatedAt)
            .FirstAsync(ct);

        auditoria.Ip.Should().NotBeNullOrWhiteSpace();
        auditoria.Entidad.Should().Be("factura");
        auditoria.EntidadId.Should().Be($"periodo:{mes.Inicio:yyyy-MM}");

        var datos = auditoria.Datos!.RootElement;
        datos.GetProperty("Periodo").GetString().Should().Be(mes.Inicio.ToString("yyyy-MM-dd"));
        datos.GetProperty("Creadas").GetInt32().Should().Be(1);
        datos.GetProperty("Omitidas").GetInt32().Should().Be(1);
        datos.GetProperty("ContratosIndicados").GetBoolean().Should().BeTrue(
            "los contratos venían de la lista que se le pasó");

        var omitidos = datos.GetProperty("Omitidos").EnumerateArray().ToList();
        omitidos.Should().ContainSingle();
        omitidos[0].GetProperty("ContratoId").GetString().Should().Be(fuera.ToString("D"));
        omitidos[0].GetProperty("Motivo").GetString().Should().Contain("D7");
    }

    /// <summary>
    /// Facturar sin mirar la simulación es legal, pero deja rastro: la auditoría dice que los
    /// contratos no venían de una lista, que es la diferencia entre «se facturó lo que se vio» y
    /// «se facturó lo que había».
    /// </summary>
    [Fact]
    public async Task FacturarSinSimularQuedaMarcadoEnLaAuditoria()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var mes = PeriodoFacturacion.DelMes(hoy);

        var (contratoId, _) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, mes.Inicio, duracionMeses: 24);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);

        var resultado = await AyudaPeriodicas.GenerarAsync(
            admin, AyudaPeriodicas.Peticion(mes.Inicio, serieId), ct);

        resultado.GetProperty("contratosIndicados").GetBoolean().Should().BeFalse();

        await using var contexto = bd.CrearContexto(bd.CadenaSuperusuario);
        // La base es compartida y otras pruebas también facturan: se toma la última ejecución, que
        // es la que acaba de hacer esta prueba.
        var auditoria = await contexto.Auditoria.AsNoTracking()
            .Where(a => a.Accion == ServicioFacturacionPeriodica.AccionEjecucion)
            .OrderByDescending(a => a.CreatedAt)
            .FirstAsync(ct);

        var datos = auditoria.Datos!.RootElement;
        datos.GetProperty("ContratosIndicados").GetBoolean().Should().BeFalse();
        datos.GetProperty("Facturas").EnumerateArray().Should()
            .Contain(f => f.GetProperty("ContratoId").GetGuid() == contratoId);
    }

    /// <summary>
    /// Un cliente agente de retención recibe su línea de retención en la factura periódica, con el
    /// tipo que la plataforma tiene marcado por defecto (§3.9, D14). El porcentaje sale de la
    /// configuración de la prueba y no está escrito en el código.
    /// </summary>
    [Fact]
    public async Task LaFacturaPeriodicaRetieneCuandoElClienteEsAgenteDeRetencion()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var mes = PeriodoFacturacion.DelMes(hoy);

        var tipo = await admin.PostAsync(
            "/api/v1/config/tipos-retencion",
            new
            {
                nombre = $"Retención de la prueba {Guid.NewGuid():N}",
                porcentaje = 3m,
                @base = "Subtotal",
                esDefecto = true,
                activo = true,
            },
            ct);
        tipo.StatusCode.Should().Be(HttpStatusCode.OK, "el tipo se crea por la configuración, que es D14");

        var tipoId = (await tipo.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();

        var (contratoId, _) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, mes.Inicio, duracionMeses: 24, agenteRetencion: true);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);

        await AyudaPeriodicas.GenerarAsync(
            admin, AyudaPeriodicas.Peticion(mes.Inicio, serieId, [contratoId]), ct);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var factura = await contexto.Factura.AsNoTracking()
            .SingleAsync(f => f.ContratoId == contratoId && f.PeriodoInicio == mes.Inicio, ct);

        var lineaRetencion = await contexto.FacturaLinea.AsNoTracking()
            .SingleAsync(l => l.FacturaId == factura.Id && l.Tipo == TipoLineaFactura.Retencion, ct);

        lineaRetencion.TipoRetencionId.Should().Be(tipoId, "retiene con el tipo marcado por defecto");
        factura.Retencion.Should().Be(
            Redondeo.Importe(factura.Subtotal * (lineaRetencion.PorcentajeRetencion!.Value / 100m)),
            "la retención sale del subtotal con el porcentaje que dice el tipo configurado");
        factura.NetoACobrar.Should().Be(factura.Total - factura.Retencion);
        lineaRetencion.Importe.Should().Be(-factura.Retencion, "la línea va en negativo y la columna en positivo");
    }

    /// <summary>
    /// Un cliente que no es agente de retención no recibe línea de retención aunque el catálogo tenga
    /// un tipo por defecto: la marca es del cliente (§3.9), no del catálogo.
    /// </summary>
    [Fact]
    public async Task UnClienteQueNoEsAgenteDeRetencionNoRetieneEnLaFacturaPeriodica()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var mes = PeriodoFacturacion.DelMes(hoy);

        var (contratoId, _) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, mes.Inicio, duracionMeses: 24);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);

        await AyudaPeriodicas.GenerarAsync(
            admin, AyudaPeriodicas.Peticion(mes.Inicio, serieId, [contratoId]), ct);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var factura = await contexto.Factura.AsNoTracking()
            .SingleAsync(f => f.ContratoId == contratoId && f.PeriodoInicio == mes.Inicio, ct);

        factura.Retencion.Should().Be(0m);
        (await contexto.FacturaLinea.AsNoTracking()
            .CountAsync(l => l.FacturaId == factura.Id && l.Tipo == TipoLineaFactura.Retencion, ct))
            .Should().Be(0, "una factura corriente no paga nada por todo esto");
    }
}