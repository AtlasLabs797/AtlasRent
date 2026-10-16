using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Jobs;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Comprobantes;

/// <summary>
/// F3-03: los avisos del numerador de comprobante (§3.7).
/// <para>
/// Se prueba <b>el trabajo contra la base de datos real</b>, no el servicio de alertas con un doble:
/// lo que importa de un aviso no es a qué método se llama, sino que la fila quede viva mientras el
/// problema siga y se cierre cuando deje. Eso solo se ve leyendo <c>alerta</c> después de que el
/// trabajo haya pasado.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ComprobanteAlertasTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    /// <summary>
    /// Por debajo del umbral: el aviso nace y se queda vivo. Al día siguiente es la misma fila,
    /// refrescada y sin acumular: el aviso no es «hoy quedan pocos», es «quedan pocos».
    /// </summary>
    [Fact]
    public async Task UnNumeradorPorDebajoDelUmbralLevantaUnAvisoVivoYSinDuplicar()
    {
        var ct = TestContext.Current.CancellationToken;
        var serieId = await SerieConNumeradorAsync(ct, umbralAlerta: 10);

        await EjecutarTrabajoAsync(ct);

        var clave = ServicioComprobantes.ClavePocos(serieId);
        var primera = await AyudaComprobante.AlertaAsync(bd, clave, ct);

        primera.Should().NotBeNull("un numerador por debajo del umbral tiene que avisar");
        primera!.Tipo.Should().Be("comprobante");
        primera.ResueltaAt.Should().BeNull();
        primera.Repeticiones.Should().Be(1);
        primera.Mensaje.Should().Contain("umbral");

        // Al día siguiente el aviso sigue siendo el mismo, refrescado y sin duplicar filas.
        await EjecutarTrabajoAsync(ct);

        var segunda = await AyudaComprobante.AlertaAsync(bd, clave, ct);
        segunda!.Id.Should().Be(primera.Id, "es la misma fila de siempre, actualizada");
        segunda.Repeticiones.Should().Be(2);
        segunda.ResueltaAt.Should().BeNull("el problema sigue abierto");
    }

    /// <summary>
    /// Vencido y agotado levantan los suyos, con claves distintas: son dos hechos y quien lee tiene
    /// que poder distinguir «no vale más este rango» de «este rango ya se terminó».
    /// </summary>
    [Fact]
    public async Task UnNumeradorVencidoYAgotadoLevantaSusDosAvisos()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var serieId = await AyudaComprobante.SerieFiscalAsync(sesion, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        // Rango de un número, ya gastado y además vencido: se comprueban los dos a la vez.
        var desde = Random.Shared.Next(1, 100);
        await AyudaComprobante.NumeradorAsync(
            sesion, serieId, hoy, ct,
            vencimiento: hoy.AddDays(-1),
            rangoDesde: desde,
            rangoHasta: desde,
            siguienteNumero: desde + 1);

        await EjecutarTrabajoAsync(ct);

        var vencido = await AyudaComprobante.AlertaAsync(
            bd, ServicioComprobantes.ClaveVencido(serieId), ct);
        vencido.Should().NotBeNull("un rango vencido tiene que avisar");
        vencido!.ResueltaAt.Should().BeNull();

        var agotado = await AyudaComprobante.AlertaAsync(
            bd, ServicioComprobantes.ClaveAgotado(serieId), ct);
        agotado.Should().NotBeNull("un rango sin números tiene que avisar");
        agotado!.ResueltaAt.Should().BeNull();

        // Agotado suprime «pocos»: los dos avisos serían dos filas para el mismo hecho.
        (await AyudaComprobante.AlertaAsync(bd, ServicioComprobantes.ClavePocos(serieId), ct))
            .Should().BeNull("con el rango agotado el aviso útil es el que pide uno nuevo");
    }

    /// <summary>
    /// Lo que de verdad hace útil un aviso es cerrarse: ampliar el rango deja de ser un problema y el
    /// trabajo siguiente tiene que cerrar la fila, sin borrarla y sin pisar cuándo se cerró.
    /// <para>
    /// Lo que sale de la condición es <b>ampliar el rango</b>, no subir el umbral: subir el umbral no
    /// saca al numerador de debajo de él, que es justo lo que el aviso está diciendo.
    /// </para>
    /// <para>
    /// Y lo que se deja por escrito aquí es un límite de <see cref="ServicioAlertas"/>, no de este
    /// trabajo: <c>alerta.clave</c> es única, así que cuando la alerta ya está cerrada el
    /// <c>ON CONFLICT … DO UPDATE … WHERE resuelta_at IS NULL</c> no inserta nada ni la revive. Es
    /// decir, <b>un numerador que vuelve a quedarse corto no vuelve a avisar con esa clave</b>. Está
    /// igual para todas las alertas del sistema (las de F2-09 entre ellas), así que arreglarlo es
    /// decisión de otro y esta prueba deja constancia de cómo está hoy.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AlAmpliarElRangoElAvisoSeResuelveYLaFilaCerradaNoSeRevive()
    {
        var ct = TestContext.Current.CancellationToken;
        var serieId = await SerieConNumeradorAsync(ct, umbralAlerta: 10);

        await EjecutarTrabajoAsync(ct);

        var clave = ServicioComprobantes.ClavePocos(serieId);
        var abierta = await AyudaComprobante.AlertaAsync(bd, clave, ct);
        abierta.Should().NotBeNull();

        await AmpliarRangoAsync(serieId, ct);

        await EjecutarTrabajoAsync(ct);

        var resuelta = await AyudaComprobante.AlertaAsync(bd, clave, ct, otra: true);
        resuelta!.ResueltaAt.Should().NotBeNull("ampliado el rango, el aviso ya no dice nada");
        resuelta.Id.Should().Be(abierta!.Id, "cerrar no es borrar: la fila sigue ahí diciendo que pasó");
        resuelta.Repeticiones.Should().Be(abierta.Repeticiones, "cerrada no se refresca");

        // Volver a quedarse corto deja la fila cerrada y no crea otra: clave única más el WHERE del
        // ON CONFLICT. Es lo que hay hoy en el sistema entero, y por eso esta prueba lo dice.
        await ReducirRangoAsync(serieId, ct);
        await EjecutarTrabajoAsync(ct);

        var trasVolver = await AyudaComprobante.AlertaAsync(bd, clave, ct, otra: true);
        trasVolver!.Id.Should().Be(abierta.Id);
        trasVolver.ResueltaAt.Should().NotBeNull();
        trasVolver.Repeticiones.Should().Be(abierta.Repeticiones);

        (await AyudaComprobante.AlertaAsync(bd, clave, ct))
            .Should().BeNull("y no hay ninguna viva con esa clave: el aviso no volvió a levantarse");
    }

    // ---------------------------------------------------------------- apoyo

    /// <summary>
    /// Serie fiscal con un numerador que ya nace corto: el rango acaba en
    /// <c>desde + umbral - 1</c> y el siguiente número es <c>desde</c>, así que los números que
    /// quedan son exactamente el umbral y el aviso salta desde el primer día.
    /// </summary>
    private async Task<Guid> SerieConNumeradorAsync(CancellationToken ct, int umbralAlerta)
    {
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var serieId = await AyudaComprobante.SerieFiscalAsync(sesion, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var desde = Random.Shared.Next(1, 100);
        await AyudaComprobante.NumeradorAsync(
            sesion, serieId, hoy, ct,
            rangoDesde: desde,
            rangoHasta: desde + umbralAlerta - 1,
            siguienteNumero: desde,
            umbralAlerta: umbralAlerta);

        return serieId;
    }

    private async Task AmpliarRangoAsync(Guid serieId, CancellationToken ct) =>
        await GuardarConRangoHastaAsync(serieId, await RangoHastaDeLaSerieAsync(serieId, ct) * 2, ct);

    private async Task ReducirRangoAsync(Guid serieId, CancellationToken ct) =>
        await GuardarConRangoHastaAsync(serieId, (await RangoHastaDeLaSerieAsync(serieId, ct)) / 2, ct);

    private async Task<int> RangoHastaDeLaSerieAsync(Guid serieId, CancellationToken ct) =>
        (await AyudaComprobante.NumeradorDeAsync(bd, serieId, ct))!.RangoHasta;

    /// <summary>
    /// Reescribe el numerador con otro rango, leyendo primero lo que hay. El cambio va por la API: el
    /// avisador y la pantalla de configuración tienen que estar de acuerdo sobre lo mismo.
    /// </summary>
    private async Task GuardarConRangoHastaAsync(Guid serieId, int rangoHasta, CancellationToken ct)
    {
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        var numerador = await AyudaComprobante.NumeradorDeAsync(bd, serieId, ct);

        var respuesta = await sesion.PutAsync(
            $"/api/v1/config/numeradores-fiscales/{serieId:D}",
            new
            {
                tipoPrefijo = numerador!.TipoPrefijo,
                rangoDesde = numerador.RangoDesde,
                rangoHasta,
                fechaVencimiento = numerador.FechaVencimiento.ToString("yyyy-MM-dd"),
                siguienteNumero = numerador.SiguienteNumero,
                umbralAlerta = numerador.UmbralAlerta,
            },
            ct);

        respuesta.IsSuccessStatusCode.Should().BeTrue("el numerador se reescribe por la misma API que lo creó");
    }

    /// <summary>Una pasada del trabajo de las 06:00 contra la base de verdad, como corre el planificador.</summary>
    private async Task EjecutarTrabajoAsync(CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var reloj = new RelojPrueba();

        await new TrabajoComprobantes(contexto, new ServicioAlertas(contexto, reloj), reloj)
            .EjecutarAsync(ct);
    }
}