using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Cobros;

/// <summary>
/// F3-07: el pago en otra moneda (docs/ESPECIFICACION.md §3.10.2, D8).
/// <para>
/// <b>La factura conserva su moneda, su importe, su número y su comprobante.</b> Pagar una factura de
/// dólares con pesos no crea otra factura ni gasta un número: eso era la práctica que D8 viene a
/// sustituir, y el criterio de aceptación lo dice en voz alta («sin duplicar factura»), así que la
/// primera prueba lee el contador de la serie de la base y compara.
/// </para>
/// <para>
/// El cambio del día de la base compartida es <see cref="AyudaCobros.CambioDelDia"/> y todas las
/// pruebas lo usan a propósito: <c>tipo_cambio</c> es único por día y moneda (BUG-006), así que una
/// prueba que escribiera un cambio propio dejaría a F3-02 leyendo otro número. Los cambios concretos
/// de estos casos entran por <c>tipoCambioAplicado</c>, que es exactamente para lo que existe.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class CobroCambioMonedaTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    /// <summary>
    /// El caso exacto de §3.10.2: factura de 1.000 USD, se reciben 59.500 DOP y a 59,50 eso son
    /// exactamente 1.000 dólares. La factura queda Pagada, la asignación guarda los tres números y
    /// <b>no se crea ninguna factura nueva ni se mueve el contador de la serie</b>.
    /// </summary>
    [Fact]
    public async Task MilDolaresPagadasConPesosDejaLaFacturaPagadaYNoCreaNingunaFactura()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaPesos = await AyudaCobros.CuentaAsync(sesion, Moneda.DOP, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        // El cobro en pesos congela el cambio del día; al terminar se deshace (BUG-006).
        await using var limpieza = AyudaCobros.SinRastroDeCambio(bd, hoy, Moneda.DOP);
        await AyudaCobros.CargarCambioAsync(sesion, hoy, Moneda.DOP, AyudaCobros.CambioDelDia, ct);

        var facturaId = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 1_000m, ct);

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var antes = await contexto.Serie.AsNoTracking().Where(s => s.Id == serieId).Select(s => s.Siguiente).SingleAsync(ct);
            var facturasAntes = await contexto.Factura.AsNoTracking().CountAsync(f => f.ContratoId == contratoId, ct);

            var registrado = await sesion.PostAsync(
                "/api/v1/cobros",
                AyudaCobros.Peticion(
                    clienteId, contratoId, hoy, Moneda.DOP, 59_500m, formaPagoId, cuentaPesos,
                    tipoCambioAplicado: AyudaCobros.CambioExacto),
                ct);
            registrado.StatusCode.Should().Be(HttpStatusCode.OK, await registrado.Cuerpo());

            // Los tres números de §3.10.2, leídos de la tabla y no de la respuesta del endpoint.
            var asignacion = await contexto.CobroAsignacion.AsNoTracking().SingleAsync(a => a.FacturaId == facturaId, ct);
            asignacion.Importe.Should().Be(1_000.00m, "el importe es la deuda que salda, en la moneda de la factura");
            asignacion.ImporteRecibido.Should().Be(59_500.00m, "y lo que salió de la caja, en la moneda del cobro");
            asignacion.TipoCambioValor.Should().Be(AyudaCobros.CambioExacto, "con el cambio realmente aplicado");

            var despues = await contexto.Serie.AsNoTracking().Where(s => s.Id == serieId).Select(s => s.Siguiente).SingleAsync(ct);
            despues.Should().Be(antes, "pagar en pesos no gasta un número de factura: la factura es la misma");

            (await contexto.Factura.AsNoTracking().CountAsync(f => f.ContratoId == contratoId, ct))
                .Should().Be(facturasAntes, "y tampoco se emite una factura nueva en la moneda del pago");
        }

        // La factura sigue siendo la misma factura: su número, su moneda y su total no se han tocado.
        await using var verificacion = bd.CrearContexto(bd.CadenaApp);
        var factura = await verificacion.Factura.AsNoTracking().SingleAsync(f => f.Id == facturaId, ct);
        factura.Moneda.Should().Be(Moneda.USD);
        factura.Total.Should().Be(1_000m);
        factura.Estado.Should().Be(EstadoFactura.Emitida);

        var leida = await AyudaCobros.FacturaAsync(sesion, facturaId, ct);
        leida.GetProperty("estadoCobro").GetString().Should().Be("Pagada");
        leida.GetProperty("pendiente").GetDecimal().Should().Be(0m);
        leida.GetProperty("cobrado").GetDecimal().Should().Be(1_000m);
    }

    /// <summary>
    /// Por defecto se aplica el cambio del día y no el que el gerente fije. Y cuando sí lo fija, en la
    /// asignación queda el que él puso y <b>en el cobro sigue el del día</b>: son dos datos distintos
    /// con dos fines distintos, y pisar uno con el otro haría que un informe justificara un cobro con un
    /// cambio que no fue el que se usó (D9 congela el del día).
    /// </summary>
    [Fact]
    public async Task SinCambioFijadoSeAplicaElDelDiaYSiSeFijaMandaElDeLaAsignacion()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaPesos = await AyudaCobros.CuentaAsync(sesion, Moneda.DOP, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        await using var limpieza = AyudaCobros.SinRastroDeCambio(bd, hoy, Moneda.DOP);
        await AyudaCobros.CargarCambioAsync(sesion, hoy, Moneda.DOP, AyudaCobros.CambioDelDia, ct);

        // Con el cambio del día: 60.000 DOP son 998,31 dólares a 60,123456.
        var primera = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 998.31m, ct);

        var conDia = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.DOP, 60_000m, formaPagoId, cuentaPesos),
            ct);
        conDia.StatusCode.Should().Be(HttpStatusCode.OK, await conDia.Cuerpo());

        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        var asignacionDelDia = await contexto.CobroAsignacion.AsNoTracking()
            .SingleAsync(a => a.FacturaId == primera, ct);
        asignacionDelDia.TipoCambioValor.Should().Be(AyudaCobros.CambioDelDia, "sin fijar nada se usa el del día");

        var cobroDelDia = await contexto.Cobro.AsNoTracking()
            .SingleAsync(c => c.ClienteId == clienteId, ct);
        cobroDelDia.TipoCambioValor.Should().Be(AyudaCobros.CambioDelDia);

        // Ahora el gerente fija el que se usó de verdad en el mostrador, que es otro.
        var segunda = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 1_000m, ct);

        var conFijado = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.DOP, 59_500m, formaPagoId, cuentaPesos,
                tipoCambioAplicado: AyudaCobros.CambioExacto),
            ct);
        conFijado.StatusCode.Should().Be(HttpStatusCode.OK, await conFijado.Cuerpo());

        var asignacionFijada = await contexto.CobroAsignacion.AsNoTracking()
            .SingleAsync(a => a.FacturaId == segunda, ct);
        asignacionFijada.TipoCambioValor.Should().Be(AyudaCobros.CambioExacto, "la asignación guarda el que fijó el gerente");

        var cobroFijado = await contexto.Cobro.AsNoTracking()
            .SingleAsync(c => c.ClienteId == clienteId && c.Asignaciones.Any(a => a.FacturaId == segunda), ct);
        cobroFijado.TipoCambioValor.Should().Be(
            AyudaCobros.CambioDelDia,
            "el cobro conserva el del día, que es el dato de referencia con el que se justifican los informes");
    }

    /// <summary>
    /// Cuando el cambio no divide exacto, la diferencia queda como pendiente y no como un importe
    /// inventado. La suma de «lo recibido dividido por el cambio» de todas las asignaciones cuadra con
    /// lo que cada factura cobró salvo ese redondeo del céntimo, que es justo lo que después mide el
    /// ajuste por diferencia de cambio.
    /// </summary>
    [Fact]
    public async Task UnCambioQueNoDivideExactoDejaElPendienteDelRedondeoYLaDiferenciaEsMedible()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaPesos = await AyudaCobros.CuentaAsync(sesion, Moneda.DOP, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        await using var limpieza = AyudaCobros.SinRastroDeCambio(bd, hoy, Moneda.DOP);
        await AyudaCobros.CargarCambioAsync(sesion, hoy, Moneda.DOP, AyudaCobros.CambioDelDia, ct);

        var facturaId = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 1_000m, ct);

        var registrado = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.DOP, 60_000m, formaPagoId, cuentaPesos),
            ct);
        registrado.StatusCode.Should().Be(HttpStatusCode.OK, await registrado.Cuerpo());

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var asignaciones = await contexto.CobroAsignacion.AsNoTracking().Where(a => a.FacturaId == facturaId).ToListAsync(ct);

        var asignacion = asignaciones.Should().ContainSingle().Which;
        asignacion.ImporteRecibido.Should().Be(60_000m, "todo el cobro se aplicó a la factura: el resto es pendiente");

        // La conversión hacia atrás, con los mismos números guardados, tiene que dar lo que se aplicó:
        // si no cuadrara, la fila guardaría dos cifras que no se cuentan.
        var equivalente = Math.Round(asignacion.ImporteRecibido!.Value / asignacion.TipoCambioValor!.Value, 2);
        equivalente.Should().Be(asignacion.Importe, "lo recibido dividido por el cambio es lo aplicado a la factura");

        var pendiente = 1_000m - asignacion.Importe;
        pendiente.Should().Be(2.05m, "la diferencia es exactamente la que va a medir el ajuste");
        pendiente.Should().BeGreaterThan(1m, "y aquí supera la tolerancia de un dólar");

        var leida = await AyudaCobros.FacturaAsync(sesion, facturaId, ct);
        leida.GetProperty("estadoCobro").GetString().Should().Be("Parcial");
        leida.GetProperty("pendiente").GetDecimal().Should().Be(pendiente);
    }

    /// <summary>
    /// La segunda salida que da §3.10.2: en vez de ajustar, se corrige el cambio. Se anula el cobro que
    /// se registró con el cambio equivocado y se vuelve a registrar el mismo dinero con el que se usó
    /// de verdad. La factura queda Pagada y <b>no existe ningún ajuste</b>, porque no hubo diferencia:
    /// hubo una captura equivocada.
    /// </summary>
    [Fact]
    public async Task CorregirElCambioEnVezDeAjustarDejaLaFacturaPagadaSinNingunAjuste()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaPesos = await AyudaCobros.CuentaAsync(sesion, Moneda.DOP, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        await using var limpieza = AyudaCobros.SinRastroDeCambio(bd, hoy, Moneda.DOP);
        await AyudaCobros.CargarCambioAsync(sesion, hoy, Moneda.DOP, AyudaCobros.CambioDelDia, ct);

        var facturaId = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 1_000m, ct);

        // Primero, con el cambio que se creyó en el mostrador: 59.500 DOP a 59,51 son 999,83.
        var equivocado = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.DOP, 59_500m, formaPagoId, cuentaPesos,
                tipoCambioAplicado: AyudaCobros.CambioConRedondeo),
            ct);
        equivocado.StatusCode.Should().Be(HttpStatusCode.OK, await equivocado.Cuerpo());

        var cobroId = (await equivocado.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();

        var despuesDePagarMal = await AyudaCobros.FacturaAsync(sesion, facturaId, ct);
        despuesDePagarMal.GetProperty("pendiente").GetDecimal().Should().Be(0.17m);

        // Se anula el cobro con su motivo y se vuelve a registrar el mismo dinero con el cambio real.
        var anulacion = await sesion.PostAsync(
            $"/api/v1/cobros/{cobroId:D}/anular",
            new { motivo = "Se aplicó un cambio que no era el del banco" },
            ct);
        anulacion.StatusCode.Should().Be(HttpStatusCode.OK, await anulacion.Cuerpo());

        var corregido = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.DOP, 59_500m, formaPagoId, cuentaPesos,
                tipoCambioAplicado: AyudaCobros.CambioExacto),
            ct);
        corregido.StatusCode.Should().Be(HttpStatusCode.OK, await corregido.Cuerpo());

        var leida = await AyudaCobros.FacturaAsync(sesion, facturaId, ct);
        leida.GetProperty("estadoCobro").GetString().Should().Be("Pagada");
        leida.GetProperty("pendiente").GetDecimal().Should().Be(0m);
        leida.GetProperty("ajustado").GetDecimal().Should().Be(0m, "no hizo falta ajustar: se corrigió el cambio");

        (await AyudaCobros.AjustesAsync(bd, facturaId, ct))
            .Should().BeEmpty("la segunda salida de §3.10.2 no deja rastro de ajuste");
    }

    /// <summary>
    /// Un cobro puede cubrir facturas de varias monedas con un solo pago recibido, y cada línea guarda
    /// su propio importe recibido y su propio cambio (§3.10.2, punto 4). Es lo que necesita F3-08, que
    /// anula una de las dos sin tocar la otra.
    /// </summary>
    [Fact]
    public async Task UnCobroCubreFacturasDeDosMonedasConUnSoloPagoRecibido()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        await using var limpieza = AyudaCobros.SinRastroDeCambio(bd, hoy, Moneda.DOP);
        await AyudaCobros.CargarCambioAsync(sesion, hoy, Moneda.DOP, AyudaCobros.CambioDelDia, ct);

        // Las dos vencen el mismo día para que la FIFO tenga que desempatar, y la de pesos va segunda a
        // propósito: el desempate es por emisión, así que primero la de dólares.
        var serieDolar = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var seriePesos = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.DOP, ct);

        var vencimiento = hoy.AddDays(10);
        var facturaDolar = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, serieDolar, Moneda.USD, vencimiento, 500m, ct);
        await Task.Delay(TimeSpan.FromSeconds(1), ct);
        var facturaPesos = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, seriePesos, Moneda.DOP, vencimiento, 1_000m, ct);

        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaPesos = await AyudaCobros.CuentaAsync(sesion, Moneda.DOP, ct);

        // 500 USD a 59,50 son 29.750, y los 25.750 que sobran se van a los 1.000 DOP de la otra factura.
        var registrado = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.DOP, 55_500m, formaPagoId, cuentaPesos,
                tipoCambioAplicado: AyudaCobros.CambioExacto),
            ct);
        registrado.StatusCode.Should().Be(HttpStatusCode.OK, await registrado.Cuerpo());

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var lineas = await contexto.CobroAsignacion.AsNoTracking().Where(a => a.FacturaId == facturaDolar || a.FacturaId == facturaPesos).ToListAsync(ct);
        lineas.Should().HaveCount(2);

        var enDolar = lineas.Single(a => a.FacturaId == facturaDolar);
        enDolar.Importe.Should().Be(500m);
        enDolar.ImporteRecibido.Should().Be(29_750m);
        enDolar.TipoCambioValor.Should().Be(AyudaCobros.CambioExacto);

        // Un solo pago recibido cubre las dos deudas, cada línea con su conversión: 500 USD a 59,50
        // son 29.750 DOP, y la factura en pesos solo necesita 1.000. Lo que sobra (24.750) no se
        // reparte a lo bruto: es excedente, y va al saldo a favor del contrato (§3.10.1).
        var enPesos = lineas.Single(a => a.FacturaId == facturaPesos);
        enPesos.Importe.Should().Be(1_000m, "la factura en pesos solo debía 1.000 y se le pagó entera");
        enPesos.ImporteRecibido.Should().BeNull("en la moneda del cobro no hubo conversión");
        enPesos.TipoCambioValor.Should().BeNull("y por eso no se guarda tipo de cambio");

        var cobros = await contexto.Cobro.AsNoTracking()
            .Where(c => c.ClienteId == clienteId)
            .SingleAsync(ct);
        cobros.Excedente.Should().Be(24_750m, "lo que sobra tras cubrir las dos facturas es crédito, no sobrepago");
    }

    /// <summary>
    /// Un cobro en la misma moneda sigue siendo exactamente lo de F3-06: sin importe recibido, sin
    /// tipo de cambio y sin nada nuevo que explicar. Las columnas nuevas existen para el pago en otra
    /// moneda; en el caso fácil tienen que quedar vacías, porque «no se aplicó conversión» no es «se
    /// aplicó una conversión de 1».
    /// </summary>
    [Fact]
    public async Task UnCobroEnLaMismaMonedaNoLlenaLasColumnasDeConversion()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var facturaId = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct);

        var registrado = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, 300m, formaPagoId, cuentaId),
            ct);
        registrado.StatusCode.Should().Be(HttpStatusCode.OK, await registrado.Cuerpo());

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var asignacion = await contexto.CobroAsignacion.AsNoTracking().SingleAsync(a => a.FacturaId == facturaId, ct);
        asignacion.Importe.Should().Be(300m);
        asignacion.ImporteRecibido.Should().BeNull();
        asignacion.TipoCambioValor.Should().BeNull();

        var leida = await AyudaCobros.FacturaAsync(sesion, facturaId, ct);
        leida.GetProperty("estadoCobro").GetString().Should().Be("Pagada");
        leida.GetProperty("ajustado").GetDecimal().Should().Be(0m);
    }

    /// <summary>
    /// El mismo <c>Idempotency-Key</c> sobre un pago en otra moneda deja un solo cobro. Un pago en
    /// pesos repetido por un reintento del cliente no puede ser dinero repetido.
    /// </summary>
    [Fact]
    public async Task ElMismoIdempotencyKeyDejaUnSoloCobro()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaPesos = await AyudaCobros.CuentaAsync(sesion, Moneda.DOP, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        await using var limpieza = AyudaCobros.SinRastroDeCambio(bd, hoy, Moneda.DOP);
        await AyudaCobros.CargarCambioAsync(sesion, hoy, Moneda.DOP, AyudaCobros.CambioDelDia, ct);

        await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 1_000m, ct);

        var cuerpo = AyudaCobros.Peticion(
            clienteId, contratoId, hoy, Moneda.DOP, 59_500m, formaPagoId, cuentaPesos,
            tipoCambioAplicado: AyudaCobros.CambioExacto);

        var clave = Guid.NewGuid().ToString("N");

        var primero = await sesion.PostAsync("/api/v1/cobros", cuerpo, clave, ct);
        primero.StatusCode.Should().Be(HttpStatusCode.OK, await primero.Cuerpo());

        var segundo = await sesion.PostAsync("/api/v1/cobros", cuerpo, clave, ct);
        segundo.StatusCode.Should().Be(HttpStatusCode.OK, await segundo.Cuerpo());

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var cobros = await contexto.Cobro.AsNoTracking()
            .Include(c => c.Asignaciones)
            .SingleAsync(c => c.ClienteId == clienteId, ct);
        cobros.Asignaciones.Should().ContainSingle("una línea: el reintento no vuelve a aplicar el dinero");
        cobros.ImporteRecibido.Should().Be(59_500m);
    }
}