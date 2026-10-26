using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Jobs;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace AtlasRent.Tests.Seguridad;

/// <summary>
/// SEC-030: endurecimientos menores de entrada y de datos en reposo.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class EndurecimientoVariosTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>docs/SEGURIDAD.md A.5: un campo que el DTO no declara es un 400, tenga o no el atributo.</summary>
    [Theory]
    [InlineData("/api/v1/seguridad/bloqueos/ip/desbloquear", "{\"ip\":\"198.51.100.9\",\"extra\":1}")]
    [InlineData("/api/v1/seguridad/bloqueos/cuenta/desbloquear", "{\"nombreAcceso\":\"alguien\",\"extra\":1}")]
    public async Task UnCampoDesconocidoEnElCuerpoEsUn400(string ruta, string cuerpo)
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);

        var respuesta = await sesion.PostAsync(ruta, System.Text.Json.JsonDocument.Parse(cuerpo).RootElement, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// intento_login se purga pasados N dias (por defecto 90) por el trabajo diario: lo viejo se va
    /// y lo reciente se queda.
    /// </summary>
    [Fact]
    public async Task ElTrabajoDiarioPurgaLosIntentosDeLoginAntiguos()
    {
        var ct = TestContext.Current.CancellationToken;
        var viejo = Guid.NewGuid();
        var reciente = Guid.NewGuid();

        await using (var conexion = new NpgsqlConnection(bd.CadenaSuperusuario))
        {
            await conexion.OpenAsync(ct);
            foreach (var (id, dias) in new[] { (viejo, 120), (reciente, 10) })
            {
                await using var orden = new NpgsqlCommand(
                    "INSERT INTO intento_login (id, nombre_acceso, ip, exito, ocurrido_at) "
                    + "VALUES (@id, 'purga-prueba', '198.51.100.20', false, now() - make_interval(days => @dias))",
                    conexion);
                orden.Parameters.AddWithValue("id", id);
                orden.Parameters.AddWithValue("dias", dias);
                await orden.ExecuteNonQueryAsync(ct);
            }
        }

        using (var ambito = api.Services.CreateScope())
        {
            await ambito.ServiceProvider.GetRequiredService<TrabajoIntegridadAuditoria>().EjecutarAsync(ct);
        }

        await using var lectura = new NpgsqlConnection(bd.CadenaSuperusuario);
        await lectura.OpenAsync(ct);
        await using var consulta = new NpgsqlCommand(
            "SELECT id FROM intento_login WHERE nombre_acceso = 'purga-prueba'", lectura);
        var quedan = new List<Guid>();
        await using var lector = await consulta.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
        {
            quedan.Add(lector.GetGuid(0));
        }

        quedan.Should().Contain(reciente).And.NotContain(viejo);
    }
}
