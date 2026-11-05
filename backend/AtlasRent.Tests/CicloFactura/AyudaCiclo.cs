using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cobros;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Tests.CicloFactura;

/// <summary>
/// Apoyo común de las pruebas del ciclo de vida de la factura (F3-08). Las piezas —cliente, contrato,
/// serie, forma de pago, cuenta, factura emitida y cobro— salen de <see cref="AyudaFacturas"/> y
/// <see cref="AyudaCobros"/> porque son las mismas que usan F3-02 y F3-06; duplicar su alta haría que
/// las tres tareas probaran facturas distintas.
/// <para>
/// Lo que se comprueba aquí se lee de la tabla y no del DTO, salvo cuando lo que importa es lo que
/// ve la pantalla. Anular no borra nada (AGENTS.md §6), y eso no se puede comprobar en un JSON: hay
/// que contar filas.
/// </para>
/// </summary>
internal static class AyudaCiclo
{
    /// <summary>Anula por la API real y comprueba que sale bien.</summary>
    public static async Task<JsonElement> AnularAsync(
        SesionCliente sesion,
        Guid facturaId,
        string motivo,
        CancellationToken ct)
    {
        var anulada = await sesion.PostAsync(
            $"/api/v1/facturas/{facturaId:D}/anular",
            new { motivo },
            ct);

        anulada.StatusCode.Should().Be(HttpStatusCode.OK, await anulada.Cuerpo());

        return await anulada.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    /// <summary>
    /// Cobra con las asignaciones que le diga la prueba. Con <c>null</c> deja que la aplicación las
    /// reparta ella; pasando la lista se cobra justo lo que se quiere comprobar y nada más.
    /// </summary>
    public static async Task<Guid> CobrarAsync(
        SesionCliente sesion,
        Guid clienteId,
        Guid contratoId,
        DateOnly hoy,
        decimal importeRecibido,
        Guid formaPagoId,
        Guid cuentaId,
        CancellationToken ct,
        params (Guid FacturaId, decimal Importe)[] asignaciones)
    {
        var cuerpo = AyudaCobros.Peticion(
            clienteId,
            contratoId,
            hoy,
            Moneda.USD,
            importeRecibido,
            formaPagoId,
            cuentaId,
            asignaciones: asignaciones.Length == 0
                ? null
                : asignaciones.Select(a => new { facturaId = a.FacturaId, importe = a.Importe }).ToArray());

        var registrado = await sesion.PostAsync("/api/v1/cobros", cuerpo, ct);
        registrado.StatusCode.Should().Be(HttpStatusCode.OK, await registrado.Cuerpo());

        return (await registrado.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    /// <summary>Factura tal y como está en la tabla, que es donde se comprueba que no se tocó.</summary>
    public static Task<Factura> FilaAsync(ContenedorBdFixture bd, Guid facturaId, CancellationToken ct) =>
        EnContextoAsync(bd, c => c.Factura.AsNoTracking().SingleAsync(f => f.Id == facturaId, ct));

    public static Task<Cobro> CobroFilaAsync(ContenedorBdFixture bd, Guid cobroId, CancellationToken ct) =>
        EnContextoAsync(bd, c => c.Cobro.AsNoTracking().SingleAsync(x => x.Id == cobroId, ct));

    public static Task<List<CobroAsignacion>> AsignacionesDeCobroAsync(
        ContenedorBdFixture bd,
        Guid cobroId,
        CancellationToken ct) =>
        EnContextoAsync(bd, c => c.CobroAsignacion.AsNoTracking().Where(a => a.CobroId == cobroId).ToListAsync(ct));

    /// <summary>Cuántas filas hay en la tabla. Para comprobar que una simulación no escribió nada.</summary>
    public static async Task<(long Facturas, long Asignaciones, long Comprobantes)> RecuentoAsync(
        ContenedorBdFixture bd,
        CancellationToken ct) =>
        await EnContextoAsync(bd, async c => (
            await c.Factura.AsNoTracking().CountAsync(ct),
            await c.CobroAsignacion.AsNoTracking().CountAsync(ct),
            await c.ComprobanteFiscal.AsNoTracking().CountAsync(ct)));

    /// <summary>
    /// Emite otra factura de la serie, que es como se comprueba que el número anulado no vuelve a
    /// salir y que el contador siguió contando.
    /// </summary>
    public static async Task<int> EmitirEnSerieAsync(
        SesionCliente sesion,
        Guid clienteId,
        Guid serieId,
        DateOnly vencimiento,
        decimal total,
        CancellationToken ct)
    {
        var facturaId = await AyudaFacturas.CrearFacturaAsync(
            sesion, clienteId, serieId, Moneda.USD, vencimiento, ct);

        await AyudaFacturas.AgregarLineaAsync(sesion, facturaId, TipoLineaFactura.Renta, 1m, total, null, ct);

        var emitida = await sesion.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);
        emitida.StatusCode.Should().Be(HttpStatusCode.OK, await emitida.Cuerpo());

        return (await emitida.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("numero").GetInt32();
    }

    private static async Task<T> EnContextoAsync<T>(
        ContenedorBdFixture bd,
        Func<AtlasRentDbContext, Task<T>> consulta)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        return await consulta(contexto);
    }
}