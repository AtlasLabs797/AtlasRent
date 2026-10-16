using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Contratos;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Tests.Periodicas;

/// <summary>
/// Atajos de las pruebas de facturación periódica (F3-05). Todo se crea por la API real, que es
/// donde viven los permisos, los estados del contrato y la validación de fechas.
/// <para>
/// El contrato se lleva hasta <c>Activo</c> porque §3.8 factura «contratos activos»: uno en
/// borrador o aprobado no factura aunque su vigencia toque el mes.
/// </para>
/// <para>
/// Cada prueba trae su cliente, su unidad, su subempresa y su serie, así que no se pisan entre sí
/// aunque compartan la base de datos de la colección.
/// </para>
/// <para>
/// <b>Las pruebas de esta carpeta usan <c>ApiTestFactoryBloqueos</c>.</b> Levantar un contrato
/// vigente necesita tres sesiones —empleado, gerente y quien factura—, y con el límite de tres
/// logins por minuto de la fábrica normal una batería de cuatro meses se comía el límite y moría
/// con un 429 que no dice nada del prorrateo.
/// </para>
/// </summary>
internal static class AyudaPeriodicas
{
    /// <summary>Renta mensual con la que trabajan las pruebas de prorrateo.</summary>
    public const decimal RentaDePrueba = 1_000m;

    /// <summary>
    /// Contrato vigente con las fechas que pida la prueba. El empleado lo crea y lo manda, y el
    /// gerente lo aprueba y lo activa, que es el recorrido real de §3.5.
    /// </summary>
    public static async Task<(Guid ContratoId, Guid ClienteId)> ContratoActivoAsync(
        ContenedorBdFixture bd,
        ApiTestFactory api,
        CancellationToken ct,
        DateOnly inicio,
        int duracionMeses = 36,
        decimal renta = RentaDePrueba,
        Moneda moneda = Moneda.USD,
        int diaPago = 5,
        bool agenteRetencion = false)
    {
        using var empleado = await AyudaFacturas.SesionAsync(bd, api, Roles.Empleado, ct);
        using var gerente = await AyudaFacturas.SesionAsync(bd, api, Roles.Gerente, ct);

        var clienteId = await AyudaFacturas.CrearClienteAsync(empleado, ct, agenteRetencion);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);

        var contratoId = await AyudaContrato.CrearAsync(
            empleado,
            AyudaContrato.Peticion(
                clienteId,
                unidadId,
                facturadoraId,
                inicio,
                duracionMeses: duracionMeses,
                renta: renta,
                moneda: moneda.ToString(),
                diaPago: diaPago),
            ct);

        await AyudaContrato.AceptarAsync(empleado, contratoId, "solicitud", new { comentario = "Para aprobación." }, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "aprobacion", null, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "activacion", null, ct);

        return (contratoId, clienteId);
    }

    /// <summary>
    /// Cuerpo de la simulación y de la ejecución: es el mismo a propósito, porque lo que se
    /// simula es lo que se ejecuta.
    /// </summary>
    public static object Peticion(DateOnly periodo, Guid serieId, IEnumerable<Guid>? contratos = null) =>
        new
        {
            periodo = periodo.ToString("yyyy-MM-dd"),
            serieId,
            contratosIds = contratos?.ToArray(),
        };

    public static Task<HttpResponseMessage> SimularRawAsync(SesionCliente sesion, object cuerpo, CancellationToken ct) =>
        sesion.PostAsync("/api/v1/facturas/periodicas/simular", cuerpo, ct);

    public static Task<HttpResponseMessage> GenerarRawAsync(SesionCliente sesion, object cuerpo, CancellationToken ct) =>
        sesion.PostAsync("/api/v1/facturas/periodicas/generar", cuerpo, ct);

    /// <summary>Simulación que tiene que salir bien, con el cuerpo en el motivo del fallo.</summary>
    public static async Task<JsonElement> SimularAsync(SesionCliente sesion, object cuerpo, CancellationToken ct)
    {
        var respuesta = await SimularRawAsync(sesion, cuerpo, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));

        return await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    /// <summary>Ejecución que tiene que salir bien, con el cuerpo en el motivo del fallo.</summary>
    public static async Task<JsonElement> GenerarAsync(SesionCliente sesion, object cuerpo, CancellationToken ct)
    {
        var respuesta = await GenerarRawAsync(sesion, cuerpo, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));

        return await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    /// <summary>El único elemento de <c>items</c> de una simulación de un solo contrato.</summary>
    public static JsonElement UnItem(JsonElement simulacion) =>
        simulacion.GetProperty("items").EnumerateArray().Single();

    /// <summary>
    /// Las facturas de un contrato en un periodo, leídas de la base. El criterio de aceptación se
    /// comprueba en las filas, no en lo que la aplicación dice que ha escrito.
    /// </summary>
    public static async Task<List<Factura>> FacturasDelPeriodoAsync(
        ContenedorBdFixture bd,
        Guid contratoId,
        DateOnly periodoInicio,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        return await contexto.Factura.AsNoTracking()
            .Where(f => f.ContratoId == contratoId && f.PeriodoInicio == periodoInicio)
            .ToListAsync(ct);
    }

    /// <summary>Definición real del índice de no duplicado, tal y como la dejó PostgreSQL.</summary>
    public static async Task<string> IndiceDePeriodoAsync(ContenedorBdFixture bd, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaSuperusuario);

        return await contexto.Database
            .SqlQuery<string>(
                $"""
                SELECT indexdef AS "Value"
                FROM pg_indexes
                WHERE indexname = 'ix_factura_periodo_unico'
                """)
            .SingleAsync(ct);
    }
}