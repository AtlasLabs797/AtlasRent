using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Cobros;

/// <summary>
/// Criterio de aceptación de F3-06 en <c>docs/PLAN.md</c>: <b>200 casos aleatorios: ningún saldo
/// descuadra</b>.
/// <para>
/// En cada caso se genera una cartera de facturas al azar —totales y vencimientos— y se registra una
/// secuencia de cobros con importes y asignaciones parciales al azar, y después de cada cobro se
/// comprueba que lo pendiente cuadra con lo facturado menos lo cobrado <b>más el crédito que quedó
/// a favor del contrato</b>, que ningún pendiente es negativo y que ninguna asignación pasó del
/// pendiente que tenía su factura en ese momento. La semilla es fija para que un fallo se pueda
/// reproducir, y el mensaje dice el caso y la semilla.
/// </para>
/// <para>
/// <b>Por qué el crédito está en la fórmula.</b> La primera versión de esta prueba comprobaba
/// <c>Σ pendientes = Σ totales − Σ retenciones − Σ cobrado</c>. Como <see cref="CalculoCobro"/>
/// acota el pendiente a cero, esa igualdad solo puede darse mientras ninguna factura quede cobrada
/// de más, y no puede darse: <see cref="ServicioCobros"/> recorta cada línea con
/// <c>Math.Min(pendiente, …)</c> y manda el sobrante a <c>excedente</c>, y de ahí a
/// <c>saldo_a_favor</c>. Ni el excedente ni el crédito entraban en la comprobación, así que la
/// prueba pasaba igual con la rama de sobrepago rota —y esa rama es la que mueve los totales de la
/// vista de cuentas de §3.13 (F3-10)—. Por eso ahora la invariante incluye el crédito y por eso el
/// generador produce sobrepagos a propósito en vez de dejarlos al azar.
/// </para>
/// <para>
/// <b>Un cuarto de los 200 casos cobra más que toda su cartera</b> (el último cobro de cada caso
/// múltiplo de cuatro) y los cobros siguientes gastan con <c>usarSaldo_a_favor</c> el crédito que
/// esos sobrepagos dejaron. Los dos caminos del dinero —el que sobra y el que se gasta— se recorren
/// siempre, no de vez en cuando.
/// </para>
/// <para>
/// Los cobros van por <see cref="ServicioCobros"/> y no por HTTP: lo que se está probando es el
/// reparto, los límites y las transacciones del servicio, y 200 casos por HTTP serían además minutos
/// de prueba. Las facturas se escriben directamente en la base porque son datos de partida: crear 800
/// documentos por la API solo mediría el API.
/// </para>
/// <para>
/// El contrato es el mismo para los 200 casos y las facturas de los casos anteriores siguen ahí, así que
/// las sumas se hacen sobre <b>todo</b> el contrato: la FIFO de un caso paga también lo que dejó el
/// caso anterior, y mirar solo las facturas nuevas daría un descuadre que no existe.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class CobroSaldosPropertyTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const int Semilla = 20261016;

    private const int Casos = 200;

    private const int FacturasPorCaso = 4;

    private const int CobrosPorCaso = 3;

    /// <summary>
    /// Uno de cada <c>DivisorSobrepago</c> casos mete un cobro mayor que toda la cartera, y ese es el
    /// divisor de la proporción, no un número suelto: si el generador deja de producir sobrepagos,
    /// el recuento del final del bucle lo dice.
    /// </summary>
    private const int DivisorSobrepago = 4;

    /// <summary>
    /// Usuario y rango de pruebas de conexión. El permiso no es lo que se prueba aquí, así que se
    /// llama al servicio con un identificador fijo que la política de la prueba deja pasar.
    /// </summary>
    private static readonly Guid Usuario = Guid.Parse("6b0f1a9e-2f3a-4c1b-9a55-000000000306");

    private const string Ip = "10.0.0.1";

    [Fact]
    public async Task DoscientosCasosAleatoriosNoDejanNingunSaldoDescuadrado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var aleatorio = new Random(Semilla);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var cobros = new ServicioCobros(
            contexto,
            new AuditoriaService(contexto),
            new ServicioIdempotencia(contexto, api.Reloj),
            new ServicioTiposCambio(
                contexto,
                new AuditoriaService(contexto),
                new ServicioIdempotencia(contexto, api.Reloj),
                new ServicioAlertas(contexto, api.Reloj),
                new FuenteSinConsultar(),
                api.Reloj),
            // F3-07: la tolerancia del ajuste sale de ajuste_general. Esta prueba solo genera cobros en la
            // moneda de la factura y nunca ajusta, así que el valor no le afecta; se pasa el mismo
            // servicio de configuración que en producción para que se construya igual. El RNC
            // cifrado tampoco lo usa: aquí solo hace falta que el constructor lo reciba.
            new ServicioConfiguracion(
                contexto,
                new AuditoriaService(contexto),
                new ServicioIdempotencia(contexto, api.Reloj),
                new ServicioCifrado(new OpcionesSeguridad
                {
                    ClaveJwt = [],
                    ClaveCifrado = ApiTestFactory.ClaveCifrado,
                    ClaveHmacDocumento = api.ClaveHmacDocumento,
                }),
                api.Reloj),
            // F4-01: el recibo descifra el documento de identidad del cliente con este servicio. Esta
            // prueba no genera PDF, así que solo hace falta que el constructor lo reciba, con la misma
            // clave que usa la API.
            new ServicioCifrado(new OpcionesSeguridad
            {
                ClaveJwt = [],
                ClaveCifrado = ApiTestFactory.ClaveCifrado,
                ClaveHmacDocumento = api.ClaveHmacDocumento,
            }),
            new PoliticaQuePermiteTodo(),
            api.Reloj);

        var numeroSerie = 1_000_000;
        var recibido = 0m;
        var aplicado = 0m;
        var usado = 0m;
        var sobrepagos = 0;

        for (var caso = 1; caso <= Casos; caso++)
        {
            try
            {
                for (var i = 0; i < FacturasPorCaso; i++)
                {
                    var total = Importe(aleatorio, 50m, 5_000m);

                    // Vencimientos repartidos entre hace tres semanas y dentro de dos meses: hay
                    // facturas ya vencidas, que es el caso interesante de la FIFO.
                    var factura = new Factura
                    {
                        Id = Guid.NewGuid(),
                        ClienteId = clienteId,
                        ContratoId = contratoId,
                        SerieId = serieId,
                        Numero = numeroSerie++,
                        Estado = EstadoFactura.Emitida,
                        Moneda = Moneda.USD,
                        FechaEmision = hoy.AddDays(-30),
                        FechaVencimiento = hoy.AddDays(aleatorio.Next(-20, 61)),
                        Concepto = $"Cartera del caso {caso}",
                        Subtotal = total,
                        Itbis = 0m,
                        Total = total,
                        // Estas facturas se escriben a mano y no las calcula `RecalcularTotales`
                        // (F3-04), así que hay que cumplir la identidad que la base exige: sin
                        // retención, el neto a cobrar es el total. Lo mismo que haría el servicio.
                        Retencion = 0m,
                        NetoACobrar = total,
                        TipoCambioValor = 1m,
                        TipoCambioFecha = hoy,
                        CreatedAt = DateTimeOffset.UtcNow,
                    };

                    contexto.Factura.Add(factura);
                }

                await contexto.SaveChangesAsync(ct);

                for (var c = 0; c < CobrosPorCaso; c++)
                {
                    var pendientesAntes = await PendientesAsync(contexto, contratoId, hoy, ct);
                    var cartera = Redondeo.Importe(pendientesAntes.Values.Sum());
                    var creditoAntes = await CreditoAsync(contexto, contratoId, ct);

                    // Un cuarto de los casos mete a propósito un cobro mayor que toda la cartera viva.
                    // Es la rama que decide qué pasa con el dinero que sobra, y antes de este cambio
                    // solo se recorría por casualidad y casi nunca. Va por FIFO y sin asignaciones
                    // propias para que sea el `Math.Min(pendiente, …)` del servicio el que recorte, no
                    // el reparto de la prueba: si el recorte estuviera mal, tiene que saltar aquí.
                    var sobrepago = caso % DivisorSobrepago == 0 && c == CobrosPorCaso - 1;
                    if (sobrepago)
                    {
                        sobrepagos++;
                    }

                    var importe = sobrepago
                        ? cartera + Importe(aleatorio, 10m, 750m)
                        : Importe(aleatorio, 1m, 4_000m);

                    // F3-06: el crédito que dejó un cobro anterior se gasta cuando quien cobra lo
                    // pide. Es la otra mitad de la rama de sobrepago —la que consume el saldo a favor
                    // en vez de dejarlo crecer— y sin esto `saldo_a_favor` solo se llenaría.
                    var usarCredito = c > 0 && creditoAntes > 0m ? creditoAntes : 0m;

                    // Con crédito aplicado o con sobrepago, las asignaciones las pone la FIFO del
                    // servicio. `Reparto` reparte un importe que no sabe cuánto crédito hay delante.
                    var asignaciones = usarCredito > 0m || sobrepago
                        ? null
                        : aleatorio.Next(2) == 0
                            ? Reparto(pendientesAntes, aleatorio, importe)
                            : null;

                    var respuesta = await cobros.RegistrarAsync(
                        new CobroPeticion
                        {
                            ClienteId = clienteId,
                            ContratoId = contratoId,
                            FechaEfectiva = hoy,
                            MonedaRecibida = Moneda.USD,
                            ImporteRecibido = importe,
                            FormaPagoId = formaPagoId,
                            CuentaReceptoraId = cuentaId,
                            UsarSaldoAFavor = usarCredito,
                            Asignaciones = asignaciones,
                        },
                        Usuario,
                        Ip,
                        mutacion: null,
                        ct);

                    recibido += importe;
                    aplicado += respuesta.TotalAsignado;
                    usado += respuesta.SaldoFavorUsado;

                    // El dinero de este cobro no se pierde: va entero a las facturas o se queda en el
                    // excedente. Esta es la igualdad que faltaba y la que se rompe si el servicio
                    // calcula mal el sobrante o si se olvida del crédito que este cobro gastó.
                    Redondeo.Importe(respuesta.TotalAsignado + respuesta.Excedente).Should().Be(
                        Redondeo.Importe(importe + respuesta.SaldoFavorUsado),
                        $"cobro {c + 1} de {CobrosPorCaso} del caso {caso}"
                            + (sobrepago ? " (sobrepago a propósito)" : string.Empty)
                            + ": lo asignado más el excedente tiene que ser el importe recibido más "
                            + "el saldo a favor aplicado");

                    respuesta.Excedente.Should().BeGreaterThanOrEqualTo(
                        0m,
                        "cobrar de más es saldo a favor (§3.10.1), nunca un excedente negativo");

                    respuesta.SaldoFavorUsado.Should().BeLessThanOrEqualTo(
                        creditoAntes,
                        "no se puede aplicar más crédito del que había: el saldo a favor del contrato "
                            + "se corrige y el banco no");

                    foreach (var asignacion in respuesta.Asignaciones)
                    {
                        // F3-09: una asignación puede ser a una fianza, que no está en `pendientesAntes`.
                        // Aquí no las hay —esta prueba no crea fianzas—, así que el assertion sigue
                        // teniendo que encontrar la factura.
                        asignacion.FacturaId.Should().NotBeNull("esta prueba solo asigna a facturas");
                        asignacion.Importe.Should().BeLessThanOrEqualTo(
                            pendientesAntes[asignacion.FacturaId!.Value],
                            "ninguna asignación puede pasar del pendiente de su factura");
                    }

                    await ComprobarInvariantesAsync(
                        contexto, contratoId, hoy, Redondeo.Importe(recibido), ct);
                }

                    aplicado.Should().BeLessThanOrEqualTo(
                    Redondeo.Importe(recibido + usado),
                    "no se puede asignar más dinero del que ha entrado, ni del que ya estaba en el "
                        + "saldo a favor del contrato: el saldo del cliente se corrige y el banco no");
            }
            catch (Exception fallo)
            {
                throw new InvalidOperationException(
                    $"Caso {caso} de {Casos} (semilla {Semilla}, "
                        + $"caso de sobrepago: {caso % DivisorSobrepago == 0}): {fallo.Message}",
                    fallo);
            }
        }

        // Las dos últimas cuentas son las que impiden que esta prueba vuelva a ser la que era: si el
        // generador dejara de producir sobrepagos o de gastar crédito, las igualdades de arriba
        // seguirían dando y nadie se enteraría de que ya no se está probando la rama.
        sobrepagos.Should().Be(
            Casos / DivisorSobrepago,
            "uno de cada cuatro casos tiene que cobrar más que su cartera entera");

        usado.Should().BeGreaterThan(
            0m,
            "el crédito que deja un cobro de más tiene que gastarse en algún cobro posterior: "
                + "si no se gasta nunca, `saldo_a_favor` solo se lee, no se ejercita");
    }

    /// <summary>
    /// Las tres igualdades que tienen que dar siempre, y las tres a la vez.
    /// <list type="number">
    /// <item>
    /// <b>Deuda.</b> <c>Σ totales − Σ retenciones − Σ cobrado − Σ ajustes = Σ pendientes − Σ
    /// sobrepago</c>. El término del sobrepago es lo que hace que la igualdad sea satisfacible: es la
    /// parte de lo cobrado que <see cref="CalculoCobro"/> no puede dejar como pendiente porque lo
    /// acotó a cero (§3.10.1). Antes de este cambio esa parte no estaba en la fórmula, así que la
    /// identidad solo se cumplía mientras nadie cobrara de más —y, con el recorte del servicio, no
    /// había forma de que eso pasara por el camino normal—.
    /// </item>
    /// <item>
    /// <b>Dinero.</b> <c>Σ importe recibido = Σ cobrado + crédito vivo en saldo_a_favor</c>.
    /// Es la que cierra la rama: el dinero que entra se reparte entre facturas y crédito, y si el
    /// servicio perdiera el excedente, o no descontara el crédito que gastó, esta —y solo esta— lo
    /// notaría. La vista de cuentas de §3.13 (F3-10) depende de que dé.
    /// </item>
    /// <item>
    /// <b>Cruce.</b> El crédito vivo nunca es menor que lo que se ha cobrado de más. Lo que un cobro
    /// paga de más a una factura tiene que acabar en el saldo a favor del contrato; si un día el
    /// recorte por factura desapareciera, aquí se vería aunque las otras dos igualdades dieran.
    /// </item>
    /// </list>
    /// <para>
    /// Las retenciones y los ajustes por diferencia de cambio entran por sus columnas de verdad
    /// (<c>factura.retencion</c> y <c>ajuste_diferencia_cambio</c>). En esta prueba los dos valen
    /// siempre cero —las facturas se generan sin retención y se cobra en la moneda de la factura, sin
    /// diferencia de cambio que ajustar—, pero están escritos y se leen de la tabla: el término que
    /// falta es justo el que hacía la comprobación anterior insensible al crédito.
    /// </para>
    /// </summary>
    private static async Task ComprobarInvariantesAsync(
        AtlasRentDbContext contexto,
        Guid contratoId,
        DateOnly hoy,
        decimal recibido,
        CancellationToken ct)
    {
        var cartera = await SituacionesAsync(contexto, contratoId, hoy, ct);

        var sumaTotales = Redondeo.Importe(cartera.Situaciones.Sum(s => s.Total));
        var sumaRetenciones = Redondeo.Importe(cartera.Situaciones.Sum(s => s.Retencion));
        var sumaCobrado = Redondeo.Importe(cartera.Situaciones.Sum(s => s.Situacion.Cobrado));
        var sumaAjustado = Redondeo.Importe(cartera.Situaciones.Sum(s => s.Situacion.Ajustado));
        var sumaPendientes = Redondeo.Importe(cartera.Situaciones.Sum(s => s.Situacion.Pendiente));
        var sumaSobrepago = Redondeo.Importe(cartera.Situaciones.Sum(s =>
            Math.Max(0m, s.Situacion.Cobrado + s.Situacion.Ajustado - (s.Total - s.Retencion))));

        foreach (var situacion in cartera.Situaciones)
        {
            situacion.Situacion.Pendiente.Should().BeGreaterThanOrEqualTo(
                0m,
                "cobrar de más es saldo a favor (§3.10.1), nunca un pendiente negativo");
        }

        Redondeo.Importe(sumaTotales - sumaRetenciones - sumaCobrado - sumaAjustado).Should().Be(
            Redondeo.Importe(sumaPendientes - sumaSobrepago),
            "Σ totales − Σ retenciones − Σ cobrado − Σ ajustes = Σ pendientes − Σ sobrepago");

        Redondeo.Importe(recibido).Should().Be(
            Redondeo.Importe(sumaCobrado + cartera.Credito),
            "Σ importe recibido = Σ cobrado + el crédito que queda en saldo_a_favor: el dinero que entra "
                + "se reparte entre facturas y saldo a favor, y no se pierde por el camino");

        cartera.Credito.Should().BeGreaterThanOrEqualTo(
            sumaSobrepago,
            "lo que un cobro ha pagado de más a una factura tiene que estar en el saldo a favor del contrato");

        cartera.Credito.Should().BeGreaterThanOrEqualTo(
            0m,
            "el saldo a favor es del contrato y nunca negativo: un crédito negativo es un cobro, no un saldo");
    }

    /// <summary>
    /// Situación de cobro de cada factura del contrato, con lo que además queda de crédito. La
    /// consulta del crédito va en la misma lectura y no aparte porque las dos se necesitan para la
    /// misma igualdad: mirar solo una de las dos es lo que hacía la prueba anterior.
    /// </summary>
    private static async Task<EstadoCartera> SituacionesAsync(
        AtlasRentDbContext contexto,
        Guid contratoId,
        DateOnly hoy,
        CancellationToken ct)
    {
        contexto.ChangeTracker.Clear();

        var facturas = await contexto.Factura.AsNoTracking()
            .Where(f => f.ContratoId == contratoId)
            .ToListAsync(ct);

        var cobrados = await CobradoPorFacturaAsync(contexto, [.. facturas.Select(f => f.Id)], ct);
        var ajustados = await AjustadoPorFacturaAsync(contexto, [.. facturas.Select(f => f.Id)], ct);

        var situaciones = facturas.Select(f => new SituacionFactura(
            f.Total,
            f.Retencion,
            CalculoCobro.Calcular(
                f.Total,
                f.Retencion,
                cobrados.GetValueOrDefault(f.Id),
                ajustados.GetValueOrDefault(f.Id),
                f.FechaVencimiento,
                hoy,
                f.Estado))).ToList();

        return new EstadoCartera(situaciones, await CreditoAsync(contexto, contratoId, ct));
    }

    /// <summary>
    /// Crédito vivo del contrato en la moneda en que se cobra, leído de la fila de
    /// <c>saldo_a_favor</c>. Es el mismo contador que mueve <c>AplicarSaldo</c>: si el servicio
    /// escribiera otra cifra, esta suma no saldría.
    /// </summary>
    private static async Task<decimal> CreditoAsync(
        AtlasRentDbContext contexto,
        Guid contratoId,
        CancellationToken ct)
        => await contexto.SaldoAFavor.AsNoTracking()
            .Where(s => s.ContratoId == contratoId && s.Moneda == Moneda.USD)
            .SumAsync(s => (decimal?)s.Importe, ct) ?? 0m;

    private static async Task<Dictionary<Guid, decimal>> PendientesAsync(
        AtlasRentDbContext contexto,
        Guid contratoId,
        DateOnly hoy,
        CancellationToken ct)
    {
        var facturas = await contexto.Factura.AsNoTracking()
            .Where(f => f.ContratoId == contratoId)
            .ToListAsync(ct);

        var cobrados = await CobradoPorFacturaAsync(contexto, [.. facturas.Select(f => f.Id)], ct);
        var ajustados = await AjustadoPorFacturaAsync(contexto, [.. facturas.Select(f => f.Id)], ct);

        return facturas.ToDictionary(
            f => f.Id,
            f => CalculoCobro.Calcular(
                f.Total,
                f.Retencion,
                cobrados.GetValueOrDefault(f.Id),
                ajustados.GetValueOrDefault(f.Id),
                f.FechaVencimiento,
                hoy,
                f.Estado).Pendiente);
    }

    /// <summary>
    /// Ajustes por diferencia de cambio vigentes de cada factura (F3-07, §3.10.2). Va aparte de
    /// <see cref="CobradoPorFacturaAsync"/> por el mismo motivo que en el servicio: no es dinero que
    /// entró en caja, y en esta prueba siempre sale vacío.
    /// </summary>
    private static async Task<Dictionary<Guid, decimal>> AjustadoPorFacturaAsync(
        AtlasRentDbContext contexto,
        Guid[] facturaIds,
        CancellationToken ct)
    {
        if (facturaIds.Length == 0)
        {
            return [];
        }

        return await contexto.AjusteDiferenciaCambio.AsNoTracking()
            .Where(a => a.AnuladoAt == null && facturaIds.Contains(a.FacturaId))
            .GroupBy(a => a.FacturaId)
            .Select(g => new { FacturaId = g.Key, Ajustado = g.Sum(a => a.Importe) })
            .ToDictionaryAsync(x => x.FacturaId, x => x.Ajustado, ct);
    }

    /// <summary>
    /// La misma suma que hace el servicio, escrita aquí a propósito: si el servicio y la prueba
    /// compartieran el método, un error en ese método haría pasar las dos a la vez.
    /// </summary>
    private static async Task<Dictionary<Guid, decimal>> CobradoPorFacturaAsync(
        AtlasRentDbContext contexto,
        Guid[] facturaIds,
        CancellationToken ct)
    {
        if (facturaIds.Length == 0)
        {
            return [];
        }

        return await contexto.CobroAsignacion.AsNoTracking()
            // F3-09: el filtro por `FacturaId` no nulo deja fuera las asignaciones a fianza, que no
            // son deuda de ninguna factura. Esta es la misma cuenta que escribe el servicio, y por eso
            // tiene que quedarse fuera igual que allí: una asignación a fianza no puede mover el
            // pendiente de una factura.
            .Where(a => a.Vigente
                && a.FacturaId != null
                && facturaIds.Contains(a.FacturaId.Value)
                && a.Cobro!.Estado == EstadoCobro.Registrado)
            .GroupBy(a => a.FacturaId!.Value)
            .Select(g => new { FacturaId = g.Key, Cobrado = g.Sum(a => a.Importe) })
            .ToDictionaryAsync(x => x.FacturaId, x => x.Cobrado, ct);
    }

    /// <summary>
    /// Reparto explícito al azar: una selección de facturas con un porcentaje al azar de su pendiente,
    /// recortado a lo que queda del cobro. Nunca pide más de lo pendiente ni más de lo que entra, que
    /// son las dos reglas que el servicio tiene que hacer cumplir.
    /// </summary>
    private static IReadOnlyList<CobroAsignacionPeticion> Reparto(
        Dictionary<Guid, decimal> pendientes,
        Random aleatorio,
        decimal disponible)
    {
        var lineas = new List<CobroAsignacionPeticion>();
        var restante = disponible;

        foreach (var (facturaId, pendiente) in pendientes.OrderBy(_ => aleatorio.Next()))
        {
            if (restante <= 0m)
            {
                break;
            }

            if (pendiente <= 0m)
            {
                continue;
            }

            var importe = Math.Round(
                pendiente * (decimal)aleatorio.NextDouble(),
                2,
                MidpointRounding.AwayFromZero);
            importe = Math.Min(importe, Math.Min(restante, pendiente));

            if (importe <= 0m)
            {
                continue;
            }

            lineas.Add(new CobroAsignacionPeticion { FacturaId = facturaId, Importe = importe });
            restante = Redondeo.Importe(restante - importe);
        }

        return lineas;
    }

    private static decimal Importe(Random aleatorio, decimal minimo, decimal maximo) =>
        Math.Round(
            (decimal)aleatorio.NextDouble() * (maximo - minimo) + minimo,
            2,
            MidpointRounding.AwayFromZero);

    private sealed record SituacionFactura(decimal Total, decimal Retencion, SituacionCobroFactura Situacion);

    /// <summary>
    /// La cartera entera con su crédito. Van juntos porque la igualdad que se comprueba los necesita a
    /// los dos: las facturas dicen lo que se debe y el crédito dice dónde está lo que se pagó de más.
    /// </summary>
    private sealed record EstadoCartera(List<SituacionFactura> Situaciones, decimal Credito);

    private sealed class FuenteSinConsultar : IFuenteTipoCambio
    {
        public Task<decimal> ObtenerAsync(Moneda moneda, CancellationToken ct) =>
            throw new NotSupportedException(
                "Esta prueba va por USD, que vale 1 sin consultar la fuente (F3-01).");
    }

    /// <summary>
    /// Política que lo permite todo: lo que se prueba aquí son los saldos, no los permisos. Los
    /// permisos tienen su propia prueba por la API real (<see cref="CobroPermisosTests"/>).
    /// </summary>
    private sealed class PoliticaQuePermiteTodo : IPolicyService
    {
        public IReadOnlySet<string> Permisos { get; } = new HashSet<string>();

        public Task CargarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public bool Puede(string accion) => true;

        public void Exigir(string accion, string? recurso = null)
        {
        }

        public Task<bool> PuedeAsync(string accion, Guid usuarioId, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<IReadOnlySet<string>> PermisosDeAsync(Guid usuarioId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task<string?> RolDeAsync(Guid usuarioId, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task AuditarDenegadoAsync(string accion, string? recurso, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task ExigirDentroDelTechoAsync(
            Guid usuarioId,
            string accion,
            bool activar,
            CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}