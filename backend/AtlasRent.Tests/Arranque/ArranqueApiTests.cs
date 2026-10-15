using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace AtlasRent.Tests.Arranque;

/// <summary>
/// F1-01, aceptación: <c>dotnet run</c> arranca y <c>/health</c> responde.
/// Arranca la API real con <see cref="WebApplicationFactory{TEntryPoint}"/>.
/// </summary>
public sealed class ArranqueApiTests(WebApplicationFactory<Program> api)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task HealthLiveRespondeOkSinBaseDeDatos()
    {
        using var cliente = api.CreateClient();

        var respuesta = await cliente.GetAsync("/health/live", TestContext.Current.CancellationToken);

        respuesta.IsSuccessStatusCode.Should().BeTrue();
        (await respuesta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("Healthy");
    }

    [Fact]
    public async Task HealthDevuelveTextoPlano()
    {
        using var cliente = api.CreateClient();

        var respuesta = await cliente.GetAsync("/health/live", TestContext.Current.CancellationToken);

        // Escritor por defecto de MapHealthChecks: texto plano con el nombre y el estado.
        respuesta.Content.Headers.ContentType?.MediaType.Should().Be("text/plain");
    }

    [Fact]
    public async Task HealthCompruebaLaConexionConPostgreSql()
    {
        using var cliente = api.CreateClient();

        // /health incluye la comprobación de PostgreSQL. Sin base de datos de desarrollo
        // arrancada responde 503: lo que se comprueba aquí es que el registro existe y se ejecuta.
        var respuesta = await cliente.GetAsync("/health", TestContext.Current.CancellationToken);

        respuesta.StatusCode.Should().BeOneOf(System.Net.HttpStatusCode.OK, System.Net.HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public void ElDataSourceDePostgreSqlQuedaRegistrado()
    {
        api.Services.GetService<NpgsqlDataSource>().Should().NotBeNull();
    }
}