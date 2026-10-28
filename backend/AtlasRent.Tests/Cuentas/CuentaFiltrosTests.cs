using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.CicloFactura;
using AtlasRent.Tests.Cobros;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Fianzas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Cuentas;

/// <summary>
/// F3-10: los filtros de §3.13, uno por prueba.
/// <para>
/// Cada filtro tiene su propia prueba porque el fallo que se quiere evitar es el filtro que no
/// filtra: una lista que acepta <c>vencidas=true</c> y devuelve lo mismo de siempre no se
/// distingüe de la que sí filtra hasta que alguien mira. Por eso cada una comprueba las dos
/// mitades —lo que tiene que salir y lo que no—.
/// </para>
/// <para>
/// <b>Lo que no se acepta no se prueba:</b> «no enviadas», «envío fallido» y «modificadas tras
/// envío» son de F4-01 y el endpoint no los acepta, porque un filtro que se acepta y se ignora es
/// peor que uno que no existe. Ver <c>CuentaFiltrosPendientesTests</c>.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class CuentaFiltrosTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    /// <summary>
    /// El año es el de la fecha de emisión y solo trae facturas de ese año. La factura del año
    /// anterior se emite con su fecha en el pasado por la misma vía que usa F3-02: no hay endpoint
    /// para fechar una factura en el pasado, así que el filtro se prueba sobre el año de hoy y sobre
    /// un año que no es el de ninguna fila del cliente.
    /// </summary>
    [Fact]
    public async Task ElAnioTraeLoDeEseAnioYNoLoDeOtro()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaId = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        var deEsteAnio = await AyudaCuentas.VistaAsync(
            admin, $"clienteId={escenario.ClienteId:D}&anio={escenario.Hoy.Year}", ct);
        AyudaCuentas.Contiene(deEsteAnio, facturaId).Should().BeTrue();

        var deOtroAnio = await AyudaCuentas.VistaAsync(
            admin, $"clienteId={escenario.ClienteId:D}&anio={escenario.Hoy.Year - 1}", ct);
        AyudaCuentas.Contiene(deOtroAnio, facturaId).Should().BeFalse();
        AyudaCuentas.Filas(deOtroAnio).Should().BeEmpty();
    }

    /// <summary>
    /// La serie filtra por serie y deja fuera a las fianzas, que no tienen serie: devolverlas «por si
    /// acaso» sería una fila que el filtro no tocó.
    /// </summary>
    [Fact]
    public async Task LaSerieTraeLoDeEsaSerieYSoloFacturas()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);

        var deLaSerie = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);
        var deLaotra = await AyudaCobros.EmitidaAsync(
            admin, escenario.ClienteId, escenario.ContratoId, serieId, Moneda.USD,
            escenario.Hoy.AddDays(11), 400m, ct);

        await AyudaCuentas.FianzaAsync(admin, escenario, 500m, ct);

        var vista = await AyudaCuentas.VistaAsync(admin, $"clienteId={escenario.ClienteId:D}&serieId={serieId:D}", ct);
        var filas = AyudaCuentas.Filas(vista);

        filas.Should().ContainSingle(f => f.GetProperty("id").GetGuid() == deLaotra);
        AyudaCuentas.Contiene(vista, deLaSerie).Should().BeFalse();
        filas.Select(f => f.GetProperty("tipo").GetString()).Should().NotContain("Fianza");
    }

    /// <summary>La moneda trae lo de esa moneda.</summary>
    [Fact]
    public async Task LaMonedaTraeLoDeEsaMoneda()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);

        // Emitir en pesos necesita el cambio del día, que es único por día y moneda y por eso lo
        // carga una sola vez por la base compartida con el valor de F3-02 (AyudaCobros.CambioDelDia).
        await AyudaCobros.CargarCambioAsync(
            admin, escenario.Hoy, Moneda.DOP, AyudaCobros.CambioDelDia, ct);

        var enUsd = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct, Moneda.USD);
        var enDop = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(11), 9_000m, ct, Moneda.DOP);

        var usd = await AyudaCuentas.VistaAsync(
            admin, $"clienteId={escenario.ClienteId:D}&moneda={nameof(Moneda.USD)}", ct);

        AyudaCuentas.Contiene(usd, enUsd).Should().BeTrue();
        AyudaCuentas.Contiene(usd, enDop).Should().BeFalse();
    }

    /// <summary>El estado es el del documento (columna), no el derivado de cobro.</summary>
    [Fact]
    public async Task ElEstadoTraeLoEmitidoYNoLoAnulado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var emitida = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);
        var anulada = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(11), 400m, ct);

        await AyudaCiclo.AnularAsync(admin, anulada, "Prueba de filtro de estado.", ct);

        var emitidaVista = await AyudaCuentas.VistaAsync(
            admin, $"clienteId={escenario.ClienteId:D}&estado={nameof(EstadoFactura.Emitida)}", ct);
        AyudaCuentas.Contiene(emitidaVista, emitida).Should().BeTrue();
        AyudaCuentas.Contiene(emitidaVista, anulada).Should().BeFalse();

        var anuladaVista = await AyudaCuentas.VistaAsync(
            admin, $"clienteId={escenario.ClienteId:D}&estado={nameof(EstadoFactura.Anulada)}", ct);
        AyudaCuentas.Filas(anuladaVista).Should().ContainSingle(f => f.GetProperty("id").GetGuid() == anulada);
    }

    /// <summary>
    /// El estado de cobro es derivado y se filtra con la misma cuenta que la columna: una factura
    /// pagada no sale en <c>Pendiente</c> ni una sin cobrar en <c>Pagada</c>.
    /// </summary>
    [Fact]
    public async Task ElEstadoDeCobroTraeLasQueLoTienen()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var pagada = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);
        var sinCobrar = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(11), 400m, ct);

        await AyudaCuentas.CobrarAsync(admin, escenario, pagada, 300m, escenario.Hoy, ct);

        var pagadas = await AyudaCuentas.VistaAsync(
            admin,
            $"clienteId={escenario.ClienteId:D}&estadoCobro={nameof(EstadoCobroFactura.Pagada)}",
            ct);

        AyudaCuentas.Filas(pagadas).Should().ContainSingle(f => f.GetProperty("id").GetGuid() == pagada);
        AyudaCuentas.Contiene(pagadas, sinCobrar).Should().BeFalse();

        var pendientes = await AyudaCuentas.VistaAsync(
            admin,
            $"clienteId={escenario.ClienteId:D}&estadoCobro={nameof(EstadoCobroFactura.Pendiente)}",
            ct);

        AyudaCuentas.Filas(pendientes).Should().ContainSingle(f => f.GetProperty("id").GetGuid() == sinCobrar);
        AyudaCuentas.Contiene(pendientes, pagada).Should().BeFalse();
    }

    /// <summary>El cliente y la unidad traen lo suyo y no lo del otro.</summary>
    [Fact]
    public async Task ElClienteYLaUnidadTraenLoSuyo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var mia = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        var otro = await AyudaCobros.ContratoAsync(admin, bd, ct);
        var ajena = await AyudaCobros.EmitidaAsync(
            admin, otro.ClienteId, otro.ContratoId, await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct),
            Moneda.USD, escenario.Hoy.AddDays(10), 300m, ct);

        var porCliente = await AyudaCuentas.VistaAsync(
            admin, $"clienteId={escenario.ClienteId:D}", ct);

        AyudaCuentas.Contiene(porCliente, mia).Should().BeTrue();
        AyudaCuentas.Contiene(porCliente, ajena).Should().BeFalse();

        var porUnidad = await AyudaCuentas.VistaAsync(
            admin, $"clienteId={escenario.ClienteId:D}&unidadId={escenario.UnidadId:D}", ct);

        AyudaCuentas.Contiene(porUnidad, mia).Should().BeTrue();

        var otraUnidad = await AyudaReserva.UnidadAsync(admin, ct);
        var porOtraUnidad = await AyudaCuentas.VistaAsync(
            admin, $"clienteId={escenario.ClienteId:D}&unidadId={otraUnidad:D}", ct);

        AyudaCuentas.Filas(porOtraUnidad).Should().BeEmpty();
    }

    /// <summary>
    /// Las vencidas son las que tienen algo pendiente y su vencimiento ya pasó, y el filtro trae los
    /// días de mora con la cuenta correcta (§3.8: el vencimiento del mismo día no está vencido).
    /// </summary>
    [Fact]
    public async Task LasVencidasTraenLosDiasDeMoraYNoLasQueNoDebenNada()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);

        var vencida = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(-20), 300m, ct);
        var alDia = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(20), 400m, ct);

        var hoyVencida = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy, 500m, ct);
        var pagada = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(-30), 600m, ct);
        await AyudaCuentas.CobrarAsync(admin, escenario, pagada, 600m, escenario.Hoy, ct);

        var vista = await AyudaCuentas.VistaAsync(
            admin, $"clienteId={escenario.ClienteId:D}&vencidas=true", ct);
        var filas = AyudaCuentas.Filas(vista);

        filas.Should().ContainSingle(f => f.GetProperty("id").GetGuid() == vencida);
        AyudaCuentas.Contiene(vista, alDia).Should().BeFalse();
        AyudaCuentas.Contiene(vista, pagada).Should().BeFalse("una factura pagada no está vencida aunque su fecha sí");
        AyudaCuentas.Contiene(vista, hoyVencida).Should().BeFalse("vence al final del día de su vencimiento");

        var fila = filas.Single(f => f.GetProperty("id").GetGuid() == vencida);
        fila.GetProperty("vencida").GetBoolean().Should().BeTrue();
        fila.GetProperty("diasVencida").GetInt32().Should().Be(20);
    }

    /// <summary>
    /// Las modificadas son las que F3-08 dejó con <c>modificada_at</c>. Una factura normal no entra y
    /// una modificada sí.
    /// </summary>
    [Fact]
    public async Task LasModificadasTraenSoloLasQueSeTocaron()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var normal = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);
        var tocada = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(11), 400m, ct);

        await admin.PutAsync(
            $"/api/v1/facturas/{tocada:D}/modificar",
            new
            {
                lineas = new[]
                {
                    new
                    {
                        lineaId = await LineaIdAsync(admin, tocada, ct),
                        tipo = nameof(TipoLineaFactura.Renta),
                        concepto = "Renta del mes, revisada",
                        cantidad = 1m,
                        precioUnitario = 450m,
                        tipoItbisId = (Guid?)null,
                        tipoRetencionId = (Guid?)null,
                    },
                },
            },
            ct);

        var vista = await AyudaCuentas.VistaAsync(
            admin, $"clienteId={escenario.ClienteId:D}&modificadas=true", ct);
        var filas = AyudaCuentas.Filas(vista);

        filas.Should().ContainSingle(f => f.GetProperty("id").GetGuid() == tocada);
        AyudaCuentas.Contiene(vista, normal).Should().BeFalse();
        filas[0].GetProperty("modificada").GetBoolean().Should().BeTrue();
        filas[0].GetProperty("total").LeerDecimal().Should().Be(450m, "y con el importe nuevo");
    }

    /// <summary>Las fianzas entran por defecto y se pueden quitar.</summary>
    [Fact]
    public async Task LasFianzasEntranPorDefectoYSePuedenQuitar()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var fianzaId = await AyudaCuentas.FianzaAsync(admin, escenario, 500m, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        var porDefecto = await AyudaCuentas.VistaAsync(admin, $"clienteId={escenario.ClienteId:D}", ct);
        AyudaCuentas.Contiene(porDefecto, fianzaId)
            .Should().BeTrue("§3.11: la fianza es visible en la vista de cobros");

        var sinFianzas = await AyudaCuentas.VistaAsync(
            admin, $"clienteId={escenario.ClienteId:D}&incluirFianzas=false", ct);
        AyudaCuentas.Contiene(sinFianzas, fianzaId).Should().BeFalse();
    }

    /// <summary>
    /// Los datos que se devuelven de vuelta son los que se filtraron. La interfaz pinta los controles
    /// con esto y no tiene por qué reconstruirlos, y sobre todo no puede teach un filtro distinto del
    /// que se aplicó.
    /// </summary>
    [Fact]
    public async Task LaRespuestaDevuelveElFiltroQueSeAplico()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);

        var vista = await AyudaCuentas.VistaAsync(
            admin,
            $"clienteId={escenario.ClienteId:D}&moneda={nameof(Moneda.USD)}&anio={escenario.Hoy.Year}",
            ct);

        var filtro = vista.GetProperty("filtro");

        filtro.GetProperty("clienteId").GetGuid().Should().Be(escenario.ClienteId);
        filtro.GetProperty("moneda").GetString().Should().Be(nameof(Moneda.USD));
        filtro.GetProperty("anio").GetInt32().Should().Be(escenario.Hoy.Year);
        filtro.GetProperty("incluirProformas").GetBoolean().Should().BeFalse();
        filtro.GetProperty("incluirFianzas").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// Ordenar por un campo que no existe es un 400, no una lista ordenada por nada. Es el mismo
    /// criterio que en el resto de la API: una errata que se ignora en silencio es una lista que
    /// nadie sabe por qué sale como sale.
    /// </summary>
    [Fact]
    public async Task OrdenarPorUnCampoQueNoExisteEsErrorYNoUnaListaSinCriterio()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var leida = await admin.GetAsync("/api/v1/cuentas?sortBy=pendiente", ct);

        leida.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Identificador de la línea de renta de una factura emitida, para poder modificarla.</summary>
    private static async Task<Guid> LineaIdAsync(SesionCliente sesion, Guid facturaId, CancellationToken ct)
    {
        var leida = await sesion.GetAsync($"/api/v1/facturas/{facturaId:D}", ct);
        leida.StatusCode.Should().Be(HttpStatusCode.OK, await leida.Cuerpo());

        return (await leida.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("lineas")
            .EnumerateArray()
            .First()
            .GetProperty("id")
            .GetGuid();
    }
}