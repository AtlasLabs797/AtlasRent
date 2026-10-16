using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cobros;
using AtlasRent.Tests.Comprobantes;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Fianzas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Tests.Cuentas;

/// <summary>
/// Apoyo común de las pruebas de la vista única de cuentas (F3-10, docs/ESPECIFICACION.md §3.13).
/// <para>
/// <b>Todo va por la API real</b>, como el resto de las pruebas de F3: los números que interesan son
/// los que se ven en pantalla, y una vista montada a mano sobre la tabla probaría una vista que
/// existe. Cliente, contrato, serie, factura emitida y fianza salen de <see cref="AyudaFacturas"/>,
/// <see cref="AyudaCobros"/> y <see cref="AyudaFianzas"/> porque son las mismas piezas que usan las
/// demás tareas del bloque.
/// </para>
/// <para>
/// La base es única y la comparten todas las pruebas, así que cada prueba trae su cliente y filtra
/// por él. Es lo que además permite comprobar los totales a mano: con una sola página que se ve
/// entera, la suma de lo que hay en pantalla tiene que ser el pie.
/// </para>
/// </summary>
internal static class AyudaCuentas
{
    public static Task<SesionCliente> SesionAsync(
        ContenedorBdFixture bd,
        ApiTestFactory api,
        string rol,
        CancellationToken ct) =>
        AyudaFacturas.SesionAsync(bd, api, rol, ct);

    /// <summary>
    /// Las piezas de partida de casi todas las pruebas: cliente, contrato, unidad y serie propia, con
    /// el día que ve la aplicación.
    /// </summary>
    public static async Task<Escenario> EscenarioAsync(
        SesionCliente sesion,
        ContenedorBdFixture bd,
        CancellationToken ct,
        Moneda? monedaSerie = null,
        TipoSerie tipoSerie = TipoSerie.Normal,
        bool generaComprobante = false)
    {
        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(
            sesion, tipoSerie, monedaSerie, ct, generaComprobante: generaComprobante);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        if (generaComprobante)
        {
            // Desde F3-03 una serie con «genera comprobante» no emite sin numerador (BUG-007), así que
            // la vista con comprobante necesita uno configurado igual que la emisión que la deja lista.
            await AyudaComprobante.NumeradorAsync(
                sesion, serieId, hoy, ct, rangoDesde: 1, rangoHasta: AyudaComprobante.Amplio);
        }

        var unidadId = await UnidadDelContratoAsync(bd, contratoId, ct);

        return new Escenario(clienteId, contratoId, unidadId, serieId, hoy);
    }

    /// <summary>
    /// La unidad del contrato, leída de la base: es la columna <c>unidad</c> de la vista y sale de la
    /// relación, no de un dato que se le pase a la factura.
    /// </summary>
    private static async Task<Guid> UnidadDelContratoAsync(
        ContenedorBdFixture bd,
        Guid contratoId,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        return await contexto.Contrato.AsNoTracking()
            .Where(c => c.Id == contratoId)
            .Select(c => c.UnidadId)
            .SingleAsync(ct);
    }

    /// <summary>Factura emitida del escenario, con el total que le pase la prueba.</summary>
    /// <para>
    /// Se emite con la unidad del contrato: la vista de cuentas muestra esa columna y se filtra por
    /// ella, y el alta de una factura no la exige, así que si no se pasara la columna saldría vacía
    /// sin que nadie lo pidiera.
    /// </para>
    public static Task<Guid> EmitidaAsync(
        SesionCliente sesion,
        Escenario escenario,
        DateOnly vencimiento,
        decimal total,
        CancellationToken ct,
        Moneda? moneda = null) =>
        AyudaCobros.EmitidaAsync(
            sesion,
            escenario.ClienteId,
            escenario.ContratoId,
            escenario.SerieId,
            moneda ?? Moneda.USD,
            vencimiento,
            total,
            ct,
            escenario.UnidadId);

    /// <summary>
    /// Proforma emitida: una factura de serie <c>Proforma</c>, que es como las guarda el sistema
    /// (§3.7 y §3.8). No entra en la vista salvo que se pida con <c>incluirProformas</c>.
    /// </summary>
    public static async Task<Guid> ProformaAsync(
        SesionCliente sesion,
        Escenario escenario,
        decimal total,
        CancellationToken ct)
    {
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Proforma, Moneda.USD, ct);

