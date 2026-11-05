using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Tests.Reservas;

/// <summary>
/// Atajos de las pruebas de reservas: los datos que necesita una reserva (cliente y unidad) y el
/// alta por la API.
/// <para>
/// Cada prueba crea su unidad y sus días, así que no se pisan entre sí aunque compartan la base de
/// datos de la colección.
/// </para>
/// </summary>
internal static class AyudaReserva
{
    /// <summary>
    /// Un día de negocio a <paramref name="desplazamiento"/> días de hoy. Se usa una fecha distinta
    /// en cada prueba, que es lo que evita que dos pruebas compartan unidad sin querer.
    /// </summary>
    public static DateOnly Dia(int desplazamiento = 0) => TiempoNegocio.Hoy().AddDays(desplazamiento);

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

    /// <summary>
    /// Unidad con las tres modalidades, que es lo que necesita una prueba de reservas: mensual,
    /// medio día y día completo (D11).
    /// </summary>
    public static async Task<Guid> UnidadAsync(SesionCliente sesion, CancellationToken ct) =>
        (await InmuebleYUnidadAsync(sesion, ct)).UnidadId;

    /// <summary>
    /// El inmueble y su unidad, con la pertenencia que le pida la prueba: quién es la propietaria del
    /// edificio, cuál es la de la unidad y qué empresa factura por defecto en ella.
    /// <para>
    /// Los tres van a null en el caso normal, que es el de las reservas. Existen para F5-01, que
    /// prueba los filtros por propietaria y por facturadora de la vista única: sin una unidad que
    /// tenga una u otra no hay dos filas distintas que el filtro pueda separar.
    /// </para>
    /// <para>
    /// Ojo con <paramref name="propietariaInmuebleId"/> y <paramref name="propietariaUnidadId"/>:
    /// al dar de alta una unidad, la del inmueble se le copia a la unidad (§3.1). Para dejar la
    /// unidad sin propietaria propia —que es el estado del que habla la herencia— hay que crear el
    /// inmueble sin ella y ponérsela después.
    /// </para>
    /// </summary>
    public static async Task<(Guid InmuebleId, Guid UnidadId)> InmuebleYUnidadAsync(
        SesionCliente sesion,
        CancellationToken ct,
        Guid? propietariaInmuebleId = null,
        Guid? propietariaUnidadId = null,
        Guid? facturadoraId = null)
    {
        var cuerpo = new Dictionary<string, object?>
        {
            ["nombre"] = $"Edificio {Guid.NewGuid():N}"[..40],
            ["tipo"] = "Edificio",
        };

        if (propietariaInmuebleId is { } duena)
        {
            cuerpo["propietariaId"] = duena;
        }

        var inmueble = await sesion.PostAsync("/api/v1/inmuebles", cuerpo, ct);
        inmueble.StatusCode.Should().Be(HttpStatusCode.OK, await inmueble.Cuerpo());

        var inmuebleId = await Id(inmueble, ct);

        var cuerpoUnidad = new Dictionary<string, object?>
        {
            ["nombre"] = $"Unidad {Guid.NewGuid():N}"[..20],
            ["tipo"] = "Oficina",
            ["modalidades"] = new[] { "Mensual", "MedioDia", "DiaCompleto" },
        };

        if (propietariaUnidadId is { } duenaUnidad)
        {
            cuerpoUnidad["propietariaId"] = duenaUnidad;
        }

        if (facturadoraId is { } facturadora)
        {
            cuerpoUnidad["facturadoraPorDefectoId"] = facturadora;
        }

        var unidad = await sesion.PostAsync(
            $"/api/v1/inmuebles/{inmuebleId:D}/unidades",
            cuerpoUnidad,
            ct);
        unidad.StatusCode.Should().Be(HttpStatusCode.OK, await unidad.Cuerpo());

        return (inmuebleId, await Id(unidad, ct));
    }

    /// <summary>Un día de reserva corta con su hora orientativa (texto libre), tal y como lo manda el cliente.</summary>
    public static object Franja(DateOnly fecha, string desde = "08:00:00", string hasta = "12:00:00") =>
        new { fecha = fecha.ToString("yyyy-MM-dd"), horaOrientativa = $"{desde[..5]} a {hasta[..5]}" };

