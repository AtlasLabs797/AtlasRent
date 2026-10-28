using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cobros;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Fianzas;

/// <summary>
/// F3-09: cobrar una fianza es registrar un <b>cobro</b> (§3.11, §3.10.1), no un endpoint aparte.
/// <para>
/// Lo que estas pruebas comprueban es que el camino único funciona y, sobre todo, que
/// <b>una asignación a fianza no es deuda de factura</b>: la factura del mismo contrato tiene que quedar
/// exactamente como estaba. Si esa fila se moviera, un cobro de una fianza cerraría una factura sin que
/// nadie la cobrara, que es el peor fallo posible en un sistema de cuentas.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class FianzaCobroTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    /// <summary>
    /// Un cobro normal asignado a la fianza sube lo cobrado y baja el saldo, y la fianza aparece en la
    /// lista con su estado y su contrato.
    /// <para>
    /// <b>El saldo antes de cobrar es cero</b>, no el importe exigido: §3.11 lo define como «cobrado
    /// menos devoluciones menos descuentos», y antes de cobrar no hay cobrado. Lo que el contrato exige
    /// y lo que falta por cobrar son las columnas <c>importeExigido</c> y <c>cobrado</c> de la misma
    /// fila, que es donde F3-10 las mira.
    /// </para>
    /// </summary>
    [Fact]
    public async Task CobrarLaFianzaSubeElCobradoYBajaElSaldo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, 1_000m, Moneda.USD, 2, ct);

        var antesDeCobrar = await AyudaFianzas.LeerAsync(admin, fianzaId, ct);
        antesDeCobrar.GetProperty("cobrado").LeerDecimal()
            .Should().Be(0m, "una fianza recién dada de alta no tiene nada cobrado");
        antesDeCobrar.GetProperty("saldo").LeerDecimal()
            .Should().Be(0m, "§3.11: el saldo es lo cobrado menos lo devuelto, y aún no se ha cobrado");

        await AyudaFianzas.CobradaAsync(admin, clienteId, contratoId, fianzaId, 1_000m, Moneda.USD, ct);

        var cifras = await AyudaFianzas.CifrasAsync(bd, fianzaId, ct);
        cifras.Cobrado.Should().Be(1_000m);
        cifras.Saldo.Should().Be(1_000m, "cobrada entera y sin devolver, queda todo por devolver");

        var leida = await AyudaFianzas.LeerAsync(admin, fianzaId, ct);
        leida.GetProperty("cobrado").LeerDecimal().Should().Be(1_000m);
        leida.GetProperty("saldo").LeerDecimal().Should().Be(1_000m);
        leida.GetProperty("importeExigido").LeerDecimal().Should().Be(1_000m);
        leida.GetProperty("numeroFianzas").GetInt32().Should().Be(2);
    }

    /// <summary>
    /// <b>La factura del mismo contrato no se ve afectada.</b> Ni su pendiente, ni su estado de pago,
    /// ni sus asignaciones. Es lo que garantiza que la fianza viva en la vista de cuentas (§3.13) sin
    /// tocar la deuda de las facturas.
    /// </summary>
    [Fact]
    public async Task CobrarLaFianzaNoAfectaALaFacturaDelMismoContrato()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var facturaId = await AyudaCobros.EmitidaAsync(
            admin, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct);

        var antes = await AyudaCobros.FacturaAsync(admin, facturaId, ct);
        var pendienteAntes = antes.GetProperty("pendiente").LeerDecimal();
        var estadoCobroAntes = antes.GetProperty("estadoCobro").GetString();

        pendienteAntes.Should().Be(300m, "la factura está emitida y sin cobrar");

        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, 500m, Moneda.USD, 1, ct);
        await AyudaFianzas.CobradaAsync(admin, clienteId, contratoId, fianzaId, 500m, Moneda.USD, ct);

        var despues = await AyudaCobros.FacturaAsync(admin, facturaId, ct);
        despues.GetProperty("pendiente").LeerDecimal()
            .Should().Be(pendienteAntes, "cobrar una fianza no es pagar una factura");
        despues.GetProperty("estadoCobro").GetString().Should().Be(estadoCobroAntes);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.CobroAsignacion.AsNoTracking().AnyAsync(a => a.FacturaId == facturaId, ct))
            .Should().BeFalse("no hay ninguna asignación viva a esa factura");
        (await contexto.CobroAsignacion.AsNoTracking().AnyAsync(a => a.FianzaId == fianzaId, ct))
            .Should().BeTrue("la asignación del cobro es a la fianza");
    }

    /// <summary>
    /// La fianza aparece en el listado con lo que §3.11 pide: importe exigido, número de fianzas,
    /// moneda, cobrado, devuelto, retenido, saldo, estado y contrato. Es exactamente lo que F3-10 va a
    /// pintar en la vista de cuentas, así que el listado es la entrega de esta tarea hacia la siguiente.
    /// <para>
    /// Va en dólares a propósito: cobrar una fianza exige el tipo de cambio de la moneda recibida
    /// (§3.10.2) y meter aquí el cambio del día del peso ataría esta prueba —que va de la fila del
    /// listado— a la maquinaria de F3-01, que ya tiene sus propias pruebas.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ElListadoDevuelveLoQuePide311ParaQueLoUseF310()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, 1_200m, Moneda.USD, 3, ct);
        await AyudaFianzas.CobradaAsync(admin, clienteId, contratoId, fianzaId, 1_200m, Moneda.USD, ct);

        var listado = await admin.GetAsync($"/api/v1/fianzas?contratoId={contratoId:D}", ct);
        listado.StatusCode.Should().Be(HttpStatusCode.OK, await listado.Cuerpo());

        var pagina = await listado.Content.ReadFromJsonAsync<JsonElement>(ct);
        pagina.GetProperty("total").GetInt32().Should().Be(1);

        var fila = pagina.GetProperty("data")[0];
        fila.GetProperty("id").GetGuid().Should().Be(fianzaId);
        fila.GetProperty("contratoId").GetGuid().Should().Be(contratoId, "la fianza es la cuenta de su contrato");
        fila.GetProperty("importeExigido").LeerDecimal().Should().Be(1_200m);
        fila.GetProperty("numeroFianzas").GetInt32().Should().Be(3);
        fila.GetProperty("moneda").GetString().Should().Be(nameof(Moneda.USD));
        fila.GetProperty("cobrado").LeerDecimal().Should().Be(1_200m);
        fila.GetProperty("devuelto").LeerDecimal().Should().Be(0m);
        fila.GetProperty("descontado").LeerDecimal().Should().Be(0m);
        fila.GetProperty("saldo").LeerDecimal().Should().Be(1_200m);
        fila.GetProperty("estado").GetString().Should().Be(nameof(EstadoFianza.Parcial));
    }

    /// <summary>
    /// El estado que se ve sale de lo cobrado, no de la columna: recién dada de alta está
    /// <c>Pendiente</c> y después de cobrarla, <c>Parcial</c>. Las dos son las que derivan del cobrado
    /// (§3.11) y no se guardan nunca.
    /// </summary>
    [Fact]
    public async Task ElEstadoVisibleSeDerivaDeLoCobrado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, 1_000m, Moneda.USD, 1, ct);

        (await AyudaFianzas.LeerAsync(admin, fianzaId, ct)).GetProperty("estado").GetString()
            .Should().Be(nameof(EstadoFianza.Pendiente), "sin cobrar no hay nada que devolver");

        await AyudaFianzas.CobradaAsync(admin, clienteId, contratoId, fianzaId, 400m, Moneda.USD, ct);

        var leida = await AyudaFianzas.LeerAsync(admin, fianzaId, ct);
        leida.GetProperty("estado").GetString()
            .Should().Be(nameof(EstadoFianza.Parcial), "con dinero cobrado y sin decisión, la fianza está en curso");
        leida.GetProperty("cobrado").LeerDecimal().Should().Be(400m);

        (await AyudaFianzas.LeerAsync(admin, fianzaId, ct)).GetProperty("saldo").LeerDecimal()
            .Should().Be(400m, "el saldo es lo cobrado: 400 cobrados y nada devuelto todavía");
    }

    /// <summary>
    /// No se puede cobrar más que el saldo de la fianza, ni cobrarla dos veces con la misma línea, ni
    /// cobrar una fianza de otro contrato. Los tres son 409 con un mensaje que dice por qué.
    /// </summary>
    [Fact]
    public async Task CobrarMasDelSaldoOLaFianzaAjenaSon409()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, 500m, Moneda.USD, 1, ct);
        await AyudaFianzas.CobradaAsync(admin, clienteId, contratoId, fianzaId, 500m, Moneda.USD, ct);

        var (otroClienteId, otroContratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(admin, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(admin, Moneda.USD, ct);

        // No hace falta una factura real: la regla de «exactamente un destino» se comprueba antes de
        // mirar si la factura existe, y lo que se está probando es esa regla, no la búsqueda.
        var facturaId = Guid.NewGuid();

        var deMas = await admin.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.USD, 100m, formaPagoId, cuentaId,
                asignaciones: new[] { new { fianzaId, importe = 100m } }),
            ct);
        deMas.StatusCode.Should().Be(HttpStatusCode.Conflict, "la fianza ya está cobrada entera");
        (await deMas.Cuerpo()).Should().Contain("fianza");

        var ajena = await admin.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                otroClienteId, otroContratoId, hoy, Moneda.USD, 100m, formaPagoId, cuentaId,
                asignaciones: new[] { new { fianzaId, importe = 100m } }),
            ct);
        ajena.StatusCode.Should().Be(HttpStatusCode.Conflict, "un cobro no cruza de contrato");
        (await ajena.Cuerpo()).Should().Contain("contrato");

        var sinDestino = await admin.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.USD, 100m, formaPagoId, cuentaId,
                asignaciones: new[] { new { importe = 100m } }),
            ct);
        sinDestino.StatusCode.Should().Be(HttpStatusCode.Conflict, "una línea sin destino no aplica nada");

        // Una línea con los dos destinos no tiene a qué deuda aplicar el importe. El «exactamente uno» se
        // comprueba antes del límite de importe, así que da igual que la fianza esté ya cobrada entera.
        var dos = await admin.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.USD, 100m, formaPagoId, cuentaId,
                asignaciones: new[] { new { fianzaId, facturaId, importe = 100m } }),
            ct);
        dos.StatusCode.Should().Be(
            HttpStatusCode.Conflict,
            "una línea con los dos destinos no tiene a qué deuda aplicar el importe");
        (await dos.Cuerpo()).Should().Contain("fianza");

        var repetida = await admin.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.USD, 100m, formaPagoId, cuentaId,
                asignaciones: new[] { new { fianzaId, importe = 10m }, new { fianzaId, importe = 10m } }),
            ct);
        repetida.StatusCode.Should().Be(HttpStatusCode.Conflict, "de un cobro a una fianza hay una sola línea");
    }

    /// <summary>
    /// Una fianza por contrato y moneda: la segunda es un 409. Dos fianzas del mismo contrato y la misma
    /// moneda obligarían a sumar dos saldos a mano para responder lo único que el cliente pregunta.
    /// </summary>
    [Fact]
    public async Task UnaSegundaFianzaDelMismoContratoYMonedaEs409()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (_, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        await AyudaFianzas.AltaAsync(admin, contratoId, 500m, Moneda.USD, 1, ct);

        var segunda = await admin.PostAsync(
            "/api/v1/fianzas",
            new { contratoId, importeExigido = 700m, moneda = nameof(Moneda.USD), numeroFianzas = 1 },
            ct);

        segunda.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await segunda.Cuerpo()).Should().Contain("moneda");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Fianza.AsNoTracking().CountAsync(f => f.ContratoId == contratoId, ct))
            .Should().Be(1, "la segunda no llegó a existir");
    }

    /// <summary>
    /// La fianza no aparece en la FIFO: cobrarla es señalar cuál es, como en toda asignación explícita.
    /// Un cobro sin asignaciones paga facturas y su excedente va a saldo a favor (§3.10.1), y la fianza
    /// se queda sin cobrar.
    /// </summary>
    [Fact]
    public async Task LaFifoDelCobroNoProponeFianzas()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, 500m, Moneda.USD, 1, ct);
        await AyudaFianzas.CobradaAsync(admin, clienteId, contratoId, fianzaId, 500m, Moneda.USD, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(admin, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(admin, Moneda.USD, ct);

        var simulacion = await admin.PostAsync(
            "/api/v1/cobros/simular",
            AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, 200m, formaPagoId, cuentaId),
            ct);

        simulacion.StatusCode.Should().Be(HttpStatusCode.OK, await simulacion.Cuerpo());

        var cuerpo = await simulacion.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("asignaciones").GetArrayLength()
            .Should().Be(0, "el contrato no tiene facturas y la FIFO no propone fianzas");
        cuerpo.GetProperty("excedente").LeerDecimal()
            .Should().Be(200m, "lo que no se aplica es excedente, que va a saldo a favor (§3.10.1)");
    }
}