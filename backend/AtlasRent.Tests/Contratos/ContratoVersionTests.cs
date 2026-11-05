using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Contratos;

/// <summary>
/// F2-05: el versionado de un contrato vigente (docs/ESPECIFICACION.md §3.5).
/// <para>
/// Cada cambio en un contrato activo genera versión nueva, y el historial es de solo añadir. El
/// cambio de renta afecta <b>solo a facturas futuras</b>: como las facturas son de F3-01 y esta tarea
/// no las toca, lo que se comprueba aquí es que la renta anterior queda guardada en el historial y
/// que el contrato deja escrito el alcance del cambio, que es lo que F3-01 va a leer.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ContratoVersionTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// Dos cambios de renta sobre un contrato vigente: tres versiones (la del alta y dos cambios), la
    /// renta anterior en cada una y el alcance escrito.
    /// </summary>
    /// <summary>
    /// BUG-052: un contrato vigente no cambia de moneda por un cambio en activo: lo ya facturado y
    /// cobrado está en la moneda del contrato y nunca se convierte (AGENTS.md §6). Es un 409, no se
    /// crea versión y la moneda sigue siendo la misma.
    /// </summary>
    [Fact]
    public async Task UnContratoActivoNoCambiaDeMoneda()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct), Contrasena, ct);
        using var gerente = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct), Contrasena, ct);
        var contratoId = await AyudaContrato.HastaActivoAsync(empleado, gerente, bd, ct);

        var respuesta = await AyudaContrato.Boton(
            gerente,
            contratoId,
            "cambios",
            new
            {
                renta = 55_000m,
                moneda = "USD",
                diaPago = 5,
                fianza = 20_000m,
                reglaRenovacion = "Anual",
                renovacionMeses = 12,
                motivo = "Pasar a dólares",
            },
            ct);

        respuesta.StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Contrato.AsNoTracking().SingleAsync(c => c.Id == contratoId, ct))
            .Moneda.Should().Be(Moneda.DOP);
        (await contexto.ContratoVersion.AsNoTracking().CountAsync(v => v.ContratoId == contratoId, ct))
            .Should().Be(1, "solo la versión de la activación: el cambio rechazado no abre otra");
    }

    [Fact]
    public async Task CadaCambioEnUnActivoCreaVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct), Contrasena, ct);
        using var gerente = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct), Contrasena, ct);

        var contratoId = await AyudaContrato.HastaActivoAsync(empleado, gerente, bd, ct);

        await AyudaContrato.AceptarAsync(
            gerente, contratoId, "cambios", AyudaContrato.Cambio(55_000m, "Revisión del IPC"), ct);
        await AyudaContrato.AceptarAsync(
            gerente, contratoId, "cambios", AyudaContrato.Cambio(60_000m, "Segunda revisión"), ct);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var versiones = await contexto.ContratoVersion.AsNoTracking()
            .Where(v => v.ContratoId == contratoId)
            .OrderBy(v => v.Numero)
            .Select(v => new
            {
                v.Numero,
                v.Cambio,
                v.RentaAnterior,
                v.RentaNueva,
                v.Alcance,
                v.CreatedBy,
                v.UpdatedAt,
            })
            .ToListAsync(ct);

        versiones.Should().HaveCount(3, "el alta del activo más los dos cambios");
        versiones.Select(v => v.Numero).Should().Equal(1, 2, 3);
        versiones[0].RentaAnterior.Should().BeNull("la primera versión es el alta: no había renta anterior");
        versiones[0].RentaNueva.Should().Be(50_000m);

        versiones[1].RentaAnterior.Should().Be(50_000m);
        versiones[1].RentaNueva.Should().Be(55_000m);
        versiones[2].RentaAnterior.Should().Be(55_000m);
        versiones[2].RentaNueva.Should().Be(60_000m);

        // La renta anterior es lo que da la razón de que el cambio no toca lo ya facturado, y el
        // alcance deja escrito que solo afecta a facturas futuras (§3.5).
        versiones[2].Alcance.Should().Contain("facturas futuras");
        versiones.Should().OnlyContain(v => v.Alcance.Contains("facturas futuras"));

        // El historial es inmutable: las versiones no se modifican ni se borran (AGENTS.md §6).
        versiones.Should().OnlyContain(v => v.UpdatedAt == null);

        var contrato = await contexto.Contrato.AsNoTracking().SingleAsync(c => c.Id == contratoId, ct);
        contrato.Renta.Should().Be(60_000m, "la renta viva es la última");

        // Lo que NO se comprueba aquí, y hay que decirlo: que lo ya facturado no cambia. La tabla de
        // facturas es de F3-01, así que en esta tarea no hay ninguna que poder tocar. Lo que sí queda
        // escrito para F3-01 es la renta anterior de cada versión y el alcance, que es justo lo que
        // tendrá que leer el generador de facturas.
    }

    /// <summary>
    /// Un cambio de contrato vigente solo lo hace quien tiene <c>contrato.estado</c>: el empleado
    /// recibe 403 también por esta vía, que es la otra forma de tocar un contrato ya aprobado.
    /// </summary>
    [Fact]
    public async Task ElEmpleadoNoModificaUnContratoVigente()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct), Contrasena, ct);
        using var gerente = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct), Contrasena, ct);

        var contratoId = await AyudaContrato.HastaActivoAsync(empleado, gerente, bd, ct);

        (await AyudaContrato.Boton(
            empleado, contratoId, "cambios", AyudaContrato.Cambio(99_000m), ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var contrato = await contexto.Contrato.AsNoTracking().SingleAsync(c => c.Id == contratoId, ct);
        contrato.Renta.Should().Be(50_000m);
    }
}