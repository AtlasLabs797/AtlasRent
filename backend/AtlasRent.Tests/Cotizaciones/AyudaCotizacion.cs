using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;

namespace AtlasRent.Tests.Cotizaciones;

/// <summary>
/// Atajos de las pruebas de cotizaciones: los datos que necesita una cotización (cliente, prospecto
/// y unidad) y el alta del documento.
/// <para>
/// Cada prueba crea los suyos, así que no se pisan entre sí aunque compartan la base de datos de la
/// colección.
/// </para>
/// </summary>
internal static class AyudaCotizacion
{
    public static async Task<Guid> ClienteAsync(SesionCliente sesion, CancellationToken ct)
    {
        var alta = await sesion.PostAsync(
            "/api/v1/clientes",
            new { nombre = $"Cliente {Guid.NewGuid():N}"[..20], tipo = "PersonaFisica" },
            ct);
        alta.StatusCode.Should().Be(HttpStatusCode.OK);

        return await Id(alta, ct);
    }

    public static async Task<Guid> ProspectoAsync(SesionCliente sesion, CancellationToken ct)
    {
        var alta = await sesion.PostAsync(
            "/api/v1/prospectos",
            new { nombre = $"Prospecto {Guid.NewGuid():N}"[..20], tipo = "Empresa" },
            ct);
        alta.StatusCode.Should().Be(HttpStatusCode.OK);

        return await Id(alta, ct);
    }

    public static async Task<Guid> UnidadAsync(SesionCliente sesion, CancellationToken ct)
    {
        var inmueble = await sesion.PostAsync(
            "/api/v1/inmuebles",
            new { nombre = $"Edificio {Guid.NewGuid():N}"[..40], tipo = "Edificio" },
            ct);
        inmueble.StatusCode.Should().Be(HttpStatusCode.OK);

        var unidad = await sesion.PostAsync(
            $"/api/v1/inmuebles/{await Id(inmueble, ct):D}/unidades",
            new
            {
                nombre = $"Unidad {Guid.NewGuid():N}"[..20],
                tipo = "Apartamento",
                modalidades = new[] { "Mensual" },
            },
            ct);
        unidad.StatusCode.Should().Be(HttpStatusCode.OK);

        return await Id(unidad, ct);
    }

    /// <summary>Línea de la petición tal y como la manda el cliente.</summary>
    public static object Linea(string descripcion = "Alquiler mensual", decimal cantidad = 1, decimal precio = 1000m) =>
        new { descripcion, cantidad, precioUnitario = precio };

    /// <summary>Alta de una cotización y su identificador. Falla el test si la API no la acepta.</summary>
    public static async Task<Guid> CrearAsync(
        SesionCliente sesion,
        Guid? clienteId,
        Guid? prospectoId = null,
        Guid? unidadId = null,
        object? lineas = null,
        CancellationToken ct = default)
    {
        var respuesta = await sesion.PostAsync(
            "/api/v1/cotizaciones",
            Cuerpo(clienteId, prospectoId, unidadId, lineas),
            ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);

        return await Id(respuesta, ct);
    }

    /// <summary>Mismo cuerpo que <see cref="CrearAsync"/> pero sin fallar: devuelve la respuesta tal cual.</summary>
    public static Task<HttpResponseMessage> CrearRawAsync(
        SesionCliente sesion,
        Guid? clienteId,
        Guid? prospectoId = null,
        Guid? unidadId = null,
        object? lineas = null,
        CancellationToken ct = default) =>
        sesion.PostAsync(
            "/api/v1/cotizaciones",
            Cuerpo(clienteId, prospectoId, unidadId, lineas),
            ct);

    public static object Cuerpo(
        Guid? clienteId,
        Guid? prospectoId = null,
        Guid? unidadId = null,
        object? lineas = null,
        string moneda = "DOP") =>
        new
        {
            clienteId,
            prospectoId,
            unidadId,
            moneda,
            lineas = lineas ?? new[] { Linea() },
        };

    /// <summary>El único camino de cambio de estado (D18).</summary>
    public static Task<HttpResponseMessage> CambiarEstadoAsync(
        SesionCliente sesion,
        Guid id,
        string estado,
        CancellationToken ct) =>
        sesion.PostAsync($"/api/v1/cotizaciones/{id:D}/estado", new { estado }, ct);

    public static async Task<Guid> Id(HttpResponseMessage respuesta, CancellationToken ct) =>
        (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();

    public static async Task<JsonElement> Json(HttpResponseMessage respuesta, CancellationToken ct) =>
        await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
}