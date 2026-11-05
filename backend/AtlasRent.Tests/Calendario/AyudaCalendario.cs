using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;

namespace AtlasRent.Tests.Calendario;

/// <summary>
/// Atajos de las pruebas del calendario (F2-04): las fechas, la llamada y la lectura del JSON.
/// <para>
/// Las pruebas del calendario no comprueban un modelo tipado sino lo que de verdad ve la pantalla,
/// que es el JSON: por eso se leen las propiedades a mano. El día se busca por fecha y no por
/// posición, para que un fallo diga "el día 12 sale mal" y no "la casilla 3".
/// </para>
/// </summary>
internal static class AyudaCalendario
{
    /// <summary>
    /// Un día de negocio a <paramref name="desplazamiento"/> días de hoy. Cada prueba usa su propio
    /// desplazamiento, que es lo que evita que dos compartan unidad sin querer.
    /// </summary>
    public static DateOnly Dia(int desplazamiento) => TiempoNegocio.Hoy().AddDays(desplazamiento);

    /// <summary>
    /// Poner la unidad en un estado manual por API. El calendario tiene que reflejarlo sin que
    /// exista ninguna ocupación con fecha: es el estado de la unidad, no un bloqueo.
    /// </summary>
    public static async Task PonerEstadoAsync(
        SesionCliente sesion,
        Guid unidadId,
        EstadoUnidad estado,
        CancellationToken ct)
    {
        var respuesta = await sesion.PutAsync(
            $"/api/v1/inmuebles/unidades/{unidadId:D}",
            new
            {
                nombre = $"Unidad {Guid.NewGuid():N}"[..20],
                tipo = "Oficina",
                estadoManual = estado.ToString(),
                modalidades = new[] { "Mensual", "MedioDia", "DiaCompleto" },
            },
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());
    }

    /// <summary>El calendario de una unidad, con el rango ya en la URL.</summary>
    public static async Task<JsonElement> DeUnidadAsync(
        SesionCliente sesion,
        Guid unidadId,
        DateOnly desde,
        DateOnly hasta,
        CancellationToken ct)
    {
        var respuesta = await sesion.GetAsync(
            $"/api/v1/calendario?unidadId={unidadId:D}&desde={desde:yyyy-MM-dd}&hasta={hasta:yyyy-MM-dd}",
            ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());

        return await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    /// <summary>La primera unidad del calendario, que es la única cuando se pide una.</summary>
    public static JsonElement PrimeraUnidad(JsonElement calendario)
    {
        var unidades = calendario.GetProperty("unidades").EnumerateArray().ToList();
        unidades.Should().ContainSingle("se pidió una unidad, así que viene una");

        return unidades[0];
    }

    /// <summary>Un día concreto del calendario, buscado por su fecha.</summary>
    public static JsonElement Dia(JsonElement unidad, DateOnly fecha)
    {
        var dias = unidad.GetProperty("dias").EnumerateArray()
            .Where(d => d.GetProperty("fecha").GetString() == $"{fecha:yyyy-MM-dd}")
            .ToList();

        dias.Should().ContainSingle($"el rango tiene que traer el día {fecha:yyyy-MM-dd} una vez");

        return dias[0];
    }

    /// <summary>El motivo winner del día, ya desempatado.</summary>
    public static string Tipo(JsonElement dia) => dia.GetProperty("tipo").GetString()!;

    /// <summary>Los bloqueos del día, en el orden estable que devuelve la API.</summary>
    public static List<(string Origen, string OrigenId, string? TipoDia)> Ocupaciones(JsonElement dia) =>
        [.. dia.GetProperty("ocupaciones").EnumerateArray().Select(o => (
            o.GetProperty("origen").GetString()!,
            o.GetProperty("origenId").GetString()!,
            o.GetProperty("tipoDia").ValueKind == JsonValueKind.Null
                ? null
                : o.GetProperty("tipoDia").GetString()))];

    /// <summary>La respuesta cruda, para las pruebas de errores y de permisos.</summary>
    public static Task<HttpResponseMessage> GetAsync(
        SesionCliente sesion,
        Guid unidadId,
        DateOnly desde,
        DateOnly hasta,
        CancellationToken ct) =>
        sesion.GetAsync(
            $"/api/v1/calendario?unidadId={unidadId:D}&desde={desde:yyyy-MM-dd}&hasta={hasta:yyyy-MM-dd}",
            ct);
}