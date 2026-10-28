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

namespace AtlasRent.Tests.Cuentas;

/// <summary>
/// F3-10: el cobro desde la misma pantalla (§3.13 «seleccionar filas → panel lateral»).
/// <para>
/// La simulación de F3-06 gana un campo opcional con los identificadores de las filas marcadas. Con
/// ellos, la propuesta es para esas facturas y <b>no propone ninguna otra</b>; sin ellos, sigue siendo
/// la FIFO de siempre. No hayendpoint nuevo ni una segunda simulación: es el mismo servicio con otro
/// punto de entrada, y por eso el panel y el registro no pueden dar dos respuestas distintas.
/// </para>
/// <para>
/// Y la simulación sigue sin escribir nada: es lo que se ve antes de confirmar, y una propuesta que
/// cobra dinero es un cobro.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class CuentaPanelCobroTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    /// <summary>
    /// Con dos facturas del contrato y una marcada, la propuesta es para la marcada y solo para ella,
    /// aunque la otra sea más antigua y sea la que la FIFO habría elegido primero.
    /// </summary>
    [Fact]
    public async Task ConFilasMarcadasLaSimulacionProponeEsasYNingunaMas()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var antigua = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(-30), 300m, ct);
        var marcada = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 400m, ct);

        var formaPagoId = await AyudaCobros.FormaPagoAsync(admin, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(admin, Moneda.USD, ct);

        var simulacion = await SimularAsync(
            admin,
            AyudaCobros.Peticion(
                escenario.ClienteId,
                escenario.ContratoId,
                escenario.Hoy,
                Moneda.USD,
                400m,
                formaPagoId,
                cuentaId,
                facturasSeleccionadas: new[] { marcada }),
            ct);

        var propuestas = Propuestas(simulacion);
        propuestas.Should().ContainSingle();
        propuestas[0].GetProperty("facturaId").GetGuid().Should().Be(marcada);
        propuestas[0].GetProperty("importe").LeerDecimal().Should().Be(400m, "su pendiente entero");
        propuestas[0].GetProperty("pendienteAntes").LeerDecimal().Should().Be(400m);

        var ids = Propuestas(simulacion)
            .Select(p => p.GetProperty("facturaId").GetGuid())
            .ToArray();

        ids.Should().NotContain(antigua, "la FIFO no decide cuando quien cobra ha marcado filas");
    }

    /// <summary>
    /// Sin marcar, la propuesta es la FIFO de siempre: la más antigua primero. Es el comportamiento
    /// de F3-06 y no puede cambiar por añadirle un campo opcional.
    /// </summary>
    [Fact]
    public async Task SinFilasMarcadasLaSimulacionProponeLaFifoDeSiempre()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var antigua = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(-30), 300m, ct);
        var reciente = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 400m, ct);

        var formaPagoId = await AyudaCobros.FormaPagoAsync(admin, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(admin, Moneda.USD, ct);

        var simulacion = await SimularAsync(
            admin,
            AyudaCobros.Peticion(
                escenario.ClienteId,
                escenario.ContratoId,
                escenario.Hoy,
                Moneda.USD,
                300m,
                formaPagoId,
                cuentaId),
            ct);

        var propuestas = Propuestas(simulacion);

        propuestas.Should().ContainSingle("el importe solo cubre la factura más antigua");
        propuestas[0].GetProperty("facturaId").GetGuid().Should().Be(antigua);
        propuestas[0].GetProperty("importe").LeerDecimal().Should().Be(300m);
        propuestas[0].GetProperty("facturaId").GetGuid().Should().NotBe(reciente);
    }

    /// <summary>
    /// Marcar más facturas de las que cubre el importe deja la primera marcada con lo que hay y las
    /// siguientes sin propuesta: el recorte va en el orden de la selección, y lo que sobra es
    /// excedente, como en cualquier otro cobro.
    /// </summary>
    [Fact]
    public async Task MarcarMasFacturasDeLasQueCubreElImporteRecortaLaUltimaYDejaExcedente()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var primera = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(-30), 300m, ct);
        var segunda = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 400m, ct);

        var formaPagoId = await AyudaCobros.FormaPagoAsync(admin, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(admin, Moneda.USD, ct);

        var simulacion = await SimularAsync(
            admin,
            AyudaCobros.Peticion(
                escenario.ClienteId,
                escenario.ContratoId,
                escenario.Hoy,
                Moneda.USD,
                500m,
                formaPagoId,
                cuentaId,
                facturasSeleccionadas: new[] { primera, segunda }),
            ct);

        var propuestas = Propuestas(simulacion);

        propuestas.Should().HaveCount(2);
        propuestas[0].GetProperty("facturaId").GetGuid().Should().Be(primera);
        propuestas[0].GetProperty("importe").LeerDecimal().Should().Be(300m, "entera");
        propuestas[1].GetProperty("facturaId").GetGuid().Should().Be(segunda);
        propuestas[1].GetProperty("importe").LeerDecimal().Should().Be(200m, "lo que queda del cobro");

        simulacion.GetProperty("totalAsignado").LeerDecimal().Should().Be(500m);
        simulacion.GetProperty("excedente").LeerDecimal().Should().Be(0m);
    }

    /// <summary>
    /// Una factura de otro contrato es un 409 que lo dice, no una propuesta en silencio que se
    /// descuente de otro sitio.
    /// </summary>
    [Fact]
    public async Task MarcarUnaFacturaDeOtroContratoEsConflicto()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        var otro = await AyudaCobros.ContratoAsync(admin, bd, ct);
        var ajena = await AyudaCobros.EmitidaAsync(
            admin, otro.ClienteId, otro.ContratoId,
            await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct),
            Moneda.USD, escenario.Hoy.AddDays(10), 300m, ct);

        var formaPagoId = await AyudaCobros.FormaPagoAsync(admin, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(admin, Moneda.USD, ct);

        var respuesta = await admin.PostAsync(
            "/api/v1/cobros/simular",
            AyudaCobros.Peticion(
                escenario.ClienteId,
                escenario.ContratoId,
                escenario.Hoy,
                Moneda.USD,
                300m,
                formaPagoId,
                cuentaId,
                facturasSeleccionadas: new[] { ajena }),
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>La simulación no escribe nada: ni cobro ni asignación.</summary>
    [Fact]
    public async Task LaSimulacionDelPanelNoEscribeNada()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaId = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        var formaPagoId = await AyudaCobros.FormaPagoAsync(admin, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(admin, Moneda.USD, ct);

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var cobrosAntes = await contexto.Cobro.AsNoTracking().CountAsync(ct);

            await SimularAsync(
                admin,
                AyudaCobros.Peticion(
                    escenario.ClienteId,
                    escenario.ContratoId,
                    escenario.Hoy,
                    Moneda.USD,
                    300m,
                    formaPagoId,
                    cuentaId,
                    facturasSeleccionadas: new[] { facturaId }),
                ct);

            (await contexto.Cobro.AsNoTracking().CountAsync(ct))
                .Should().Be(cobrosAntes, "una propuesta no es un cobro");
            (await contexto.CobroAsignacion.AsNoTracking().CountAsync(a => a.FacturaId == facturaId, ct))
                .Should().Be(0, "ni deja el dinero aplicado a la factura");
        }
    }

    /// <summary>El empleado puede ver la vista y no puede cobrar desde ella.</summary>
    [Fact]
    public async Task ElEmpleadoNoPuedeCobrarDesdeLaVista()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);
        using var empleado = await AyudaCuentas.SesionAsync(bd, api, Roles.Empleado, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaId = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        var formaPagoId = await AyudaCobros.FormaPagoAsync(admin, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(admin, Moneda.USD, ct);

        var cuerpo = AyudaCobros.Peticion(
            escenario.ClienteId,
            escenario.ContratoId,
            escenario.Hoy,
            Moneda.USD,
            300m,
            formaPagoId,
            cuentaId,
            facturasSeleccionadas: new[] { facturaId });

        (await empleado.PostAsync("/api/v1/cobros/simular", cuerpo, ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        (await empleado.PostAsync("/api/v1/cobros", cuerpo, ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Cobro.AsNoTracking().CountAsync(c => c.ClienteId == escenario.ClienteId, ct))
            .Should().Be(0, "un 403 no registra nada");
    }

    // ---------------------------------------------------------------- apoyo

    private static async Task<JsonElement> SimularAsync(SesionCliente sesion, object cuerpo, CancellationToken ct)
    {
        var respuesta = await sesion.PostAsync("/api/v1/cobros/simular", cuerpo, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());

        return await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    private static JsonElement[] Propuestas(JsonElement simulacion) =>
        [.. simulacion.GetProperty("asignaciones").EnumerateArray()];
}