    /// <summary>Cuerpo de una reserva corta de medios días.</summary>
    public static object Corta(
        Guid? clienteId,
        Guid unidadId,
        DateOnly fecha,
        object? dias = null,
        string modalidad = "MedioDia",
        DateOnly? entrada = null,
        DateOnly? salida = null,
        decimal importe = 1000m,
        decimal? anticipo = null,
        string? anticipoNota = null,
        Guid? prospectoId = null) =>
        new
        {
            unidadId,
            clienteId,
            prospectoId,
            tipo = "Corta",
            entradaPrevista = (entrada ?? fecha).ToString("yyyy-MM-dd"),
            salidaPrevista = salida?.ToString("yyyy-MM-dd"),
            modalidad,
            moneda = "DOP",
            importe,
            anticipoImporte = anticipo,
            anticipoNota,
            dias = dias ?? new[] { Franja(fecha) },
        };

    /// <summary>Cuerpo de una reserva mensual, que bloquea la unidad desde la entrada prevista.</summary>
    public static object Mensual(
        Guid? clienteId,
        Guid unidadId,
        DateOnly entrada,
        decimal importe = 5000m,
        decimal? anticipo = null,
        string? anticipoNota = null,
        Guid? prospectoId = null) =>
        new
        {
            unidadId,
            clienteId,
            prospectoId,
            tipo = "Mensual",
            entradaPrevista = entrada.ToString("yyyy-MM-dd"),
            modalidad = "Mensual",
            moneda = "DOP",
            importe,
            anticipoImporte = anticipo,
            anticipoNota,
        };

    /// <summary>Alta de una reserva y su identificador. Falla el test si la API no la acepta.</summary>
    public static async Task<Guid> CrearAsync(SesionCliente sesion, object cuerpo, CancellationToken ct)
    {
        var respuesta = await sesion.PostAsync("/api/v1/reservas", cuerpo, ct);
        // El cuerpo va en el motivo del fallo: sin él, un 409 no dice si es una regla de negocio o
        // un bloqueo de la unidad.
        respuesta.StatusCode.Should().Be(HttpStatusCode.Created, await respuesta.Cuerpo());

        return await Id(respuesta, ct);
    }

    public static Task<HttpResponseMessage> CrearRawAsync(SesionCliente sesion, object cuerpo, CancellationToken ct) =>
        sesion.PostAsync("/api/v1/reservas", cuerpo, ct);

    /// <summary>El cuerpo de la respuesta, para volcarlo cuando algo falla.</summary>
    public static async Task<string> Cuerpo(this HttpResponseMessage respuesta) =>
        await respuesta.Content.ReadAsStringAsync();

    public static async Task<Guid> Id(HttpResponseMessage respuesta, CancellationToken ct) =>
        (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();

    public static async Task<JsonElement> Json(HttpResponseMessage respuesta, CancellationToken ct) =>
        await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);

    /// <summary>
    /// Los bloqueos vivos de una unidad en un día, leídos de <c>ocupacion_dia</c> directamente.
    /// Es la tabla que hay que mirar: dice si la unidad está tomada y por qué, y no depende de que
    /// la aplicación la haya escrito bien.
    /// </summary>
    public static async Task<List<(OrigenOcupacion Origen, TipoOcupacionDia Tipo, bool Bloquea)>>
        BloqueosAsync(ContenedorBdFixture bd, Guid unidadId, DateOnly fecha, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var filas = await contexto.OcupacionDia.AsNoTracking()
            .Where(d => d.UnidadId == unidadId && d.Fecha == fecha)
            .OrderBy(d => d.Tipo)
            .ToListAsync(ct);

        return [.. filas.Select(d => (d.Origen, d.Tipo, d.Bloquea))];
    }

    /// <summary>
    /// Cuántos bloqueos vivos tiene esa unidad, contando las dos tablas: una mensual ocupa un rango
    /// y una corta ocupa días, así que mirar solo una de las dos daría una respuesta falsa.
    /// </summary>
    public static async Task<int> BloqueosVivosAsync(
        ContenedorBdFixture bd,
        Guid unidadId,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        var dias = await contexto.OcupacionDia.CountAsync(d => d.UnidadId == unidadId && d.Bloquea, ct);
        var rangos = await contexto.OcupacionRango.CountAsync(r => r.UnidadId == unidadId && r.Bloquea, ct);

        return dias + rangos;
    }
}