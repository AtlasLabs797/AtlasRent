using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Jobs;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Cobros;
using AtlasRent.Tests.Comprobantes;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Fianzas;

/// <summary>
/// F3-09: el aviso de §3.16 de las fianzas cuyo contrato terminó y que siguen sin devolver.
/// <para>
/// <b>Se levanta con el trabajo diario y se resuelve con el movimiento</b>, no cuando alguien marca la
/// alerta: el problema es que falte devolver la fianza, y devolverla es lo que lo quita. Es el mismo
/// criterio que aplica F3-05 al periodo sin facturar y F3-04 al comprobante que no llega.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class FianzaAlertaTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    /// <summary>
    /// Contrato terminado con la fianza cobrada y sin devolver: el aviso está vivo. Al devolverla, el
    /// aviso se resuelve en el mismo movimiento —y cerrar no es borrar: la fila se queda con su
    /// <c>resuelta_at</c>—.
    /// </summary>
    [Fact]
    public async Task ContratoTerminadoSinDevolverLevantaElAvisoYAlDevolverSeResuelve()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, 500m, Moneda.USD, 1, ct);
        await AyudaFianzas.CobradaAsync(admin, clienteId, contratoId, fianzaId, 500m, Moneda.USD, ct);
        await AyudaFianzas.TerminarContratoAsync(bd, contratoId, ct);

        var clave = ServicioFianzas.ClaveAlerta(fianzaId);

        await EjecutarTrabajoAsync(ct);

        var viva = await AyudaComprobante.AlertaAsync(bd, clave, ct);
        viva.Should().NotBeNull("el contrato terminó y la fianza sigue sin devolver");
        viva!.Tipo.Should().Be(ServicioFianzas.TipoAlerta, "F3-09 añade el tipo nuevo de §3.16");
        viva.Clave.Should().Be(clave);
        // La cantidad va con el separador de la cultura del proceso, así que no se comprueba con un
        // "500.00" escrito a mano: lo que importa es que el aviso dice cuánto queda, y el separador
        // depende de dónde corra la prueba.
        viva.Mensaje.Should().Contain("500", "el mensaje dice el importe exigido y el que queda por devolver");

        var devuelta = await admin.PostAsync(
            $"/api/v1/fianzas/{fianzaId:D}/devolver",
            AyudaFianzas.PeticionDevolucion(500m),
            ct);
        devuelta.StatusCode.Should().Be(HttpStatusCode.OK, await devuelta.Cuerpo());

        (await AyudaComprobante.AlertaAsync(bd, clave, ct))
            .Should().BeNull("devolver es lo que quita el problema");

        var cerrada = await AyudaComprobante.AlertaAsync(bd, clave, ct, otra: true);
        cerrada.Should().NotBeNull("cerrar una alerta no es borrarla: el rastro se queda");
        cerrada!.ResueltaAt.Should().NotBeNull();
    }

    /// <summary>
    /// Un contrato que no ha terminado no avisa, aunque su fianza esté cobrada y sin devolver: §3.16 es
    /// de fianzas <b>pendientes de devolver</b> y una fianza de un contrato vivo no lo es.
    /// </summary>
    [Fact]
    public async Task UnContratoQueNoHaTerminadoNoLevantaElAviso()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, 500m, Moneda.USD, 1, ct);
        await AyudaFianzas.CobradaAsync(admin, clienteId, contratoId, fianzaId, 500m, Moneda.USD, ct);

        await EjecutarTrabajoAsync(ct);

        (await AyudaComprobante.AlertaAsync(bd, ServicioFianzas.ClaveAlerta(fianzaId), ct))
            .Should().BeNull("el contrato sigue vivo: la fianza se devuelve al final, no se avisa cada día");
    }

    /// <summary>
    /// Poner la fianza en liquidación también resuelve el aviso: el asunto dejó de estar pendiente de
    /// quien mira la bandeja. Y una fianza retenida a medias también, porque devolver en parte es
    /// devolver (§3.6): avisar de ella todos los días sería la alerta que enseña a ignorar la bandeja.
    /// </summary>
    [Fact]
    public async Task LiquidarYRetenerParcialResuelvenElAviso()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, 500m, Moneda.USD, 1, ct);
        await AyudaFianzas.CobradaAsync(admin, clienteId, contratoId, fianzaId, 500m, Moneda.USD, ct);
        await AyudaFianzas.TerminarContratoAsync(bd, contratoId, ct);

        var clave = ServicioFianzas.ClaveAlerta(fianzaId);
        await EjecutarTrabajoAsync(ct);
        (await AyudaComprobante.AlertaAsync(bd, clave, ct)).Should().NotBeNull();

        var liquidada = await admin.PostAsync($"/api/v1/fianzas/{fianzaId:D}/liquidar", new { }, ct);
        liquidada.StatusCode.Should().Be(HttpStatusCode.OK, await liquidada.Cuerpo());

        (await AyudaComprobante.AlertaAsync(bd, clave, ct))
            .Should().BeNull("una fianza en liquidación ya no es un asunto pendiente de la bandeja");

        await EjecutarTrabajoAsync(ct);
        (await AyudaComprobante.AlertaAsync(bd, clave, ct))
            .Should().BeNull("y el trabajo tampoco vuelve a levantarla: el estado ya no es de los que avisa");
    }

    /// <summary>
    /// <b>La reapertura (BUG-008).</b> Una fianza que vuelve a tener saldo pendiente después de que su
    /// aviso se resolvió tiene que volver a avisar, y lo hace en la misma fila, no en una copia: el
    /// índice único de <c>alerta</c> es parcial sobre <c>(tipo, clave)</c> filtrado a
    /// <c>resuelta_at IS NULL</c>, así que la fila cerrada queda fuera de él y sin ese paso previo una
    /// condición que reaparece no volvería a levantar ningún aviso.
    /// <para>
    /// El estado no tiene un camino de vuelta desde <c>Devuelta</c> —es terminal por §3.11—, así que la
    /// situación se monta como lo que de verdad puede ocurrir: una alerta resuelta mientras la fianza
   /// seguía sin devolver. Es el mismo camino de código que el de un aviso cerrado a mano, y es el que
    /// BUG-008 arregló.
    /// </para>
    /// </summary>
    [Fact]
    public async Task UnaAlertaResueltaVuelveALevantarseSiElAsuntoReincide()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, 500m, Moneda.USD, 1, ct);
        await AyudaFianzas.CobradaAsync(admin, clienteId, contratoId, fianzaId, 500m, Moneda.USD, ct);
        await AyudaFianzas.TerminarContratoAsync(bd, contratoId, ct);

        var clave = ServicioFianzas.ClaveAlerta(fianzaId);

        await EjecutarTrabajoAsync(ct);
        (await AyudaComprobante.AlertaAsync(bd, clave, ct)).Should().NotBeNull();

        // Se resuelve sola, como si alguien la hubiera marcado, mientras la fianza sigue pendiente.
        await ResolverAsync(bd, clave, ct);
        (await AyudaComprobante.AlertaAsync(bd, clave, ct))
            .Should().BeNull("la resolución no es borrar: sigue ahí, cerrada");

        await EjecutarTrabajoAsync(ct);

        var reabierta = await AyudaComprobante.AlertaAsync(bd, clave, ct);
        reabierta.Should().NotBeNull("el asunto sigue sin resolver, así que vuelve a avisar (BUG-008)");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Alerta.AsNoTracking().CountAsync(a => a.Clave == clave && a.ResueltaAt == null, ct))
            .Should().Be(1, "y reabre la misma fila en vez de crear una copia");
        (await contexto.Alerta.AsNoTracking().CountAsync(a => a.Clave == clave, ct))
            .Should().Be(1, "cerrar y reabrir no crea filas nuevas");
    }

    /// <summary>Una pasada del trabajo de las 06:00 contra la base de verdad, como corre el planificador.</summary>
    private async Task EjecutarTrabajoAsync(CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        await new TrabajoFianzas(contexto, new ServicioAlertas(contexto, new RelojPrueba()))
            .EjecutarAsync(ct);
    }

    /// <summary>Cierra la alerta viva por SQL, que es lo que hace <c>ResolverAsync</c>.</summary>
    private static async Task ResolverAsync(ContenedorBdFixture bd, string clave, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        await new ServicioAlertas(contexto, new RelojPrueba()).ResolverAsync(clave, ct);
    }
}