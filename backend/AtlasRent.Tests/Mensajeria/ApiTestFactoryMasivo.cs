using System.Collections.Concurrent;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace AtlasRent.Tests.Mensajeria;

/// <summary>
/// Fábrica de las pruebas de envío masivo (F4-03): la de <see cref="ApiTestFactoryCorreo"/> con la cola
/// sustituida por <see cref="ColaEnviosDoble"/>.
/// <para>
/// <b>En las pruebas no hay servidor de jobs</b> (<c>Jobs__ServidorActivo=false</c>), así que la cola
/// real no podría ni ejecutarse ni dejar rastro de qué se encoló. Y lo que hay que comprobar aquí es
/// justo eso: qué se encoló, cuántas veces y de qué. Por eso el doble expone la lista en vez de
/// ejecutarla, y las pruebas invocan <c>TrabajoEnvio.EjecutarAsync</c> a mano, como hacen las de F2 y
/// F3 con sus trabajos.
/// </para>
/// </summary>
public sealed class ApiTestFactoryMasivo(ContenedorBdFixture bd) : ApiTestFactoryCorreo(bd)
{
    /// <summary>La cola con la que esta instancia registra lo que hay que mandar.</summary>
    public ColaEnviosDoble Cola { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(servicios =>
        {
            servicios.RemoveAll<IColaEnvios>();
            servicios.AddSingleton<IColaEnvios>(Cola);
        });
    }
}

/// <summary>
/// Cola de mentira: guarda lo que se encoló y no manda nada. Solo la cola; el correo lo sigue
/// simulando <see cref="CorreoDoble"/>.
/// </summary>
public sealed class ColaEnviosDoble : IColaEnvios
{
    private readonly ConcurrentQueue<Guid> _encolados = new();

    /// <summary>Los identificadores de envío encolados, en orden.</summary>
    public IReadOnlyList<Guid> Encolados => [.. _encolados];

    /// <summary>Cuántos se han encolado en total.</summary>
    public int Total => _encolados.Count;

    /// <summary>
    /// Vacía la lista. Cada prueba empieza en cero para que una prueba no vea lo que encoló otra:
    /// la base es compartida y la cola no.
    /// </summary>
    public void Vaciar()
    {
        while (_encolados.TryDequeue(out _))
        {
            // Vaciar es quitar hasta que no queda nada, y no hay nada más que hacer con cada elemento.
        }
    }

    public void EncolarEnvio(Guid envioId, Guid usuario, string ip) => _encolados.Enqueue(envioId);
}