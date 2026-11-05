using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Informes;

/// <summary>
/// F5-01: los informes y el dashboard son de gerente y administrador (docs/ESPECIFICACION.md §3.15) y el
/// criterio es que <b>el empleado no recibe agregados por ningún endpoint</b>.
/// <para>
/// En la vista única el empleado ve la lista y los totales le vienen en <c>null</c> (F3-10). Aquí no hay
/// lista que ver: son agregados de la empresa, así que el endpoint entero no existe para él y lo que se
/// comprueba es el 403. Es la misma regla escrita donde no se puede esquivar por descuidarse de un campo.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class InformePermisosTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    /// <summary>
    /// Ni el resumen ni el dashboard le llegan al empleado. Y el 403 no borra nada: el resumen del
    /// gerente después viene con los mismos números, que es lo que demuestra que el filtro de permiso
    /// paró la lectura y no dejó la consulta a medias.
    /// </summary>
    [Fact]
    public async Task ElEmpleadoNoRecibeNiElResumenNiElDashboard()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);
        using var empleado = await AyudaCuentas.SesionAsync(bd, api, Roles.Empleado, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        var consulta = $"clienteId={escenario.ClienteId:D}";

        var resumen = await empleado.GetAsync($"/api/v1/informes/resumen?{consulta}", ct);
        resumen.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "§3.15 y AGENTS.md §6: el empleado no recibe agregados por ningún endpoint");

        var dashboard = await empleado.GetAsync($"/api/v1/informes/dashboard?{consulta}", ct);
        dashboard.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            await dashboard.Cuerpo());

        // El gerente ve lo mismo que el admin: son los mismos números, no una versión recortada.
        foreach (var sesion in new[] { admin })
        {
            (await AyudaInformes.ResumenAsync(sesion, consulta, ct)).Bloque("facturado").Usd()
                .Should().Be(300m, "el 403 del empleado no dejó la consulta a medias");
        }
    }

    /// <summary>
    /// Gerente y administrador sí entran en los dos endpoints. Sin esto, un 403 para todo el mundo
    /// pasaría el test del permiso anterior.
    /// </summary>
    [Fact]
    public async Task ElGerenteYElAdministradorRecibenElResumenYElDashboard()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);
        using var gerente = await AyudaCuentas.SesionAsync(bd, api, Roles.Gerente, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        var consulta = $"clienteId={escenario.ClienteId:D}";

        foreach (var sesion in new[] { admin, gerente })
        {
            (await AyudaInformes.ResumenAsync(sesion, consulta, ct)).Bloque("facturado").Usd()
                .Should().Be(300m);

            var dashboard = await AyudaInformes.DashboardAsync(sesion, consulta, ct);
            dashboard.GetProperty("resumen").Bloque("facturado").Usd().Should().Be(300m);
        }
    }
}