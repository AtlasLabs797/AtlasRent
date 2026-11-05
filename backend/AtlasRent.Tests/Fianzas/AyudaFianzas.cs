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

namespace AtlasRent.Tests.Fianzas;

/// <summary>
/// Lo que las pruebas de fianzas (F3-09) tienen en común: crear el contrato, dar de alta la fianza y
/// cobrarla.
/// <para>
/// <b>Todo va por la API real</b>, también el alta y el cobro, porque los dos dependen de cosas que solo
/// existen dentro del dominio: el cobro necesita el tipo de cambio congelado y el contador de la serie
/// (§3.7, D9), y una fianza creada por debajo saltaría por encima de la auditoría y del permiso.
/// </para>
/// </summary>
public static class AyudaFianzas
{
    /// <summary>
    /// Contrato con su cliente, por el camino de F3-06 que ya existe. La fianza necesita un contrato y
    /// probarlo con un contrato de verdad es lo que hace que el 409 de "otra fianza para este contrato"
    /// signifique algo.
    /// </summary>
    public static Task<(Guid ClienteId, Guid ContratoId)> ContratoAsync(
        SesionCliente sesion,
        ContenedorBdFixture bd,
        CancellationToken ct) =>
        AyudaCobros.ContratoAsync(sesion, bd, ct);

    /// <summary>
    /// Alta de la fianza por la API. Devuelve el identificador leído de la respuesta, que es lo que la
    /// interfaz usaría, y no el de la fila: así la prueba comprueba que el endpoint devuelve algo
    /// utilizable y no solo que escribe en la base.
    /// </summary>
    public static async Task<Guid> AltaAsync(
        SesionCliente sesion,
        Guid contratoId,
        decimal importeExigido,
        Moneda moneda,
        int numeroFianzas,
        CancellationToken ct)
    {
        var creada = await sesion.PostAsync(
            "/api/v1/fianzas",
            new { contratoId, importeExigido, moneda = moneda.ToString(), numeroFianzas },
            ct);

        creada.StatusCode.Should().Be(HttpStatusCode.OK, await creada.Cuerpo());

        return (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("fianza").GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Fianza de <paramref name="exigido"/> cobrada en un solo cobro del mismo contrato. Es el camino
    /// de §3.11 —cobrar una fianza es un cobro con una asignación a la fianza— y no uno propio, que es
    /// justo lo que hay que comprobar.
    /// <para>
    /// La moneda se dice y no se deduce: el cobro se recibe en la moneda de la cuenta receptora, y esa
    /// es también la del importe. Pasarla como dato hace que una prueba que se equivoca de moneda falle
    /// por su cuenta y no por un 409 que no dice de qué iba.
    /// </para>
    /// <para>
    /// <b>La forma de pago y la cuenta se pueden traer de fuera</b> porque crearlas es configuración
    /// (F1-04), que es de administrador, y una prueba del permiso de fianza no puede crearlas con un
    /// gerente o se mediría el permiso equivocado.
    /// </para>
    /// </summary>
    public static async Task<Guid> CobradaAsync(
        SesionCliente sesion,
        Guid clienteId,
        Guid contratoId,
        Guid fianzaId,
        decimal cobrado,
        Moneda moneda,
        CancellationToken ct,
        Guid? formaPagoId = null,
        Guid? cuentaReceptoraId = null)
    {
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);
        var forma = formaPagoId ?? await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuenta = cuentaReceptoraId ?? await AyudaCobros.CuentaAsync(sesion, moneda, ct);

        var registrado = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId,
                contratoId,
                hoy,
                moneda,
                cobrado,
                forma,
                cuenta,
                asignaciones: new[] { new { fianzaId, importe = cobrado } }),
            ct);

        registrado.StatusCode.Should().Be(HttpStatusCode.OK, await registrado.Cuerpo());

        return fianzaId;
    }

    /// <summary>
    /// Cuerpo de una devolución. Los parámetros con valor por defecto son los de la devolución entera
    /// sin descuento, que es el caso más simple; los demás se dicen solo cuando se prueba otra cosa.
    /// </summary>
    public static object PeticionDevolucion(
        decimal importe,
        decimal importeDescontado = 0m,
        string? motivo = null,
        DateOnly? fecha = null) =>
        new { importe, importeDescontado, motivo, fecha = fecha?.ToString("yyyy-MM-dd") };

    /// <summary>La fianza tal y como la devuelve la API de lectura, para comprobar sus números.</summary>
    public static async Task<JsonElement> LeerAsync(SesionCliente sesion, Guid fianzaId, CancellationToken ct)
    {
        var leida = await sesion.GetAsync($"/api/v1/fianzas/{fianzaId:D}", ct);
        leida.StatusCode.Should().Be(HttpStatusCode.OK, await leida.Cuerpo());

        return (await leida.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("fianza");
    }

    /// <summary>
    /// Las cifras leídas de la base y no de la respuesta: cobrado, devuelto, retenido y saldo. Es el
    /// mismo criterio que el resto de las pruebas de F3 —la respuesta puede mentir por un DTO mal
    /// hecho, la tabla no—.
    /// </summary>
    public static async Task<(decimal Cobrado, decimal Devuelto, decimal Descontado, decimal Saldo)> CifrasAsync(
        ContenedorBdFixture bd,
        Guid fianzaId,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        var cobrado = await contexto.CobroAsignacion.AsNoTracking()
            .Where(a => a.FianzaId == fianzaId && a.Vigente && a.Cobro!.Estado == EstadoCobro.Registrado)
            .SumAsync(a => (decimal?)a.Importe, ct) ?? 0m;

        var salidas = await contexto.FianzaDevolucion.AsNoTracking()
            .Where(d => d.FianzaId == fianzaId)
            .GroupBy(d => d.FianzaId)
            .Select(g => new { Devuelto = g.Sum(d => d.Importe), Descontado = g.Sum(d => d.ImporteDescontado) })
            .FirstOrDefaultAsync(ct);

        return (
            Redondeo.Importe(cobrado),
            Redondeo.Importe(salidas?.Devuelto ?? 0m),
            Redondeo.Importe(salidas?.Descontado ?? 0m),
            CalculoFianza.Saldo(cobrado, salidas?.Devuelto ?? 0m, salidas?.Descontado ?? 0m));
    }

    /// <summary>
    /// Estado guardado de la fianza. Se lee de la base porque la columna guarda la decisión y no el
    /// estado que ve el usuario: son cosas distintas y una prueba que las confunde no comprueba nada.
    /// </summary>
    public static async Task<EstadoFianza> EstadoGuardadoAsync(
        ContenedorBdFixture bd,
        Guid fianzaId,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        return await contexto.Fianza.AsNoTracking().Where(f => f.Id == fianzaId).Select(f => f.Estado).SingleAsync(ct);
    }

    /// <summary>
    /// Fija el estado del contrato a finalizado por debajo de la API. Es lo que hace falta para el
    /// aviso de §3.16: recorrer la liquidación entera de F2-09 probaría el recorrido de contratos, no
    /// el de fianzas.
    /// </summary>
    public static async Task TerminarContratoAsync(ContenedorBdFixture bd, Guid contratoId, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaMigrador);

        await contexto.Contrato
            .Where(c => c.Id == contratoId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.Estado, EstadoContrato.Finalizado),
                ct);
    }
}