using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Facturas;

/// <summary>
/// F3-02, criterio de <c>docs/PLAN.md</c>: «50 emisiones concurrentes: números únicos y sin huecos por
/// fallo».
/// <para>
/// La carrera es de verdad, con <see cref="Barrier"/>: las 50 peticiones salen a la vez contra la
/// misma serie activa, cada una con su contexto y su transacción, como en producción. Lo que se
/// comprueba son las tres cosas que pueden salir mal —un número repetido, un número que se pierde, un
/// número sin fila en el histórico— y las tres tienen que salir bien a la vez.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class FacturaConcurrenciaTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const int Emisiones = 50;

    [Fact]
    public async Task CincuentaEmisionesSimultaneasSacanCincuentaNumerosConsecutivos()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct);

        // Las facturas se dejan en borrador antes: la carrera que importa es la de la emisión, que es
        // la que abre la transacción, saca el número y audita.
        var facturas = new List<Guid>(Emisiones);
        for (var i = 0; i < Emisiones; i++)
        {
            facturas.Add(await AyudaFacturas.BorradorConLineaAsync(
                sesion, clienteId, serieId, Moneda.USD, ct));
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

            await using var contexto = bd.CrearContexto(bd.CadenaApp);

            var emitidas = await contexto.Factura.AsNoTracking()
                .Where(f => f.SerieId == serieId)
                .Select(f => f.Numero)
                .ToListAsync(ct);

            emitidas.Should().HaveCount(Emisiones, "las 50 facturas salieron emitidas");
            emitidas.Should().OnlyContain(n => n != null, "ninguna se quedó sin número");
            emitidas.Should().OnlyHaveUniqueItems("dos facturas con el mismo número es lo que no puede pasar");

            var ordenados = emitidas.OrderBy(n => n).Select(n => n!.Value).ToList();
            ordenados.Should().Equal(Enumerable.Range(1, Emisiones));

            var historico = await contexto.NumeroSerie.AsNoTracking()
                .Where(n => n.SerieId == serieId)
                .Select(n => n.Numero)
                .OrderBy(n => n)
                .ToListAsync(ct);

            historico.Should().Equal(Enumerable.Range(1, Emisiones));

            (await contexto.Serie.AsNoTracking().SingleAsync(s => s.Id == serieId, ct)).Siguiente
                .Should().Be(Emisiones + 1, "el contador quedó uno más allá del último emitido");

            // Y la red que lo hace posible está en la base, no solo en el código.
            var indice = await contexto.Database
                .SqlQueryRaw<string>(
                    """
                    SELECT indexdef AS "Value"
                    FROM pg_indexes
                    WHERE schemaname = 'public'
                      AND tablename = 'factura'
                      AND indexname = 'ix_factura_serie_id_numero'
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
    /// El número de cada factura emitida es el que la serie le asignó a ella y no el de otra: con
    /// emisiones simultáneas, cada factura conserva el suyo y todas comparten el día de emisión.
    /// </summary>
    [Fact]
    public async Task CadaFacturaConservaSuNumeroBajoConcurrencia()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var facturas = new List<Guid>();
        for (var i = 0; i < 8; i++)
        {
            facturas.Add(await AyudaFacturas.BorradorConLineaAsync(
                sesion, clienteId, serieId, Moneda.USD, ct));
        }

        using var barrera = new Barrier(facturas.Count);
        var emisiones = facturas.Select(facturaId => Task.Run(async () =>
        {
            barrera.SignalAndWait();

            var respuesta = await sesion.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
            respuesta.Dispose();
        }, ct)).ToArray();

        await Task.WhenAll(emisiones);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var filas = await contexto.Factura.AsNoTracking()
            .Where(f => f.SerieId == serieId)
            .Select(f => new { f.Id, f.Numero, f.FechaEmision })
            .ToListAsync(ct);

        foreach (var factura in facturas)
        {
            filas.Should().ContainSingle(f => f.Id == factura, "la factura que se emitió es la que se emitió");
        }

        filas.Select(f => f.Numero).Should().OnlyHaveUniqueItems();
        filas.Should().OnlyContain(f => f.FechaEmision == hoy, "todas comparten el día de emisión");
    }

    /// <summary>
    /// Una serie nueva arranca en 1 y su primera emisión sale con ese número. Es lo que hace que el
    /// rango 1..N de la prueba de concurrencia sea comprobable y no una casualidad del orden.
    /// </summary>
    [Fact]
    public async Task UnaSerieNuevaEmiteDesdeElNumeroUno()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct);
        var facturaId = await AyudaFacturas.BorradorConLineaAsync(
            sesion, clienteId, serieId, Moneda.USD, ct);

        var emitida = await sesion.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);
        emitida.StatusCode.Should().Be(HttpStatusCode.OK);

        var cuerpo = await emitida.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("numero").GetInt32().Should().Be(1);
        cuerpo.GetProperty("estado").GetString().Should().Be("Emitida");
    }
}
