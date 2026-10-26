using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Clientes;

/// <summary>
/// F1-09, criterio de aceptación: números de cliente sin duplicados bajo concurrencia.
/// <para>
/// El número sale de la secuencia de PostgreSQL y no de un <c>MAX(numero) + 1</c> calculado en
/// código, que es justo lo que repetiría números cuando dos altas llegan a la vez.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ClienteNumeroTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";
    private const string Ruta = "/api/v1/clientes";

    /// <summary>Número con el que se simula la importación de Factusol.</summary>
    private const int NumeroImportado = 5_000;

    [Fact]
    public async Task VariasAltasSimultaneasNoRepitenNumero()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionDeAdminAsync(ct);

        const int altas = 12;
        var respuestas = await Task.WhenAll(
            Enumerable.Range(0, altas).Select(_ => sesion.PostAsync(Ruta, Cliente(), ct)));

        foreach (var respuesta in respuestas)
        {
            respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var numeros = await Task.WhenAll(respuestas.Select(async respuesta =>
            (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("numero").GetInt32()));

        numeros.Should().OnlyHaveUniqueItems("la secuencia no puede dar el mismo número dos veces");

        // Correlativos respecto al menor de este lote: otras pruebas de la suite ya consumieron
        // números antes, así que el punto de partida no es cero.
        var menor = numeros.Min();
        numeros.Order().Select(numero => numero - menor)
            .Should().BeEquivalentTo(Enumerable.Range(0, altas));
    }

    /// <summary>
    /// La importación de Factusol conserva números que ya existían, y el siguiente número que se
    /// asigne tiene que ir por encima de ellos.
    /// </summary>
    [Fact]
    public async Task ElNumeroImportadoNoSeRepiteEnLasAltasSiguientes()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionDeAdminAsync(ct);

        var marca = $"Importado{Guid.NewGuid():N}"[..20];

        var importado = await sesion.PostAsync(
            Ruta, Cliente(nombre: marca, numero: NumeroImportado), ct);
        importado.StatusCode.Should().Be(HttpStatusCode.OK);
        (await importado.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("numero").GetInt32()
            .Should().Be(NumeroImportado, "la importación conserva el número que ya existía");

        var nuevo = await sesion.PostAsync(Ruta, Cliente(), ct);
        nuevo.StatusCode.Should().Be(HttpStatusCode.OK);

        (await nuevo.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("numero").GetInt32()
            .Should().BeGreaterThan(NumeroImportado, "la secuencia queda por encima del número importado");
    }

    /// <summary>Dos clientes con el mismo número no pueden existir: el índice único salta.</summary>
    [Fact]
    public async Task DosClientesConElMismoNumeroSeRechazan()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionDeAdminAsync(ct);

        var primero = await sesion.PostAsync(Ruta, Cliente(numero: 4_321), ct);
        primero.StatusCode.Should().Be(HttpStatusCode.OK);

        var segundo = await sesion.PostAsync(Ruta, Cliente(numero: 4_321), ct);
        segundo.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// SEC-022: fijar el número es cosa de la importación (acción de administración), no del
    /// empleado: se rechaza con 403 en vez de ignorarlo en silencio, para que no crea que lo fijó.
    /// </summary>
    [Fact]
    public async Task UnEmpleadoNoPuedeFijarElNumeroDeCliente()
    {
        var ct = TestContext.Current.CancellationToken;
        var empleado = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, empleado, Contrasena, ct);

        var con = await sesion.PostAsync(Ruta, Cliente(numero: 6_001), ct);
        con.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var sin = await sesion.PostAsync(Ruta, Cliente(), ct);
        sin.StatusCode.Should().Be(HttpStatusCode.OK, "el empleado sigue pudiendo dar de alta sin número");
    }

    /// <summary>SEC-022: un número cerca de int.MaxValue haría desbordar el siguiente nextval.</summary>
    [Fact]
    public async Task ElNumeroTieneUnTopeQueEvitaDesbordarLaSecuencia()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionDeAdminAsync(ct);

        var respuesta = await sesion.PostAsync(Ruta, Cliente(numero: int.MaxValue - 1), ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------- apoyo

    private static object Cliente(string? nombre = null, int? numero = null) => new
    {
        nombre = nombre ?? $"Cliente {Guid.NewGuid():N}"[..20],
        tipo = "PersonaFisica",
        numero,
        email = "cliente@example.test",
    };

    private async Task<SesionCliente> SesionDeAdminAsync(CancellationToken ct)
    {
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        return await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);
    }
}