using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;

namespace AtlasRent.Tests.Informes;

/// <summary>
/// Apoyo común de las pruebas de informes y dashboard (F5-01, docs/ESPECIFICACION.md §3.15).
/// <para>
/// Se lee el JSON tal cual lo devuelve la API, como en el resto de las pruebas de F3: lo que importa es
/// lo que ve quien consume el endpoint, y un DTO bien hecho puede mentir.
/// </para>
/// </summary>
internal static class AyudaInformes
{
    /// <summary>
    /// El resumen con la consulta que le pase la prueba. La sesión decide el permiso: con el empleado
    /// devuelve 403 y el error dice por qué, que también es una cosa que hay que comprobar.
    /// </summary>
    public static async Task<JsonElement> ResumenAsync(
        SesionCliente sesion,
        string consulta,
        CancellationToken ct)
    {
        var leido = await sesion.GetAsync($"/api/v1/informes/resumen?{consulta}", ct);
        leido.StatusCode.Should().Be(HttpStatusCode.OK, await leido.Cuerpo());

        return await leido.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    public static async Task<JsonElement> DashboardAsync(
        SesionCliente sesion,
        string consulta,
        CancellationToken ct)
    {
        var leido = await sesion.GetAsync($"/api/v1/informes/dashboard?{consulta}", ct);
        leido.StatusCode.Should().Be(HttpStatusCode.OK, await leido.Cuerpo());

        return await leido.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    /// <summary>
    /// La exportación en el formato pedido, tal cual. <b>No comprueba el estado</b>: devolverse la
    /// respuesta es justo lo que necesitan las pruebas de permiso (403) y de tipo de contenido, que
    /// son las que deciden qué se espera de ella.
    /// </summary>
    public static Task<HttpResponseMessage> ExportarAsync(
        SesionCliente sesion,
        string formato,
        string consulta,
        CancellationToken ct) =>
        sesion.GetAsync($"/api/v1/informes/exportar?formato={formato}&{consulta}", ct);

    /// <summary>
    /// Un bloque de dinero del resumen. Los nombres son los de §3.15 escritos como los devuelve la API,
    /// en minúsculas como sale el resto de la API.
    /// </summary>
    public static JsonElement Bloque(this JsonElement resumen, string nombre) =>
        resumen.GetProperty(nombre);

    /// <summary>
    /// El consolidado en dólares de un bloque, que es lo que se compara entre el informe y lo que
    /// espera quien lee el gráfico.
    /// </summary>
    public static decimal Consolidado(this JsonElement bloque) =>
        bloque.GetProperty("consolidadoUsd").GetDecimal();

    public static decimal Usd(this JsonElement bloque) => bloque.GetProperty("usd").GetDecimal();

    public static decimal Dop(this JsonElement bloque) => bloque.GetProperty("dop").GetDecimal();

    /// <summary>Un número de la serie mensual, para el gráfico.</summary>
    public static decimal SerieDe(JsonElement resumen, string mes, string columna) =>
        resumen.GetProperty("serieMensual").EnumerateArray()
            .Single(s => s.GetProperty("mes").GetString() == mes)
            .GetProperty(columna)
            .GetDecimal();

    /// <summary>Un día de negocio de la respuesta, en su forma de la API.</summary>
    public static DateOnly Fecha(JsonElement elemento) =>
        DateOnly.Parse(elemento.GetString()!, CultureInfo.InvariantCulture);
}