using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Catalogos;

/// <summary>
/// Catálogos de solo lectura para elegir (series, formas de pago, cuentas receptoras, subempresas) y los
/// parámetros del registro de cobros. Los lee el gerente sin pasar por <c>configuracion.editar</c>, que
/// sigue siendo del administrador.
/// <para>
/// Cada endpoint exige una sola acción: la mínima que necesita quien va a elegir.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class CatalogosApiTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = AyudaFacturas.Contrasena;

    // ---------------------------------------------------------------- series

    [Fact]
    public async Task SeriesSoloActivasYGerenteYEmpleadoLasLeen()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        var activa = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var inactiva = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        (await admin.PutAsync($"/api/v1/config/series/{inactiva:D}", await CuerpoSerieInactivaAsync(admin, inactiva, ct), ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        foreach (var rol in new[] { Roles.Gerente, Roles.Empleado })
        {
            using var sesion = await AyudaFacturas.SesionAsync(bd, api, rol, ct);
            var ids = await IdsAsync(sesion, "/api/v1/catalogos/series", ct);

            ids.Should().Contain(activa, $"el {rol} elige serie al facturar");
            ids.Should().NotContain(inactiva, "una serie inactiva no emite");
        }
    }

    [Fact]
    public async Task SeriesDevuelveSoloLoNecesarioParaElegir()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await AyudaFacturas.SesionAsync(bd, api, Roles.Gerente, ct);

        var fila = (await Datos(gerente, "/api/v1/catalogos/series", ct)).First();
        var campos = fila.EnumerateObject().Select(p => p.Name).ToList();

        campos.Should().Contain(["id", "numero", "prefijo", "moneda", "tipo"]);
        campos.Should().NotContain(["siguiente", "plantillaPdf", "condicionesPago"]);
    }

    [Fact]
    public async Task SeriesConElPermisoRecortadoDa403()
    {
        var ct = TestContext.Current.CancellationToken;
        // La acción es factura.borrador: quien no puede ni crear un borrador no elige serie.
        using var sesion = await GerenteRecortadoAsync(Acciones.FacturaBorrador, ct);

        (await sesion.GetAsync("/api/v1/catalogos/series", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------- formas de pago

    [Fact]
    public async Task FormasPagoSoloActivasGerente200Empleado403()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        var activa = await CrearAsync(admin, "/api/v1/config/formas-pago", new { nombre = $"FP {Guid.NewGuid():N}"[..16], activa = true }, ct);
        var inactiva = await CrearAsync(admin, "/api/v1/config/formas-pago", new { nombre = $"FP {Guid.NewGuid():N}"[..16], activa = false }, ct);

        using var gerente = await AyudaFacturas.SesionAsync(bd, api, Roles.Gerente, ct);
        var ids = await IdsAsync(gerente, "/api/v1/catalogos/formas-pago", ct);
        ids.Should().Contain(activa);
        ids.Should().NotContain(inactiva);

        using var empleado = await AyudaFacturas.SesionAsync(bd, api, Roles.Empleado, ct);
        (await empleado.GetAsync("/api/v1/catalogos/formas-pago", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LosCatalogosDeCobroConElPermisoRecortadoDan403()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await GerenteRecortadoAsync(Acciones.CobroRegistrar, ct);

        (await sesion.GetAsync("/api/v1/catalogos/formas-pago", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sesion.GetAsync("/api/v1/catalogos/cuentas-receptoras", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sesion.GetAsync("/api/v1/catalogos/parametros-cobro", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------- cuentas receptoras

    [Fact]
    public async Task CuentasReceptorasSoloActivasConMonedaGerente200Empleado403()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        var activa = await CrearAsync(admin, "/api/v1/config/cuentas", new { nombre = $"Cta {Guid.NewGuid():N}"[..16], banco = "Banco X", moneda = "DOP", activa = true }, ct);
        var inactiva = await CrearAsync(admin, "/api/v1/config/cuentas", new { nombre = $"Cta {Guid.NewGuid():N}"[..16], moneda = "USD", activa = false }, ct);

        using var gerente = await AyudaFacturas.SesionAsync(bd, api, Roles.Gerente, ct);
        var filas = await Datos(gerente, "/api/v1/catalogos/cuentas-receptoras", ct);
        filas.Select(f => f.GetProperty("id").GetGuid()).Should().Contain(activa).And.NotContain(inactiva);

        var propia = filas.Single(f => f.GetProperty("id").GetGuid() == activa);
        propia.GetProperty("moneda").GetString().Should().Be("DOP");

        using var empleado = await AyudaFacturas.SesionAsync(bd, api, Roles.Empleado, ct);
        (await empleado.GetAsync("/api/v1/catalogos/cuentas-receptoras", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------- subempresas

    [Fact]
    public async Task SubempresasSoloActivasSinRncNiDatosDeContacto()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        var activa = await CrearAsync(admin, "/api/v1/config/subempresas",
            new { nombre = $"Sub {Guid.NewGuid():N}"[..16], rnc = "101010101", propietaria = true, facturadora = true, activa = true }, ct);
        var inactiva = await CrearAsync(admin, "/api/v1/config/subempresas",
            new { nombre = $"Sub {Guid.NewGuid():N}"[..16], propietaria = true, activa = false }, ct);

        // factura.ver lo tienen gerente y empleado.
        foreach (var rol in new[] { Roles.Gerente, Roles.Empleado })
        {
            using var sesion = await AyudaFacturas.SesionAsync(bd, api, rol, ct);
            var filas = await Datos(sesion, "/api/v1/catalogos/subempresas", ct);

            filas.Select(f => f.GetProperty("id").GetGuid()).Should().Contain(activa).And.NotContain(inactiva);
            filas.First().EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["id", "nombre", "propietaria", "facturadora"]);
        }
    }

    [Fact]
    public async Task SubempresasConElPermisoRecortadoDa403()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await GerenteRecortadoAsync(Acciones.FacturaVer, ct);

        (await sesion.GetAsync("/api/v1/catalogos/subempresas", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------- parámetros de cobro

    [Fact]
    public async Task ParametrosDeCobroDevuelveLaToleranciaConfigurada()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        (await admin.PutAsync(
            "/api/v1/config/ajustes",
            new { toleranciaCambioUsd = 2.5m, toleranciaCambioDop = 60m, modoRecordatorios = "Manual" },
            ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var gerente = await AyudaFacturas.SesionAsync(bd, api, Roles.Gerente, ct);
        var respuesta = await gerente.GetAsync("/api/v1/catalogos/parametros-cobro", ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);

        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        Decimal(cuerpo.GetProperty("toleranciaCambioUsd")).Should().Be(2.5m);
        Decimal(cuerpo.GetProperty("toleranciaCambioDop")).Should().Be(60m);
        cuerpo.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(["toleranciaCambioUsd", "toleranciaCambioDop"], "no se cuela el resto de ajustes");

        using var empleado = await AyudaFacturas.SesionAsync(bd, api, Roles.Empleado, ct);
        (await empleado.GetAsync("/api/v1/catalogos/parametros-cobro", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ParametrosDeCobroSinToleranciaDevuelveNulos()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        (await admin.PutAsync(
            "/api/v1/config/ajustes",
            new { toleranciaCambioUsd = (decimal?)null, toleranciaCambioDop = (decimal?)null, modoRecordatorios = "Manual" },
            ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Lo lee el gerente, que es quien registra cobros.
        using var gerente = await AyudaFacturas.SesionAsync(bd, api, Roles.Gerente, ct);
        var cuerpo = await (await gerente.GetAsync("/api/v1/catalogos/parametros-cobro", ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);

        cuerpo.GetProperty("toleranciaCambioUsd").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ---------------------------------------------------------------- recordatorios

    [Fact]
    public async Task ModoDeRecordatoriosLoLeeMensajeriaMasivoPeroNoElEmpleado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        (await admin.PutAsync(
            "/api/v1/config/ajustes",
            new { toleranciaCambioUsd = (decimal?)null, toleranciaCambioDop = (decimal?)null, modoRecordatorios = "Automatico" },
            ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var gerente = await AyudaFacturas.SesionAsync(bd, api, Roles.Gerente, ct);
        var respuesta = await gerente.GetAsync("/api/v1/recordatorios/modo", ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("modo").GetString()
            .Should().Be("Automatico");

        using var empleado = await AyudaFacturas.SesionAsync(bd, api, Roles.Empleado, ct);
        (await empleado.GetAsync("/api/v1/recordatorios/modo", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var recortado = await GerenteRecortadoAsync(Acciones.MensajeriaMasivo, ct);
        (await recortado.GetAsync("/api/v1/recordatorios/modo", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------- apoyo

    private async Task<SesionCliente> GerenteRecortadoAsync(string accion, CancellationToken ct)
    {
        var gerente = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);

        using var sesionAdmin = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);
        (await sesionAdmin.PutAsync(
            $"/api/v1/usuarios/{gerente.Id:D}/permisos",
            new { accion, permitido = false },
            ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        return await AyudaPermisos.SesionAsync(api, gerente, Contrasena, ct);
    }

    private static async Task<object> CuerpoSerieInactivaAsync(SesionCliente admin, Guid id, CancellationToken ct)
    {
        var lista = await Datos(admin, "/api/v1/config/series?pageSize=200", ct);
        var s = lista.Single(f => f.GetProperty("id").GetGuid() == id);

        return new
        {
            numero = s.GetProperty("numero").GetInt32(),
            prefijo = s.GetProperty("prefijo").GetString(),
            tipo = s.GetProperty("tipo").GetString(),
            moneda = s.GetProperty("moneda").GetString(),
            sumaAFacturado = s.GetProperty("sumaAFacturado").GetBoolean(),
            sumaASaldos = s.GetProperty("sumaASaldos").GetBoolean(),
            generaComprobante = s.GetProperty("generaComprobante").GetBoolean(),
            activa = false,
        };
    }

    private static async Task<Guid> CrearAsync(SesionCliente sesion, string ruta, object cuerpo, CancellationToken ct)
    {
        var creada = await sesion.PostAsync(ruta, cuerpo, ct);
        creada.StatusCode.Should().Be(HttpStatusCode.OK, await creada.Content.ReadAsStringAsync(ct));

        return (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    private static async Task<List<JsonElement>> Datos(SesionCliente sesion, string ruta, CancellationToken ct)
    {
        var respuesta = await sesion.GetAsync(ruta, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));

        return [.. (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("data").EnumerateArray()];
    }

    private static async Task<List<Guid>> IdsAsync(SesionCliente sesion, string ruta, CancellationToken ct) =>
        [.. (await Datos(sesion, ruta, ct)).Select(f => f.GetProperty("id").GetGuid())];

    private static decimal Decimal(JsonElement valor) =>
        valor.ValueKind == JsonValueKind.String
            ? decimal.Parse(valor.GetString()!, CultureInfo.InvariantCulture)
            : valor.GetDecimal();
}
