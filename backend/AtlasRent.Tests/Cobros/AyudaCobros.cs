using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Contratos;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Tests.Cobros;

/// <summary>
/// Apoyo común de las pruebas de cobros (F3-06).
/// <para>
/// Cliente, serie, tipo de cambio y factura salen de <see cref="AyudaFacturas"/>: son las mismas
/// piezas que usa F3-02 y duplicar su alta haría que las dos tareas probaran datos distintos.
/// Lo que se añade aquí es lo que es propio de esta tarea: forma de pago y cuenta receptora, que son
/// configuración y las siembra el operador (§9 de <c>DOC_TECNICA.md</c>), y el contrato, sin el cual
/// no hay a qué facturas asignar.
/// </para>
/// <para>
/// La base es única y la comparten todas las pruebas de la colección, así que nada puede afirmar sobre
/// valores absolutos: cada prueba trae su cliente, su contrato, su serie y sus facturas.
/// </para>
/// </summary>
internal static class AyudaCobros
{
    /// <summary>
    /// Cambio del día en pesos que usan las pruebas que necesitan uno.
    /// <para>
    /// <b>Es el mismo valor que carga F3-02</b> y tiene que serlo: la base es única y
    /// <c>tipo_cambio</c> es único por día y moneda, así que dos tareas que escribieran valores
    /// distintos el mismo día se pisarían y una de las dos assertions leería el cambio de la otra. Por
    /// eso el valor vive aquí y no se elige por prueba.
    /// </para>
    /// </summary>
    public const decimal CambioDelDia = 60.123456m;

    /// <summary>
    /// Devuelve el cambio de un día al estado en que lo encontró la base compartida: sin fila y sin
    /// documentos apuntando a ella. Se usa con <c>await using</c> al final de una prueba que haya emitido
    /// documentos en DOP.
    /// <para>
    /// El problema que resuelve está anotado en <c>docs/BUGS.md</c> (BUG-006): la prueba de F3-02 que
    /// comprueba el 409 de la emisión **borra** la fila de <c>tipo_cambio</c> del día, y en cuanto
    /// cualquier factura emitida o cualquier cobro de DOP la congela, ese borrado choca con
    /// <c>fk_*_tipo_cambio_tipo_cambio_id</c> y la prueba de F3-02 falla con un error de clave foránea. El
    /// diseño del producto es correcto (el cambio congelado no se puede borrar, y por eso
    /// <c>tipo_cambio</c> no concede <c>DELETE</c> a <c>rent_app</c>); lo que hay que devolver es la base
    /// compartida al punto de partida, y eso solo lo puede hacer la conexión de superusuario.
    /// </para>
    /// <para>
    /// Toca <b>todos</b> los documentos que referencien el cambio de ese día y ese tipo de moneda,
    /// no solo los de la prueba: el SQL no filtra por documento porque no hay forma de saber
    /// cuáles son «de esta prueba». Hoy es inocuo porque las clases de la colección se ejecutan en
    /// serie y cada una se limpia al terminar, y porque dejar esas referencias a <c>NULL</c> no
    /// rompe nada (la columna es nula por diseño y la emisión la vuelve a poner). Si alguna vez se
    /// paraleliza la colección, este helper tiene que pasar a filtrar por los documentos que la
    /// prueba creó.
    /// </para>
    /// </summary>
    public static IAsyncDisposable SinRastroDeCambio(ContenedorBdFixture bd, DateOnly fecha, Moneda moneda)
        => new LimpiezaDeCambio(bd, fecha, moneda);

    private sealed class LimpiezaDeCambio(ContenedorBdFixture bd, DateOnly fecha, Moneda moneda) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await using var contexto = bd.CrearContexto(bd.CadenaSuperusuario);

