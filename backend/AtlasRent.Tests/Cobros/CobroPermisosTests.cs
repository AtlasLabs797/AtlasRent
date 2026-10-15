using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Cobros;

/// <summary>
/// F3-06: <c>cobro.registrar</c> es del gerente y del administrador, no del empleado (D15, §2). Las
/// pruebas van por la API real, que es donde vive el filtro <c>[Accion]</c>, y comprueban que el
/// intento denegado queda en la auditoría firmada.
/// <para>
/// El recurso de la denegación lo pone el filtro global de acciones —método y plantilla de la ruta—,
/// y se lee por la conexión de superusuario porque <c>auditoria</c> tiene RLS. Se filtra por usuario
/// porque la base es compartida y de la misma acción hay otros intentos denegados.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class CobroPermisosTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    [Fact]
    public async Task ElEmpleadoNoPuedeRegistrarUnCobro()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuarioEmpleado = await AyudaAuth.CrearUsuarioAsync(
            bd, AyudaFacturas.Contrasena, rol: Roles.Empleado, ct: ct);

        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);
        using var empleado = await AyudaPermisos.SesionAsync(api, usuarioEmpleado, AyudaFacturas.Contrasena, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(admin, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(admin, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(admin, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);

        await AyudaCobros.EmitidaAsync(admin, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct);

        var cuerpo = AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, 300m, formaPagoId, cuentaId);

        var registro = await empleado.PostAsync("/api/v1/cobros", cuerpo, ct);
        registro.StatusCode.Should().Be(HttpStatusCode.Forbidden, await registro.Cuerpo());

        var simulacion = await empleado.PostAsync("/api/v1/cobros/simular", cuerpo, ct);
        simulacion.StatusCode.Should().Be(HttpStatusCode.Forbidden, "simular es parte de registrar");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Cobro.AsNoTracking().CountAsync(c => c.ClienteId == clienteId, ct))
            .Should().Be(0, "un 403 no registra nada");

        await using var sinRls = bd.CrearContexto(bd.CadenaSuperusuario);
        var denegadas = await sinRls.Auditoria.AsNoTracking()
            .Where(a => a.Accion == $"permiso.denegado.{Acciones.CobroRegistrar}" && a.UsuarioId == usuarioEmpleado.Id)
            .ToListAsync(ct);

        denegadas.Should().NotBeEmpty("el intento denegado queda en la auditoría firmada");
        denegadas.Should().OnlyContain(a => a.Resultado == "denegado" && a.Ip != null);
        denegadas.Should().Contain(a => a.Datos!.RootElement.GetProperty("rol").GetString() == Roles.Empleado);
    }

    /// <summary>
    /// Anular es corregir dinero, así que lleva el mismo permiso que registrar. El anular va contra un
    /// cobro que el propio administrador acaba de registrar, porque lo que se comprueba es que el
    /// empleado no puede deshacerlo.
    /// </summary>
    [Fact]
    public async Task ElEmpleadoNoPuedeAnularUnCobro()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuarioEmpleado = await AyudaAuth.CrearUsuarioAsync(
            bd, AyudaFacturas.Contrasena, rol: Roles.Empleado, ct: ct);

        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);
        using var empleado = await AyudaPermisos.SesionAsync(api, usuarioEmpleado, AyudaFacturas.Contrasena, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(admin, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(admin, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(admin, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);

        await AyudaCobros.EmitidaAsync(admin, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct);

        var registrado = await admin.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, 300m, formaPagoId, cuentaId),
            ct);
        registrado.StatusCode.Should().Be(HttpStatusCode.OK, await registrado.Cuerpo());
        var cobroId = (await registrado.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();

        var anulacion = await empleado.PostAsync(
            $"/api/v1/cobros/{cobroId:D}/anular",
            new { motivo = "Quiero deshacerlo" },
            ct);

        anulacion.StatusCode.Should().Be(HttpStatusCode.Forbidden, await anulacion.Cuerpo());

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Cobro.AsNoTracking().SingleAsync(c => c.Id == cobroId, ct)).Estado
            .Should().Be(EstadoCobro.Registrado);
        (await contexto.CobroAsignacion.AsNoTracking().CountAsync(a => a.CobroId == cobroId && a.Vigente, ct))
            .Should().Be(1, "y sus asignaciones siguen contando");

        await using var sinRls = bd.CrearContexto(bd.CadenaSuperusuario);
        var denegada = (await sinRls.Auditoria.AsNoTracking()
                .Where(a => a.Accion == $"permiso.denegado.{Acciones.CobroRegistrar}" && a.UsuarioId == usuarioEmpleado.Id)
                .ToListAsync(ct))
            .Should().ContainSingle(a => a.Datos!.RootElement.GetProperty("recurso").GetString()!.Contains("anular"))
            .Which;

        denegada.Ip.Should().NotBeNullOrWhiteSpace();
        denegada.Resultado.Should().Be("denegado");
        denegada.Origen.Should().Be("api");
    }

    /// <summary>
    /// Ver un cobro no es lo mismo que mover dinero: el permiso de lectura es el de las facturas y el
    /// empleado lo tiene, así que el 403 de D15 no puede cerrar también la lectura.
    /// </summary>
    [Fact]
    public async Task ElEmpleadoSiPuedeVerUnCobro()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);
        using var empleado = await AyudaCobros.SesionAsync(bd, api, Roles.Empleado, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(admin, bd, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(admin, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(admin, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);

        var registrado = await admin.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(clienteId, contratoId, hoy, Moneda.USD, 300m, formaPagoId, cuentaId),
            ct);
        registrado.StatusCode.Should().Be(HttpStatusCode.OK, await registrado.Cuerpo());
        var cobroId = (await registrado.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();

        var detalle = await empleado.GetAsync($"/api/v1/cobros/{cobroId:D}", ct);
        detalle.StatusCode.Should().Be(HttpStatusCode.OK, await detalle.Cuerpo());

        var listado = await empleado.GetAsync($"/api/v1/cobros?clienteId={clienteId:D}", ct);
        listado.StatusCode.Should().Be(HttpStatusCode.OK, await listado.Cuerpo());
    }
}