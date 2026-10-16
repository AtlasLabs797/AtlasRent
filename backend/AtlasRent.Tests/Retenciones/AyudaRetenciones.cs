using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Contratos;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Tests.Retenciones;

/// <summary>
/// Apoyo común de las pruebas de retención (F3-04, docs/ESPECIFICACION.md §3.9, D14). Todo se crea
/// por la API real, que es donde viven los permisos, la validación y el contador de series.
/// <para>
/// <b>Ningún porcentaje está escrito aquí ni en las pruebas</b>: cada una inventa el suyo y lo pasa
/// a la configuración, que es exactamente lo que D14 exige. Un 5 o un 10 fijo en el código sería
/// inventar un tipo de retención que nadie ha autorizado, igual que un rango de comprobante.
/// </para>
/// <para>
/// Cada prueba trae su cliente, su serie y sus tipos: la base es única y la comparten todas las
/// pruebas de la colección, así que nada puede afirmar sobre valores absolutos.
/// </para>
/// </summary>
internal static class AyudaRetenciones
{
    /// <summary>
    /// Tipo de retención de la prueba, con la base que le pase el test. Es el dato que decide de
    /// dónde sale el importe, así que la base va explícita en el nombre de cada prueba que lo usa.
    /// </summary>
    public static async Task<Guid> CrearTipoAsync(
        SesionCliente sesion,
        BaseRetencion baseCalculo,
        decimal porcentaje,
        CancellationToken ct)
    {
        // El nombre lleva un guid porque la base es compartida entre pruebas y el nombre es único.
        // No lleva el porcentaje, que además podría leerse como si el sistema lo tuviera escrito.
        var creado = await sesion.PostAsync(
            "/api/v1/config/tipos-retencion",
            new
            {
                nombre = $"Retención {baseCalculo} {Guid.NewGuid():N}",
                porcentaje,
                @base = baseCalculo.ToString(),
                esDefecto = false,
                activo = true,
            },
            ct);

        creado.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "el tipo de retención se crea por la configuración, que es de donde sale la cifra (D14)");

        return (await creado.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Factura en borrador de un cliente agente de retención, con una línea de renta y la línea de
    /// retención del tipo dado. Devuelve el identificador de la factura.
    /// </summary>
    /// <param name="contratoId">
    /// Nulo salvo en la prueba que además registra un cobro: sin contrato, un cobro con asignaciones
    /// explícitas no tiene a qué factura aplicarle el dinero.
    /// </param>
    public static async Task<Guid> BorradorConRetencionAsync(
        SesionCliente sesion,
        Guid tipoRetencionId,
        CancellationToken ct,
        decimal cantidad = 1m,
        decimal precioUnitario = 1_000m,
        Guid? tipoItbisId = null,
        Guid? contratoId = null)
    {
        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct, agenteRetencion: true);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var facturaId = await AyudaFacturas.CrearFacturaAsync(
            sesion, clienteId, serieId, Moneda.USD, hoy.AddDays(30), ct, contratoId);

        await AyudaFacturas.AgregarLineaAsync(
            sesion, facturaId, TipoLineaFactura.Renta, cantidad, precioUnitario, tipoItbisId, ct);

        await AgregarLineaRetencionAsync(sesion, facturaId, tipoRetencionId, ct);

        return facturaId;
    }

    /// <summary>
    /// Cliente agente de retención con un contrato, para la prueba que registra un cobro. Devuelve
    /// ambos identificadores.
    /// </summary>
    public static async Task<(Guid ClienteId, Guid ContratoId)> ClienteConContratoAsync(
        SesionCliente sesion,
        ContenedorBdFixture bd,
        CancellationToken ct)
    {
        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct, agenteRetencion: true);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);

        return (clienteId, await AyudaContrato.CrearAsync(
            sesion, AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, moneda: "USD"), ct));
    }

    /// <summary>Añade la línea de retención, que es la que lleva el tipo configurado.</summary>
    public static async Task<HttpResponseMessage> AgregarLineaRetencionAsync(
        SesionCliente sesion,
        Guid facturaId,
        Guid tipoRetencionId,
        CancellationToken ct) =>
        await sesion.PostAsync(
            $"/api/v1/facturas/{facturaId:D}/lineas",
            new
            {
                tipo = nameof(TipoLineaFactura.Retencion),
                concepto = $"Retención {Guid.NewGuid():N}"[..20],
                tipoRetencionId,
            },
            ct);

    /// <summary>
    /// Emite la factura y devuelve la respuesta entera. Se usa cuando lo que interesa es leer los
    /// totales y el indicador que salen de la propia emisión.
    /// </summary>
    public static async Task<JsonElement> EmitirAsync(
        SesionCliente sesion,
        Guid facturaId,
        CancellationToken ct)
    {
        var emitida = await sesion.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);
        emitida.StatusCode.Should().Be(HttpStatusCode.OK, "la factura está en borrador y se emite");

        return await emitida.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    // ---------------------------------------------------------------- lecturas

    public static Task<ComprobanteRetencion?> ComprobanteAsync(
        ContenedorBdFixture bd,
        Guid facturaId,
        CancellationToken ct) =>
        EnContextoAsync(bd, c => c.ComprobanteRetencion.AsNoTracking()
            .SingleOrDefaultAsync(x => x.FacturaId == facturaId, ct));

    /// <summary>Cuántos comprobantes hay para esa factura: más de uno es un duplicado.</summary>
    public static Task<int> ComprobantesAsync(
        ContenedorBdFixture bd,
        Guid facturaId,
        CancellationToken ct) =>
        EnContextoAsync(bd, c => c.ComprobanteRetencion.AsNoTracking()
            .CountAsync(x => x.FacturaId == facturaId, ct));

    /// <summary>Retención y neto a cobrar tal y como quedaron guardados en la cabecera.</summary>
    public static async Task<(decimal Retencion, decimal NetoACobrar)> TotalesAsync(
        ContenedorBdFixture bd,
        Guid facturaId,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        var fila = await contexto.Factura.AsNoTracking()
            .Where(f => f.Id == facturaId)
            .Select(f => new { f.Retencion, f.NetoACobrar })
            .SingleAsync(ct);

        return (fila.Retencion, fila.NetoACobrar);
    }

    /// <summary>Alerta viva de la clave de una factura retenida.</summary>
    public static Task<Alerta?> AlertaAsync(
        ContenedorBdFixture bd,
        Guid facturaId,
        CancellationToken ct,
        bool otra = false) =>
        EnContextoAsync(bd, c => otra
            ? c.Alerta.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Clave == ServicioRetenciones.ClaveAlerta(facturaId), ct)
            : c.Alerta.AsNoTracking()
                .FirstOrDefaultAsync(
                    a => a.Clave == ServicioRetenciones.ClaveAlerta(facturaId) && a.ResueltaAt == null, ct));

    private static async Task<T> EnContextoAsync<T>(
        ContenedorBdFixture bd,
        Func<AtlasRentDbContext, Task<T>> consulta)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        return await consulta(contexto);
    }
}