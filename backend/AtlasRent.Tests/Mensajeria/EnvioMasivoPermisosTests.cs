using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Mensajeria;

/// <summary>
/// F4-03: el envío masivo es de <c>mensajeria.masivo</c>, que §2 no le da al empleado
/// (docs/ESPECIFICACION.md §2 y §3.14, «gerente/admin»).
/// <para>
/// Y el intento denegado queda auditado con IP, que es lo que dice AGENTS.md §6: no basta con cerrar
/// la puerta, hay que saber quién intentó empujarla.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class EnvioMasivoPermisosTests(ContenedorBdFixture bd, ApiTestFactoryMasivo api)
    : IClassFixture<ApiTestFactoryMasivo>
{
    /// <summary>El empleado recibe 403 en los cuatro endpoints y no se encola ni se escribe nada.</summary>
    [Fact]
    public async Task ElEmpleadoNoEnviaEnMasa()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaEnviosMasivo.SesionAsync(bd, api, Roles.Admin, ct);
        var (empleado, _) = await AyudaEnviosMasivo.SesionAsync(bd, api, Roles.Empleado, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        api.Cola.Vaciar();
        var peticion = AyudaEnviosMasivo.Peticion(escenario.ClienteId);

        (await empleado.PostAsync("/api/v1/envios/masivo/preparar", peticion, ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await empleado.PostAsync("/api/v1/envios/masivo/confirmar", peticion, ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await empleado.GetAsync($"/api/v1/envios/masivo/{Guid.NewGuid():D}", ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await empleado.PostAsync($"/api/v1/envios/masivo/{Guid.NewGuid():D}/reintentar", new { }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        api.Cola.Encolados.Should().BeEmpty();
        (await AyudaEnviosMasivo.FilasDelDocumentoAsync(
            bd, await PrimeraFacturaAsync(escenario, ct), ct)).Should().BeEmpty();
    }

    /// <summary>El gerente sí entra, y el intento del empleado queda escrito en la auditoría.</summary>
    [Fact]
    public async Task ElGerenteSiEnviaEnMasaYElIntentoDelEmpleadoQuedaAuditado()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaEnviosMasivo.SesionAsync(bd, api, Roles.Admin, ct);
        var (gerente, _) = await AyudaEnviosMasivo.SesionAsync(bd, api, Roles.Gerente, ct);
        var (empleado, usuarioEmpleado) = await AyudaEnviosMasivo.SesionAsync(bd, api, Roles.Empleado, ct);

        // El escenario lo monta el administrador porque crear series es de configuración; lo que se
        // prueba aquí es que el gerente puede con el envío masivo.
        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        api.Cola.Vaciar();
        var peticion = AyudaEnviosMasivo.Peticion(escenario.ClienteId);

        (await empleado.PostAsync("/api/v1/envios/masivo/preparar", peticion, ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var resumen = await AyudaEnviosMasivo.PrepararAsync(gerente, peticion, ct);
        resumen.GetProperty("total").GetInt32().Should().Be(1);

        var loteId = await AyudaEnviosMasivo.ConfirmarAsync(gerente, peticion, ct);
        api.Cola.Encolados.Should().HaveCount(1, "§3.14: el envío masivo es de gerente y administrador");

        (await AyudaEnviosMasivo.FilasDelLoteAsync(bd, loteId, ct)).Should().ContainSingle();

        await using var sinRls = bd.CrearContexto(bd.CadenaSuperusuario);
        var denegados = await sinRls.Auditoria.AsNoTracking()
            .Where(a => a.Accion == $"permiso.denegado.{Acciones.MensajeriaMasivo}" && a.UsuarioId == usuarioEmpleado)
            .ToListAsync(ct);

        denegados.Should().NotBeEmpty("el intento denegado queda en la auditoría firmada");
        denegados.Should().OnlyContain(a => a.Resultado == "denegado" && a.Ip != null);
    }

    /// <summary>La primera factura del escenario, para comprobar que no se escribió nada.</summary>
    private async Task<Guid> PrimeraFacturaAsync(AyudaCuentas.Escenario escenario, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        return await contexto.Factura.AsNoTracking()
            .Where(f => f.ClienteId == escenario.ClienteId)
            .Select(f => f.Id)
            .FirstAsync(ct);
    }
}