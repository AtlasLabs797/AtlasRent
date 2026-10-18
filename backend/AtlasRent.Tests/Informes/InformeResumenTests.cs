using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using AtlasRent.Tests.CicloFactura;
using AtlasRent.Tests.Cobros;
using AtlasRent.Tests.Contratos;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Fianzas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AtlasRent.Tests.Retenciones;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Informes;

/// <summary>
/// F5-01: los informes de §3.15.
/// <para>
/// <b>La prueba de cuadre es el criterio de aceptación de la tarea</b> y lo que justifica que los bloques
/// se calculen sobre las filas de la vista única en vez de con una fórmula paralela: si el informe y la
/// tabla se calculan con la misma cuenta, cuadran por construcción, y la prueba lo comprueba sobre
/// números que salen de la API de verdad.
/// </para>
/// <para>
/// Cada prueba trae su cliente y filtra por él, porque la base es única y la comparten todas.
/// </para>
/// <para>
/// La instancia es <see cref="ApiTestFactoryBloqueos"/> a propósito. La fábrica da una IP por clase y
/// el límite de SEG-002 son 20 peticiones de login por minuto y por IP; con MFA un login son cuatro
/// (entrar, activar, confirmar y verificar). Con más de cinco pruebas por clase la clase se come el
/// límite y falla por <c>429</c> sin que nada esté roto: estas pruebas miden el informe, no el límite.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class InformeResumenTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    /// <summary>
    /// El cuadre. Una factura emitida a medias, una proforma y una fianza cobrada a medias, y el
    /// informe dice exactamente lo que dice el pie de la tabla.
    /// <para>
    /// Lo que se compara es dinero con dinero y no «casi dinero»: el <c>total</c> de la vista son las
    /// facturas más lo exigido de las fianzas, así que el facturado del informe —que son las facturas,
    /// y solo las que suman a facturado— se compara con la suma de las filas de tipo factura, que es la
    /// misma cifra que ve una persona arriba en la tabla.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ElResumenCuadraConLosTotalesDeLaVistaUnica()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaId = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);
        await AyudaCuentas.ProformaAsync(admin, escenario, 200m, ct);
        var fianzaId = await AyudaCuentas.FianzaAsync(admin, escenario, 500m, ct);

        await AyudaCuentas.CobrarAsync(admin, escenario, facturaId, 120m, escenario.Hoy, ct);
        await AyudaFianzas.CobradaAsync(
            admin, escenario.ClienteId, escenario.ContratoId, fianzaId, 200m, Moneda.USD, ct);

        var consulta = $"clienteId={escenario.ClienteId:D}";
        var vista = await AyudaCuentas.VistaAsync(admin, $"{consulta}&incluirProformas=true", ct);
        var filas = AyudaCuentas.Filas(vista);
        var totales = vista.GetProperty("totales").EnumerateArray()
            .Single(t => t.GetProperty("moneda").GetString() == nameof(Moneda.USD));

        var resumen = await AyudaInformes.ResumenAsync(admin, consulta, ct);

        var facturas = filas.Where(f => f.GetProperty("tipo").GetString() == nameof(TipoFilaCuenta.Factura));

        // El pie primero: que la vista se cuadre consigo misma es lo que hace que la comparación de
        // abajo signifique algo.
        totales.GetProperty("total").GetDecimal()
            .Should().Be(AyudaCuentas.SumaDeFilas(filas, "total"))
            .And.Be(1_000m, "300 de factura, 500 exigidos de fianza y los 200 de la proforma");

        resumen.Bloque("facturado").Usd()
            .Should().Be(AyudaCuentas.SumaDeFilas(facturas, "total"))
            .And.Be(300m, "la proforma no es facturación: va en su propio bloque");

        resumen.Bloque("proformado").Usd().Should().Be(200m);

        resumen.Bloque("cobrado").GetProperty("facturas").Usd().Should().Be(120m);
        resumen.Bloque("cobrado").GetProperty("fianzas").Usd().Should().Be(200m);
        resumen.Bloque("cobrado").GetProperty("saldoAFavor").Usd().Should().Be(0m);

        resumen.Bloque("cobrado").GetProperty("total").Usd()
            .Should().Be(totales.GetProperty("cobrado").GetDecimal())
            .And.Be(320m, "el mismo dinero de verdad que cuenta el pie de la vista");

        resumen.Bloque("pendiente").GetProperty("total").Usd()
            .Should().Be(totales.GetProperty("pendiente").GetDecimal())
            .And.Be(480m, "180 de la factura y los 500 − 200 que faltan por exigir de la fianza");

        resumen.GetProperty("documentosSinTipoCambioCongelado").GetInt32()
            .Should().Be(0, "todo es en dólares y el par no hace falta");
    }

    /// <summary>
    /// El cuadre con filtros, que es donde se rompe si el informe se calcula aparte: un filtro que se
    /// aplica a un lado y no al otro da dos informes distintos del mismo conjunto de documentos.
    /// </summary>
    [Fact]
    public async Task ElResumenCuadraConLaVistaConFiltros()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaId = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);
        var otraId = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(20), 700m, ct);
        await AyudaCuentas.FianzaAsync(admin, escenario, 500m, ct);

        await AyudaCuentas.CobrarAsync(admin, escenario, facturaId, 120m, escenario.Hoy, ct);
        await AyudaCuentas.CobrarAsync(admin, escenario, otraId, 700m, escenario.Hoy, ct);

        var consulta = $"clienteId={escenario.ClienteId:D}&serieId={escenario.SerieId:D}";
        var vista = await AyudaCuentas.VistaAsync(admin, consulta, ct);
        var filas = AyudaCuentas.Filas(vista);
        var totales = vista.GetProperty("totales").EnumerateArray()
            .Single(t => t.GetProperty("moneda").GetString() == nameof(Moneda.USD));

        var resumen = await AyudaInformes.ResumenAsync(admin, consulta, ct);

        filas.Should().HaveCount(2, "con filtro de serie las fianzas quedan fuera, que es su regla");
        totales.GetProperty("total").GetDecimal().Should().Be(AyudaCuentas.SumaDeFilas(filas, "total"));

        resumen.Bloque("facturado").Usd()
            .Should().Be(AyudaCuentas.SumaDeFilas(filas, "total"))
            .And.Be(totales.GetProperty("total").GetDecimal(), "sin fianzas, facturado y total son lo mismo");

        resumen.Bloque("cobrado").GetProperty("total").Usd()
            .Should().Be(totales.GetProperty("cobrado").GetDecimal())
            .And.Be(820m);

        resumen.Bloque("pendiente").GetProperty("total").Usd()
            .Should().Be(totales.GetProperty("pendiente").GetDecimal())
            .And.Be(180m, "solo la primera factura se quedó a medias");

        resumen.Bloque("pendiente").GetProperty("fianzas").Usd()
            .Should().Be(0m, "con filtro de serie la fianza ni está");
    }

    /// <summary>
    /// El consolidado en dólares sale del cambio congelado del documento. Con los datos de hoy lo que se
    /// puede comprobar es el <b>signo</b> de la conversión, que es el error posible: el cambio congelado
    /// es cuántos pesos vale un dólar (<c>ServicioCobros.cs:1194</c> multiplica en el sentido
    /// contrario), así que consolidar es dividir. Con los mismos 6.000 pesos, dividir da 99,77 dólares y
    /// multiplicar daría 360.733,44.
    /// </summary>
    [Fact]
    public async Task ElConsolidadoUsaElCambioCongeladoDelDocumento()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        await AyudaCobros.CargarCambioAsync(admin, hoy, Moneda.DOP, AyudaCobros.CambioDelDia, ct);

        var pesos = await AyudaCuentas.EscenarioAsync(admin, bd, ct, monedaSerie: Moneda.DOP);
        await AyudaCuentas.EmitidaAsync(admin, pesos, pesos.Hoy.AddDays(10), 6_000m, ct, moneda: Moneda.DOP);

        var dolares = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, dolares, dolares.Hoy.AddDays(10), 100m, ct, moneda: Moneda.USD);

        var enPesos = await AyudaInformes.ResumenAsync(admin, $"clienteId={pesos.ClienteId:D}", ct);
        var enDolares = await AyudaInformes.ResumenAsync(admin, $"clienteId={dolares.ClienteId:D}", ct);

        var esperado = Math.Round(6_000m / AyudaCobros.CambioDelDia, 2, MidpointRounding.AwayFromZero);

        enPesos.Bloque("facturado").Dop().Should().Be(6_000m, "los pesos se enseñan tal cual");
        enPesos.Bloque("facturado").Consolidado()
            .Should().Be(esperado, "6.000 pesos al cambio congelado del día de la emisión");
        enPesos.GetProperty("documentosSinTipoCambioCongelado").GetInt32()
            .Should().Be(0, "la factura congeló su cambio al emitirse");

        // En dólares el par no existe y el importe ya es el consolidado.
        enDolares.Bloque("facturado").Consolidado().Should().Be(100m);

        // Y el gráfico dice lo mismo que el bloque: son dos caminos al mismo número.
        AyudaInformes.SerieDe(enPesos, hoy.ToString("yyyy-MM"), "facturadoUsd").Should().Be(esperado);
    }

    /// <summary>
    /// Vencido: solo el pendiente con el vencimiento pasado en la fecha de corte. La factura que vence
    /// dentro de un mes no entra aunque tenga el mismo importe y el mismo cliente.
    /// </summary>
    [Fact]
    public async Task VencidoSoloCuentaLoQueTieneVencimientoSuperado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(-5), 300m, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(30), 800m, ct);

        var consulta = $"clienteId={escenario.ClienteId:D}&hasta={escenario.Hoy:yyyy-MM-dd}";
        var resumen = await AyudaInformes.ResumenAsync(admin, consulta, ct);

        resumen.Bloque("pendiente").GetProperty("facturas").Usd()
            .Should().Be(1_100m, "las dos están pendientes");
        resumen.Bloque("vencido").Usd()
            .Should().Be(300m, "solo la que venció hace cinco días");
        resumen.GetProperty("fechaCorte").GetString().Should().Be(escenario.Hoy.ToString("yyyy-MM-dd"));
    }

    /// <summary>
    /// Fianzas retenidas (§3.15): una cobrada y no devuelta cuenta por lo que la empresa tiene en la
    /// mano, que es el saldo a devolver de la fila —cobrado menos devuelto— y no lo que falta por exigir.
    /// Son dos preguntas distintas (§3.11) y un informe que las mezclara diría que se deben 300 donde hay
    /// 200 en el cajón. Al devolverlo, el saldo es cero y deja de contar.
    /// </summary>
    [Fact]
    public async Task LasFianzasRetenidasSonLasCobradasYNoDevueltas()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var fianzaId = await AyudaCuentas.FianzaAsync(admin, escenario, 500m, ct);

        await AyudaFianzas.CobradaAsync(
            admin, escenario.ClienteId, escenario.ContratoId, fianzaId, 200m, Moneda.USD, ct);

        var consulta = $"clienteId={escenario.ClienteId:D}";
        var conSaldo = await AyudaInformes.ResumenAsync(admin, consulta, ct);

        conSaldo.Bloque("fianzasRetenidas").Usd()
            .Should().Be(200m, "lo cobrado y no devuelto, no lo que falta por exigir");

        conSaldo.Bloque("pendiente").GetProperty("fianzas").Usd()
            .Should().Be(300m, "lo que falta por exigir es otra columna y son los 500 exigidos");

        var devuelta = await admin.PostAsync(
            $"/api/v1/fianzas/{fianzaId:D}/devolver",
            AyudaFianzas.PeticionDevolucion(200m),
            ct);
        devuelta.StatusCode.Should().Be(HttpStatusCode.OK, await devuelta.Cuerpo());

        (await AyudaInformes.ResumenAsync(admin, consulta, ct)).Bloque("fianzasRetenidas").Usd()
            .Should().Be(0m, "una fianza devuelta no está retenida: su saldo es cero");
    }

    /// <summary>
    /// El rango de emisión acota el informe y la vista única. Los dos se comprueban juntos porque uno sin
    /// el otro valdría poco: un informe que acota sobre una vista que no acota no cuadra con la pantalla
    /// que el gerente tiene delante.
    /// </summary>
    [Fact]
    public async Task ElRangoDeEmisionAcotaElInformeYLaVista()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        var delDia =
            $"clienteId={escenario.ClienteId:D}&desde={escenario.Hoy:yyyy-MM-dd}&hasta={escenario.Hoy:yyyy-MM-dd}";
        var deManana = $"clienteId={escenario.ClienteId:D}&desde={escenario.Hoy.AddDays(1):yyyy-MM-dd}";

        (await AyudaCuentas.VistaAsync(admin, delDia, ct)).GetProperty("total").GetInt32()
            .Should().Be(1, "el día de la emisión entra en el rango");
        (await AyudaCuentas.VistaAsync(admin, deManana, ct)).GetProperty("total").GetInt32()
            .Should().Be(0, "una factura emitida hoy no cae en un rango que empieza mañana");

        (await AyudaInformes.ResumenAsync(admin, delDia, ct)).Bloque("facturado").Usd().Should().Be(300m);
        (await AyudaInformes.ResumenAsync(admin, deManana, ct)).Bloque("facturado").Usd().Should().Be(0m);
    }

    /// <summary>
    /// Los filtros de unidad que añadió F5-01 a la vista: el inmueble leen el de la unidad de la
    /// factura. Un filtro que se acepta y no acota sería peor que uno que no existe, porque devuelve
    /// filas que parecen filtradas.
    /// </summary>
    [Fact]
    public async Task LosFiltrosDeUnidadAcotanLaVistaUnica()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        var inmuebleId = await InmuebleDeAsync(escenario.UnidadId, ct);
        var consulta = $"clienteId={escenario.ClienteId:D}";
        var otro = $"{consulta}&inmuebleId={Guid.NewGuid():D}";

        (await AyudaCuentas.VistaAsync(admin, $"{consulta}&inmuebleId={inmuebleId:D}", ct))
            .GetProperty("total").GetInt32().Should().Be(1);

        (await AyudaCuentas.VistaAsync(admin, otro, ct))
            .GetProperty("total").GetInt32().Should().Be(0, "otro inmueble no devuelve filas de esta unidad");

        (await AyudaInformes.ResumenAsync(admin, $"{consulta}&inmuebleId={inmuebleId:D}", ct))
            .Bloque("facturado").Usd().Should().Be(300m);

        (await AyudaInformes.ResumenAsync(admin, otro, ct))
            .Bloque("facturado").Usd().Should().Be(0m);
    }

    /// <summary>
    /// El filtro por propietaria: dos unidades de dueñas distintas y cada filtro devuelve solo las
    /// filas de la suya. Sin esto, un <c>propietariaId</c> que se aceptara y no acotara devolvería
    /// la tabla entera y nadie se enteraría.
    /// </summary>
    [Fact]
    public async Task ElFiltroDePropietariaAcotaLaVistaYElInforme()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var duena = await AyudaContrato.SubempresaAsync(bd, ct, propietaria: true);
        var otraDuena = await AyudaContrato.SubempresaAsync(bd, ct, propietaria: true);

        var clienteId = await AyudaFacturas.CrearClienteAsync(admin, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);

        var (_, unidadDeLaDuena) = await AyudaReserva.InmuebleYUnidadAsync(
            admin, ct, propietariaUnidadId: duena);
        var (_, unidadDeLaOtra) = await AyudaReserva.InmuebleYUnidadAsync(
            admin, ct, propietariaUnidadId: otraDuena);

        var deLaDuena = await AyudaCobros.EmitidaAsync(
            admin, clienteId, null, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct, unidadDeLaDuena);
        var deLaOtra = await AyudaCobros.EmitidaAsync(
            admin, clienteId, null, serieId, Moneda.USD, hoy.AddDays(20), 700m, ct, unidadDeLaOtra);

        var consulta = $"clienteId={clienteId:D}";
        var suyas = await AyudaCuentas.VistaAsync(admin, $"{consulta}&propietariaId={duena:D}", ct);
        var deLaOtraVista = await AyudaCuentas.VistaAsync(admin, $"{consulta}&propietariaId={otraDuena:D}", ct);

        suyas.GetProperty("total").GetInt32()
            .Should().Be(1, "una de las dos facturas es de la otra dueña");
        AyudaCuentas.Contiene(suyas, deLaDuena).Should().BeTrue();
        AyudaCuentas.Contiene(suyas, deLaOtra).Should().BeFalse("es de otra dueña");

        deLaOtraVista.GetProperty("total").GetInt32().Should().Be(1);
        AyudaCuentas.Contiene(deLaOtraVista, deLaOtra).Should().BeTrue();
        AyudaCuentas.Contiene(deLaOtraVista, deLaDuena).Should().BeFalse();

        (await AyudaInformes.ResumenAsync(admin, $"{consulta}&propietariaId={duena:D}", ct))
            .Bloque("facturado").Usd().Should().Be(300m, "el informe cuadra con las mismas filas");

        (await AyudaInformes.ResumenAsync(admin, $"{consulta}&propietariaId={otraDuena:D}", ct))
            .Bloque("facturado").Usd().Should().Be(700m);
    }

    /// <summary>
    /// La herencia de §3.1, que es la regla con contenido detrás de este filtro: una unidad sin
    /// <c>propietaria_id</c> propio es de la dueña del inmueble. Sin la rama que la respeta, filtrar
    /// por la dueña del edificio dejaría fuera los alquileres de todas sus unidades, y la prueba
    /// se cae.
    /// <para>
    /// El estado se monta como lo montaría el producto: el inmueble nace sin dueña y se le pone una
    /// después de que la unidad exista, de modo que la unidad se queda con <c>propietaria_id</c> a
    /// nulo. Antes de afirmar nada se comprueba en la base que el estado es ese: si mañana el alta o
    /// el cambio del inmueble empezaran a copiar la dueña a las unidades, esta prueba tiene que
    /// decirlo, porque a partir de ahí dejaría de estar probando la herencia.
    /// </para>
    /// <para>
    /// <b>Omitida durante el arreglo de BUG-016 y hoy activa.</b> La herencia está en la documentación
    /// (§3.1) y en el comentario del servicio, pero <c>ServicioCuentas</c> correlacionaba el inmueble
    /// con <c>i.Id == f.UnidadId</c> —el identificador de la <b>unidad</b>— en vez de con
    /// <c>unidad.inmueble_id</c>, así que el EXISTS nunca encontraba el edificio y el filtro por dueña
    /// devolvía cero filas para toda unidad sin <c>propietaria_id</c> propio. Corregido a
    /// <c>f.Unidad!.InmuebleId</c>; el filtro del dashboard ya usaba esa misma forma y no estaba afectado.
    /// </summary>
    [Fact]
    public async Task LaUnidadSinPropietariaPropiaSaleAlFiltrarPorLaDelInmueble()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var duenaDelEdificio = await AyudaContrato.SubempresaAsync(bd, ct, propietaria: true);
        var otraDuena = await AyudaContrato.SubempresaAsync(bd, ct, propietaria: true);

        var clienteId = await AyudaFacturas.CrearClienteAsync(admin, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);

        var (inmuebleId, unidadHeredera) = await AyudaReserva.InmuebleYUnidadAsync(admin, ct);
        var (_, unidadAjena) = await AyudaReserva.InmuebleYUnidadAsync(
            admin, ct, propietariaUnidadId: otraDuena);

        // El edificio cambia de dueña después de que la unidad ya exista. La unidad no se toca, así
        // que se queda sin `propietaria_id` y es de la nueva por herencia (§3.1).
        await using (var escritura = bd.CrearContexto(bd.CadenaSuperusuario))
        {
            await escritura.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE inmueble SET propietaria_id = {duenaDelEdificio} WHERE id = {inmuebleId}", ct);
        }

        var heredera = await AyudaCobros.EmitidaAsync(
            admin, clienteId, null, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct, unidadHeredera);
        var ajena = await AyudaCobros.EmitidaAsync(
            admin, clienteId, null, serieId, Moneda.USD, hoy.AddDays(20), 700m, ct, unidadAjena);

        // La precondición, leída de la base: sin esto la prueba podría estar comprobando el caso
        // fácil (unidad con dueña propia) y decirlo como si fuera el de la herencia.
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Unidad.AsNoTracking()
            .Where(u => u.Id == unidadHeredera)
            .Select(u => u.PropietariaId)
            .SingleAsync(ct)).Should().BeNull("la unidad no tiene dueña propia");
        (await contexto.Inmueble.AsNoTracking()
            .Where(i => i.Id == inmuebleId)
            .Select(i => i.PropietariaId)
            .SingleAsync(ct)).Should().Be(duenaDelEdificio);

        var consulta = $"clienteId={clienteId:D}";
        var delEdificio = await AyudaCuentas.VistaAsync(
            admin, $"{consulta}&propietariaId={duenaDelEdificio:D}", ct);

        delEdificio.GetProperty("total").GetInt32()
            .Should().Be(1, "la factura de la unidad sin dueña propia es de la dueña del edificio");
        AyudaCuentas.Contiene(delEdificio, heredera).Should().BeTrue("esta es la herencia de §3.1");
        AyudaCuentas.Contiene(delEdificio, ajena).Should().BeFalse("y esa es de otra dueña");

        (await AyudaInformes.ResumenAsync(admin, $"{consulta}&propietariaId={duenaDelEdificio:D}", ct))
            .Bloque("facturado").Usd()
            .Should().Be(300m, "el informe ve la misma fila que la vista");

        (await AyudaInformes.ResumenAsync(admin, $"{consulta}&propietariaId={otraDuena:D}", ct))
            .Bloque("facturado").Usd().Should().Be(700m);
    }

    /// <summary>
    /// El filtro por facturadora, que es el otro de los tres filtros de unidad de F5-01 y el único
    /// que no tiene herencia: mira solo <c>unidad.facturadora_por_defecto_id</c>.
    /// </summary>
    [Fact]
    public async Task ElFiltroDeFacturadoraAcotaLaVistaYElInforme()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var facturadora = await AyudaContrato.SubempresaAsync(bd, ct);
        var otraFacturadora = await AyudaContrato.SubempresaAsync(bd, ct);

        var clienteId = await AyudaFacturas.CrearClienteAsync(admin, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);

        var (_, unidadFacturadora) = await AyudaReserva.InmuebleYUnidadAsync(
            admin, ct, facturadoraId: facturadora);
        var (_, unidadOtra) = await AyudaReserva.InmuebleYUnidadAsync(
            admin, ct, facturadoraId: otraFacturadora);

        var deLaFacturadora = await AyudaCobros.EmitidaAsync(
            admin, clienteId, null, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct, unidadFacturadora);
        var deLaOtra = await AyudaCobros.EmitidaAsync(
            admin, clienteId, null, serieId, Moneda.USD, hoy.AddDays(20), 700m, ct, unidadOtra);

        var consulta = $"clienteId={clienteId:D}";
        var suyas = await AyudaCuentas.VistaAsync(admin, $"{consulta}&facturadoraId={facturadora:D}", ct);
        var deLaOtraVista = await AyudaCuentas.VistaAsync(
            admin, $"{consulta}&facturadoraId={otraFacturadora:D}", ct);

        suyas.GetProperty("total").GetInt32()
            .Should().Be(1, "una de las dos facturas se factura por la otra empresa");
        AyudaCuentas.Contiene(suyas, deLaFacturadora).Should().BeTrue();
        AyudaCuentas.Contiene(suyas, deLaOtra).Should().BeFalse("factura otra empresa");

        deLaOtraVista.GetProperty("total").GetInt32().Should().Be(1);
        AyudaCuentas.Contiene(deLaOtraVista, deLaOtra).Should().BeTrue();
        AyudaCuentas.Contiene(deLaOtraVista, deLaFacturadora).Should().BeFalse();

        (await AyudaInformes.ResumenAsync(admin, $"{consulta}&facturadoraId={facturadora:D}", ct))
            .Bloque("facturado").Usd().Should().Be(300m, "el informe cuadra con las mismas filas");

        (await AyudaInformes.ResumenAsync(admin, $"{consulta}&facturadoraId={otraFacturadora:D}", ct))
            .Bloque("facturado").Usd().Should().Be(700m);
    }

    /// <summary>
    /// El dashboard: los indicadores de ocupación del día y los mismos bloques del resumen.
    /// <para>
    /// El recorrido del contrato va entero hasta <c>Activo</c> porque un contrato en borrador no bloquea
    /// la unidad (§3.5), y un dashboard que dijera que la unidad de un borrador está libre sería correcto
    /// y no diría nada.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ElDashboardTraeLosIndicadoresYLosBloquesDelResumen()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var clienteId = await AyudaReserva.ClienteAsync(admin, ct);
        var unidadId = await AyudaReserva.UnidadAsync(admin, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);

        var contratoId = await AyudaContrato.CrearAsync(
            admin,
            AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, inicio: hoy, moneda: "USD"),
            ct);

        await AyudaContrato.AceptarAsync(admin, contratoId, "solicitud", new { }, ct);
        await AyudaContrato.AceptarAsync(admin, contratoId, "aprobacion", null, ct);
        await AyudaContrato.AceptarAsync(admin, contratoId, "activacion", null, ct);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        await AyudaCobros.EmitidaAsync(
            admin, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct, unidadId);

        var consulta = $"clienteId={clienteId:D}&unidadId={unidadId:D}&fecha={hoy:yyyy-MM-dd}";
        var dashboard = await AyudaInformes.DashboardAsync(admin, consulta, ct);

        AyudaInformes.Fecha(dashboard.GetProperty("fecha")).Should().Be(hoy);

        var unidades = dashboard.GetProperty("unidades");
        unidades.GetProperty("ocupadas").GetInt32()
            .Should().Be(1, "un contrato activo bloquea su unidad ese día (§3.4 y §3.5)");
        unidades.GetProperty("reservadas").GetInt32().Should().Be(0);
        unidades.GetProperty("mantenimiento").GetInt32().Should().Be(0);
        unidades.GetProperty("disponibles").GetInt32()
            .Should().Be(0, "la unidad de un contrato activo no está libre");

        dashboard.GetProperty("contratosActivos").GetInt32().Should().Be(1);

        dashboard.GetProperty("resumen").Bloque("facturado").Usd().Should().Be(300m);
    }

    /// <summary>
    /// Retenido: los dos subbloques de §3.15, que sin ninguna aserción eran de los ocho bloques los
    /// únicos que nadie miraba. Una factura retenida con el comprobante ya recibido y otra sin él: si
    /// mañana alguien multiplica la retención por dos, o invierte el criterio de qué va con comprobante,
    /// esta prueba se cae.
    /// </summary>
    [Fact]
    public async Task ElRetenidoSeparaLoQueTieneComprobanteDeLoQueNo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var tipoRetencion = await AyudaRetenciones.CrearTipoAsync(admin, BaseRetencion.Total, 10m, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);

        // 2.000 de base al 10 %: 200 de retención, y el comprobante sin llegar.
        var sinPapel = await AyudaRetenciones.BorradorConRetencionAsync(
            admin, tipoRetencion, ct, precioUnitario: 2_000m);
        await AyudaRetenciones.EmitirAsync(admin, sinPapel, ct);
        var clienteSinPapel = (await AyudaCiclo.FilaAsync(bd, sinPapel, ct)).ClienteId;

        var conPapel = await AyudaRetenciones.BorradorConRetencionAsync(
            admin, tipoRetencion, ct, precioUnitario: 1_000m);
        await AyudaRetenciones.EmitirAsync(admin, conPapel, ct);
        var clienteConPapel = (await AyudaCiclo.FilaAsync(bd, conPapel, ct)).ClienteId;

        var comprobante = await AyudaRetenciones.ComprobanteAsync(bd, conPapel, ct);
        var recibida = await admin.PutAsync(
            $"/api/v1/comprobantes-retencion/{comprobante!.Id:D}/recibir",
            new { numero = "607-PRUEBA-INFORME", fechaRecepcion = $"{hoy:yyyy-MM-dd}" },
            ct);
        recibida.StatusCode.Should().Be(HttpStatusCode.OK, await recibida.Cuerpo());

        var pendiente = (await AyudaInformes.ResumenAsync(admin, $"clienteId={clienteSinPapel:D}", ct))
            .Bloque("retenido");

        pendiente.GetProperty("sinComprobante").Usd().Should().Be(200m, "el 10 % de los 2.000");
        pendiente.GetProperty("conComprobante").Usd().Should().Be(0m);
        pendiente.GetProperty("total").Usd().Should().Be(200m, "y el total es la suma de los dos");

        var conPapelBloque = (await AyudaInformes.ResumenAsync(admin, $"clienteId={clienteConPapel:D}", ct))
            .Bloque("retenido");

        conPapelBloque.GetProperty("conComprobante").Usd().Should().Be(100m, "el 10 % de los 1.000");
        conPapelBloque.GetProperty("sinComprobante").Usd().Should().Be(0m);
        conPapelBloque.GetProperty("total").Usd().Should().Be(100m);
    }

    /// <summary>
    /// Diferencias de cambio: otro bloque de §3.15 sin una sola aserción. El ajuste por diferencia de
    /// cambio (F3-07) es lo que separa este bloque del cobrado, así que la prueba comprueba las dos
    /// cosas a la vez: que el ajuste sale en su bloque y que <b>no</b> engorda el cobrado, porque no
    /// es dinero que entró en la caja.
    /// </summary>
    [Fact]
    public async Task LasDiferenciasDeCambioVanEnSuBloqueYNoEnElCobrado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);

        await using var limpieza = AyudaCobros.SinRastroDeCambio(bd, hoy, Moneda.DOP);
        await AyudaCobros.CargarCambioAsync(admin, hoy, Moneda.DOP, AyudaCobros.CambioDelDia, ct);
        await using var tolerancia = await AyudaCobros.ToleranciaTemporal(admin, 1.00m, null, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaId = await AyudaCuentas.EmitidaAsync(
            admin, escenario, escenario.Hoy.AddDays(10), 1_000m, ct);

        var formaPagoId = await AyudaCobros.FormaPagoAsync(admin, ct);
        var cuentaPesos = await AyudaCobros.CuentaAsync(admin, Moneda.DOP, ct);

        // 59.455 DOP a 59,50 son 999,24 dólares de los 1.000: quedan 0,76, que cabe en la tolerancia.
        var cobro = await admin.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                escenario.ClienteId,
                escenario.ContratoId,
                hoy,
                Moneda.DOP,
                59_455m,
                formaPagoId,
                cuentaPesos,
                tipoCambioAplicado: AyudaCobros.CambioExacto),
            ct);
        cobro.StatusCode.Should().Be(HttpStatusCode.OK, await cobro.Cuerpo());

        var ajuste = await admin.PostAsync(
            "/api/v1/cobros/ajustes-diferencia-cambio",
            new
            {
                facturaId,
                tipoCambioValor = AyudaCobros.CambioExacto,
                motivo = "Diferencia de 0,76 por el cambio del banco",
            },
            ct);
        ajuste.StatusCode.Should().Be(HttpStatusCode.OK, await ajuste.Cuerpo());

        var resumen = await AyudaInformes.ResumenAsync(admin, $"clienteId={escenario.ClienteId:D}", ct);

        resumen.Bloque("diferenciasCambio").Usd()
            .Should().Be(0.76m, "el importe es el pendiente real que cerró el ajuste");

        resumen.Bloque("cobrado").GetProperty("facturas").Usd()
            .Should().Be(999.24m, "el ajuste cierra saldo: no es dinero que entró en la caja");

        (await AyudaCobros.AjustesAsync(bd, facturaId, ct)).Should().ContainSingle();
    }

    /// <summary>
    /// §3.15: «Facturado = facturas emitidas <b>no anuladas</b>». El filtro es del informe, no de la
    /// vista —la vista única enseña también las anuladas— así que sin esta prueba una factura anulada
    /// podría estar sumando facturación y la suite seguiría verde.
    /// </summary>
    [Fact]
    public async Task ElFacturadoNoCuentaLasFacturasAnuladas()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);
        var anuladaId = await AyudaCuentas.EmitidaAsync(
            admin, escenario, escenario.Hoy.AddDays(10), 700m, ct);

        await AyudaCiclo.AnularAsync(admin, anuladaId, "Se emitió por error", ct);

        var consulta = $"clienteId={escenario.ClienteId:D}";
        var filas = AyudaCuentas.Filas(await AyudaCuentas.VistaAsync(admin, consulta, ct));

        filas.Where(f => f.GetProperty("tipo").GetString() == nameof(TipoFilaCuenta.Factura))
            .Should().HaveCount(2, "la vista única las enseña las dos: el filtro es del informe");

        var resumen = await AyudaInformes.ResumenAsync(admin, consulta, ct);

        resumen.Bloque("facturado").Usd()
            .Should().Be(300m, "la anulada no es facturación, aunque su número siga ocupado (D28)");
        resumen.Bloque("pendiente").GetProperty("total").Usd()
            .Should().Be(300m, "y tampoco es deuda pendiente");
    }

    /// <summary>
    /// §3.15: el otro medio del mismo filtro, «de series que <b>suman a facturado</b>» (§3.7). Una
    /// serie con <c>sumaAFacturado=false</c> emite facturas como cualquier otra y sale en la vista
    /// única; que no cuenten como facturación lo decide el informe.
    /// </summary>
    [Fact]
    public async Task ElFacturadoIgnoraLasSeriesQueNoSumanAFacturado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        var serieSinFacturado = await AyudaFacturas.CrearSerieAsync(
            admin, TipoSerie.Normal, Moneda.USD, ct, sumaAFacturado: false);

        await AyudaCobros.EmitidaAsync(
            admin,
            escenario.ClienteId,
            escenario.ContratoId,
            serieSinFacturado,
            Moneda.USD,
            escenario.Hoy.AddDays(10),
            700m,
            ct,
            escenario.UnidadId);

        var consulta = $"clienteId={escenario.ClienteId:D}";
        var filas = AyudaCuentas.Filas(await AyudaCuentas.VistaAsync(admin, consulta, ct));

        filas.Where(f => f.GetProperty("tipo").GetString() == nameof(TipoFilaCuenta.Factura))
            .Should().HaveCount(2, "la vista única no filtra por la bandera de la serie: eso lo pone el informe");

        var resumen = await AyudaInformes.ResumenAsync(admin, consulta, ct);

        resumen.Bloque("facturado").Usd()
            .Should().Be(300m, "la serie que no suma a facturado no entra en el facturado");
        resumen.Bloque("pendiente").GetProperty("facturas").Usd()
            .Should().Be(1_000m, "pero su deuda sí es deuda: solo deja de ser facturación");
    }

    /// <summary>
    /// El saldo a favor no tiene fecha de negocio (<c>saldo_a_favor</c> es un saldo por contrato y
    /// moneda, no un movimiento), así que con rango de fechas no puede sumar al cobrado total: lo
    /// único honesto es dejarlo fuera y decirlo. Sin rango, el informe es del histórico entero y ahí sí
    /// es la foto del momento.
    /// </summary>
    [Fact]
    public async Task ElSaldoAFavorNoSumaAlCobradoTotalCuandoElFiltroTieneFechas()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        // Se pagan 400 por una factura de 300 y no se dicen a quién: los 100 de más los deja la
        // aplicación como crédito del contrato (§3.10.1), que es justo lo que se quiere medir.
        var formaPagoId = await AyudaCobros.FormaPagoAsync(admin, ct);
        var cuenta = await AyudaCobros.CuentaAsync(admin, Moneda.USD, ct);

        var cobro = await admin.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                escenario.ClienteId,
                escenario.ContratoId,
                escenario.Hoy,
                Moneda.USD,
                400m,
                formaPagoId,
                cuenta),
            ct);
        cobro.StatusCode.Should().Be(HttpStatusCode.OK, await cobro.Cuerpo());

        (await AyudaCobros.SaldoAsync(bd, escenario.ContratoId, Moneda.USD, ct))
            .Should().Be(100m, "el crédito existe, no es una hipótesis de la prueba");

        var consulta = $"clienteId={escenario.ClienteId:D}";
        var sinRango = await AyudaInformes.ResumenAsync(admin, consulta, ct);

        sinRango.Bloque("cobrado").GetProperty("saldoAFavor").Usd().Should().Be(100m);
        sinRango.Bloque("cobrado").GetProperty("total").Usd()
            .Should().Be(400m, "sin rango, el saldo es la foto del momento y cuenta");
        sinRango.GetProperty("saldoAFavorFueraDePeriodo").GetBoolean()
            .Should().BeFalse("no hay nada fuera del periodo");

        var delDia = $"{consulta}&desde={escenario.Hoy:yyyy-MM-dd}&hasta={escenario.Hoy:yyyy-MM-dd}";
        var conRango = await AyudaInformes.ResumenAsync(admin, delDia, ct);

        conRango.Bloque("cobrado").GetProperty("saldoAFavor").Usd()
            .Should().Be(100m, "el crédito se sigue enseñando: ocultarlo no sería una solución");
        conRango.Bloque("cobrado").GetProperty("total").Usd()
            .Should().Be(300m, "con rango, el total es lo cobrado del periodo y nada más");
        conRango.GetProperty("saldoAFavorFueraDePeriodo").GetBoolean()
            .Should().BeTrue("y la respuesta dice que el saldo quedó fuera");
    }

    private async Task<Guid> InmuebleDeAsync(Guid unidadId, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        return await contexto.Unidad.AsNoTracking()
            .Where(u => u.Id == unidadId)
            .Select(u => u.InmuebleId)
            .SingleAsync(ct);
    }
}
