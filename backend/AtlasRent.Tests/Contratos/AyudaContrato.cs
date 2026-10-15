using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Tests.Contratos;

/// <summary>
/// Atajos de las pruebas de contratos (F2-05): los datos que necesita un contrato (cliente,
/// unidad y subempresa que factura), el cuerpo del alta y el recorrido de las sesiones.
/// <para>
/// Cliente y unidad se crean con la ayuda de reservas: es la misma cartera y la misma unidad, y
/// duplicar el alta aquí haría que las dos pruebas dieran datos distintos.
/// </para>
/// <para>
/// Cada prueba crea su unidad y su subempresa, así que no se pisan entre sí aunque compartan la
/// base de datos de la colección.
/// </para>
/// </summary>
internal static class AyudaContrato
{
    /// <summary>
    /// Un día de negocio a <paramref name="desplazamiento"/> días de hoy. Cada prueba usa su propio
    /// desplazamiento, que es lo que evita que dos compartan unidad sin querer.
    /// </summary>
    public static DateOnly Dia(int desplazamiento = 0) => TiempoNegocio.Hoy().AddDays(desplazamiento);

    /// <summary>
    /// Subempresa que factura. Se escribe directamente en la base en vez de por la API por una razón
    /// concreta: crearla por API exige <c>configuracion.editar</c>, que en la matriz de §2 es solo del
    /// administrador, así que una prueba con gerente y empleado no podría tener los datos que su
    /// contrato necesita. Es el mismo truco que <see cref="AyudaAuth.CrearUsuarioAsync"/>.
    /// </summary>
    public static async Task<Guid> SubempresaAsync(ContenedorBdFixture bd, CancellationToken ct)
    {
        var subempresa = new Subempresa
        {
            Id = Guid.NewGuid(),
            Nombre = $"Empresa {Guid.NewGuid():N}"[..20],
            Propietaria = false,
            Facturadora = true,
            Activa = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        contexto.Subempresa.Add(subempresa);
        await contexto.SaveChangesAsync(ct);

        return subempresa.Id;
    }

    /// <summary>
    /// Cuerpo de alta de un contrato con todos los obligatorios de §3.5 puestos. Los parámetros que
    /// se pueden dejar a null son los que las pruebas de obligatorios usan para quitarlos.
    /// </summary>
    public static object Peticion(
        Guid? clienteId,
        Guid? unidadId,
        Guid facturadoraId,
        DateOnly? inicio = null,
        bool omitirInicio = false,
        int? duracionMeses = 12,
        decimal? renta = 50_000m,
        string? moneda = "DOP",
        int? diaPago = 5,
        decimal? fianza = 20_000m,
        string regla = "Anual",
        int? renovacionMeses = 12,
        string? plantilla = "estandar",
        string? observaciones = null) =>
        new
        {
            clienteId,
            unidadId,
            facturadoraId,
            fechaInicio = omitirInicio ? null : (inicio ?? Dia(30)).ToString("yyyy-MM-dd"),
            duracionInicialMeses = duracionMeses,
            renta,
            moneda,
            diaPago,
            fianza,
            reglaRenovacion = regla,
            renovacionMeses,
            plantillaContratoId = plantilla,
            observaciones,
        };

    /// <summary>Alta del contrato y su identificador. Falla el test si la API no lo acepta.</summary>
    public static async Task<Guid> CrearAsync(SesionCliente sesion, object cuerpo, CancellationToken ct)
    {
        var respuesta = await sesion.PostAsync("/api/v1/contratos", cuerpo, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Created, await respuesta.Cuerpo());

        return await AyudaReserva.Id(respuesta, ct);
    }

    /// <summary>
    /// Botón de transición sin cuerpo. Cada botón es su propio endpoint porque su permiso y sus
    /// reglas son distintos, así que las pruebas también los llaman por separado.
    /// </summary>
    public static Task<HttpResponseMessage> Boton(
        SesionCliente sesion,
        Guid contratoId,
        string paso,
        object? cuerpo = null,
        CancellationToken ct = default) =>
        sesion.PostAsync($"/api/v1/contratos/{contratoId:D}/{paso}", cuerpo ?? new { }, ct);

    /// <summary>
    /// Botón de transición que tiene que salir bien, con su cuerpo en el motivo del fallo: un 409 sin
    /// contexto no dice si fue una regla de negocio o un candado.
    /// </summary>
    public static async Task AceptarAsync(
        SesionCliente sesion,
        Guid contratoId,
        string paso,
        object? cuerpo,
        CancellationToken ct)
    {
        var respuesta = await Boton(sesion, contratoId, paso, cuerpo, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());
    }

    /// <summary>
    /// Recorrido completo hasta <c>Activo</c>: el empleado lo crea y lo manda, y el gerente lo
    /// aprueba y lo activa. Devuelve el identificador del contrato ya vigente.
    /// </summary>
    public static async Task<Guid> HastaActivoAsync(
        SesionCliente empleado,
        SesionCliente gerente,
        ContenedorBdFixture bd,
        CancellationToken ct)
    {
        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await SubempresaAsync(bd, ct);

        var id = await CrearAsync(
            empleado, Peticion(clienteId, unidadId, facturadoraId), ct);

        await AceptarAsync(empleado, id, "solicitud", new { comentario = "Para aprobación." }, ct);
        await AceptarAsync(gerente, id, "aprobacion", null, ct);
        await AceptarAsync(gerente, id, "activacion", null, ct);

        return id;
    }

    /// <summary>Cuerpo del cambio de renta u otro de un contrato vigente.</summary>
    public static object Cambio(decimal renta, string motivo = "Ajuste pactado") =>
        new
        {
            renta,
            moneda = "DOP",
            diaPago = 5,
            fianza = 20_000m,
            reglaRenovacion = "Anual",
            renovacionMeses = 12,
            motivo,
        };

    /// <summary>Estado del contrato leído de la base, que es donde se comprueba.</summary>
    public static async Task<EstadoContrato> EstadoAsync(
        ContenedorBdFixture bd,
        Guid contratoId,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        return await contexto.Contrato.AsNoTracking()
            .Where(c => c.Id == contratoId)
            .Select(c => c.Estado)
            .SingleAsync(ct);
    }

    /// <summary>La ficha del contrato tal y como la devuelve la API, para lo que se quiera mirar.</summary>
    public static async Task<JsonElement> FichaAsync(
        SesionCliente sesion,
        Guid contratoId,
        CancellationToken ct)
    {
        var respuesta = await sesion.GetAsync($"/api/v1/contratos/{contratoId:D}", ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());

        return await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
    }
}