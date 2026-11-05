using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Configuracion;

/// <summary>
/// F1-08: el contador de series (docs/ESPECIFICACION.md §3.7, docs/DOC_TECNICA.md §4.3,
/// AGENTS.md §6). Sin números repetidos ni huecos bajo concurrencia, y un número emitido nunca se
/// reutiliza.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class SeriesContadorTests(ContenedorBdFixture bd)
{
    private const int EmisionesSimultaneas = 24;

    /// <summary>
    /// Varias emisiones a la vez, cada una con su conexión y su transacción, como en producción.
    /// Los números que salen tienen que ser 1..N sin repetir y sinholes.
    /// </summary>
    [Fact]
    public async Task ElContadorNoRepiteNiDejaHuecosBajoConcurrencia()
    {
        var ct = TestContext.Current.CancellationToken;
        var serieId = await CrearSerieAsync(ct);

        var numeros = await Task.WhenAll(
            Enumerable.Range(0, EmisionesSimultaneas).Select(_ => ReservarAsync(serieId, ct)));

        numeros.Should().OnlyHaveUniqueItems();
        numeros.OrderBy(n => n).Should().Equal(Enumerable.Range(1, EmisionesSimultaneas));

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        // 'siguiente' es el número que se emitirá a continuación: uno más allá del último emitido.
        (await contexto.Serie.AsNoTracking().SingleAsync(s => s.Id == serieId, ct)).Siguiente
            .Should().Be(EmisionesSimultaneas + 1);

        var emitidos = await contexto.NumeroSerie.AsNoTracking()
            .Where(n => n.SerieId == serieId)
            .Select(n => n.Numero)
            .OrderBy(n => n)
            .ToListAsync(ct);
        emitidos.Should().Equal(Enumerable.Range(1, EmisionesSimultaneas));
    }

    /// <summary>Un número emitido no se reutiliza: el contador nunca retrocede (D28).</summary>
    [Fact]
    public async Task UnNumeroEmitidoNoSeReutiliza()
    {
        var ct = TestContext.Current.CancellationToken;
        var serieId = await CrearSerieAsync(ct);

        var primero = await ReservarAsync(serieId, ct);
        var segundo = await ReservarAsync(serieId, ct);
        var tercero = await ReservarAsync(serieId, ct);

        primero.Should().Be(1);
        segundo.Should().Be(2);
        tercero.Should().Be(3);
    }

    /// <summary>
    /// Una factura anulada marca su número como anulado, no lo libera: el siguiente número sigue
    /// siendo el de después y el anulado no vuelve a salir.
    /// </summary>
    [Fact]
    public async Task UnNumeroAnuladoNoSeLibera()
    {
        var ct = TestContext.Current.CancellationToken;
        var serieId = await CrearSerieAsync(ct);
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, "Clave-de-prueba-2026", rol: Roles.Admin, ct: ct);

        var primero = await ReservarAsync(serieId, ct);
        var segundo = await ReservarAsync(serieId, ct);

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var servicio = new ServicioSeries(contexto, new AuditoriaService(contexto), TimeProvider.System);
            await servicio.AnularNumeroAsync(serieId, primero, "Factura anulada por el cliente", usuario.Id,
                "198.51.100.9", ct);
        }

        var siguiente = await ReservarAsync(serieId, ct);

        siguiente.Should().Be(segundo + 1, "anular un número no devuelve el contador atrás");

        await using var verificacion = bd.CrearContexto(bd.CadenaApp);
        var anulado = await verificacion.NumeroSerie.AsNoTracking()
            .SingleAsync(n => n.SerieId == serieId && n.Numero == primero, ct);
        anulado.Estado.Should().Be(EstadoNumeroSerie.Anulado);
        anulado.MotivoAnulacion.Should().Be("Factura anulada por el cliente");

        (await verificacion.NumeroSerie.AsNoTracking().CountAsync(n => n.SerieId == serieId, ct))
            .Should().Be(3, "no sale un número nuevo por la anulación");
    }

    /// <summary>
    /// SEC-017: el motivo va en el JSON de auditoría y lo escribe quien anula. Con comillas, saltos
    /// de línea o un trozo de JSON la operación sigue funcionando y la auditoría guarda el motivo
    /// literal y el número correcto, sin que el motivo pise campos.
    /// </summary>
    [Theory]
    [InlineData("Cliente dijo \"no pagó\"\nx")]
    [InlineData("x\",\"numero\":999,\"motivo\":\"y")]
    public async Task ElMotivoDeAnulacionNoRompeNiPisaLaAuditoria(string motivo)
    {
        var ct = TestContext.Current.CancellationToken;
        var serieId = await CrearSerieAsync(ct);
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, "Clave-de-prueba-2026", rol: Roles.Admin, ct: ct);
        var numero = await ReservarAsync(serieId, ct);

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var servicio = new ServicioSeries(contexto, new AuditoriaService(contexto), TimeProvider.System);
            await servicio.AnularNumeroAsync(serieId, numero, motivo, usuario.Id, "198.51.100.9", ct);
        }

        await using var sinRls = bd.CrearContexto(bd.CadenaSuperusuario);
        var fila = await sinRls.NumeroSerie.AsNoTracking()
            .SingleAsync(n => n.SerieId == serieId && n.Numero == numero, ct);
        var registro = await sinRls.Auditoria.AsNoTracking()
            .SingleAsync(a => a.Accion == "numero_serie.anulado" && a.EntidadId == fila.Id.ToString("D"), ct);

        registro.Datos!.RootElement.GetProperty("motivo").GetString().Should().Be(motivo.Trim());
        registro.Datos.RootElement.GetProperty("numero").GetInt32().Should().Be(numero);
    }

    [Fact]
    public async Task UnaSerieDesactivadaNoEmiteNumeros()
    {
        var ct = TestContext.Current.CancellationToken;
        var serieId = await CrearSerieAsync(ct);

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var serie = await contexto.Serie.SingleAsync(s => s.Id == serieId, ct);
            serie.Activa = false;
            await contexto.SaveChangesAsync(ct);
        }

        var excepcion = await Record.ExceptionAsync(() => ReservarAsync(serieId, ct));

        excepcion.Should().BeOfType<KeyNotFoundException>();
    }

    // ---------------------------------------------------------------- auxiliares

    /// <summary>
    /// Emisión en su propia conexión y su propia transacción: sin esto no hay concurrencia real y
    /// la prueba no demostraría nada del contador.
    /// </summary>
    private async Task<int> ReservarAsync(Guid serieId, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var servicio = new ServicioSeries(contexto, new AuditoriaService(contexto), TimeProvider.System);

        return await servicio.ReservarNumeroAsync(serieId, Guid.NewGuid(), ct);
    }

    /// <summary>
    /// Números de serie de las pruebas. Van por encima de 1000, fuera del rango que acepta la API
    /// (1-999), para no chocar con las series que crean otras pruebas ni con las de la
    /// especificación.
    /// </summary>
    private static int _siguienteNumero = 1000;

    private async Task<Guid> CrearSerieAsync(CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var serie = new Serie
        {
            Id = Guid.NewGuid(),
            Numero = Interlocked.Increment(ref _siguienteNumero),
            Prefijo = $"P{Guid.NewGuid():N}"[..9],
            Tipo = TipoSerie.Normal,
            Moneda = Moneda.USD,
            Siguiente = 1,
            Activa = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        contexto.Serie.Add(serie);
        await contexto.SaveChangesAsync(ct);

        return serie.Id;
    }
}