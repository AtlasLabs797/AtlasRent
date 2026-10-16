using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Comprobantes;

/// <summary>
/// F3-03 sobre el criterio de <c>docs/PLAN.md</c>: «50 emisiones concurrentes en serie 3:
/// comprobantes únicos; agotado → emisión bloqueada sin consumir número de factura».
/// <para>
/// La carrera es de verdad, con <see cref="Barrier"/> y el patrón de <c>FacturaConcurrenciaTests</c>:
/// las peticiones salen a la vez contra la misma serie, cada una con su contexto y su transacción.
/// Lo que se mira son las dos mitades del criterio: que los comprobantes salgan únicos y
/// consecutivos, y que un numerador que no puede dar más rechace la emisión <b>sin haber gastado el
/// número de factura</b>.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ComprobanteEmisionTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const int Emisiones = 50;

    /// <summary>
    /// Cincuenta emisiones simultáneas en una serie fiscal: cincuenta comprobantes, todos distintos
    /// y todos seguidos, y el contador del numerador donde toca. El número de factura y el de
    /// comprobante van por caminos distintos y ambos tienen que salir bien.
    /// </summary>
    [Fact]
    public async Task CincuentaEmisionesSimultaneasSacanCincuentaComprobantesUnicosYConsecutivos()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var serieId = await AyudaComprobante.SerieFiscalAsync(sesion, ct);
        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);
        await AyudaComprobante.NumeradorAsync(sesion, serieId, hoy, ct, umbralAlerta: 1);

        var facturas = new List<Guid>(Emisiones);
        for (var i = 0; i < Emisiones; i++)
        {
            facturas.Add(await AyudaComprobante.BorradorAsync(sesion, clienteId, serieId, ct));
        }

        using var barrera = new Barrier(Emisiones);
        var respuestas = new HttpResponseMessage[Emisiones];

        var emisiones = facturas.Select((facturaId, i) => Task.Run(async () =>
        {
            barrera.SignalAndWait();

            respuestas[i] = await sesion.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);
        }, ct)).ToArray();

        await Task.WhenAll(emisiones);

        try
        {
            foreach (var respuesta in respuestas)
            {
                respuesta.StatusCode.Should().Be(HttpStatusCode.OK, "ninguna emisión se cae");
            }

            var comprobantes = await AyudaComprobante.ComprobantesAsync(bd, serieId, ct);
            comprobantes.Should().HaveCount(Emisiones, "cada factura emitida lleva su comprobante");
            comprobantes.Select(c => c.Numero).Should().OnlyHaveUniqueItems(
                "dos comprobantes con el mismo número es lo que no puede pasar");
            comprobantes.Should().OnlyContain(c => c.Estado == EstadoComprobanteFiscal.Emitido);

            var numerador = await AyudaComprobante.NumeradorDeAsync(bd, serieId, ct);
            comprobantes.Select(c => c.Numero).OrderBy(n => n)
                .Should().Equal(Enumerable.Range(numerador!.RangoDesde, Emisiones));

            // Los dos contadores, cada uno en su sitio: el de la serie y el del numerador.
            (await AyudaComprobante.SiguienteDeSerieAsync(bd, serieId, ct))
                .Should().Be(Emisiones + 1);
            numerador.SiguienteNumero.Should().Be(numerador.RangoDesde + Emisiones);

            await using var contexto = bd.CrearContexto(bd.CadenaApp);
            (await contexto.NumeroSerie.AsNoTracking().CountAsync(n => n.SerieId == serieId, ct))
                .Should().Be(Emisiones, "y cada factura sigue teniendo su número de serie");

            // Y la red que lo hace posible está en la base, no solo en el modelo.
            var indice = await contexto.Database
                .SqlQueryRaw<string>(
                    """
                    SELECT indexdef AS "Value"
                    FROM pg_indexes
                    WHERE schemaname = 'public'
                      AND tablename = 'comprobante_fiscal'
                      AND indexname = 'ix_comprobante_fiscal_serie_id_numero'
                    """)
                .SingleAsync(ct);
            indice.Should().Contain("UNIQUE", "el índice único de (serie, número) es la red de la carrera");
        }
        finally
        {
            foreach (var respuesta in respuestas.Where(r => r is not null))
            {
                respuesta.Dispose();
            }
        }
    }

    /// <summary>
    /// Numerador agotado: la emisión se rechaza y <b>no se consume número de factura</b>. Se lee de
    /// las dos tablas —<c>serie.siguiente</c> y <c>numero_serie</c>— porque un 409 con la factura
    /// todavía en borrador no dice por sí solo que el contador quedó intacto.
    /// </summary>
    [Fact]
    public async Task ConElNumeradorAgotadoLaEmisionSeBloqueaSinConsumirElNumeroDeFactura()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var serieId = await AyudaComprobante.SerieFiscalAsync(sesion, ct);
        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        // Un rango de un solo número, ya gastado: siguiente_numero queda una posición por encima.
        var desde = Random.Shared.Next(1, 100);
        await AyudaComprobante.NumeradorAsync(
            sesion, serieId, hoy, ct,
            rangoDesde: desde, rangoHasta: desde, siguienteNumero: desde + 1);

        var facturaId = await AyudaComprobante.BorradorAsync(sesion, clienteId, serieId, ct);

        var emitida = await sesion.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);

        emitida.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await emitida.Content.ReadAsStringAsync(ct)).Should().Contain("agotó");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var fila = await contexto.Factura.AsNoTracking().SingleAsync(f => f.Id == facturaId, ct);
        fila.Estado.Should().Be(EstadoFactura.Borrador, "la factura no llegó a salir");
        fila.Numero.Should().BeNull();

        (await contexto.Serie.AsNoTracking().SingleAsync(s => s.Id == serieId, ct)).Siguiente
            .Should().Be(1, "la emisión rechazada no puede gastar el número de la factura");
        (await contexto.NumeroSerie.AsNoTracking().CountAsync(n => n.SerieId == serieId, ct))
            .Should().Be(0, "ni queda fila en el histórico");
        (await contexto.ComprobanteFiscal.AsNoTracking().CountAsync(c => c.SerieId == serieId, ct))
            .Should().Be(0, "ni comprobante");
    }

    /// <summary>
    /// Numerador vencido: mismo bloqueo y mismo motivo por el que no se gasta número. Un rango que ya
    /// no vale no se puede salvar gastando números de factura hasta llegar al final.
    /// </summary>
    [Fact]
    public async Task ConElNumeradorVencidoLaEmisionSeBloqueaSinConsumirElNumeroDeFactura()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var serieId = await AyudaComprobante.SerieFiscalAsync(sesion, ct);
        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);
        await AyudaComprobante.NumeradorAsync(sesion, serieId, hoy, ct, vencimiento: hoy.AddDays(-1));

        var facturaId = await AyudaComprobante.BorradorAsync(sesion, clienteId, serieId, ct);

        var emitida = await sesion.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);

        emitida.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await emitida.Content.ReadAsStringAsync(ct)).Should().Contain("venció");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var fila = await contexto.Factura.AsNoTracking().SingleAsync(f => f.Id == facturaId, ct);
        fila.Estado.Should().Be(EstadoFactura.Borrador);
        fila.Numero.Should().BeNull();

        (await contexto.Serie.AsNoTracking().SingleAsync(s => s.Id == serieId, ct)).Siguiente
            .Should().Be(1, "vencido también bloquea antes de gastar el número de la factura");
        (await contexto.NumeroSerie.AsNoTracking().CountAsync(n => n.SerieId == serieId, ct))
            .Should().Be(0);
        (await contexto.ComprobanteFiscal.AsNoTracking().CountAsync(c => c.SerieId == serieId, ct))
            .Should().Be(0);
    }

    /// <summary>
    /// Serie fiscal sin numerador: 409 con un motivo accionable. No es una degradación que emita sin
    /// comprobante: una factura de serie fiscal sin comprobante es un documento que no se puede
    /// declarar, así que el mensaje tiene que decir dónde se configura.
    /// </summary>
    [Fact]
    public async Task UnaSerieFiscalSinNumeradorNoEmiteYLoDice()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var serieId = await AyudaComprobante.SerieFiscalAsync(sesion, ct);
        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct);
        var facturaId = await AyudaComprobante.BorradorAsync(sesion, clienteId, serieId, ct);

        var emitida = await sesion.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);

        emitida.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var cuerpo = await emitida.Content.ReadAsStringAsync(ct);
        cuerpo.Should().Contain("numerador");
        cuerpo.Should().Contain("Numeradores fiscales", "el mensaje dice dónde se configura, no solo qué faltó");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Factura.AsNoTracking().SingleAsync(f => f.Id == facturaId, ct)).Estado
            .Should().Be(EstadoFactura.Borrador);
    }

    /// <summary>
    /// El comprobante es de la misma transacción que la factura. El fallo se fuerza por debajo,
    /// ocupando a mano el número de comprobante que va a sacar el numerador: el guardado del
    /// comprobante choca con el índice único <b>después</b> de que la factura ya tenga número
    /// reservado. Si la transacción no fuera única, aquí quedarían un número de factura gastado y un
    /// hueco en el rango autorizado del comprobante.
    /// </summary>
    [Fact]
    public async Task UnFalloDespuesDeReservarElNumeroDeFacturaNoDejaNiEseNiElDeComprobante()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var serieId = await AyudaComprobante.SerieFiscalAsync(sesion, ct);
        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var desde = Random.Shared.Next(1, 100);
        await AyudaComprobante.NumeradorAsync(
            sesion, serieId, hoy, ct, rangoDesde: desde, rangoHasta: desde + 1);

        var facturaId = await AyudaComprobante.BorradorAsync(sesion, clienteId, serieId, ct);

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            // Se ocupa por fuera de la API el número que el numerador va a sacar. La fila apurada
            // está puesta a propósito: no es un documento real, es el choque.
            contexto.ComprobanteFiscal.Add(new ComprobanteFiscal
            {
                Id = Guid.NewGuid(),
                FacturaId = facturaId,
                SerieId = serieId,
                Numero = desde,
                Tipo = $"T{Guid.NewGuid():N}"[..6],
                Estado = EstadoComprobanteFiscal.Emitido,
                RegistradoAt = DateTimeOffset.UtcNow,
                RegistradoPor = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
            });

            await contexto.SaveChangesAsync(ct);
        }

        var emitida = await sesion.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);
        emitida.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var fila = await contexto.Factura.AsNoTracking().SingleAsync(f => f.Id == facturaId, ct);
            fila.Estado.Should().Be(EstadoFactura.Borrador, "la factura no llegó a salir");
            fila.Numero.Should().BeNull();

            (await contexto.Serie.AsNoTracking().SingleAsync(s => s.Id == serieId, ct)).Siguiente
                .Should().Be(1, "el número de factura no se consumió: la transacción entera se deshizo");
            (await contexto.NumeroSerie.AsNoTracking().CountAsync(n => n.SerieId == serieId, ct))
                .Should().Be(0, "no hay fila en el histórico de la serie");
            (await contexto.NumeradorFiscal.AsNoTracking().SingleAsync(n => n.SerieId == serieId, ct))
                .SiguienteNumero.Should().Be(desde, "el número de comprobante tampoco se consumió");
            (await contexto.ComprobanteFiscal.AsNoTracking().CountAsync(c => c.SerieId == serieId, ct))
                .Should().Be(1, "solo queda la fila apurada que se puso a mano");

            await using var contextoSinRls = bd.CrearContexto(bd.CadenaSuperusuario);
            (await contextoSinRls.Auditoria.AsNoTracking().CountAsync(
                a => a.Accion == "factura.emitida" && a.EntidadId == facturaId.ToString("D"), ct))
                .Should().Be(0, "la auditoría va en la misma transacción que la factura");
        }
    }
}