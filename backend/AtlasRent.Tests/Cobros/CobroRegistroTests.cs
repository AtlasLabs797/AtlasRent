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
/// F3-06: el registro de un cobro. FIFO por antigüedad, excedente que se convierte en saldo a favor,
/// crédito que se consume cuando quien cobra lo pide, e idempotencia del reintento.
/// <para>
/// Los importes se comprueban leyendo las dos tablas —<c>cobro</c>, <c>cobro_asignacion</c> y
/// <c>saldo_a_favor</c>— y no el DTO: el DTO es lo que devuelve la aplicación y sería justo lo que
/// hay que poner en duda.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class CobroRegistroTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    /// <summary>
    /// La FIFO de §3.10.1 es «la más antigua primero», y antigua es por vencimiento. Las tres facturas
    /// se crean en orden que no es el de sus vencimientos, para que el test no pueda pasar por
    /// casualidad: si el servicio ordenara por creación, esta propuesta saldría al revés.
    /// </summary>
    [Fact]
    public async Task LaPropuestaAsignaPrimeroALaFacturaMasAntiguaYUnCobroParcialDejaLaSiguienteAMedias()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        // Se crean primero las de vencimiento más tardío.
        var tardia = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(30), 300m, ct);
        var media = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(20), 300m, ct);
        var antigua = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct);

        var simulada = await sesion.PostAsync(
            "/api/v1/cobros/simular",
            AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, 650m, formaPagoId, cuentaId),
            ct);

        simulada.StatusCode.Should().Be(HttpStatusCode.OK, await simulada.Cuerpo());
        var propuesta = await simulada.Content.ReadFromJsonAsync<JsonElement>(ct);

        var asignadas = propuesta.GetProperty("asignaciones").EnumerateArray().ToList();
        asignadas.Should().HaveCount(3);
        asignadas[0].GetProperty("facturaId").GetGuid().Should().Be(antigua, "la más antigua se paga primero");
        asignadas[0].GetProperty("importe").LeerDecimal().Should().Be(300m);
        asignadas[1].GetProperty("facturaId").GetGuid().Should().Be(media);
        asignadas[1].GetProperty("importe").LeerDecimal().Should().Be(300m);
        asignadas[2].GetProperty("facturaId").GetGuid().Should().Be(tardia);
        asignadas[2].GetProperty("importe").LeerDecimal().Should().Be(50m, "un cobro parcial no se salta a la siguiente");

        propuesta.GetProperty("totalAsignado").LeerDecimal().Should().Be(650m);
        propuesta.GetProperty("excedente").LeerDecimal().Should().Be(0m);

        var registrado = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, 650m, formaPagoId, cuentaId),
            ct);

        registrado.StatusCode.Should().Be(HttpStatusCode.OK, await registrado.Cuerpo());

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var lineas = await contexto.CobroAsignacion.AsNoTracking()
            .Where(a => a.FacturaId == antigua || a.FacturaId == media || a.FacturaId == tardia)
            .ToListAsync(ct);

        lineas.Should().HaveCount(3);
        lineas.Single(a => a.FacturaId == antigua).Importe.Should().Be(300m);
        lineas.Single(a => a.FacturaId == media).Importe.Should().Be(300m);
        lineas.Single(a => a.FacturaId == tardia).Importe.Should().Be(50m);
        lineas.Should().OnlyContain(a => a.Vigente);

        (await AyudaCobros.SaldoAsync(bd, contratoId, Moneda.USD, ct))
            .Should().BeNull("cobró justo lo que debía: no hay crédito y no se crea la fila");
    }

    /// <summary>
    /// El segundo cobro de la cartera sigue la FIFO desde donde se quedó la primera, no desde el
    /// principio: la factura a medias se termina antes de tocar la siguiente.
    /// </summary>
    [Fact]
    public async Task ElCobroSiguienteTerminaLaFacturaAMediasAntesDePasarALaSiguiente()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var antigua = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct);
        var siguiente = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(20), 300m, ct);

        var primero = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, 400m, formaPagoId, cuentaId),
            ct);
        primero.StatusCode.Should().Be(HttpStatusCode.OK, await primero.Cuerpo());

        var segundo = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, 250m, formaPagoId, cuentaId),
            ct);
        segundo.StatusCode.Should().Be(HttpStatusCode.OK, await segundo.Cuerpo());

        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        // F3-09: sin el filtro por `FacturaId` no nulo, una asignación a fianza agruparía bajo una clave
        // nula y esta cuenta dejaría de ser la de las facturas. Aquí no hay fianzas, pero el filtro
        // tiene que ser el mismo que usa el servicio para que la cuenta signifique lo mismo.
        var cobrada = await contexto.CobroAsignacion.AsNoTracking()
            .Where(a => a.Vigente && a.FacturaId != null)
            .GroupBy(a => a.FacturaId!.Value)
            .Select(g => new { FacturaId = g.Key, Cobrado = g.Sum(a => a.Importe) })
            .ToDictionaryAsync(x => x.FacturaId, x => x.Cobrado, ct);

        cobrada[antigua].Should().Be(300m);
        cobrada[siguiente].Should().Be(300m, "100 del primer cobro y los 200 que quedaban, que el segundo alcanza");
    }

    /// <summary>
    /// Cobrar de más genera saldo a favor del contrato, y un cobro posterior lo consume cuando quien
    /// cobra lo pide. El crédito <b>no</b> se aplica solo: la especificación no dice a qué factura se
    /// descuenta, así que lo elige quien registra.
    /// </summary>
    [Fact]
    public async Task ElExcedenteQuedaComoSaldoAFavorYUnCobroPosteriorLoUsa()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var primera = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct);
        var segunda = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(20), 100m, ct);

        // 500 contra 400 de deuda: la FIFO paga las dos y sobran 100, que son crédito del contrato.
        var deMas = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, 500m, formaPagoId, cuentaId, referencia: "TRX-1"),
            ct);

        deMas.StatusCode.Should().Be(HttpStatusCode.OK, await deMas.Cuerpo());
        (await deMas.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("excedente").LeerDecimal()
            .Should().Be(100m);

        (await AyudaCobros.SaldoAsync(bd, contratoId, Moneda.USD, ct))
            .Should().Be(100m, "lo que sobra es crédito del contrato");

        // La factura que entra después es la que recoge el crédito junto con el dinero nuevo.
        var tercera = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(30), 150m, ct);

        var conCredito = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, 50m, formaPagoId, cuentaId, usarSaldoAFavor: 100m),
            ct);

        conCredito.StatusCode.Should().Be(HttpStatusCode.OK, await conCredito.Cuerpo());
        var cuerpo = await conCredito.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("saldoFavorUsado").LeerDecimal().Should().Be(100m);
        cuerpo.GetProperty("excedente").LeerDecimal().Should().Be(0m, "los 100 de crédito y los 50 nuevos cubren los 150");

        (await AyudaCobros.SaldoAsync(bd, contratoId, Moneda.USD, ct))
            .Should().Be(0m, "el crédito gastado no queda de saldo");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.CobroAsignacion.AsNoTracking()
                .Where(a => a.FacturaId == tercera && a.Vigente)
                .SumAsync(a => a.Importe, ct))
            .Should().Be(150m, "el crédito cubrió parte de la factura que quedaba");
        (await contexto.CobroAsignacion.AsNoTracking()
                .Where(a => a.FacturaId == segunda && a.Vigente)
                .SumAsync(a => a.Importe, ct))
            .Should().Be(100m);

        await using var sinRls = bd.CrearContexto(bd.CadenaSuperusuario);
        (await sinRls.Auditoria.AsNoTracking()
                .CountAsync(a => a.Accion == "cobro.saldo_a_favor_usado", ct))
            .Should().BeGreaterThan(0, "el crédito consumido tiene su propia acción de auditoría");
    }

    /// <summary>
    /// Un cobro que no dice que usa crédito no lo gasta, aunque haya crédito disponible y aunque la
    /// FIFO lo aplicaría a la misma factura. Aplicarlo solo sería inventar una regla que la
    /// especificación no dice (§3.10.1 no dice a qué factura se descuenta el saldo a favor).
    /// </summary>
    [Fact]
    public async Task UnCobroQueNoPideCreditoNoLoGasta()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var pendiente = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 100m, ct);

        // Un cobro de 300 contra una factura de 100 deja 200 de crédito.
        var deMas = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, 300m, formaPagoId, cuentaId),
            ct);
        deMas.StatusCode.Should().Be(HttpStatusCode.OK, await deMas.Cuerpo());
        (await AyudaCobros.SaldoAsync(bd, contratoId, Moneda.USD, ct)).Should().Be(200m);

        var sinPedir = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, 50m, formaPagoId, cuentaId),
            ct);

        sinPedir.StatusCode.Should().Be(HttpStatusCode.OK, await sinPedir.Cuerpo());
        (await sinPedir.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("saldoFavorUsado").LeerDecimal()
            .Should().Be(0m);

        (await AyudaCobros.SaldoAsync(bd, contratoId, Moneda.USD, ct))
            .Should().Be(250m, "el crédito de 200 sigue ahí porque nadie pidió gastarlo");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.CobroAsignacion.AsNoTracking()
                .Where(a => a.FacturaId == pendiente && a.Vigente)
                .SumAsync(a => a.Importe, ct))
            .Should().Be(100m, "la factura se pagó con el dinero del primer cobro y no con el crédito");
    }

    /// <summary>
    /// Un reintento con la misma <c>Idempotency-Key</c> devuelve el cobro ya registrado y no mete dos
    /// veces el mismo dinero en la caja.
    /// </summary>
    [Fact]
    public async Task RegistrarDosVecesConLaMismaClaveDejaUnSoloCobro()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var facturaId = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct);

        var clave = $"cobro-{Guid.NewGuid():N}";
        var cuerpo = AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, 300m, formaPagoId, cuentaId);

        var primera = await sesion.PostAsync("/api/v1/cobros", cuerpo, clave, ct);
        var segunda = await sesion.PostAsync("/api/v1/cobros", cuerpo, clave, ct);

        primera.StatusCode.Should().Be(HttpStatusCode.OK, await primera.Cuerpo());
        segunda.StatusCode.Should().Be(HttpStatusCode.OK);

        var cuerpoPrimero = await primera.Content.ReadFromJsonAsync<JsonElement>(ct);
        var cuerpoSegundo = await segunda.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpoSegundo.GetProperty("id").GetGuid().Should().Be(cuerpoPrimero.GetProperty("id").GetGuid());

        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        (await contexto.Cobro.AsNoTracking().CountAsync(c => c.ClienteId == clienteId, ct))
            .Should().Be(1, "el reintento no registró el mismo dinero dos veces");
        (await contexto.CobroAsignacion.AsNoTracking().CountAsync(a => a.FacturaId == facturaId, ct))
            .Should().Be(1, "ni una segunda línea de asignación para la misma factura");
    }

    /// <summary>
    /// Una asignación explícita manda sobre la FIFO: es la prueba de que la asignación es editable y de
    /// que el servicio no mueve el dinero por su cuenta cuando le dicen a dónde va.
    /// </summary>
    [Fact]
    public async Task UnaAsignacionExplicitaMandaSobreLaPropuesta()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var antigua = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct);
        var nueva = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(40), 300m, ct);

        var registrado = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.USD, 300m, formaPagoId, cuentaId,
                asignaciones: new[] { new { facturaId = nueva, importe = 300m } }),
            ct);

        registrado.StatusCode.Should().Be(HttpStatusCode.OK, await registrado.Cuerpo());

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.CobroAsignacion.AsNoTracking().AnyAsync(a => a.FacturaId == antigua, ct))
            .Should().BeFalse("la más antigua se queda sin pagar porque así lo pidió quien cobró");
        (await contexto.CobroAsignacion.AsNoTracking().AnyAsync(a => a.FacturaId == nueva, ct))
            .Should().BeTrue();
    }

    /// <summary>
    /// Hay dos límites distintos y los dos tienen que decirse: ninguna asignación puede pasar del
    /// pendiente de su factura, y la suma de todas no puede pasar de lo que trae el cobro.
    /// </summary>
    [Fact]
    public async Task AsignarMasDeLoQueQuedaPendienteSeRechaza()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var facturaId = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct);

        // La suma de las asignaciones no puede pasar de lo que entra: 300 asignados contra 100 cobrados.
        var masQueElCobro = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.USD, 100m, formaPagoId, cuentaId,
                asignaciones: new[] { new { facturaId, importe = 300m } }),
            ct);

        masQueElCobro.StatusCode.Should().Be(HttpStatusCode.Conflict, await masQueElCobro.Cuerpo());
        (await masQueElCobro.Content.ReadAsStringAsync(ct))
            .Should().Contain("solo trae", "el mensaje dice cuánto trae el cobro, no un error genérico de guardado");

        // Y con la factura ya a medias, tampoco se le puede asignar más de lo que le queda.
        var primero = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, 200m, formaPagoId, cuentaId),
            ct);
        primero.StatusCode.Should().Be(HttpStatusCode.OK, await primero.Cuerpo());

        var masQueLaFactura = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.USD, 200m, formaPagoId, cuentaId,
                asignaciones: new[] { new { facturaId, importe = 200m } }),
            ct);

        masQueLaFactura.StatusCode.Should().Be(HttpStatusCode.Conflict, await masQueLaFactura.Cuerpo());
        (await masQueLaFactura.Content.ReadAsStringAsync(ct)).Should().Contain("quedan");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Cobro.AsNoTracking().CountAsync(c => c.ClienteId == clienteId, ct))
            .Should().Be(1, "los dos rechazos no dejaron cobros a medias");
        (await contexto.CobroAsignacion.AsNoTracking()
                .Where(a => a.FacturaId == facturaId && a.Vigente)
                .SumAsync(a => a.Importe, ct))
            .Should().Be(200m);
    }
}