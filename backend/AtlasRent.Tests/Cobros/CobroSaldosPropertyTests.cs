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
/// comprueba que lo pendiente cuadra con lo facturado menos lo cobrado, que ningún pendiente es
/// negativo y que ninguna asignación pasó del pendiente que tenía su factura en ese momento. La
/// semilla es fija para que un fallo se pueda reproducir, y el mensaje dice el caso y la semilla.
/// </para>
/// <para>
/// Los cobros van por <see cref="ServicioCobros"/> y no por HTTP: lo que se está probando es el
/// reparto, los límites y las transacciones del servicio, y 200 casos por HTTP serían además minutos
/// de prueba. Las facturas se escriben directamente en la base porque son datos de partida: crear 600
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
            new PoliticaQuePermiteTodo(),
            api.Reloj);

        var numeroSerie = 1_000_000;
        var recibido = 0m;
        var aplicado = 0m;

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

                    var importe = Importe(aleatorio, 1m, 4_000m);
                    var asignaciones = aleatorio.Next(2) == 0
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
                            Asignaciones = asignaciones,
                        },
                        Usuario,
                        Ip,
                        mutacion: null,
                        ct);

                    recibido += importe;
                    aplicado += respuesta.TotalAsignado;

                    foreach (var asignacion in respuesta.Asignaciones)
                    {
                        asignacion.Importe.Should().BeLessThanOrEqualTo(
                            pendientesAntes[asignacion.FacturaId],
                            "ninguna asignación puede pasar del pendiente de su factura");
                    }

                    ComprobarInvariantes(await SituacionesAsync(contexto, contratoId, hoy, ct));
                }

                    aplicado.Should().BeLessThanOrEqualTo(
                    recibido,
                    "no se puede asignar más dinero del que ha entrado: el saldo del cliente se corrige y el banco no");
            }
            catch (Exception fallo)
            {
                throw new InvalidOperationException(
                    $"Caso {caso} de {Casos} (semilla {Semilla}): {fallo.Message}", fallo);
            }
        }
    }

    /// <summary>
    /// La identidad que tiene que dar siempre: <b>Σ pendientes = Σ totales − Σ retenciones − Σ
    /// cobrado</b>. Las retenciones son cero porque la columna es de F3-04, y el término está escrito
    /// para que su día baste con restar lo mismo que ya se resta.
    /// </summary>
    private static void ComprobarInvariantes(List<SituacionFactura> situaciones)
    {
        var sumaTotales = Redondeo.Importe(situaciones.Sum(s => s.Total));
        var sumaRetenciones = 0m;
        var sumaCobrado = Redondeo.Importe(situaciones.Sum(s => s.Situacion.Cobrado));

        foreach (var situacion in situaciones)
        {
            situacion.Situacion.Pendiente.Should().BeGreaterThanOrEqualTo(
                0m,
                "cobrar de más es saldo a favor (§3.10.1), nunca un pendiente negativo");
        }

        Redondeo.Importe(sumaTotales - sumaRetenciones - sumaCobrado)
            .Should().Be(
                Redondeo.Importe(situaciones.Sum(s => s.Situacion.Pendiente)),
                "Σ pendientes = Σ totales − Σ retenciones − Σ cobrado");
    }

    private static async Task<List<SituacionFactura>> SituacionesAsync(
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

        return
        [
            .. facturas.Select(f => new SituacionFactura(
                f.Total,
                CalculoCobro.Calcular(f.Total, 0m, cobrados.GetValueOrDefault(f.Id), f.FechaVencimiento, hoy))),
        ];
    }

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

        return facturas.ToDictionary(
            f => f.Id,
            f => CalculoCobro.Calcular(f.Total, 0m, cobrados.GetValueOrDefault(f.Id), f.FechaVencimiento, hoy).Pendiente);
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
            .Where(a => a.Vigente && facturaIds.Contains(a.FacturaId) && a.Cobro!.Estado == EstadoCobro.Registrado)
            .GroupBy(a => a.FacturaId)
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

    private sealed record SituacionFactura(decimal Total, SituacionCobroFactura Situacion);

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