            await contexto.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 UPDATE factura SET tipo_cambio_id = NULL
                 WHERE tipo_cambio_id IN (SELECT id FROM tipo_cambio WHERE fecha = {fecha} AND moneda = {(int)moneda});
                 UPDATE cobro SET tipo_cambio_id = NULL
                 WHERE tipo_cambio_id IN (SELECT id FROM tipo_cambio WHERE fecha = {fecha} AND moneda = {(int)moneda});
                 DELETE FROM tipo_cambio WHERE fecha = {fecha} AND moneda = {(int)moneda};
                 """);
        }
    }

    public static Task<SesionCliente> SesionAsync(
        ContenedorBdFixture bd,
        ApiTestFactory api,
        string rol,
        CancellationToken ct) =>
        AyudaFacturas.SesionAsync(bd, api, rol, ct);

    /// <summary>
    /// Forma de pago de la prueba, creada por la configuración. No se siembra en el código: es dato de
    /// configuración y quien lo pone es el operador, así que la prueba lo crea por la misma API que
    /// usaría él.
    /// </summary>
    public static async Task<Guid> FormaPagoAsync(SesionCliente sesion, CancellationToken ct)
    {
        var creada = await sesion.PostAsync(
            "/api/v1/config/formas-pago",
            new { nombre = $"Forma {Guid.NewGuid():N}"[..20], activa = true },
            ct);

        creada.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    /// <summary>Cuenta receptora de la prueba, en la moneda que le pida la prueba.</summary>
    public static async Task<Guid> CuentaAsync(SesionCliente sesion, Moneda moneda, CancellationToken ct)
    {
        var creada = await sesion.PostAsync(
            "/api/v1/config/cuentas",
            new
            {
                nombre = $"Cuenta {Guid.NewGuid():N}"[..20],
                banco = "Prueba",
                moneda = moneda.ToString(),
                activa = true,
            },
            ct);

        creada.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Cliente y contrato de la prueba. El contrato se queda en borrador: para asignar un cobro solo
    /// hace falta que exista y que sea del cliente, y recorrer el flujo entero de aprobación de §3.5
    /// probaría el recorrido de contratos, no el de cobros.
    /// </summary>
    public static async Task<(Guid ClienteId, Guid ContratoId)> ContratoAsync(
        SesionCliente sesion,
        ContenedorBdFixture bd,
        CancellationToken ct)
    {
        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);

        var contratoId = await AyudaContrato.CrearAsync(
            sesion,
            AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, moneda: "USD"),
            ct);

        return (clienteId, contratoId);
    }

    /// <summary>
    /// Factura emitida del contrato, con una única línea de renta y el total pedido. Va por la API
    /// porque el número y el congelado del tipo de cambio los saca el contador transaccional de
    /// §3.7, y una factura emitida sin pasar por ahí no es una factura emitida.
    /// </summary>
    /// <param name="unidadId">
    /// Nulo es lo normal. Desde F3-10 se pasa cuando la prueba necesita la columna de unidad o el
    /// filtro por unidad de la vista única: la factura puede no tenerla porque el alta no la exige, y
    /// una vista de cuentas sin unidad no se puede filtrar por ella.
    /// </param>
    public static async Task<Guid> EmitidaAsync(
        SesionCliente sesion,
        Guid clienteId,
        Guid? contratoId,
        Guid serieId,
        Moneda moneda,
        DateOnly vencimiento,
        decimal total,
        CancellationToken ct,
        Guid? unidadId = null)
    {
        var creada = await sesion.PostAsync(
            "/api/v1/facturas",
            new
            {
                clienteId,
                contratoId,
                unidadId,
                serieId,
                moneda = moneda.ToString(),
                fechaVencimiento = vencimiento.ToString("yyyy-MM-dd"),
                concepto = $"Factura {Guid.NewGuid():N}"[..30],
            },
            ct);

        creada.StatusCode.Should().Be(HttpStatusCode.OK, "la factura se crea en borrador");
        var facturaId = (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();

        await AyudaFacturas.AgregarLineaAsync(sesion, facturaId, TipoLineaFactura.Renta, 1m, total, null, ct);

        var emitida = await sesion.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);
        emitida.StatusCode.Should().Be(HttpStatusCode.OK, await emitida.Cuerpo());

        return facturaId;
    }

    /// <summary>Cuerpo de un cobro, con los campos que no dependen del test ya puestos.</summary>
    /// <param name="tipoCambioAplicado">
    /// F3-07 (§3.10.2): el cambio que el gerente dice que se usó de verdad. Nulo es lo normal y lo que
    /// quiere la mayoría de las pruebas: entonces se usa el del día.
    /// </param>
    /// <param name="facturasSeleccionadas">
    /// F3-10 (§3.13): los identificadores de las filas marcadas en la vista de cuentas, para que la
    /// propuesta sea para esas y no para la FIFO. Nulo es lo normal.
    /// </param>
    public static object Peticion(
        Guid clienteId,
        Guid? contratoId,
        DateOnly fechaEfectiva,
        Moneda moneda,
        decimal importeRecibido,
        Guid formaPagoId,
        Guid cuentaReceptoraId,
        decimal usarSaldoAFavor = 0m,
        string? referencia = null,
        object? asignaciones = null,
        decimal? tipoCambioAplicado = null,
        object? facturasSeleccionadas = null) =>
        new
        {
            clienteId,
            contratoId,
            fechaEfectiva = fechaEfectiva.ToString("yyyy-MM-dd"),
            monedaRecibida = moneda.ToString(),
            importeRecibido,
            formaPagoId,
            referencia,
            cuentaReceptoraId,
            usarSaldoAFavor,
            asignaciones,
            tipoCambioAplicado,
            facturasSeleccionadas,
        };

    /// <summary>
    /// Carga (o recarga) el cambio de un día por la API de F3-01, que es la carga manual que el propio
    /// 409 le señala a quien lo lee.
    /// </summary>
    public static async Task CargarCambioAsync(
        SesionCliente sesion,
        DateOnly fecha,
        Moneda moneda,
        decimal valor,
        CancellationToken ct)
    {
        var cargado = await sesion.PostAsync(
            "/api/v1/tipos-cambio",
            new
            {
                fecha = fecha.ToString("yyyy-MM-dd"),
                moneda = moneda.ToString(),
                valor = valor.ToString(CultureInfo.InvariantCulture),
            },
            ct);

        cargado.StatusCode.Should().Be(HttpStatusCode.OK, await cargado.Cuerpo());
    }

    /// <summary>
    /// Borra el cambio de un día. Hace falta porque la base es compartida: si otra prueba dejó
    /// cargado el cambio de hoy, la de «sin tipo de cambio» no estaría probando nada. Se borra por la
    /// conexión de superusuario porque <c>tipo_cambio</c> no concede <c>DELETE</c> a <c>rent_app</c>:
    /// ni la propia aplicación puede borrarlo.
    /// </summary>
    public static async Task QuitarCambioAsync(
        ContenedorBdFixture bd,
        DateOnly fecha,
        Moneda moneda,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaSuperusuario);
        await contexto.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM tipo_cambio WHERE fecha = {fecha} AND moneda = {(int)moneda}", ct);
    }

    /// <summary>
    /// Cambios del día que usan las pruebas de pago en otra moneda (F3-07).
    /// <para>
    /// <b>Valores que sí dividen y valores que no.</b> 59,50 con 59.500 da exactamente 1.000 dólares,
    /// que es el ejemplo de §3.10.2; 59,51 deja 17 centavos de diferencia, que es el caso para el que
    /// existe el ajuste por diferencia de cambio. Los dos tienen que caber en <c>numeric(18,6)</c> y
    /// ninguno puede ser el de F3-02 (<see cref="CambioDelDia"/>), porque el día es único por moneda:
    /// si esta prueba escribiera 59,50 en el cambio del día, la prueba de F3-02 leería 59,50 y
    /// fallaría.
    /// </para>
    /// </summary>
    public const decimal CambioExacto = 59.50m;

    public const decimal CambioConRedondeo = 59.51m;

    /// <summary>
    /// Fija (o borra) la tolerancia de diferencia de cambio por la misma API que usa la pantalla de
    /// ajustes generales, y devuelve lo que había antes para devolverlo al final.
    /// <para>
    /// <b>La base es compartida</b> (una sola fila de <c>ajuste_general</c> para toda la instalación),
    /// así que una prueba que deje la tolerancia puesta cambia el resultado de las demás. Por eso la
    /// ayuda devuelve lo anterior y las pruebas lo restauran con <c>using</c>.
    /// </para>
    /// <para>
    /// El modo de recordatorios se lee y se devuelve tal cual: el endpoint exige el campo entero, y
    /// esta ayuda no tiene por qué saber nada de él.
    /// </para>
    /// </summary>
    public static async Task<(decimal? Usd, decimal? Dop)> FijarToleranciaAsync(
        SesionCliente sesion,
        decimal? toleranciaUsd,
        decimal? toleranciaDop,
        CancellationToken ct)
    {
        var leidos = await sesion.GetAsync("/api/v1/config/ajustes", ct);
        leidos.StatusCode.Should().Be(HttpStatusCode.OK, await leidos.Cuerpo());

        var actuales = await leidos.Content.ReadFromJsonAsync<JsonElement>(ct);
        var modo = actuales.GetProperty("modoRecordatorios").GetString();

        var guardado = await sesion.PutAsync(
            "/api/v1/config/ajustes",
            new { toleranciaCambioUsd = toleranciaUsd, toleranciaCambioDop = toleranciaDop, modoRecordatorios = modo },
            ct);
        guardado.StatusCode.Should().Be(HttpStatusCode.OK, await guardado.Cuerpo());

        return (actuales.GetProperty("toleranciaCambioUsd").ValueKind == System.Text.Json.JsonValueKind.Null
            ? null
            : actuales.GetProperty("toleranciaCambioUsd").GetDecimal(),
            actuales.GetProperty("toleranciaCambioDop").ValueKind == System.Text.Json.JsonValueKind.Null
                ? null
                : actuales.GetProperty("toleranciaCambioDop").GetDecimal());
    }

    /// <summary>
    /// Tolerancia que se puede tocar durante una prueba y devuelve a su valor anterior al terminar, con
    /// <c>await using var tolerancia = await AyudaCobros.ToleranciaTemporal(...)</c>.
    /// </summary>
    public static async Task<IAsyncDisposable> ToleranciaTemporal(
        SesionCliente sesion,
        decimal? toleranciaUsd,
        decimal? toleranciaDop,
        CancellationToken ct)
    {
        var anterior = await FijarToleranciaAsync(sesion, toleranciaUsd, toleranciaDop, ct);
        return new RestaurarTolerancia(sesion, anterior);
    }

    private sealed class RestaurarTolerancia(SesionCliente sesion, (decimal? Usd, decimal? Dop) anterior)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
            => await FijarToleranciaAsync(sesion, anterior.Usd, anterior.Dop, CancellationToken.None);
    }

    /// <summary>Ajuste por diferencia de cambio tal y como queda guardado, para comprobarlo fila a fila.</summary>
    public static async Task<List<AjusteDiferenciaCambio>> AjustesAsync(
        ContenedorBdFixture bd,
        Guid facturaId,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        return await contexto.AjusteDiferenciaCambio.AsNoTracking()
            .Where(a => a.FacturaId == facturaId)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Saldo a favor del contrato leído de la base, que es donde se comprueba.
    /// </summary>
    public static async Task<decimal?> SaldoAsync(
        ContenedorBdFixture bd,
        Guid contratoId,
        Moneda moneda,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        return await contexto.SaldoAFavor.AsNoTracking()
            .Where(s => s.ContratoId == contratoId && s.Moneda == moneda)
            .Select(s => (decimal?)s.Importe)
            .SingleOrDefaultAsync(ct);
    }

    /// <summary>
    /// Asignaciones de una factura tal y como están en la tabla, vivas y ninguna: para comprobar que la
    /// anulación las deja de contar sin borrarlas.
    /// </summary>
    public static async Task<List<CobroAsignacion>> AsignacionesAsync(
        ContenedorBdFixture bd,
        Guid facturaId,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        return await contexto.CobroAsignacion.AsNoTracking().Where(a => a.FacturaId == facturaId).ToListAsync(ct);
    }

    /// <summary>Facturas del contrato con su total, para las sumas del test que las necesite.</summary>
    public static async Task<List<(Guid Id, decimal Total)>> FacturasAsync(
        ContenedorBdFixture bd,
        Guid contratoId,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        var filas = await contexto.Factura.AsNoTracking()
            .Where(f => f.ContratoId == contratoId)
            .Select(f => new { f.Id, f.Total })
            .ToListAsync(ct);

        return [.. filas.Select(f => (f.Id, f.Total))];
    }

    /// <summary>Lo que dice la factura leída por la API, que es el DTO que hay que comprobar.</summary>
    public static async Task<JsonElement> FacturaAsync(
        SesionCliente sesion,
        Guid facturaId,
        CancellationToken ct)
    {
        var leida = await sesion.GetAsync($"/api/v1/facturas/{facturaId:D}", ct);
        leida.StatusCode.Should().Be(HttpStatusCode.OK, await leida.Cuerpo());

        return await leida.Content.ReadFromJsonAsync<JsonElement>(ct);
    }
}