        var facturaId = await AyudaFacturas.CrearFacturaAsync(
            sesion, escenario.ClienteId, serieId, Moneda.USD, escenario.Hoy.AddDays(15), ct,
            contratoId: escenario.ContratoId, unidadId: escenario.UnidadId);

        await AyudaFacturas.AgregarLineaAsync(
            sesion, facturaId, TipoLineaFactura.Renta, 1m, total, null, ct);

        var emitida = await sesion.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);
        emitida.StatusCode.Should().Be(HttpStatusCode.OK, await emitida.Cuerpo());

        return facturaId;
    }

    /// <summary>Alta de la fianza del contrato del escenario.</summary>
    public static Task<Guid> FianzaAsync(
        SesionCliente sesion,
        Escenario escenario,
        decimal exigido,
        CancellationToken ct,
        Moneda? moneda = null) =>
        AyudaFianzas.AltaAsync(sesion, escenario.ContratoId, exigido, moneda ?? Moneda.USD, 1, ct);

    /// <summary>
    /// Cobro a la factura con una asignación explícita, por el camino de F3-06. La forma de pago y la
    /// cuenta se crean por la API porque son configuración y quien las pone es el operador (§9 de
    /// <c>DOC_TECNICA.md</c>), no la prueba.
    /// </summary>
    public static async Task<Guid> CobrarAsync(
        SesionCliente sesion,
        Escenario escenario,
        Guid facturaId,
        decimal importe,
        DateOnly fechaEfectiva,
        CancellationToken ct)
    {
        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, Moneda.USD, ct);

        var registrado = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                escenario.ClienteId,
                escenario.ContratoId,
                fechaEfectiva,
                Moneda.USD,
                importe,
                formaPagoId,
                cuentaId,
                asignaciones: new[] { new { facturaId, importe } }),
            ct);

        registrado.StatusCode.Should().Be(HttpStatusCode.OK, await registrado.Cuerpo());

        return (await registrado.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    // ---------------------------------------------------------------- lectura de la vista

    /// <summary>
    /// La vista con la consulta que le pase la prueba. La página se pide entera (200 es el máximo que
    /// acepta la API) porque las pruebas filtro incluyen <c>clienteId</c> y con eso cabe todo lo que la
    /// prueba ha creado.
    /// </summary>
    public static async Task<JsonElement> VistaAsync(
        SesionCliente sesion,
        string consulta,
        CancellationToken ct,
        int? page = null,
        int? pageSize = 200)
    {
        var partes = new List<string> { $"pageSize={pageSize ?? 200}" };
        if (page is { } numero)
        {
            partes.Add($"page={numero}");
        }

        if (consulta.Length > 0)
        {
            partes.Add(consulta);
        }

        var leida = await sesion.GetAsync($"/api/v1/cuentas?{string.Join('&', partes)}", ct);
        leida.StatusCode.Should().Be(HttpStatusCode.OK, await leida.Cuerpo());

        return await leida.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    /// <summary>Las filas de la respuesta.</summary>
    public static JsonElement[] Filas(JsonElement vista) =>
        [.. vista.GetProperty("data").EnumerateArray()];

    /// <summary>Las filas de un tipo, que es como se busca una en concreto.</summary>
    public static JsonElement FilaDe(JsonElement vista, string tipo) =>
        Filas(vista).Should().ContainSingle(f => f.GetProperty("tipo").GetString() == tipo).Which;

    public static bool Contiene(JsonElement vista, Guid id) =>
        Filas(vista).Any(f => f.GetProperty("id").GetGuid() == id);

    /// <summary>
    /// Suma de una columna de dinero de las filas que se ven, tratando el nulo como cero. Es la
    /// cuenta que hace una persona al pie de la tabla, y la que tiene que dar lo mismo que el
    /// <c>totales</c> del servidor.
    /// </summary>
    public static decimal SumaDeFilas(IEnumerable<JsonElement> filas, string columna)
    {
        var suma = 0m;

        foreach (var fila in filas)
        {
            var valor = fila.GetProperty(columna);

            if (valor.ValueKind != JsonValueKind.Null)
            {
                suma += valor.GetDecimal();
            }
        }

        return suma;
    }

    /// <summary>Las piezas de partida de una prueba, con el día que ve la aplicación.</summary>
    public sealed record Escenario(Guid ClienteId, Guid ContratoId, Guid UnidadId, Guid SerieId, DateOnly Hoy);
}