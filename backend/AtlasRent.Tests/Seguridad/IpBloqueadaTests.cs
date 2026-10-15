using System.Net;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Seguridad;

/// <summary>
/// F1-05: una IP bloqueada no llega a ninguna ruta, ni al login ni a las de salud.
/// <para>
/// El bloqueo se mete en la base de datos directamente porque lo que se comprueba aquí es el
/// middleware, no el contador; el contador está en
/// <see cref="EscaladoBloqueoIpTests"/>.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class IpBloqueadaTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    [Fact]
    public async Task UnaIpBloqueadaRecibe403EnCualquierRuta()
    {
        var ct = TestContext.Current.CancellationToken;
        var ahora = api.Reloj.GetUtcNow();

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            contexto.BloqueoIp.Add(new BloqueoIp
            {
                Id = Guid.NewGuid(),
                Ip = api.IpCliente,
                Nivel = 1,
                Fallos = 10,
                BloqueadaHasta = ahora.AddHours(1),
                CreatedAt = ahora,
                UpdatedAt = ahora,
            });
            await contexto.SaveChangesAsync(ct);
        }

        using var cliente = api.CrearCliente();

        foreach (var ruta in new[] { "/api/v1/auth/yo", "/health/live", "/health" })
        {
            var respuesta = await cliente.GetAsync(ruta, ct);

            respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"la ruta {ruta} tiene que estar bloqueada");
            (await respuesta.Content.ReadAsStringAsync(ct)).Should().Contain(Problemas.IpBloqueada);
        }
    }
}