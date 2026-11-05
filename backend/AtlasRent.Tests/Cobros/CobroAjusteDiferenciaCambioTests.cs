using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Cobros;

/// <summary>
/// F3-07: el ajuste por diferencia de cambio (docs/ESPECIFICACION.md §3.10.2).
/// <para>
/// El ajuste cierra el pendiente que dejó un cambio que no dividía exacto. <b>No es dinero
/// recibido</b>: por eso no se pide el importe (es el pendiente real, y que quien lo firma elija de
/// cuánto sería convertir el botón en la manera de tapar cualquier saldo), y por eso sale en su
/// propio campo de la factura, separado de <c>cobrado</c>.
/// </para>
/// <para>
/// La tolerancia sale de <c>ajuste_general</c> (F1-04) y <b>nace a <c>NULL</c></b>: sin que el
/// administrador la escriba, §3.10.2 marca esos importes como [Supuesto] y el ajuste por diferencia
/// de cambio está <b>desactivado</b>. Es lo que comprueba la primera prueba de esta clase.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class CobroAjusteDiferenciaCambioTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    /// <summary>Tolerancia de un dólar, la que el administrador escribiría para poder ajustar céntimos.</summary>
    private const decimal ToleranciaUsd = 1.00m;

    /// <summary>
    /// <b>Sin tolerancia configurada no hay ajuste, y hay que decirlo.</b> La fila de
    /// <c>ajuste_general</c> se crea sin tolerancias (§3.10.2 los importes son [Supuesto], no
    /// decisión), así que en una instalación recién hecha el ajuste está desactivado.
    /// <para>
    /// Es la decisión que hace esta tarea: un pendiente que se cierra sin que nadie haya dicho que se
    /// puede cerrar es una pérdida de dinero silenciosa. El 409 tiene que explicar dónde se configura,
    /// porque quien lo lee casi siempre va a ser el gerente en el mostrador y no el que despliega.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SinToleranciaConfiguradaElAjusteEstaDesactivado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaPesos = await AyudaCobros.CuentaAsync(sesion, Moneda.DOP, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        await using var limpieza = AyudaCobros.SinRastroDeCambio(bd, hoy, Moneda.DOP);
        await AyudaCobros.CargarCambioAsync(sesion, hoy, Moneda.DOP, AyudaCobros.CambioDelDia, ct);

        var facturaId = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 1_000m, ct);

        // Un pendiente de 0,76 que entraría de sobra con un dólar de tolerancia. Sin tolerancia no se
        // ajusta, precisamente porque el pendiente por sí solo no dice si el ajuste es prudente.
        await using var tolerancia = await AyudaCobros.ToleranciaTemporal(sesion, null, null, ct);

        var cobro = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.DOP, 59_455m, formaPagoId, cuentaPesos,
                tipoCambioAplicado: AyudaCobros.CambioExacto),
            ct);
        cobro.StatusCode.Should().Be(HttpStatusCode.OK, await cobro.Cuerpo());

        var rechazo = await sesion.PostAsync(
            "/api/v1/cobros/ajustes-diferencia-cambio",
            new { facturaId, tipoCambioValor = AyudaCobros.CambioExacto, motivo = "Diferencia de 0,76" },
            ct);

        rechazo.StatusCode.Should().Be(HttpStatusCode.Conflict, await rechazo.Cuerpo());

        var detalle = await rechazo.Content.ReadAsStringAsync(ct);
        detalle.Should().Contain("tolerancia", "el mensaje dice qué falta, no solo que hay un conflicto");
        detalle.Should().Contain("Ajustes generales", "y dónde se configura, que es lo que quien lo lee necesita");

        (await AyudaCobros.AjustesAsync(bd, facturaId, ct))
            .Should().BeEmpty("sin tolerancia no se crea el ajuste, ni a medias");

        var leida = await AyudaCobros.FacturaAsync(sesion, facturaId, ct);
        leida.GetProperty("ajustado").LeerDecimal().Should().Be(0m);
        leida.GetProperty("pendiente").LeerDecimal().Should().Be(0.76m, "el pendiente sigue ahí, sin tocar");
        leida.GetProperty("estadoCobro").GetString().Should().Be("Parcial");

        // Y con la tolerancia configurada, el mismo ajuste entra. Es el mismo caso que la primera
        // prueba de esta clase, comprobado desde el otro lado: lo que faltaba era configurarla.
        await AyudaCobros.FijarToleranciaAsync(sesion, ToleranciaUsd, null, ct);

        var ahoraSi = await sesion.PostAsync(
            "/api/v1/cobros/ajustes-diferencia-cambio",
            new { facturaId, tipoCambioValor = AyudaCobros.CambioExacto, motivo = "Diferencia de 0,76" },
            ct);
        ahoraSi.StatusCode.Should().Be(HttpStatusCode.OK, await ahoraSi.Cuerpo());

        var cerrada = await AyudaCobros.FacturaAsync(sesion, facturaId, ct);
        cerrada.GetProperty("estadoCobro").GetString().Should().Be("Pagada");
        cerrada.GetProperty("ajustado").LeerDecimal().Should().Be(0.76m);
    }
    /// <summary>
    /// Pendiente dentro de la tolerancia: el ajuste lo cierra, la factura queda Pagada, la fila existe
    /// con su IP y su motivo, y el pendiente pasa a cero. 59.455 DOP a 59,50 son 999,24 dólares de los
    /// 1.000: quedan 0,76, que cabe en el dólar de tolerancia.
    /// </summary>
    [Fact]
    public async Task UnPendienteDentroDeLaToleranciaSeCierraConUnAjuste()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);

        // La tolerancia sale de ajuste_general y nace a NULL: sin ella no hay ajuste (F1-04, §3.10.2).
        await using var tolerancia = await AyudaCobros.ToleranciaTemporal(sesion, ToleranciaUsd, null, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaPesos = await AyudaCobros.CuentaAsync(sesion, Moneda.DOP, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        await using var limpieza = AyudaCobros.SinRastroDeCambio(bd, hoy, Moneda.DOP);
        await AyudaCobros.CargarCambioAsync(sesion, hoy, Moneda.DOP, AyudaCobros.CambioDelDia, ct);

        var facturaId = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 1_000m, ct);

        var cobro = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.DOP, 59_455m, formaPagoId, cuentaPesos,
                tipoCambioAplicado: AyudaCobros.CambioExacto),
            ct);
        cobro.StatusCode.Should().Be(HttpStatusCode.OK, await cobro.Cuerpo());

        var antesDeAjustar = await AyudaCobros.FacturaAsync(sesion, facturaId, ct);
        antesDeAjustar.GetProperty("pendiente").LeerDecimal().Should().Be(0.76m);
        antesDeAjustar.GetProperty("estadoCobro").GetString().Should().Be("Parcial");

        var ajuste = await sesion.PostAsync(
            "/api/v1/cobros/ajustes-diferencia-cambio",
            new
            {
                facturaId,
                tipoCambioValor = AyudaCobros.CambioExacto,
                motivo = "El cliente entregó 59.455 pesos y el banco cobró a 59,50; la diferencia es del cambio",
            },
            ct);
        ajuste.StatusCode.Should().Be(HttpStatusCode.OK, await ajuste.Cuerpo());

        var cuerpoAjuste = await ajuste.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpoAjuste.GetProperty("importe").LeerDecimal().Should().Be(0.76m, "el importe es el pendiente real, no uno elegido");
        cuerpoAjuste.GetProperty("vigente").GetBoolean().Should().BeTrue();

        // La fila, leída de la base: existe, con su motivo y con la IP desde la que se corrigió.
        var filas = await AyudaCobros.AjustesAsync(bd, facturaId, ct);
        var guardada = filas.Should().ContainSingle().Which;
        guardada.Importe.Should().Be(0.76m);
        guardada.Moneda.Should().Be(Moneda.USD);
        guardada.TipoCambioValor.Should().Be(AyudaCobros.CambioExacto);
        guardada.Motivo.Should().Contain("diferencia es del cambio");
        guardada.Ip.Should().NotBeNullOrWhiteSpace("la fila dice desde dónde se corrigió el saldo, no solo la auditoría");
        guardada.CreatedBy.Should().NotBeNull();

        var despues = await AyudaCobros.FacturaAsync(sesion, facturaId, ct);
        despues.GetProperty("estadoCobro").GetString().Should().Be("Pagada");
        despues.GetProperty("pendiente").LeerDecimal().Should().Be(0m);
        despues.GetProperty("ajustado").LeerDecimal().Should().Be(0.76m);

        // Y lo ajustado va aparte de lo cobrado: el informe de caja no tiene que engordar.
        despues.GetProperty("cobrado").LeerDecimal()
            .Should().Be(999.24m, "cobrado es el dinero que entró en la caja, y el ajuste no lo es");
    }

    /// <summary>
    /// Pendiente por encima de la tolerancia: no se ajusta. El 409 dice cuál es el importe, cuál es la
    /// tolerancia y las dos salidas de §3.10.2, y el pendiente se queda como estaba porque el intento
    /// fallido no mueve nada.
    /// </summary>
    [Fact]
    public async Task UnPendientePorEncimaDeLaToleranciaSeRechazaYToleraLaFactura()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);

        // La tolerancia sale de ajuste_general y nace a NULL: sin ella no hay ajuste (F1-04, §3.10.2).
        await using var tolerancia = await AyudaCobros.ToleranciaTemporal(sesion, ToleranciaUsd, null, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaPesos = await AyudaCobros.CuentaAsync(sesion, Moneda.DOP, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        await using var limpieza = AyudaCobros.SinRastroDeCambio(bd, hoy, Moneda.DOP);
        await AyudaCobros.CargarCambioAsync(sesion, hoy, Moneda.DOP, AyudaCobros.CambioDelDia, ct);

        var facturaId = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 1_000m, ct);

        // 59.400 DOP a 59,50 son 998,32: faltan 1,68, que es más que el dólar de tolerancia.
        var cobro = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.DOP, 59_400m, formaPagoId, cuentaPesos,
                tipoCambioAplicado: AyudaCobros.CambioExacto),
            ct);
        cobro.StatusCode.Should().Be(HttpStatusCode.OK, await cobro.Cuerpo());

        var rechazo = await sesion.PostAsync(
            "/api/v1/cobros/ajustes-diferencia-cambio",
            new { facturaId, tipoCambioValor = AyudaCobros.CambioExacto, motivo = "Quiero cerrar esto como sea" },
            ct);

        rechazo.StatusCode.Should().Be(HttpStatusCode.Conflict, await rechazo.Cuerpo());

        var detalle = await rechazo.Content.ReadAsStringAsync(ct);
        // El mensaje va en la cultura del servidor (es-DO), así que se comprueba la magnitud tal y
        // como la ve quien lee el 409, no en notación de código.
        detalle.Should().Contain("1,68", "el mensaje dice cuánto queda, para que quien lo lee sepa la magnitud");
        detalle.Should().Contain("1,00", "y cuál es el límite aprobado");
        detalle.Should().Contain("tolerancia");
        detalle.Should().Contain("cambio", "y dice las dos salidas que da §3.10.2, no solo que no se puede");

        (await AyudaCobros.AjustesAsync(bd, facturaId, ct))
            .Should().BeEmpty("un 409 no deja el ajuste a medias");

        var leida = await AyudaCobros.FacturaAsync(sesion, facturaId, ct);
        leida.GetProperty("pendiente").LeerDecimal().Should().Be(1.68m, "el pendiente sigue igual");
        leida.GetProperty("estadoCobro").GetString().Should().Be("Parcial");
    }

    /// <summary>
    /// Sin motivo no hay ajuste: es un 400, no un 409. Un importe que no cuadra y que nadie explica es
    /// justo el movimiento que después no se sabe si fue una diferencia o un error de captura.
    /// </summary>
    [Fact]
    public async Task UnAjusteSinMotivoEsPeticionInvalida()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);

        // La tolerancia sale de ajuste_general y nace a NULL: sin ella no hay ajuste (F1-04, §3.10.2).
        await using var tolerancia = await AyudaCobros.ToleranciaTemporal(sesion, ToleranciaUsd, null, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaPesos = await AyudaCobros.CuentaAsync(sesion, Moneda.DOP, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        await using var limpieza = AyudaCobros.SinRastroDeCambio(bd, hoy, Moneda.DOP);
        await AyudaCobros.CargarCambioAsync(sesion, hoy, Moneda.DOP, AyudaCobros.CambioDelDia, ct);

        var facturaId = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 1_000m, ct);

        var cobro = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.DOP, 59_455m, formaPagoId, cuentaPesos,
                tipoCambioAplicado: AyudaCobros.CambioExacto),
            ct);
        cobro.StatusCode.Should().Be(HttpStatusCode.OK, await cobro.Cuerpo());

        var sinMotivo = await sesion.PostAsync(
            "/api/v1/cobros/ajustes-diferencia-cambio",
            new { facturaId, tipoCambioValor = AyudaCobros.CambioExacto, motivo = "   " },
            ct);

        sinMotivo.StatusCode.Should().Be(HttpStatusCode.BadRequest, await sinMotivo.Cuerpo());
        (await AyudaCobros.AjustesAsync(bd, facturaId, ct)).Should().BeEmpty();
    }

    /// <summary>
    /// Un ajuste se anula, no se borra (AGENTS.md §6). La factura recupera el pendiente que tenía y la
    /// fila queda con su motivo, su usuario y su fecha.
    /// </summary>
    [Fact]
    public async Task AnularElAjusteDejaLaFacturaComoEstabaYConservaLaFila()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);

        // La tolerancia sale de ajuste_general y nace a NULL: sin ella no hay ajuste (F1-04, §3.10.2).
        await using var tolerancia = await AyudaCobros.ToleranciaTemporal(sesion, ToleranciaUsd, null, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaPesos = await AyudaCobros.CuentaAsync(sesion, Moneda.DOP, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        await using var limpieza = AyudaCobros.SinRastroDeCambio(bd, hoy, Moneda.DOP);
        await AyudaCobros.CargarCambioAsync(sesion, hoy, Moneda.DOP, AyudaCobros.CambioDelDia, ct);

        var facturaId = await AyudaCobros.EmitidaAsync(
            sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 1_000m, ct);

        var cobro = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.DOP, 59_455m, formaPagoId, cuentaPesos,
                tipoCambioAplicado: AyudaCobros.CambioExacto),
            ct);
        cobro.StatusCode.Should().Be(HttpStatusCode.OK, await cobro.Cuerpo());

        var ajuste = await sesion.PostAsync(
            "/api/v1/cobros/ajustes-diferencia-cambio",
            new { facturaId, tipoCambioValor = AyudaCobros.CambioExacto, motivo = "Diferencia por redondeo del cambio" },
            ct);
        ajuste.StatusCode.Should().Be(HttpStatusCode.OK, await ajuste.Cuerpo());
        var ajusteId = (await ajuste.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();

        var anulacion = await sesion.PostAsync(
            $"/api/v1/cobros/ajustes-diferencia-cambio/{ajusteId:D}/anular",
            new { motivo = "El banco sí cobró el total; el ajuste no hacía falta" },
            ct);
        anulacion.StatusCode.Should().Be(HttpStatusCode.OK, await anulacion.Cuerpo());

        var cuerpoAnulacion = await anulacion.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpoAnulacion.GetProperty("vigente").GetBoolean().Should().BeFalse();

        var filas = await AyudaCobros.AjustesAsync(bd, facturaId, ct);
        var guardada = filas.Should().ContainSingle("anular no borra: la fila sigue ahí").Which;
        guardada.AnuladoAt.Should().NotBeNull();
        guardada.AnuladoPor.Should().NotBeNull();
        guardada.MotivoAnulacion.Should().Contain("no hacía falta");

        var leida = await AyudaCobros.FacturaAsync(sesion, facturaId, ct);
        leida.GetProperty("ajustado").LeerDecimal().Should().Be(0m, "un ajuste anulado no cuenta");
        leida.GetProperty("pendiente").LeerDecimal().Should().Be(0.76m, "y la factura vuelve a tener lo que le faltaba");
        leida.GetProperty("estadoCobro").GetString().Should().Be("Parcial");
    }

    /// <summary>
    /// Ajustar es mover el saldo que la factura tiene pendiente, así que lleva el mismo permiso que
    /// registrar y anular un cobro: <c>cobro.registrar</c>, que D15 deja desactivado al empleado. Las
    /// pruebas van por la API real, que es donde vive el filtro <c>[Accion]</c>, y comprueban que el
    /// intento denegado queda en la auditoría firmada.
    /// </summary>
    [Fact]
    public async Task ElEmpleadoNoPuedeRegistrarUnPagoEnOtraMonedaNiCrearUnAjuste()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuarioEmpleado = await AyudaAuth.CrearUsuarioAsync(
            bd, AyudaFacturas.Contrasena, rol: Roles.Empleado, ct: ct);

        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);
        using var empleado = await AyudaPermisos.SesionAsync(api, usuarioEmpleado, AyudaFacturas.Contrasena, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(admin, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(admin, ct);
        var cuentaPesos = await AyudaCobros.CuentaAsync(admin, Moneda.DOP, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);

        await using var limpieza = AyudaCobros.SinRastroDeCambio(bd, hoy, Moneda.DOP);
        await AyudaCobros.CargarCambioAsync(admin, hoy, Moneda.DOP, AyudaCobros.CambioDelDia, ct);

        var facturaId = await AyudaCobros.EmitidaAsync(
            admin, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 1_000m, ct);

        var pagoEnPesos = await empleado.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.DOP, 59_455m, formaPagoId, cuentaPesos,
                tipoCambioAplicado: AyudaCobros.CambioExacto),
            ct);
        pagoEnPesos.StatusCode.Should().Be(HttpStatusCode.Forbidden, await pagoEnPesos.Cuerpo());

        var ajuste = await empleado.PostAsync(
            "/api/v1/cobros/ajustes-diferencia-cambio",
            new { facturaId, tipoCambioValor = AyudaCobros.CambioExacto, motivo = "Quiero cerrar la diferencia" },
            ct);
        ajuste.StatusCode.Should().Be(HttpStatusCode.Forbidden, await ajuste.Cuerpo());

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Cobro.AsNoTracking().CountAsync(c => c.ClienteId == clienteId, ct))
            .Should().Be(0, "los dos 403 no registran nada");
        (await contexto.AjusteDiferenciaCambio.AsNoTracking().CountAsync(a => a.FacturaId == facturaId, ct))
            .Should().Be(0);

        await using var sinRls = bd.CrearContexto(bd.CadenaSuperusuario);
        var denegadas = await sinRls.Auditoria.AsNoTracking()
            .Where(a => a.Accion == $"permiso.denegado.{Acciones.CobroRegistrar}" && a.UsuarioId == usuarioEmpleado.Id)
            .ToListAsync(ct);

        denegadas.Should().NotBeEmpty("el intento denegado queda en la auditoría firmada");
        denegadas.Should().OnlyContain(a => a.Resultado == "denegado" && a.Ip != null);
        // El recurso lo pone el filtro global de acciones: método y plantilla de la ruta.
        denegadas.Should().Contain(a => a.Datos!.RootElement.GetProperty("recurso").GetString()!.Contains("ajustes-diferencia-cambio"));
        denegadas.Should().Contain(a => a.Datos!.RootElement.GetProperty("recurso").GetString() == "POST api/v1/cobros");
    }
}