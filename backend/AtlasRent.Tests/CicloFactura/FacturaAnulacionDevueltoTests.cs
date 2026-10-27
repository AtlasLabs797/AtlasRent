using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cobros;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.CicloFactura;

/// <summary>
/// BUG-020: anular una factura de un cobro con varias facturas no puede hacer desaparecer el dinero
/// de esa parte. Decisión del responsable (26/10/2026): nada de saldo a favor; se anula la
/// asignación de A y su importe queda marcado como devuelto en el cobro.
/// <para>
/// Invariante que se comprueba contra la tabla: <c>recibido + crédito usado = asignado vigente +
/// excedente + devuelto</c>, en la moneda recibida.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class FacturaAnulacionDevueltoTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    /// <summary>Cobro 1000 = A 500 + B 500; anular A deja B pagada y 500 devueltos, sin saldo a favor.</summary>
    [Fact]
    public async Task AnularUnaFacturaDeUnCobroMultiFacturaMarcaSuPartePorDevuelta()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var a = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 500m, ct);
        var b = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 500m, ct);

        var cobroId = await AyudaCiclo.CobrarAsync(
            sesion, clienteId, contratoId, hoy, 1_000m, formaPagoId, cuentaId, ct, (a, 500m), (b, 500m));

        var saldoAntes = await AyudaCobros.SaldoAsync(bd, contratoId, Moneda.USD, ct);

        await AyudaCiclo.AnularAsync(sesion, a, "Se emitió por error", ct);

        var cobro = await AyudaCiclo.CobroFilaAsync(bd, cobroId, ct);
        cobro.Estado.Should().Be(EstadoCobro.Registrado);
        cobro.ImporteDevuelto.Should().Be(500m, "la parte de A queda marcada como devuelta");
        await InvarianteAsync(cobroId, ct);

        var asignaciones = await AyudaCiclo.AsignacionesDeCobroAsync(bd, cobroId, ct);
        var deA = asignaciones.Single(x => x.FacturaId == a);
        deA.Vigente.Should().BeFalse();
        deA.AnuladaAt.Should().NotBeNull("fecha, usuario y motivo de la devolución quedan en la asignación");
        deA.AnuladaPor.Should().NotBeNull();
        deA.MotivoAnulacion.Should().Be("Se emitió por error");

        (await AyudaCobros.FacturaAsync(sesion, b, ct)).GetProperty("estadoCobro").GetString()
            .Should().Be(nameof(EstadoCobroFactura.Pagada));

        (await AyudaCobros.SaldoAsync(bd, contratoId, Moneda.USD, ct))
            .Should().Be(saldoAntes, "no hay saldos a favor: lo devuelto no se envía al saldo");
    }

    /// <summary>
    /// Mismo caso con el cobro en pesos y las facturas en dólares: lo devuelto va en la moneda
    /// recibida (la de la asignación), no en la de la factura.
    /// </summary>
    [Fact]
    public async Task LoDevueltoVaEnLaMonedaRecibidaDelCobro()
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

        var a = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 500m, ct);
        var b = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 500m, ct);

        var recibido = Math.Round(1_000m * AyudaCobros.CambioExacto, 2, MidpointRounding.AwayFromZero);
        var registrado = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.DOP, recibido, formaPagoId, cuentaPesos,
                asignaciones: new[] { new { facturaId = a, importe = 500m }, new { facturaId = b, importe = 500m } },
                tipoCambioAplicado: AyudaCobros.CambioExacto),
            ct);
        registrado.StatusCode.Should().Be(HttpStatusCode.OK, await registrado.Cuerpo());

        Guid cobroId;
        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            cobroId = await contexto.CobroAsignacion.AsNoTracking()
                .Where(x => x.FacturaId == a).Select(x => x.CobroId).SingleAsync(ct);
        }

        await AyudaCiclo.AnularAsync(sesion, a, "Moneda equivocada", ct);

        var cobro = await AyudaCiclo.CobroFilaAsync(bd, cobroId, ct);
        cobro.Estado.Should().Be(EstadoCobro.Registrado);
        cobro.ImporteDevuelto.Should().Be(
            Math.Round(500m * AyudaCobros.CambioExacto, 2, MidpointRounding.AwayFromZero),
            "la parte de A, en pesos");
        await InvarianteAsync(cobroId, ct);
    }

    /// <summary>
    /// Cobro de una sola factura con excedente: se anula el cobro entero (comportamiento de F3-08) y
    /// lo que pagaba la factura queda también como devuelto, de modo que la invariante cuadra igual.
    /// </summary>
    [Fact]
    public async Task UnCobroDeUnaSolaFacturaQuedaCoherenteConLaMismaRegla()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var a = await AyudaCobros.EmitidaAsync(sesion, clienteId, contratoId, serieId, Moneda.USD, hoy.AddDays(10), 300m, ct);
        var cobroId = await AyudaCiclo.CobrarAsync(
            sesion, clienteId, contratoId, hoy, 400m, formaPagoId, cuentaId, ct, (a, 300m));

        await AyudaCiclo.AnularAsync(sesion, a, "Error de captura", ct);

        var cobro = await AyudaCiclo.CobroFilaAsync(bd, cobroId, ct);
        cobro.Estado.Should().Be(EstadoCobro.Anulado);
        cobro.ImporteDevuelto.Should().Be(300m);
        cobro.Excedente.Should().Be(100m);
        await InvarianteAsync(cobroId, ct);
    }

    private async Task InvarianteAsync(Guid cobroId, CancellationToken ct)
    {
        var cobro = await AyudaCiclo.CobroFilaAsync(bd, cobroId, ct);
        var asignaciones = await AyudaCiclo.AsignacionesDeCobroAsync(bd, cobroId, ct);

        var vigente = asignaciones.Where(a => a.Vigente).Sum(a => a.ImporteRecibido ?? a.Importe);

        (cobro.ImporteRecibido + cobro.SaldoFavorUsado)
            .Should().Be(
                vigente + cobro.Excedente + cobro.ImporteDevuelto,
                "recibido + crédito usado = asignado vigente + excedente + devuelto");
    }
}
