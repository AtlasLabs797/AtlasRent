using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Cobros;
using AtlasRent.Tests.Cotizaciones;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Reservas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Mensajeria;

/// <summary>
/// F4-02: <c>documento.enviar</c> y <c>cotizacion.enviar</c> son dos permisos distintos (docs/ESPECIFICACION.md §2)
/// y quien no los tiene recibe 403.
/// <para>
/// Que sean distintos no es un detalle de la matriz: una cotización es un documento comercial que se
/// manda para que alguien lo acepte, y una factura es un documento que ya está emitido y se manda
/// para que lo pague. Quien puede registrar cobros no debería poder mandar una cotización, ni al revés.
/// </para>
/// <para>
/// Y el permiso se comprueba <b>en el servicio</b>, no solo en el atributo del endpoint: los trabajos de
/// Hangfire entran por el servicio y no tienen sesión de la que sacar un usuario.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class EnvioPermisosTests(ContenedorBdFixture bd, ApiTestFactoryCorreo api)
    : IClassFixture<ApiTestFactoryCorreo>
{
    /// <summary>
    /// El administrador trae los dos permisos y por eso manda una factura y una cotización.
    /// </summary>
    [Fact]
    public async Task ElAdministradorMandaFacturasYCotizaciones()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaEnvios.SesionAsync(bd, api, Roles.Admin, ct);

        var permisos = await PermisosAsync(admin, ct);
        permisos.Should().Contain(Acciones.DocumentoEnviar);
        permisos.Should().Contain(Acciones.CotizacionEnviar);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaId = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        (await admin.PostAsync(
            "/api/v1/envios",
            AyudaEnvios.PeticionFactura(facturaId, AyudaEnvios.CorreoDePrueba()),
            ct)).EnsureSuccessStatusCode();

        var cotizacionId = await AyudaCotizacion.CrearAsync(admin, escenario.ClienteId, ct: ct);
        var enviada = await admin.PostAsync(
            "/api/v1/envios",
            AyudaEnvios.PeticionCotizacion(cotizacionId, AyudaEnvios.CorreoDePrueba()),
            ct);

        enviada.StatusCode.Should().Be(HttpStatusCode.OK, await enviada.Cuerpo());
    }

    /// <summary>
    /// Un empleado no trae <c>documento.enviar</c> —§2 solo se lo da a gerente y administrador—, así
    /// que una factura le da 403. Y sí trae <c>cotizacion.enviar</c>, así que una cotización sale.
    /// <para>
    /// Eso es exactamente para lo que son dos permisos distintos: quien registra cobros puede
    /// mandar una cotización —es parte de su trabajo comercial— pero no puede mandar una factura que
    /// ya está emitida, porque mandarla es una decisión sobre un documento con número y efecto
    /// económico.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ElEmpleadoMandaCotizacionesPeroNoFacturas()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaEnvios.SesionAsync(bd, api, Roles.Admin, ct);
        using var empleado = await AyudaEnvios.SesionAsync(bd, api, Roles.Empleado, ct);

        var permisos = await PermisosAsync(empleado, ct);
        permisos.Should().NotContain(Acciones.DocumentoEnviar);
        permisos.Should().Contain(Acciones.CotizacionEnviar, "§2 se lo da al empleado, y por eso son dos");

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaId = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        var denegado = await empleado.PostAsync(
            "/api/v1/envios",
            AyudaEnvios.PeticionFactura(facturaId, AyudaEnvios.CorreoDePrueba()),
            ct);

        denegado.StatusCode.Should().Be(HttpStatusCode.Forbidden, await denegado.Cuerpo());
        (await AyudaEnvios.UltimoEnvioAsync(bd, facturaId, ct))
            .Should().BeNull("un permiso denegado no llega a mandar nada ni a escribir una fila");

        // La cotización sí sale, con el otro permiso. Sin esto, el 403 de arriba no probaría que los
        // dos permisos están bien diferenciados y no solo que el endpoint está cerrado para todos.
        var cotizacionId = await AyudaCotizacion.CrearAsync(empleado, escenario.ClienteId, ct: ct);
        var enviada = await empleado.PostAsync(
            "/api/v1/envios",
            AyudaEnvios.PeticionCotizacion(cotizacionId, AyudaEnvios.CorreoDePrueba()),
            ct);

        enviada.StatusCode.Should().Be(HttpStatusCode.OK, await enviada.Cuerpo());
    }

    /// <summary>Los permisos efectivos de la sesión, tal y como los ve el filtro global.</summary>
    private static async Task<HashSet<string>> PermisosAsync(SesionCliente sesion, CancellationToken ct)
    {
        var yo = await sesion.GetAsync("/api/v1/auth/yo", ct);
        yo.StatusCode.Should().Be(HttpStatusCode.OK, await yo.Cuerpo());

        var cuerpo = await yo.Content.ReadFromJsonAsync<JsonElement>(ct);

        return [.. cuerpo.GetProperty("permisosEfectivos").EnumerateArray().Select(p => p.GetString()!)];
    }
}