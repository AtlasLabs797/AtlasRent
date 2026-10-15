using System.Net;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Contratos;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Inventario;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Liquidacion;

/// <summary>
/// F2-09: la liquidación de verdad (§3.5: "la liquidación cierra inventario de salida, fianza y
/// saldos"). Un cambio de estado no es una liquidación, así que estas pruebas miran lo que queda
/// escrito además del estado.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class LiquidacionContratoTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    private static readonly (string Nombre, decimal Cantidad, string? Notas)[] UnElemento =
    [
        ("Colchón", 1, null),
    ];

    /// <summary>
    /// Cuerpo de firma con una línea que el modelo acepta. No importa que el elemento no exista: en
    /// borrador y en aprobado el rechazo es por el estado del contrato, y ese se mira antes que las
    /// líneas. Un cuerpo con <c>lineas</c> vacío daría un 400 de validación del modelo y el test
    /// estaría comprobando otra cosa.
    /// </summary>
    private static object CuerpoDeFirma() =>
        new
        {
            lineas = new[]
            {
                new { contratoInventarioItemId = Guid.NewGuid(), estado = "Presente", nota = (string?)null },
            },
            importePropuesto = 0m,
            observacion = "Prueba de estado.",
        };

    /// <summary>
    /// Con inventario, la revisión firmada es la puerta (§3.6): sin ella el contrato tiene inventario
    /// pero nadie ha dicho qué falta, y liquidar así sería cerrar el contrato con una pregunta abierta
    /// dentro.
    /// </summary>
    [Fact]
    public async Task NoSeLiquidaSinRevisionFirmada()
    {
        var ct = TestContext.Current.CancellationToken;
        var (gerenteUsuario, gerente) = await AyudaLiquidacion.SesionDeAsync(api, bd, Roles.Gerente, ct);
        using var empleado = await AyudaLiquidacion.SesionAsync(api, bd, Roles.Empleado, ct);

        var (_, contratoId) = await AyudaInventario.ContratoActivoConInventarioAsync(
            empleado, gerente, bd, UnElemento, desplazamientoInicio: 320, ct: ct);

        await AyudaLiquidacion.DarBajaYFinalizarAsync(gerente, contratoId, AyudaContrato.Dia(320).AddMonths(12), ct);
        (await AyudaContrato.EstadoAsync(bd, contratoId, ct)).Should().Be(EstadoContrato.Finalizado);

        // Sin firma: 409, y el contrato se queda exactamente como estaba.
        var sinRevision = await AyudaContrato.Boton(gerente, contratoId, "liquidacion", null, ct);
        sinRevision.StatusCode.Should().Be(HttpStatusCode.Conflict, await sinRevision.Cuerpo());
        (await AyudaContrato.EstadoAsync(bd, contratoId, ct)).Should().Be(EstadoContrato.Finalizado);
        (await AyudaLiquidacion.LiquidacionAsync(bd, contratoId, ct))
            .Should().BeNull("una liquidación rechazada no deja fila a medias");

        // Con firma: entra, y queda escrito qué se liquidó y qué se hace con la fianza.
        var congelado = await AyudaInventario.InventarioContratoAsync(empleado, contratoId, ct);
        await AyudaInventario.FirmarAsync(
            gerente, contratoId, congelado, [("Ausente", "No aparece")], importePropuesto: 1_500m, ct: ct);

        var liquidado = await AyudaContrato.Boton(gerente, contratoId, "liquidacion", null, ct);
        liquidado.StatusCode.Should().Be(HttpStatusCode.OK, await liquidado.Cuerpo());
        (await AyudaContrato.EstadoAsync(bd, contratoId, ct)).Should().Be(EstadoContrato.Liquidado);

        var registro = await AyudaLiquidacion.LiquidacionAsync(bd, contratoId, ct);
        registro.Should().NotBeNull("la liquidación escribe su propia fila: no es una etiqueta");
        registro!.ContratoId.Should().Be(contratoId);
        registro.FechaLiquidacion.Should().Be(AyudaContrato.Dia(), "hoy, no la fecha en que terminó el contrato");
        registro.LiquidadoPorId.Should().Be(gerenteUsuario.Id, "liquidar es siempre una persona");
        registro.ImporteFaltantes.Should().Be(1_500m, "el importe que propuso la revisión de salida");
        registro.FianzaContrato.Should().Be(20_000m, "la fianza pactada, copiada al liquidar");
        registro.DecisionFianza.Should().Be(DecisionFianza.RetenidaPorFaltantes);
        registro.FianzaRetenida.Should().Be(1_500m, "de una fianza no se retiene más que el faltante");
        registro.RevisionInventarioId.Should().NotBeNull("la liquidación apunta a la revisión que la respalda");
    }

    /// <summary>
    /// Sin inventario no hay nada que liquidar de salida: §3.6 pide la revisión "si el contrato tiene
    /// inventario" y §3.1 deja el inventario base como opcional. Exigirla siempre dejaría sin poder
    /// liquidar a la mayoría de las unidades.
    /// </summary>
    [Fact]
    public async Task SePuedeLiquidarSinInventario()
    {
        var ct = TestContext.Current.CancellationToken;
        var (gerenteUsuario, gerente) = await AyudaLiquidacion.SesionDeAsync(api, bd, Roles.Gerente, ct);
        using var empleado = await AyudaLiquidacion.SesionAsync(api, bd, Roles.Empleado, ct);

        var (_, contratoId) = await AyudaLiquidacion.ContratoActivoAsync(
            empleado, gerente, bd, AyudaContrato.Dia(-400), ct);

        await AyudaLiquidacion.DarBajaYFinalizarAsync(gerente, contratoId, AyudaContrato.Dia(1), ct);

        var liquidado = await AyudaContrato.Boton(gerente, contratoId, "liquidacion", null, ct);
        liquidado.StatusCode.Should().Be(HttpStatusCode.OK, await liquidado.Cuerpo());
        (await AyudaContrato.EstadoAsync(bd, contratoId, ct)).Should().Be(EstadoContrato.Liquidado);

        var registro = await AyudaLiquidacion.LiquidacionAsync(bd, contratoId, ct);
        registro.Should().NotBeNull();
        registro!.RevisionInventarioId.Should().BeNull("no hay revisión porque no hay inventario");
        registro.ImporteFaltantes.Should().Be(0m, "no hay faltante que cobrar: ni se inventa");
        registro.DecisionFianza.Should().Be(DecisionFianza.Devuelta);
        registro.FianzaContrato.Should().Be(20_000m);
        registro.FianzaRetenida.Should().Be(0m);
        registro.LiquidadoPorId.Should().Be(gerenteUsuario.Id);
    }

    /// <summary>
    /// Liquidado es el final. Reintentar es 409 por estado, como el incremento ya aplicado de F2-06: un
    /// cierre que se puede volver a escribir no es un cierre.
    /// </summary>
    [Fact]
    public async Task LiquidadoNoSeVuelveATocar()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaLiquidacion.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaLiquidacion.SesionAsync(api, bd, Roles.Gerente, ct);

        var (_, contratoId) = await AyudaLiquidacion.ContratoActivoAsync(
            empleado, gerente, bd, AyudaContrato.Dia(-400), ct);

        await AyudaLiquidacion.DarBajaYFinalizarAsync(gerente, contratoId, AyudaContrato.Dia(1), ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "liquidacion", null, ct);

        var repetida = await AyudaContrato.Boton(gerente, contratoId, "liquidacion", null, ct);
        repetida.StatusCode.Should().Be(HttpStatusCode.Conflict, await repetida.Cuerpo());
        (await repetida.Cuerpo()).Should().Contain("liquidado", "el motivo dice que ya está liquidado");

        (await AyudaLiquidacion.LiquidacionAsync(bd, contratoId, ct))
            .Should().NotBeNull("y la liquidación sigue siendo la primera, no una segunda");
        (await AyudaLiquidacion.EnContextoAsync(bd, c => c.ContratoLiquidacion.CountAsync(l => l.ContratoId == contratoId, ct)))
            .Should().Be(1, "el índice único de contrato_id también impide el duplicado en la base");
    }

    /// <summary>
    /// El punto 0 de esta tarea, que corrige una instrucción anterior: la revisión de salida se firma
    /// en <c>Activo</c>, en <c>BajaProgramada</c> y en <c>Finalizado</c>, y no antes ni después.
    /// <para>
    /// Antes se rechazaba en <c>Finalizado</c> y eso era un callejón sin salida: con el fin automático
    /// por fecha efectiva, el contrato pasa a finalizado solo y la revisión se firma ese mismo día;
    /// impedir la firma ahí hacía que un contrato finalizado sin firmar no se pudiera firmar ni
    /// liquidar <b>nunca</b>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task LaRevisionSePuedeFirmarEnFinalizado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaLiquidacion.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaLiquidacion.SesionAsync(api, bd, Roles.Gerente, ct);

        // En borrador: el cliente todavía no ha entrado y no hay nada que revisar.
        var enBorrador = await AyudaContrato.CrearAsync(
            empleado,
            AyudaContrato.Peticion(
                await AyudaReserva.ClienteAsync(empleado, ct),
                await AyudaReserva.UnidadAsync(empleado, ct),
                await AyudaContrato.SubempresaAsync(bd, ct),
                AyudaContrato.Dia(331)),
            ct);
        (await AyudaContrato.EstadoAsync(bd, enBorrador, ct)).Should().Be(EstadoContrato.Borrador);

        var enBorradorFirmada = await AyudaInventario.FirmarRawAsync(gerente, enBorrador, CuerpoDeFirma(), ct);
        enBorradorFirmada.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await enBorradorFirmada.Cuerpo())
            .Should().Contain("no tiene salida que revisar", "el motivo es el estado, no el inventario");

        // En aprobado tampoco: sigue sin haber entrado el inquilino.
        await AyudaContrato.AceptarAsync(empleado, enBorrador, "solicitud", new { }, ct);
        await AyudaContrato.AceptarAsync(gerente, enBorrador, "aprobacion", null, ct);
        (await AyudaContrato.EstadoAsync(bd, enBorrador, ct)).Should().Be(EstadoContrato.Aprobado);

        var enAprobadoFirmada = await AyudaInventario.FirmarRawAsync(gerente, enBorrador, CuerpoDeFirma(), ct);
        enAprobadoFirmada.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await enAprobadoFirmada.Cuerpo()).Should().Contain("no tiene salida que revisar");

        // En finalizado: sí se puede, y es el caso que el fin por fecha efectiva produce todos los
        // días sin que nadie tenga que hacer nada.
        var (_, finalizado) = await AyudaInventario.ContratoActivoConInventarioAsync(
            empleado, gerente, bd, UnElemento, desplazamientoInicio: 332, ct: ct);
        await AyudaLiquidacion.DarBajaYFinalizarAsync(
            gerente, finalizado, AyudaContrato.Dia(332).AddMonths(12), ct);
        (await AyudaContrato.EstadoAsync(bd, finalizado, ct)).Should().Be(EstadoContrato.Finalizado);

        var congelado = await AyudaInventario.InventarioContratoAsync(empleado, finalizado, ct);
        var firmada = await AyudaInventario.FirmarAsync(
            gerente, finalizado, congelado, [("Presente", null)], importePropuesto: 0m, ct: ct);
        firmada.GetProperty("contratoId").GetGuid().Should().Be(finalizado);

        // Y con la firma hecha, ese contrato sí se puede liquidar: era el callejón sin salida que se
        // cerraba aquí.
        (await AyudaContrato.Boton(gerente, finalizado, "liquidacion", null, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Liquidar es de gerencia: §3.6 dice que los descuentos se aplican contra la fianza solo con
    /// gerente/admin, y el botón de liquidar tiene el mismo techo. El empleado sí lee la ficha, que es
    /// <c>cliente.ver</c>.
    /// </summary>
    [Fact]
    public async Task SoloGerenteOAdminLiquidanYElEmpleadoSigueLeyendo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaLiquidacion.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaLiquidacion.SesionAsync(api, bd, Roles.Gerente, ct);

        var (_, contratoId) = await AyudaLiquidacion.ContratoActivoAsync(
            empleado, gerente, bd, AyudaContrato.Dia(-400), ct);
        await AyudaLiquidacion.DarBajaYFinalizarAsync(gerente, contratoId, AyudaContrato.Dia(1), ct);

        var porEmpleado = await AyudaContrato.Boton(empleado, contratoId, "liquidacion", null, ct);
        porEmpleado.StatusCode.Should().Be(HttpStatusCode.Forbidden, await porEmpleado.Cuerpo());
        (await AyudaContrato.EstadoAsync(bd, contratoId, ct))
            .Should().Be(EstadoContrato.Finalizado, "un 403 no cambia el estado de nadie");

        // La lectura es de cliente.ver y el empleado la tiene.
        var ficha = await empleado.GetAsync($"/api/v1/contratos/{contratoId:D}", ct);
        ficha.StatusCode.Should().Be(HttpStatusCode.OK, await ficha.Cuerpo());

        var porGerente = await AyudaContrato.Boton(gerente, contratoId, "liquidacion", null, ct);
        porGerente.StatusCode.Should().Be(HttpStatusCode.OK, await porGerente.Cuerpo());
    }
}