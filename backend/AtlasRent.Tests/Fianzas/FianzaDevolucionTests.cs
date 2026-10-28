using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cobros;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Fianzas;

/// <summary>
/// F3-09: la devolución de una fianza (docs/ESPECIFICACION.md §3.11 y §3.6).
/// <para>
/// El criterio de aceptación de la tarea es que <b>una devolución parcial exige motivo e importe
/// descontado</b>, y eso es lo primero que comprueban estas pruebas: los tres casos —parcial con los dos,
/// parcial sin motivo, parcial sin descuento— con su propio mensaje, porque un 400 que dice «datos no
/// válidos» sin más no le sirve a quien lo tiene que corregir.
/// </para>
/// <para>
/// Va por la API real porque el 400 y el 409 los decide el servicio y los traduce el controlador, y una
/// prueba del servicio no comprobaría que el usuario recibe lo que dice leer.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class FianzaDevolucionTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    /// <summary>
    /// Devolver todo lo que se cobró, sin descuento y sin motivo, deja la fianza devuelta y a cero. Es el
    /// camino feliz: el motivo no es obligatorio cuando no se retiene nada (§3.11).
    /// </summary>
    [Fact]
    public async Task DevolverEnteraDejaLaFianzaADevueltaYASaldoCero()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, 500m, Moneda.USD, 1, ct);
        await AyudaFianzas.CobradaAsync(admin, clienteId, contratoId, fianzaId, 500m, Moneda.USD, ct);

        var devuelta = await admin.PostAsync(
            $"/api/v1/fianzas/{fianzaId:D}/devolver",
            AyudaFianzas.PeticionDevolucion(500m),
            ct);

        devuelta.StatusCode.Should().Be(HttpStatusCode.OK, await devuelta.Cuerpo());

        var cifras = await AyudaFianzas.CifrasAsync(bd, fianzaId, ct);
        cifras.Cobrado.Should().Be(500m);
        cifras.Devuelto.Should().Be(500m);
        cifras.Saldo.Should().Be(0m, "devolver todo es dejar el saldo en cero");

        (await AyudaFianzas.EstadoGuardadoAsync(bd, fianzaId, ct))
            .Should().Be(EstadoFianza.Devuelta);

        var leida = await AyudaFianzas.LeerAsync(admin, fianzaId, ct);
        leida.GetProperty("estado").GetString().Should().Be(nameof(EstadoFianza.Devuelta));
        leida.GetProperty("saldo").LeerDecimal().Should().Be(0m);
    }

    /// <summary>
    /// BUG-034 (c): devolver todo el saldo pero con importe descontado es retener a medias, no una
    /// devolución entera: queda «Retenida parcial» (§3.11).
    /// </summary>
    [Fact]
    public async Task DevolverTodoElSaldoConDescuentoDejaRetenidaParcialYNoDevuelta()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, 500m, Moneda.USD, 1, ct);
        await AyudaFianzas.CobradaAsync(admin, clienteId, contratoId, fianzaId, 500m, Moneda.USD, ct);

        var respuesta = await admin.PostAsync(
            $"/api/v1/fianzas/{fianzaId:D}/devolver",
            AyudaFianzas.PeticionDevolucion(400m, 100m, "Faltantes del inventario de salida"),
            ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());

        (await AyudaFianzas.EstadoGuardadoAsync(bd, fianzaId, ct)).Should().Be(EstadoFianza.RetenidaParcial);
        (await AyudaFianzas.LeerAsync(admin, fianzaId, ct)).GetProperty("estado").GetString()
            .Should().Be(nameof(EstadoFianza.RetenidaParcial));
    }

    /// <summary>
    /// <b>El criterio de aceptación.</b> Una devolución parcial con importe y motivo baja el saldo por
    /// la suma de los dos y deja la fianza retenida a medias (§3.6).
    /// </summary>
    [Fact]
    public async Task LaDevolucionParcialBajaElSaldoPorElImporteYElDescuentoYDejaRetenidaParcial()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, 500m, Moneda.USD, 1, ct);
        await AyudaFianzas.CobradaAsync(admin, clienteId, contratoId, fianzaId, 500m, Moneda.USD, ct);

        var respuesta = await admin.PostAsync(
            $"/api/v1/fianzas/{fianzaId:D}/devolver",
            AyudaFianzas.PeticionDevolucion(200m, 100m, "Faltantes del inventario de salida"),
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());

        var cifras = await AyudaFianzas.CifrasAsync(bd, fianzaId, ct);
        cifras.Devuelto.Should().Be(200m);
        cifras.Descontado.Should().Be(100m);
        cifras.Saldo.Should().Be(200m, "500 cobrados menos 200 devueltos menos 100 retenidos");

        (await AyudaFianzas.EstadoGuardadoAsync(bd, fianzaId, ct))
            .Should().Be(EstadoFianza.RetenidaParcial, "devolver una parte y retener el resto es eso");
    }

    /// <summary>
    /// Parcial sin motivo: 400 que lo dice. El dinero que no se devuelve necesita una razón, y sin ella
    /// no hay forma de saber después qué pasó con ese saldo.
    /// </summary>
    [Fact]
    public async Task LaDevolucionParcialSinMotivoEs400QueLoDice()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, 500m, Moneda.USD, 1, ct);
        await AyudaFianzas.CobradaAsync(admin, clienteId, contratoId, fianzaId, 500m, Moneda.USD, ct);

        var respuesta = await admin.PostAsync(
            $"/api/v1/fianzas/{fianzaId:D}/devolver",
            AyudaFianzas.PeticionDevolucion(200m, 100m),
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await respuesta.Cuerpo()).Should().Contain("motivo");

        var cifras = await AyudaFianzas.CifrasAsync(bd, fianzaId, ct);
        cifras.Devuelto.Should().Be(0m, "un 400 no registra ninguna devolución");
        cifras.Saldo.Should().Be(500m);
    }

    /// <summary>
    /// Parcial sin importe descontado: 400 que lo dice. Devolver 200 de 500 dejando los otros 300 sin
    /// explicar ni como retención no es una devolución parcial: es medio cobro y medio borrado, y §3.11
    /// no lo admite.
    /// </summary>
    [Fact]
    public async Task LaDevolucionParcialSinImporteDescontadoEs400QueLoDice()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, 500m, Moneda.USD, 1, ct);
        await AyudaFianzas.CobradaAsync(admin, clienteId, contratoId, fianzaId, 500m, Moneda.USD, ct);

        var respuesta = await admin.PostAsync(
            $"/api/v1/fianzas/{fianzaId:D}/devolver",
            AyudaFianzas.PeticionDevolucion(200m, 0m, "Quiero devolver solo una parte"),
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await respuesta.Cuerpo()).Should().Contain("descontado");

        var cifras = await AyudaFianzas.CifrasAsync(bd, fianzaId, ct);
        cifras.Devuelto.Should().Be(0m, "un 400 no registra ninguna devolución");
        cifras.Saldo.Should().Be(500m);
    }

    /// <summary>
    /// Devolver más de lo cobrado es un 409 con los números del saldo y de lo pedido (§3.11). El
    /// mensaje lleva las dos cifras porque quien lo lee tiene que poder calcular de inmediato cuánto se
    /// pasó, y un «cantidad superior al saldo» sin cifras no le sirve para eso.
    /// </summary>
    [Fact]
    public async Task DevolverMasDeLoCobradoEs409ConLosNumerosYNoDejaElSaldoNegativo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, 500m, Moneda.USD, 1, ct);
        await AyudaFianzas.CobradaAsync(admin, clienteId, contratoId, fianzaId, 500m, Moneda.USD, ct);

        var respuesta = await admin.PostAsync(
            $"/api/v1/fianzas/{fianzaId:D}/devolver",
            AyudaFianzas.PeticionDevolucion(600m, 100m, "Me paso a proposito"),
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var cuerpo = await respuesta.Cuerpo();
        cuerpo.Should().Contain("500");
        cuerpo.Should().Contain("700");

        var cifras = await AyudaFianzas.CifrasAsync(bd, fianzaId, ct);
        cifras.Saldo.Should().Be(500m, "un 409 no toca el saldo");
        cifras.Devuelto.Should().Be(0m);
    }

    /// <summary>
    /// Cobrar más de lo exigido es un 409 en el <b>cobro</b>, no en la devolución, y por el mismo motivo
    /// que no se puede asignar de más a una factura (§3.10.1): el importe exigido es lo que el contrato
    /// pide y lo que sobra no es fianza. Lo que exceda iría al saldo a favor del contrato, así que
    /// cobrando de más no se paga dos veces, se acumula un crédito.
    /// </summary>
    [Fact]
    public async Task CobrarMasDelImporteExigidoEs409()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, 500m, Moneda.USD, 1, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(admin, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(admin, Moneda.USD, ct);

        var deMas = await admin.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.USD, 800m, formaPagoId, cuentaId,
                asignaciones: new[] { new { fianzaId, importe = 800m } }),
            ct);

        deMas.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await deMas.Cuerpo()).Should().Contain("500");

        (await AyudaFianzas.CifrasAsync(bd, fianzaId, ct)).Cobrado
            .Should().Be(0m, "un 409 no cobra nada");
    }

    /// <summary>
    /// §3.11: cada cambio de estado se valida contra el actual. Devuelta no vuelve a moverse, y una
    /// fianza ya liquidada no se liquida otra vez.
    /// </summary>
    [Fact]
    public async Task LasTransicionesInvalidasSon409()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, 500m, Moneda.USD, 1, ct);
        await AyudaFianzas.CobradaAsync(admin, clienteId, contratoId, fianzaId, 500m, Moneda.USD, ct);

        // Devolver dos veces.
        var primera = await admin.PostAsync(
            $"/api/v1/fianzas/{fianzaId:D}/devolver",
            AyudaFianzas.PeticionDevolucion(500m),
            ct);
        primera.StatusCode.Should().Be(HttpStatusCode.OK, await primera.Cuerpo());

        var segunda = await admin.PostAsync(
            $"/api/v1/fianzas/{fianzaId:D}/devolver",
            AyudaFianzas.PeticionDevolucion(500m),
            ct);
        segunda.StatusCode.Should().Be(HttpStatusCode.Conflict, "§3.11: una fianza devuelta no se vuelve a mover");
        (await segunda.Cuerpo()).Should().Contain(nameof(EstadoFianza.Devuelta));

        // Liquidar una devuelta.
        var liquidarDevuelta = await admin.PostAsync($"/api/v1/fianzas/{fianzaId:D}/liquidar", new { }, ct);
        liquidarDevuelta.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Retener una devuelta tampoco.
        var retenerDevuelta = await admin.PostAsync($"/api/v1/fianzas/{fianzaId:D}/retener", new { }, ct);
        retenerDevuelta.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Retener y luego liquidar vale (§3.11: Parcial → Retenida → En liquidación), pero hacerlo dos
    /// veces no: el segundo intento es un 409 que dice en qué estado está.
    /// </summary>
    [Fact]
    public async Task RetenerYLiquidarValePeroRepetirLaLiquidacionEs409()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, 500m, Moneda.USD, 1, ct);
        await AyudaFianzas.CobradaAsync(admin, clienteId, contratoId, fianzaId, 500m, Moneda.USD, ct);

        var retenida = await admin.PostAsync($"/api/v1/fianzas/{fianzaId:D}/retener", new { }, ct);
        retenida.StatusCode.Should().Be(HttpStatusCode.OK, await retenida.Cuerpo());
        (await AyudaFianzas.EstadoGuardadoAsync(bd, fianzaId, ct)).Should().Be(EstadoFianza.Retenida);

        var retenidaOtra = await admin.PostAsync($"/api/v1/fianzas/{fianzaId:D}/retener", new { }, ct);
        retenidaOtra.StatusCode.Should().Be(HttpStatusCode.Conflict, "ya estaba retenida");

        var liquidada = await admin.PostAsync($"/api/v1/fianzas/{fianzaId:D}/liquidar", new { }, ct);
        liquidada.StatusCode.Should().Be(HttpStatusCode.OK, await liquidada.Cuerpo());
        (await AyudaFianzas.EstadoGuardadoAsync(bd, fianzaId, ct)).Should().Be(EstadoFianza.EnLiquidacion);

        var liquidadaOtra = await admin.PostAsync($"/api/v1/fianzas/{fianzaId:D}/liquidar", new { }, ct);
        liquidadaOtra.StatusCode.Should().Be(HttpStatusCode.Conflict, "pasar de retenida a liquidación dos veces");
        (await liquidadaOtra.Cuerpo()).Should().Contain(nameof(EstadoFianza.EnLiquidacion));

        var retenerEnLiquidacion = await admin.PostAsync($"/api/v1/fianzas/{fianzaId:D}/retener", new { }, ct);
        retenerEnLiquidacion.StatusCode.Should().Be(
            HttpStatusCode.Conflict,
            "una liquidación que ya empezó no se deshace desde aquí");
    }

    /// <summary>
    /// Una fianza con saldo retenido que ya no se devuelve: devolverla otra vez es 409 también, porque
    /// <c>RetenidaParcial</c> es un final igual que <c>Devuelta</c>.
    /// </summary>
    [Fact]
    public async Task UnaFianzaRetenidaParcialTampocoSeVuelveADevolver()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, 500m, Moneda.USD, 1, ct);
        await AyudaFianzas.CobradaAsync(admin, clienteId, contratoId, fianzaId, 500m, Moneda.USD, ct);

        var parcial = await admin.PostAsync(
            $"/api/v1/fianzas/{fianzaId:D}/devolver",
            AyudaFianzas.PeticionDevolucion(200m, 100m, "Faltantes del inventario"),
            ct);
        parcial.StatusCode.Should().Be(HttpStatusCode.OK, await parcial.Cuerpo());

        var otra = await admin.PostAsync(
            $"/api/v1/fianzas/{fianzaId:D}/devolver",
            AyudaFianzas.PeticionDevolucion(200m, 0m, "Intento devolver el resto"),
            ct);

        otra.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await AyudaFianzas.CifrasAsync(bd, fianzaId, ct)).Saldo
            .Should().Be(200m, "el saldo se queda donde estaba");
    }
}