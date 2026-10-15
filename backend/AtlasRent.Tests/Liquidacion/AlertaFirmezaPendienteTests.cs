using AtlasRent.Api.Authorization;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Contratos;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Liquidacion;

/// <summary>
/// F2-09 sobre §3.5: "alerta persistente hasta liquidar".
/// <para>
/// Persistente no es "una alerta nueva cada día", que es ruido y no avisa de nada: es <b>la misma
/// fila viva</b>, refrescada con el estado de hoy, y cerrada el día que se liquida.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class AlertaFirmezaPendienteTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    /// <summary>
    /// Mañana, sin liquidar, sigue habiendo alerta y no hay una segunda: una fila, viva, con el
    /// mensaje al día. Y al liquidar, esa misma fila queda resuelta y deja de refrescarse.
    /// </summary>
    [Fact]
    public async Task LaAlertaPersisteHastaLiquidar()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaLiquidacion.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaLiquidacion.SesionAsync(api, bd, Roles.Gerente, ct);

        var (_, contratoId) = await AyudaLiquidacion.ContratoActivoAsync(
            empleado, gerente, bd, AyudaContrato.Dia(-400), ct);
        await AyudaContrato.AceptarAsync(
            gerente, contratoId, "baja", new { fechaFin = AyudaContrato.Dia(1).ToString("yyyy-MM-dd") }, ct);

        // El trabajo finaliza y deja la alerta viva en la misma transacción que el fin.
        await AyudaLiquidacion.EjecutarTrabajoAsync(bd, ct, TimeSpan.FromDays(3));

        var clave = ServicioFines.ClaveFirmezaPendiente(contratoId);
        var primera = await AyudaLiquidacion.AlertaAsync(bd, contratoId, ct);
        primera.Should().NotBeNull("un contrato finalizado y sin liquidar tiene una alerta viva");
        primera!.ResueltaAt.Should().BeNull();
        primera.Repeticiones.Should().Be(1);
        primera.Mensaje.Should().Contain("todavía sin liquidar");
        primera.Mensaje.Should().Contain("2 día(s)", "los días se cuentan desde la fecha efectiva del fin");

        // Mañana: el trabajo vuelve a pasar y la alerta sigue viva, refrescada y sin duplicarse.
        await AyudaLiquidacion.EjecutarTrabajoAsync(bd, ct, TimeSpan.FromDays(4));

        (await AyudaLiquidacion.CuantasAlertasAsync(bd, contratoId, ct))
            .Should().Be(1, "la clave única es la red: persistente no es duplicar");

        var segunda = await AyudaLiquidacion.AlertaAsync(bd, contratoId, ct);
        segunda!.ResueltaAt.Should().BeNull("el problema sigue abierto: el contrato no está liquidado");
        segunda.Repeticiones.Should().Be(2, "la misma fila acumula los días que lleva esperando");
        segunda.Mensaje.Should().Contain("3 día(s)", "el mensaje dice el estado de hoy, no el de aquel día");
        segunda.Id.Should().Be(primera.Id, "es la misma fila de siempre, actualizada");

        // Se liquida: la alerta se resuelve en la misma transacción que escribe la liquidación.
        await AyudaContrato.AceptarAsync(gerente, contratoId, "liquidacion", null, ct);

        var resuelta = await AyudaLiquidacion.AlertaAsync(bd, contratoId, ct);
        resuelta!.ResueltaAt.Should().NotBeNull("liquidar es lo que cierra la alerta de §3.5");
        resuelta.Mensaje.Should().Contain("todavía sin liquidar", "el texto es el del último refresco");

        // Y el trabajo siguiente no la resucita: un contrato liquidado ya no está en el barrido.
        await AyudaLiquidacion.EjecutarTrabajoAsync(bd, ct, TimeSpan.FromDays(5));

        var despues = await AyudaLiquidacion.AlertaAsync(bd, contratoId, ct);
        despues!.ResueltaAt.Should().Be(resuelta.ResueltaAt, "cerrada es cerrada: el reloj del cierre no se pisa");
        despues.Repeticiones.Should().Be(resuelta.Repeticiones, "y no vuelve a refrescarse");
        (await AyudaLiquidacion.CuantasAlertasAsync(bd, contratoId, ct))
            .Should().Be(1, "ni se abre otra ni se borra la que se cerró");
    }

    /// <summary>
    /// La alerta nace también cuando el fin lo hace una persona con el botón: finalizar a mano no es
    /// un camino alternativo que deje el contrato sin avisar hasta el día siguiente.
    /// </summary>
    [Fact]
    public async Task LaAlertaTambienNaceEnElFinManual()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaLiquidacion.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaLiquidacion.SesionAsync(api, bd, Roles.Gerente, ct);

        var (_, contratoId) = await AyudaLiquidacion.ContratoActivoAsync(
            empleado, gerente, bd, AyudaContrato.Dia(-400), ct);

        await AyudaLiquidacion.DarBajaYFinalizarAsync(gerente, contratoId, AyudaContrato.Dia(1), ct);

        var alerta = await AyudaLiquidacion.AlertaAsync(bd, contratoId, ct);
        alerta.Should().NotBeNull("el fin manual deja el mismo aviso que el fin automático");
        alerta!.ResueltaAt.Should().BeNull();
        alerta.Mensaje.Should().Contain(contratoId.ToString("D"));
    }

}