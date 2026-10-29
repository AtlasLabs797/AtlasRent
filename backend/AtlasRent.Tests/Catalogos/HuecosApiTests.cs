using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Fianzas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AtlasRent.Tests.Retenciones;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Catalogos;

/// <summary>
/// Huecos de la API que la interfaz tocaba: los impuestos de un borrador sin pasar por configuración,
/// las marcas de las subempresas, prospectos inactivos o convertidos, y el cliente, la unidad y el
/// inmueble por su nombre en fianzas y unidades.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class HuecosApiTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = AyudaFacturas.Contrasena;

    // ---------------------------------------------------------------- impuestos

    [Fact]
    public async Task TiposItbisSoloActivosConCamposMinimosGerenteYEmpleado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        var activo = await CrearAsync(admin, "/api/v1/config/tipos-itbis",
            new { nombre = $"ITBIS {Guid.NewGuid():N}"[..20], porcentaje = 18m, activo = true }, ct);
        var inactivo = await CrearAsync(admin, "/api/v1/config/tipos-itbis",
            new { nombre = $"ITBIS {Guid.NewGuid():N}"[..20], porcentaje = 16m, activo = false }, ct);

        foreach (var rol in new[] { Roles.Gerente, Roles.Empleado })
        {
            using var sesion = await AyudaFacturas.SesionAsync(bd, api, rol, ct);
            var filas = await Datos(sesion, "/api/v1/catalogos/tipos-itbis", ct);

            filas.Select(f => f.GetProperty("id").GetGuid()).Should().Contain(activo).And.NotContain(inactivo);
            var propia = filas.Single(f => f.GetProperty("id").GetGuid() == activo);
            Decimal(propia.GetProperty("porcentaje")).Should().Be(18m);
            propia.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["id", "nombre", "porcentaje"]);
        }
    }

    [Fact]
    public async Task TiposRetencionSoloActivosGerenteYEmpleado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        var activo = await AyudaRetenciones.CrearTipoAsync(admin, BaseRetencion.Total, 10m, ct);
        var inactivo = await CrearAsync(admin, "/api/v1/config/tipos-retencion",
            new { nombre = $"Ret {Guid.NewGuid():N}"[..20], porcentaje = 5m, @base = "Subtotal", esDefecto = false, activo = false }, ct);

        foreach (var rol in new[] { Roles.Gerente, Roles.Empleado })
        {
            using var sesion = await AyudaFacturas.SesionAsync(bd, api, rol, ct);
            var filas = await Datos(sesion, "/api/v1/catalogos/tipos-retencion", ct);

            filas.Select(f => f.GetProperty("id").GetGuid()).Should().Contain(activo).And.NotContain(inactivo);
            var propia = filas.Single(f => f.GetProperty("id").GetGuid() == activo);
            Decimal(propia.GetProperty("porcentaje")).Should().Be(10m);
            propia.GetProperty("base").GetString().Should().Be("Total");
            propia.EnumerateObject().Select(p => p.Name)
                .Should().BeEquivalentTo(["id", "nombre", "porcentaje", "base", "esDefecto"]);
        }
    }

    [Fact]
    public async Task LosCatalogosDeImpuestosConElPermisoRecortadoDan403()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await GerenteRecortadoAsync(Acciones.FacturaBorrador, ct);

        (await sesion.GetAsync("/api/v1/catalogos/tipos-itbis", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sesion.GetAsync("/api/v1/catalogos/tipos-retencion", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SubempresasTraenPropietariaYFacturadora()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        var soloPropietaria = await CrearAsync(admin, "/api/v1/config/subempresas",
            new { nombre = $"Sub {Guid.NewGuid():N}"[..16], propietaria = true, facturadora = false, activa = true }, ct);
        var soloFacturadora = await CrearAsync(admin, "/api/v1/config/subempresas",
            new { nombre = $"Sub {Guid.NewGuid():N}"[..16], propietaria = false, facturadora = true, activa = true }, ct);

        using var gerente = await AyudaFacturas.SesionAsync(bd, api, Roles.Gerente, ct);
        var filas = await Datos(gerente, "/api/v1/catalogos/subempresas", ct);

        var a = filas.Single(f => f.GetProperty("id").GetGuid() == soloPropietaria);
        a.GetProperty("propietaria").GetBoolean().Should().BeTrue();
        a.GetProperty("facturadora").GetBoolean().Should().BeFalse();
        var b = filas.Single(f => f.GetProperty("id").GetGuid() == soloFacturadora);
        b.GetProperty("propietaria").GetBoolean().Should().BeFalse();
        b.GetProperty("facturadora").GetBoolean().Should().BeTrue();
        a.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["id", "nombre", "propietaria", "facturadora"]);
    }

    // ---------------------------------------------------------------- prospectos

    [Theory]
    [InlineData(Roles.Empleado)]
    [InlineData(Roles.Gerente)]
    [InlineData(Roles.Admin)]
    public async Task ProspectosIncluirInactivosTraeInactivosYConvertidos(string rol)
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(rol, ct);
        var vivo = await AyudaReserva.ProspectoAsync(sesion, ct);
        var inactivo = await AyudaReserva.ProspectoAsync(sesion, ct);
        var convertido = await AyudaReserva.ProspectoAsync(sesion, ct);
        (await sesion.DeleteAsync($"/api/v1/prospectos/{inactivo:D}", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var conversion = await sesion.PostAsync(
            $"/api/v1/prospectos/{convertido:D}/convertir",
            new { nombre = $"Conv {Guid.NewGuid():N}"[..16], tipo = "PersonaFisica", documento = $"001-{Guid.NewGuid():N}"[..13] },
            ct);
        conversion.StatusCode.Should().Be(HttpStatusCode.OK, await conversion.Content.ReadAsStringAsync(ct));
        var clienteId = (await conversion.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();

        var porDefecto = (await Datos(sesion, "/api/v1/prospectos?pageSize=200", ct))
            .Select(f => f.GetProperty("id").GetGuid()).ToList();
        porDefecto.Should().Contain(vivo).And.NotContain([inactivo, convertido]);

        var todos = await Datos(sesion, "/api/v1/prospectos?pageSize=200&incluirInactivos=true", ct);
        todos.Select(f => f.GetProperty("id").GetGuid()).Should().Contain([vivo, inactivo, convertido]);
        todos.Single(f => f.GetProperty("id").GetGuid() == convertido).GetProperty("clienteId").GetGuid()
            .Should().Be(clienteId);
        todos.Single(f => f.GetProperty("id").GetGuid() == inactivo).GetProperty("activo").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ProspectosIncluirInactivosConElPermisoRecortadoDa403()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await GerenteRecortadoAsync(Acciones.ClienteVer, ct);

        (await sesion.GetAsync("/api/v1/prospectos?incluirInactivos=true", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------- fianzas

    [Fact]
    public async Task LaFianzaTraeElNombreDelClienteYDeLaUnidadEnLaListaYElDetalle()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, 1_000m, Moneda.USD, 1, ct);

        var cliente = await Leer(admin, $"/api/v1/clientes/{clienteId:D}", ct);
        var contrato = await Leer(admin, $"/api/v1/contratos/{contratoId:D}", ct);
        var unidadId = contrato.GetProperty("unidadId").GetGuid();
        var unidad = await Leer(admin, $"/api/v1/inmuebles/unidades/{unidadId:D}", ct);

        foreach (var rol in new[] { Roles.Gerente, Roles.Empleado })
        {
            using var sesion = await AyudaFacturas.SesionAsync(bd, api, rol, ct);
            var lista = await Datos(sesion, $"/api/v1/fianzas?contratoId={contratoId:D}", ct);

            var fila = lista.Should().ContainSingle().Which;
            fila.GetProperty("id").GetGuid().Should().Be(fianzaId);
            fila.GetProperty("clienteNombre").GetString().Should().Be(cliente.GetProperty("nombre").GetString());
            fila.GetProperty("clienteId").GetGuid().Should().Be(clienteId);
            fila.GetProperty("unidadNombre").GetString().Should().Be(unidad.GetProperty("nombre").GetString());
            fila.GetProperty("unidadId").GetGuid().Should().Be(unidadId);
        }

        var detalle = await Leer(admin, $"/api/v1/fianzas/{fianzaId:D}", ct);
        detalle.GetProperty("fianza").GetProperty("clienteNombre").GetString()
            .Should().Be(cliente.GetProperty("nombre").GetString());
    }

    // ---------------------------------------------------------------- unidades

    [Theory]
    [InlineData(Roles.Empleado)]
    [InlineData(Roles.Gerente)]
    [InlineData(Roles.Admin)]
    public async Task LaUnidadTraeElNombreDelInmuebleEnListasFichaYEscrituras(string rol)
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(rol, ct);
        var (inmuebleId, unidadId) = await AyudaReserva.InmuebleYUnidadAsync(sesion, ct);
        var nombre = (await Leer(sesion, $"/api/v1/inmuebles/{inmuebleId:D}", ct)).GetProperty("nombre").GetString();

        (await Leer(sesion, $"/api/v1/inmuebles/unidades/{unidadId:D}", ct))
            .GetProperty("inmuebleNombre").GetString().Should().Be(nombre);

        var global = await Datos(sesion, $"/api/v1/inmuebles/unidades?inmuebleId={inmuebleId:D}", ct);
        global.Should().ContainSingle().Which.GetProperty("inmuebleNombre").GetString().Should().Be(nombre);

        var delInmueble = await Datos(sesion, $"/api/v1/inmuebles/{inmuebleId:D}/unidades", ct);
        delInmueble.Should().ContainSingle().Which.GetProperty("inmuebleNombre").GetString().Should().Be(nombre);

        var desactivada = await sesion.DeleteAsync($"/api/v1/inmuebles/unidades/{unidadId:D}", ct);
        (await desactivada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("inmuebleNombre").GetString()
            .Should().Be(nombre);
        var reactivada = await sesion.PostAsync($"/api/v1/inmuebles/unidades/{unidadId:D}/reactivar", new { }, ct);
        (await reactivada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("inmuebleNombre").GetString()
            .Should().Be(nombre);

        var ficha = await Leer(sesion, $"/api/v1/inmuebles/{inmuebleId:D}", ct);
        ficha.GetProperty("unidades").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("inmuebleNombre").GetString().Should().Be(nombre);
    }

    // ---------------------------------------------------------------- apoyo

    private async Task<SesionCliente> SesionAsync(string rol, CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: rol, ct: ct);
        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }

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

    private static async Task<Guid> CrearAsync(SesionCliente sesion, string ruta, object cuerpo, CancellationToken ct)
    {
        var creada = await sesion.PostAsync(ruta, cuerpo, ct);
        creada.StatusCode.Should().Be(HttpStatusCode.OK, await creada.Content.ReadAsStringAsync(ct));

        return (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> Leer(SesionCliente sesion, string ruta, CancellationToken ct)
    {
        var respuesta = await sesion.GetAsync(ruta, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));

        return await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    private static async Task<List<JsonElement>> Datos(SesionCliente sesion, string ruta, CancellationToken ct) =>
        [.. (await Leer(sesion, ruta, ct)).GetProperty("data").EnumerateArray()];

    private static decimal Decimal(JsonElement valor) =>
        valor.ValueKind == JsonValueKind.String
            ? decimal.Parse(valor.GetString()!, CultureInfo.InvariantCulture)
            : valor.GetDecimal();
}
