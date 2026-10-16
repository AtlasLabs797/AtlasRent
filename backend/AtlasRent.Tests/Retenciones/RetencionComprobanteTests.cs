using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Jobs;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Cobros;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Retenciones;

/// <summary>
/// F3-04: el comprobante de retención y el aviso de las facturas que lo esperan
/// (docs/ESPECIFICACION.md §3.9, D14).
/// <para>
/// Lo que se comprueba es el ciclo entero: la fila nace con la emisión, el aviso se levanta por las
/// pendientes, la recepción lo cierra y el denegado queda en la auditoría.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class RetencionComprobanteTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    /// <summary>
    /// El comprobante se crea al emitir, en la misma transacción y con los datos congelados: tipo,
    /// base, porcentaje e importe. Copiarlos es lo que hace que cambiar el tipo mañana no cambie lo
    /// que dice un comprobante que ya se emitió.
    /// </summary>
    [Fact]
    public async Task AlEmitirUnaFacturaQueRetieneSeCreaSuComprobantePendiente()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        const decimal porcentaje = 6.5m;

        var tipoRetencion = await AyudaRetenciones.CrearTipoAsync(sesion, BaseRetencion.Total, porcentaje, ct);
        var facturaId = await AyudaRetenciones.BorradorConRetencionAsync(sesion, tipoRetencion, ct);

        var emitida = await AyudaRetenciones.EmitirAsync(sesion, facturaId, ct);

        emitida.GetProperty("retencionSinComprobante")
            .GetBoolean().Should().BeTrue("la factura sale emitida con el comprobante pendiente");

        var comprobante = await AyudaRetenciones.ComprobanteAsync(bd, facturaId, ct);
        comprobante.Should().NotBeNull("el comprobante se crea al emitir, no cuando lo pida alguien");

        comprobante!.Estado.Should().Be(EstadoComprobanteRetencion.Pendiente);
        comprobante.Numero.Should().BeNull("mientras está pendiente no hay número que anotar");
        comprobante.FechaRecepcion.Should().BeNull();
        comprobante.RecibidoAt.Should().BeNull();
        comprobante.Porcentaje.Should().Be(porcentaje, "el porcentaje queda congelado");
        comprobante.Base.Should().Be(BaseRetencion.Total);
        comprobante.Importe.Should().Be(65m);
        comprobante.Tipo.Should().NotBeNullOrWhiteSpace("el nombre del tipo se copia de la configuración");

        (await AyudaRetenciones.ComprobantesAsync(bd, facturaId, ct))
            .Should().Be(1, "uno por factura, no uno por línea de retención");
    }

    /// <summary>
    /// El comprobante sale en la misma transacción que la factura. El fallo se fuerza por debajo,
    /// ocupando a mano el comprobante que va a salir: el guardado choca con el único por factura
    /// <b>después</b> de que la factura ya tenga número reservado. Si la transacción no fuera única,
    /// aquí quedarían un número de factura gastado y un comprobante retenido que no se emitió.
    /// </summary>
    [Fact]
    public async Task UnFalloDespuesDeReservarElNumeroNoDejaComprobanteDeRetencion()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var tipoRetencion = await AyudaRetenciones.CrearTipoAsync(sesion, BaseRetencion.Total, 10m, ct);
        var facturaId = await AyudaRetenciones.BorradorConRetencionAsync(sesion, tipoRetencion, ct);
        var serieId = await SerieDeAsync(sesion, facturaId, ct);

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            // Fila apurada a propósito: no es un documento real, es el choque.
            contexto.ComprobanteRetencion.Add(new ComprobanteRetencion
            {
                Id = Guid.NewGuid(),
                FacturaId = facturaId,
                Tipo = "Retención apurada",
                Base = BaseRetencion.Total,
                Porcentaje = 10m,
                Importe = 100m,
                Estado = EstadoComprobanteRetencion.Pendiente,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = Guid.NewGuid(),
            });

            await contexto.SaveChangesAsync(ct);
        }

        var emitida = await sesion.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);
        emitida.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var contextoFinal = bd.CrearContexto(bd.CadenaApp);

        (await contextoFinal.Factura.AsNoTracking().SingleAsync(f => f.Id == facturaId, ct)).Estado
            .Should().Be(EstadoFactura.Borrador, "la emisión se deshace entera");
        (await contextoFinal.Factura.AsNoTracking().SingleAsync(f => f.Id == facturaId, ct)).Numero
            .Should().BeNull("el número no se consume");
        (await contextoFinal.Serie.AsNoTracking().SingleAsync(s => s.Id == serieId, ct)).Siguiente
            .Should().Be(1, "y el contador de la serie tampoco se mueve");

        (await contextoFinal.ComprobanteRetencion.AsNoTracking()
            .CountAsync(c => c.FacturaId == facturaId, ct))
            .Should().Be(1, "solo queda la fila apurada a mano: la de la emisión no llegó a escribirse");
    }

    /// <summary>
    /// El aviso de las facturas que retienen y no han recibido su comprobante. Con dos, son dos
    /// alertas: una por factura, porque la clave es la factura y no el día.
    /// </summary>
    [Fact]
    public async Task ElTrabajoLevantaUnAvisoPorCadaFacturaRetenidaPendiente()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var tipoRetencion = await AyudaRetenciones.CrearTipoAsync(sesion, BaseRetencion.Total, 10m, ct);

        var primera = await AyudaRetenciones.BorradorConRetencionAsync(sesion, tipoRetencion, ct);
        var segunda = await AyudaRetenciones.BorradorConRetencionAsync(sesion, tipoRetencion, ct);

        await AyudaRetenciones.EmitirAsync(sesion, primera, ct);
        await AyudaRetenciones.EmitirAsync(sesion, segunda, ct);

        await EjecutarTrabajoAsync(ct);

        (await AyudaRetenciones.AlertaAsync(bd, primera, ct)).Should().NotBeNull();
        (await AyudaRetenciones.AlertaAsync(bd, segunda, ct)).Should().NotBeNull();

        // Dos facturas, dos avisos: la clave es la factura, así que una no pisa a la otra.
        (await AyudaRetenciones.AlertaAsync(bd, primera, ct))!.Tipo
            .Should().Be(ServicioRetenciones.TipoAlerta, "es el tipo que la tabla ya preveía");
    }

    /// <summary>
    /// Al recibir el comprobante de una, el aviso de esa se resuelve y el de la otra sigue vivo: son
    /// asuntos distintos y resolver uno no puede tapar el otro.
    /// </summary>
    [Fact]
    public async Task AlRecibirUnoDeLosComprobantesSeResuelveSuAvisoYElOtroSigue()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var tipoRetencion = await AyudaRetenciones.CrearTipoAsync(sesion, BaseRetencion.Total, 10m, ct);

        var primera = await AyudaRetenciones.BorradorConRetencionAsync(sesion, tipoRetencion, ct);
        var segunda = await AyudaRetenciones.BorradorConRetencionAsync(sesion, tipoRetencion, ct);

        await AyudaRetenciones.EmitirAsync(sesion, primera, ct);
        await AyudaRetenciones.EmitirAsync(sesion, segunda, ct);
        await EjecutarTrabajoAsync(ct);

        var deLaPrimera = await AyudaRetenciones.ComprobanteAsync(bd, primera, ct);
        var recibida = await sesion.PutAsync(
            $"/api/v1/comprobantes-retencion/{deLaPrimera!.Id:D}/recibir",
            new { numero = $"607-{Guid.NewGuid():N}"[..16], fechaRecepcion = "2026-10-16" },
            ct);

        recibida.StatusCode.Should().Be(HttpStatusCode.OK);
        (await AyudaRetenciones.AlertaAsync(bd, primera, ct)).Should().BeNull("el aviso se cierra al llegar el papel");
        (await AyudaRetenciones.AlertaAsync(bd, segunda, ct)).Should().NotBeNull("el otro sigue pendiente y avisa");

        // Cerrar no es borrar: la fila sigue ahí, con su fecha de cierre.
        var cerrada = await AyudaRetenciones.AlertaAsync(bd, primera, ct, otra: true);
        cerrada.Should().NotBeNull("resolver no borra el rastro: la alerta dice que pasó y cuándo");
        cerrada!.ResueltaAt.Should().NotBeNull();
    }

    /// <summary>
    /// La recepción deja el comprobante recibido con su número y su fecha, y apaga el indicador de la
    /// factura. El número es lo que dice el cliente y no se valida contra nada (D6, D14).
    /// </summary>
    [Fact]
    public async Task RecibirElComprobanteLoDejaRecibidoYApagaElIndicador()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var tipoRetencion = await AyudaRetenciones.CrearTipoAsync(sesion, BaseRetencion.Total, 10m, ct);
        var facturaId = await AyudaRetenciones.BorradorConRetencionAsync(sesion, tipoRetencion, ct);
        await AyudaRetenciones.EmitirAsync(sesion, facturaId, ct);

        var comprobante = await AyudaRetenciones.ComprobanteAsync(bd, facturaId, ct);
        const string numero = "607-PRUEBA-1";

        var recibida = await sesion.PutAsync(
            $"/api/v1/comprobantes-retencion/{comprobante!.Id:D}/recibir",
            new { numero, fechaRecepcion = "2026-10-15" },
            ct);

        recibida.StatusCode.Should().Be(HttpStatusCode.OK);

        var cuerpo = await recibida.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("estado").GetString().Should().Be(nameof(EstadoComprobanteRetencion.Recibido));
        cuerpo.GetProperty("numero").GetString().Should().Be(numero, "se guarda tal cual llega");
        cuerpo.GetProperty("fechaRecepcion").GetString().Should().Be("2026-10-15");
        cuerpo.GetProperty("recibidoAt").GetString().Should().NotBeNullOrWhiteSpace(
            "el instante lo pone el reloj de la aplicación, no quien teclea");
        cuerpo.GetProperty("importe").GetDecimal().Should().Be(100m, "lo congelado no cambia al recibirse");

        var leida = await sesion.GetAsync($"/api/v1/facturas/{facturaId:D}", ct);
        var factura = await leida.Content.ReadFromJsonAsync<JsonElement>(ct);
        factura.GetProperty("retencionSinComprobante").GetBoolean()
            .Should().BeFalse("el indicador se apaga cuando el papel llega");
    }

    /// <summary>
    /// Lo que llegó no se reescribe. Un segundo intento es un 409 que lo dice, y la fila se queda con
    /// el número que tenía.
    /// </summary>
    [Fact]
    public async Task UnComprobanteYaRecibidoNoSeVuelveARecibir()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var tipoRetencion = await AyudaRetenciones.CrearTipoAsync(sesion, BaseRetencion.Total, 10m, ct);
        var facturaId = await AyudaRetenciones.BorradorConRetencionAsync(sesion, tipoRetencion, ct);
        await AyudaRetenciones.EmitirAsync(sesion, facturaId, ct);

        var comprobante = await AyudaRetenciones.ComprobanteAsync(bd, facturaId, ct);

        (await sesion.PutAsync(
            $"/api/v1/comprobantes-retencion/{comprobante!.Id:D}/recibir",
            new { numero = "607-UNO", fechaRecepcion = "2026-10-15" }, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var repetido = await sesion.PutAsync(
            $"/api/v1/comprobantes-retencion/{comprobante.Id:D}/recibir",
            new { numero = "607-OTRO", fechaRecepcion = "2026-10-16" }, ct);

        repetido.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await repetido.Content.ReadAsStringAsync(ct)).Should().Contain("607-UNO", "el 409 dice cuál se había recibido");

        var guardado = await AyudaRetenciones.ComprobanteAsync(bd, facturaId, ct);
        guardado!.Numero.Should().Be("607-UNO");
    }

    /// <summary>
    /// El empleado puede preparar facturas pero no tocar lo que ya salió (D15): sin permiso no recibe
    /// el comprobante y el intento queda en la auditoría firmada con su IP.
    /// </summary>
    [Fact]
    public async Task ElEmpleadoNoPuedeRecibirElComprobanteYElIntentoQuedaAuditado()
    {
        var ct = TestContext.Current.CancellationToken;

        var usuarioEmpleado = await AyudaAuth.CrearUsuarioAsync(
            bd, AyudaFacturas.Contrasena, rol: Roles.Empleado, ct: ct);

        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        using var empleado = await AyudaPermisos.SesionAsync(api, usuarioEmpleado, AyudaFacturas.Contrasena, ct);

        var tipoRetencion = await AyudaRetenciones.CrearTipoAsync(admin, BaseRetencion.Total, 10m, ct);
        var facturaId = await AyudaRetenciones.BorradorConRetencionAsync(admin, tipoRetencion, ct);
        await AyudaRetenciones.EmitirAsync(admin, facturaId, ct);

        var comprobante = await AyudaRetenciones.ComprobanteAsync(bd, facturaId, ct);

        var denegado = await empleado.PutAsync(
            $"/api/v1/comprobantes-retencion/{comprobante!.Id:D}/recibir",
            new { numero = "607-EMPLEADO", fechaRecepcion = "2026-10-15" }, ct);

        denegado.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "recibir un comprobante es gestionar un documento emitido, y el empleado no puede");

        (await AyudaRetenciones.ComprobanteAsync(bd, facturaId, ct))!.Estado
            .Should().Be(EstadoComprobanteRetencion.Pendiente, "el intento denegado no cambia nada");

        // La denegación queda en la auditoría firmada, con su IP, por el filtro global de acciones.
        await using var contexto = bd.CrearContexto(bd.CadenaSuperusuario);
        var denegada = (await contexto.Auditoria.AsNoTracking()
            .Where(a => a.Accion == $"permiso.denegado.{Acciones.FacturaEmitir}" && a.UsuarioId == usuarioEmpleado.Id)
            .ToListAsync(ct))
            .Should()
            .ContainSingle("el intento denegado de este usuario queda en la auditoría").Which;

        denegada.Ip.Should().NotBeNullOrWhiteSpace();
        denegada.Resultado.Should().Be("denegado");
        denegada.Origen.Should().Be("api");
        denegada.Datos!.RootElement.GetProperty("rol").GetString().Should().Be(Roles.Empleado);
    }

    /// <summary>
    /// El caso literal del criterio de aceptación sobre la API: una factura a la que la retención
    /// cubre por completo y <b>sin un solo cobro</b>. El porcentaje del tipo es el 100% porque es el
    /// único valor con el que la retención puede igualar al total, y por eso está aquí y no en
    /// ninguna configuración del sistema: es el borde del dato, no un tipo de retención autorizado.
    /// </summary>
    [Fact]
    public async Task UnaFacturaCubiertaSoloConRetencionYSinCobrosQuedaPagada()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var tipoRetencion = await AyudaRetenciones.CrearTipoAsync(sesion, BaseRetencion.Total, 100m, ct);
        var facturaId = await AyudaRetenciones.BorradorConRetencionAsync(sesion, tipoRetencion, ct);

        await AyudaRetenciones.EmitirAsync(sesion, facturaId, ct);

        var leida = await sesion.GetAsync($"/api/v1/facturas/{facturaId:D}", ct);
        leida.StatusCode.Should().Be(HttpStatusCode.OK);
        var factura = await leida.Content.ReadFromJsonAsync<JsonElement>(ct);

        factura.GetProperty("retencion").GetDecimal().Should().Be(1_000m);
        factura.GetProperty("netoACobrar").GetDecimal().Should().Be(0m);
        factura.GetProperty("estadoCobro").GetString().Should().Be("Pagada");
        factura.GetProperty("pendiente").GetDecimal().Should().Be(0m);
        factura.GetProperty("cobrado").GetDecimal()
            .Should().Be(0m, "nada ha entrado en caja: la factura está cerrada por retención, no por cobro");
    }

    /// <summary>
    /// Y el otro camino del mismo criterio sobre la API: un cobro parcial más la retención cierran la
    /// factura entre los dos.
    /// </summary>
    [Fact]
    public async Task UnaFacturaCubiertaConRetencionYUnCobroQuedaPagadaSinContarLaRetencionComoCobro()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var tipoRetencion = await AyudaRetenciones.CrearTipoAsync(sesion, BaseRetencion.Total, 10m, ct);
        var (clienteId, contratoId) = await AyudaRetenciones.ClienteConContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var facturaId = await AyudaFacturas.CrearFacturaAsync(
            sesion, clienteId, serieId, Moneda.USD, hoy.AddDays(30), ct, contratoId);
        await AyudaFacturas.AgregarLineaAsync(
            sesion, facturaId, TipoLineaFactura.Renta, 1m, 1_000m, null, ct);
        await AyudaRetenciones.AgregarLineaRetencionAsync(sesion, facturaId, tipoRetencion, ct);

        var emitida = await AyudaRetenciones.EmitirAsync(sesion, facturaId, ct);

        var total = emitida.GetProperty("total").GetDecimal();
        var retencion = emitida.GetProperty("retencion").GetDecimal();
        var neto = emitida.GetProperty("netoACobrar").GetDecimal();

        neto.Should().Be(total - retencion);

        // Sin cobros y con una retención que solo cubre una parte, sigue pendiente: retener no es cobrar.
        var sinCobros = await AyudaCobros.FacturaAsync(sesion, facturaId, ct);

        sinCobros.GetProperty("estadoCobro").GetString().Should().Be("Pendiente");
        sinCobros.GetProperty("pendiente").GetDecimal().Should().Be(neto, "lo que queda es el neto, sin un céntimo cobrado");
        sinCobros.GetProperty("cobrado").GetDecimal().Should().Be(0m);

        // Con un cobro justo del neto, el cobro y la retención juntos cierran la factura: este es el
        // criterio de aceptación literal, «cobro + retención = total ⇒ Pagada». Y lo retenido no se
        // cuela dentro de lo cobrado: si lo hiciera, el informe de caja mentiría.
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);

        var cobrado = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId,
                contratoId,
                hoy,
                Moneda.USD,
                neto,
                formaPagoId,
                cuentaId,
                asignaciones: new[] { new { facturaId, importe = neto } }),
            ct);
        cobrado.StatusCode.Should().Be(HttpStatusCode.OK, await cobrado.Content.ReadAsStringAsync(ct));

        var conCobro = await AyudaCobros.FacturaAsync(sesion, facturaId, ct);

        conCobro.GetProperty("estadoCobro").GetString().Should().Be("Pagada");
        conCobro.GetProperty("pendiente").GetDecimal().Should().Be(0m);
        conCobro.GetProperty("cobrado").GetDecimal().Should().Be(neto, "lo cobrado es solo el dinero del cobro");
    }

    // ---------------------------------------------------------------- apoyo

    /// <summary>Serie de la factura, para comprobar que el contador no se movió.</summary>
    private async Task<Guid> SerieDeAsync(SesionCliente sesion, Guid facturaId, CancellationToken ct)
    {
        var leida = await sesion.GetAsync($"/api/v1/facturas/{facturaId:D}", ct);
        leida.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await leida.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("serieId").GetGuid();
    }

    /// <summary>Una pasada del trabajo diario contra la base de verdad, como corre el planificador.</summary>
    private async Task EjecutarTrabajoAsync(CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var reloj = new RelojPrueba();

        await new TrabajoRetenciones(contexto, new ServicioAlertas(contexto, reloj), reloj)
            .EjecutarAsync(ct);
    }
}