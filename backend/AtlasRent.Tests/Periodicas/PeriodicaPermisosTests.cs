using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Jobs;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Periodicas;

/// <summary>
/// F3-05: los permisos de la facturación periódica.
/// <para>
/// Facturar es emitir, así que vale <c>factura.emitir</c>, que D15 deja desactivado al empleado.
/// La simulación pide el mismo permiso a propósito: enseña los importes, los vencimientos y la
/// serie y el comprobante de facturas que van a existir, y eso no es menos dato que el de la factura
/// emitida.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class PeriodicaPermisosTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    /// <summary>
    /// El empleado no genera facturas periódicas: un 403 de la API real, y el intento queda
    /// auditado con su acción denegada.
    /// </summary>
    [Fact]
    public async Task ElEmpleadoNoGeneraFacturasPeriodicasYElIntentoQuedaAuditado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        using var empleado = await AyudaFacturas.SesionAsync(bd, api, Roles.Empleado, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var mes = PeriodoFacturacion.DelMes(hoy);

        var (contratoId, _) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, mes.Inicio, duracionMeses: 24);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);

        var respuesta = await AyudaPeriodicas.GenerarRawAsync(
            empleado, AyudaPeriodicas.Peticion(mes.Inicio, serieId, [contratoId]), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await AyudaPeriodicas.FacturasDelPeriodoAsync(bd, contratoId, mes.Inicio, ct))
            .Should().BeEmpty();

        await using var contexto = bd.CrearContexto(bd.CadenaSuperusuario);
        (await contexto.Auditoria.AsNoTracking().CountAsync(
            a => a.Accion == $"permiso.denegado.{Acciones.FacturaEmitir}", ct))
            .Should().BeGreaterThan(0, "el intento denegado también se audita");
    }

    /// <summary>
    /// La simulación tampoco es de quien no puede emitir, y además no escribe nada: ni facturas, ni
    /// números de serie, ni alertas, aunque enseñe lo que va a salir.
    /// </summary>
    [Fact]
    public async Task LaSimulacionExigeElMismoPermisoQueEmitirYNoEscribeNada()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        using var empleado = await AyudaFacturas.SesionAsync(bd, api, Roles.Empleado, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var mes = PeriodoFacturacion.DelMes(hoy);

        var (contratoId, _) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, mes.Inicio, duracionMeses: 24);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var peticion = AyudaPeriodicas.Peticion(mes.Inicio, serieId, [contratoId]);

        var denegada = await AyudaPeriodicas.SimularRawAsync(empleado, peticion, ct);
        denegada.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "la simulación enseña lo que va a salir, y eso es contenido de emisión");

        // Para el que sí puede, la simulación responde el importe sin tocar nada.
        var simulacion = await AyudaPeriodicas.SimularAsync(admin, peticion, ct);
        AyudaPeriodicas.UnItem(simulacion).GetProperty("importe").GetDecimal()
            .Should().Be(AyudaPeriodicas.RentaDePrueba);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Factura.AsNoTracking().CountAsync(f => f.ContratoId == contratoId, ct))
            .Should().Be(0, "la simulación no crea nada");
        (await contexto.NumeroSerie.AsNoTracking().CountAsync(n => n.SerieId == serieId, ct))
            .Should().Be(0, "ni gasta un número de serie");
        (await contexto.Alerta.AsNoTracking().CountAsync(
            a => a.Clave == TrabajoPeriodosSinFacturar.Clave(contratoId, mes.Inicio), ct))
            .Should().Be(0, "ni deja un aviso");
    }

    /// <summary>
    /// El gerente puede: D15 le deja emitir, y la facturación periódica no es una excepción al
    /// permiso sino un uso más de él.
    /// </summary>
    [Fact]
    public async Task ElGerenteSiPuedeGenerarLaFacturaPeriodica()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        using var gerente = await AyudaFacturas.SesionAsync(bd, api, Roles.Gerente, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var mes = PeriodoFacturacion.DelMes(hoy);

        var (contratoId, _) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, mes.Inicio, duracionMeses: 24);

        // La serie la crea el administrador porque es configuración (D15); lo que se comprueba aquí
        // es que el gerente, que puede emitir, factura el periodo.
        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);

        var resultado = await AyudaPeriodicas.GenerarAsync(
            gerente, AyudaPeriodicas.Peticion(mes.Inicio, serieId, [contratoId]), ct);

        resultado.GetProperty("creadas").GetInt32().Should().Be(1);
    }
}