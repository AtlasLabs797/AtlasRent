using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cobros;
using AtlasRent.Tests.Fianzas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Cuentas;

/// <summary>
/// F3-10: los totales al pie (docs/ESPECIFICACION.md §3.13) son de gerente y administrador, y el
/// criterio de aceptación de la tarea es literalmente «Empleado no recibe totales por API».
/// <para>
/// Lo que se comprueba no es solo que el empleado no los reciba, sino que los reciba <b>vacíos</b>:
/// <c>totales</c> en <c>null</c>, no un objeto con ceros. Cero es un total —«se ha cobrado 0,00»— y
/// un total que el empleado no puede ver tiene que ser un nulo, porque con ceros se puede sumar,
/// imprimir y archivar.
/// </para>
/// <para>
/// Y lo que se comprueba del otro lado es que los totales <b>cuadren con lo que hay en pantalla</b>:
/// la prueba suma las columnas de las filas que ve y las compara con el pie. Cada prueba crea su
/// cliente y filtra por él, así que la página trae todo lo que ha creado y la suma es la que haría
/// una persona.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class CuentaTotalesTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    /// <summary>
    /// El empleado ve la lista y no ve el pie. Con filas —que es lo que sí le corresponde— y con
    /// <c>totales</c> nulo.
    /// </summary>
    [Fact]
    public async Task ElEmpleadoRecibeLasFilasYNoRecibeTotales()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);
        using var empleado = await AyudaCuentas.SesionAsync(bd, api, Roles.Empleado, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);
        await AyudaCuentas.FianzaAsync(admin, escenario, 500m, ct);

        var vista = await AyudaCuentas.VistaAsync(empleado, $"clienteId={escenario.ClienteId:D}", ct);

        var filas = AyudaCuentas.Filas(vista);
        filas.Should().HaveCount(2, "el empleado ve la misma tabla que el gerente");
        filas.Select(f => f.GetProperty("tipo").GetString()).Should().Contain(["Factura", "Fianza"]);

        vista.GetProperty("totales").ValueKind
            .Should().Be(System.Text.Json.JsonValueKind.Null, "§3.13: los totales son de gerente y admin");
    }

    /// <summary>
    /// Gerente y administrador reciben los totales, y por moneda. Con una sola moneda en el pie no se
    /// vería si el desglose es real o un número suelto: por eso esta prueba mira además que el pie
    /// venga en una lista y con la moneda dentro.
    /// </summary>
    [Fact]
    public async Task ElGerenteYElAdministradorRecibenLosTotalesPorMoneda()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);
        using var gerente = await AyudaCuentas.SesionAsync(bd, api, Roles.Gerente, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        foreach (var sesion in new[] { admin, gerente })
        {
            var vista = await AyudaCuentas.VistaAsync(sesion, $"clienteId={escenario.ClienteId:D}", ct);

            var totales = vista.GetProperty("totales");
            totales.ValueKind.Should().Be(JsonValueKind.Array);

            var porMoneda = totales.EnumerateArray().ToArray();
            porMoneda.Should().ContainSingle(t => t.GetProperty("moneda").GetString() == nameof(Moneda.USD));
            porMoneda[0].GetProperty("total").GetDecimal().Should().Be(300m);
        }
    }

    /// <summary>
    /// Los totales cuadran con lo que se ve en pantalla. Se cobra la factura a medias y se mira el pie:
    /// el total es lo de las dos filas, lo cobrado es el dinero de verdad y el pendiente es lo que
    /// falta —la factura por un lado y lo que se le exige a la fianza por otro—.
    /// </summary>
    [Fact]
    public async Task LosTotalesCuadranConLoQueHayEnPantalla()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaId = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);
        await AyudaCuentas.FianzaAsync(admin, escenario, 500m, ct);

        await AyudaCuentas.CobrarAsync(admin, escenario, facturaId, 120m, escenario.Hoy, ct);

        var vista = await AyudaCuentas.VistaAsync(admin, $"clienteId={escenario.ClienteId:D}", ct);
        var filas = AyudaCuentas.Filas(vista);

        filas.Should().HaveCount(2);

        // El pie tiene que ser exactamente lo que alguien suma en la tabla.
        var totales = vista.GetProperty("totales").EnumerateArray().ToArray();
        totales.Should().ContainSingle();

        totales[0].GetProperty("moneda").GetString().Should().Be(nameof(Moneda.USD));
        totales[0].GetProperty("total").GetDecimal()
            .Should().Be(AyudaCuentas.SumaDeFilas(filas, "total"), "300 de factura más 500 exigidos");
        totales[0].GetProperty("retencion").GetDecimal()
            .Should().Be(AyudaCuentas.SumaDeFilas(filas, "retencion"))
            .And.Be(0m, "una fianza no retiene");
        totales[0].GetProperty("cobrado").GetDecimal()
            .Should().Be(AyudaCuentas.SumaDeFilas(filas, "cobrado"))
            .And.Be(120m, "solo hay dinero cobrado de verdad");
        totales[0].GetProperty("pendiente").GetDecimal()
            .Should().Be(AyudaCuentas.SumaDeFilas(filas, "pendiente"))
            .And.Be(680m, "180 de la factura y los 500 que se le exigen a la fianza");
    }

    /// <summary>
    /// Los totales se agrupan por moneda y <b>no se convierten</b>: consolidar en dólares es de
    /// F5-01 con el cambio congelado de cada documento (D9), y aquí cada moneda se suma consigo misma.
    /// </summary>
    [Fact]
    public async Task LosTotalesVanSeparadosPorMonedaYSinConvertir()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);

        // Emitir en pesos necesita el cambio del día, único por día y moneda en la base compartida.
        await AyudaCobros.CargarCambioAsync(
            admin, escenario.Hoy, Moneda.DOP, AyudaCobros.CambioDelDia, ct);

        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct, Moneda.USD);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(11), 9_000m, ct, Moneda.DOP);

        var vista = await AyudaCuentas.VistaAsync(admin, $"clienteId={escenario.ClienteId:D}", ct);
        var totales = vista.GetProperty("totales").EnumerateArray().ToArray();

        totales.Should().HaveCount(2, "no se puede consolidar en una moneda sin el tipo de cambio de cada documento");

        var usd = totales.Single(t => t.GetProperty("moneda").GetString() == nameof(Moneda.USD));
        var dop = totales.Single(t => t.GetProperty("moneda").GetString() == nameof(Moneda.DOP));

        usd.GetProperty("total").GetDecimal().Should().Be(300m);
        dop.GetProperty("total").GetDecimal().Should().Be(9_000m);
    }

    /// <summary>
    /// La fianza se lee con las dos cuentas distintas que §3.13 pide: lo que falta por exigir y lo que
    /// queda por devolver. Una fianza recién dada de alta tiene la primera en verde y la segunda en
    /// cero, y confundirlas daría un saldo que no existe.
    /// </summary>
    [Fact]
    public async Task LaFianzaMuestraLoQueFaltaPorExigirYElSaldoADevolverPorSeparado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var fianzaId = await AyudaCuentas.FianzaAsync(admin, escenario, 500m, ct);

        var fila = AyudaCuentas.FilaDe(
            await AyudaCuentas.VistaAsync(admin, $"clienteId={escenario.ClienteId:D}", ct), "Fianza");

        fila.GetProperty("id").GetGuid().Should().Be(fianzaId);
        fila.GetProperty("total").GetDecimal().Should().Be(500m, "el total de una fianza es lo exigido");
        fila.GetProperty("cobrado").GetDecimal().Should().Be(0m);
        fila.GetProperty("pendiente").GetDecimal().Should().Be(500m, "lo que falta por exigir");
        fila.GetProperty("saldoADevolver").GetDecimal().Should().Be(0m, "no se ha cobrado nada que devolver");
        fila.GetProperty("estado").GetString().Should().Be(nameof(EstadoFianza.Pendiente));
        fila.GetProperty("contratoId").GetGuid().Should().Be(escenario.ContratoId);

        // Las cifras son las de la base, no las que dice el DTO: es la misma regla que en F3-09.
        var cifras = await AyudaFianzas.CifrasAsync(bd, fianzaId, ct);
        cifras.Cobrado.Should().Be(0m);
        cifras.Saldo.Should().Be(0m);
    }
}