using AtlasRent.Api.Jobs;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AtlasRent.Tests.Jobs;

/// <summary>
/// F1-12: los tests de integración no levantan el servidor de jobs.
/// <para>
/// Sin esto, cada clase que levanta la API dejaría un planificador real apuntando a la base de
/// datos de pruebas: el trabajo del backup escribiría en la carpeta de copias de la máquina y la
/// verificación de integridad correría sobre los datos de otro test. Por eso
/// <see cref="ApiTestFactory"/> apaga el servidor, y este test vigila que siga apagado.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ServidorJobsApagadoTests(ContenedorBdFixture bd)
{
    [Fact]
    public async Task LaApiDePruebasArrancaSinServidorDeJobs()
    {
        var ct = TestContext.Current.CancellationToken;
        using var api = new ApiTestFactory(bd);

        // Una petición de verdad, para que el host arranque del todo y no solo se construya.
        using var cliente = api.CrearClienteSinCookies();
        using var respuesta = await cliente.GetAsync("/health/live", ct);
        respuesta.EnsureSuccessStatusCode();

        api.Services.GetRequiredService<OpcionesJobs>().ServidorActivo.Should().BeFalse();

        api.Services.GetServices<IHostedService>()
            .Where(h => h.GetType().Namespace?.StartsWith("Hangfire", StringComparison.Ordinal) == true)
            .Should().BeEmpty("un servidor de jobs real ejecutaría backups de verdad en la máquina de pruebas");
    }
}