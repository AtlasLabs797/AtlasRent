using System.Net;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AtlasRent.Tests.Seguridad;

/// <summary>
/// F1-05: escalado del bloqueo de IP, 1 h â†’ 24 h â†’ indefinido (docs/SEGURIDAD.md A.2).
/// <para>
/// Todo desde la vÃ­a real: 10 fallos de login desde la misma IP dentro de la ventana de 15 minutos.
/// El tiempo se mueve con el reloj inyectado, asÃ­ que las 24 horas tampoco se esperan.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class EscaladoBloqueoIpTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    private const string ContrasenaMala = "contrasena-equivocada";

    [Fact]
    public async Task LaReincidenciaEscalaDeUnaHoraADiasYMedioAIndefinido()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cliente = api.CrearCliente();

        // Nueve fallos no bloquean: el umbral son diez dentro de la ventana.
        await FallarLoginAsync(cliente, 9, ct);
        (await BloqueoAsync(ct)).Should().BeNull();

        await FallarLoginAsync(cliente, 1, ct);

        var primero = await BloqueoAsync(ct);
        primero!.Nivel.Should().Be(1);
        primero.BloqueadaHasta.Should()
            .BeCloseTo(api.Reloj.GetUtcNow().Add(ServicioBloqueos.PrimerNivel), TimeSpan.FromSeconds(5));

        // Pasada la hora la IP vuelve a estar libre y reincide: 24 horas.
        api.Reloj.Avanzar(ServicioBloqueos.PrimerNivel + TimeSpan.FromMinutes(1));
        await FallarLoginAsync(cliente, 10, ct);

        var segundo = await BloqueoAsync(ct);
        segundo!.Nivel.Should().Be(2);
        segundo.BloqueadaHasta.Should()
            .BeCloseTo(api.Reloj.GetUtcNow().Add(ServicioBloqueos.SegundoNivel), TimeSpan.FromSeconds(5));

        // Y a la tercera, indefinido: ya no caduca solo.
        api.Reloj.Avanzar(ServicioBloqueos.SegundoNivel + TimeSpan.FromMinutes(1));
        await FallarLoginAsync(cliente, 10, ct);

        var tercero = await BloqueoAsync(ct);
        tercero!.Nivel.Should().Be(ServicioBloqueos.NivelIndefinido);
        tercero.BloqueadaHasta.Should().BeNull();

        api.Reloj.Avanzar(TimeSpan.FromDays(30));
        (await EstaBloqueadaAsync(ct)).Should().BeTrue();

        // Lo levanta el administrador, que es la Ãºnica vÃ­a para un bloqueo indefinido.
        await DesbloquearAsync(ct);

        (await BloqueoAsync(ct)).Should().BeNull();
        (await EstaBloqueadaAsync(ct)).Should().BeFalse();
    }

    private async Task FallarLoginAsync(HttpClient cliente, int intentos, CancellationToken ct)
    {
        for (var intento = 0; intento < intentos; intento++)
        {
            var respuesta = await AyudaAuth.LoginAsync(cliente, $"nadie-{Guid.NewGuid():N}", ContrasenaMala, ct);

            // La IP no estÃ¡ bloqueada todavÃ­a, asÃ­ que la respuesta es 401 y no 403.
            respuesta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    private async Task<BloqueoIp?> BloqueoAsync(CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        return await contexto.BloqueoIp.AsNoTracking().SingleOrDefaultAsync(b => b.Ip == api.IpCliente, ct);
    }

    private async Task<bool> EstaBloqueadaAsync(CancellationToken ct)
    {
        using var ambito = api.Services.CreateScope();
        var bloqueos = ambito.ServiceProvider.GetRequiredService<ServicioBloqueos>();
        return await bloqueos.IpBloqueadaAsync(api.IpCliente, ct);
    }

    private async Task DesbloquearAsync(CancellationToken ct)
    {
        using var ambito = api.Services.CreateScope();
        var bloqueos = ambito.ServiceProvider.GetRequiredService<ServicioBloqueos>();
        await bloqueos.DesbloquearIpAsync(api.IpCliente, Guid.NewGuid(), ct);
    }
}
