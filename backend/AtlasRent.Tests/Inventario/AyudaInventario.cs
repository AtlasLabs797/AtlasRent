using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Contratos;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Plantillas;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Inventario;

/// <summary>
/// Atajos de las pruebas de inventario (F2-08): el alta del inventario base por la API, el recorrido
/// de un contrato hasta activo con su inventario ya congelado, y la firma de la revisión de salida.
/// <para>
/// Cada prueba crea su unidad y su contrato, así que no se pisan entre sí aunque compartan la base de
/// datos de la colección.
/// </para>
/// </summary>
internal static class AyudaInventario
{
    public const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>Sesión de un rol, lista para llamar por HTTP.</summary>
    public static Task<SesionCliente> SesionAsync(
        ApiTestFactoryBloqueos api,
        ContenedorBdFixture bd,
        string rol,
        CancellationToken ct) =>
        AyudaPlantilla.SesionAsync(api, bd, rol, ct);

    /// <summary>Cuerpo de alta o de edición de un elemento del inventario base.</summary>
    public static object Elemento(string? nombre = "Colchón", decimal? cantidad = 1, string? notas = null) =>
        new { nombre, cantidad, notas };

    /// <summary>
    /// Escribe los elementos del inventario base de la unidad, en orden, y falla el test si alguno no
    /// entra. Devuelve los identificadores por el mismo orden.
    /// </summary>
    public static async Task<List<Guid>> BaseAsync(
        SesionCliente sesion,
        Guid unidadId,
        (string Nombre, decimal Cantidad, string? Notas)[] elementos,
        CancellationToken ct)
    {
        var ids = new List<Guid>();
        foreach (var elemento in elementos)
        {
            var alta = await sesion.PostAsync(
                $"/api/v1/unidades/{unidadId:D}/inventario-base",
                Elemento(elemento.Nombre, elemento.Cantidad, elemento.Notas),
                ct);
            alta.StatusCode.Should().Be(HttpStatusCode.Created, await alta.Cuerpo());
            ids.Add(await AyudaReserva.Id(alta, ct));
        }

        return ids;
    }

    /// <summary>
    /// Unidad con inventario base y contrato ya activo, que es el punto del que parten casi todas las
    /// pruebas: en ese momento el inventario del contrato ya está congelado.
    /// <para>
    /// Se escribe el inventario base <b>antes</b> de activar, que es el orden real: la copia sale de
    /// lo que había en la unidad el día de la entrada.
    /// </para>
    /// </summary>
    public static async Task<(Guid UnidadId, Guid ContratoId)> ContratoActivoConInventarioAsync(
        SesionCliente empleado,
        SesionCliente gerente,
        ContenedorBdFixture bd,
        (string Nombre, decimal Cantidad, string? Notas)[] elementos,
        int desplazamientoInicio,
        CancellationToken ct)
    {
        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);

        await BaseAsync(empleado, unidadId, elementos, ct);

        var contratoId = await AyudaContrato.CrearAsync(
            empleado,
            AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, AyudaContrato.Dia(desplazamientoInicio)),
            ct);
        await AyudaContrato.AceptarAsync(empleado, contratoId, "solicitud", new { }, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "aprobacion", null, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "activacion", null, ct);

        return (unidadId, contratoId);
    }

    /// <summary>
    /// El inventario congelado del contrato, leído por la API. Es la foto: lo que había cuando entró
    /// el cliente.
    /// </summary>
    public static async Task<JsonElement> InventarioContratoAsync(
        SesionCliente sesion,
        Guid contratoId,
        CancellationToken ct)
    {
        var respuesta = await sesion.GetAsync($"/api/v1/contratos/{contratoId:D}/inventario", ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());

        return await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    /// <summary>
    /// Una línea de la revisión por cada elemento del inventario congelado, emparejadas por posición
    /// y con el estado que se le pida. La posición es la del inventario congelado, que el servicio
    /// entrega en el orden que lo escribió el empleado.
    /// <para>
    /// Empareja por <see cref="Enumerable.Zip{T1,T2,TResult}(IEnumerable{T1},IEnumerable{T2},Func{T1,T2,TResult})"/>
    /// y a propósito no exige que las dos listas midan lo mismo: las pruebas de la revisión
    /// incompleta necesitan justamente poder mandar menos líneas de las que hay.
    /// </para>
    /// </summary>
    public static object[] LineasDe(JsonElement congelado, params (string Estado, string? Nota)[] estados) =>
        [.. congelado.EnumerateArray()
            .Zip(estados, (elemento, estado) => new
            {
                contratoInventarioItemId = elemento.GetProperty("id").GetGuid(),
                estado = estado.Estado,
                nota = estado.Nota,
            })];

    public static Task<HttpResponseMessage> FirmarRawAsync(
        SesionCliente sesion,
        Guid contratoId,
        object cuerpo,
        CancellationToken ct) =>
        sesion.PostAsync($"/api/v1/contratos/{contratoId:D}/revision-inventario", cuerpo, ct);

    /// <summary>
    /// Cuerpo de la firma: las líneas de todos los elementos del inventario congelado, el importe del
    /// faltante que F2-09 liquidará y la observación.
    /// </summary>
    public static object CuerpoDeFirma(
        JsonElement congelado,
        (string Estado, string? Nota)[] estados,
        decimal? importePropuesto = null) =>
        new
        {
            lineas = LineasDe(congelado, estados),
            importePropuesto,
            observacion = "Salida con revisión de inventario.",
        };

    /// <summary>Firma la revisión y falla el test si no sale 201.</summary>
    public static async Task<JsonElement> FirmarAsync(
        SesionCliente sesion,
        Guid contratoId,
        JsonElement congelado,
        (string Estado, string? Nota)[] estados,
        decimal? importePropuesto = null,
        CancellationToken ct = default)
    {
        var respuesta = await FirmarRawAsync(
            sesion, contratoId, CuerpoDeFirma(congelado, estados, importePropuesto), ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Created, await respuesta.Cuerpo());

        return await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    /// <summary>Los estados de la revisión firmada, tal y como los devuelve la API.</summary>
    public static List<string> Estados(JsonElement revision) =>
        [.. revision.GetProperty("lineas").EnumerateArray().Select(l => l.GetProperty("estado").GetString()!)];
}