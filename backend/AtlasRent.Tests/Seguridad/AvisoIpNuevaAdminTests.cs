using System.Collections.Concurrent;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Mensajeria;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace AtlasRent.Tests.Seguridad;

/// <summary>Cola de aviso de IP nueva que solo anota lo encolado.</summary>
public sealed class ColaAvisosAccesoDoble : IColaAvisosAcceso
{
    private readonly ConcurrentQueue<(Guid Usuario, string Ip)> _encolados = new();

    public IReadOnlyList<(Guid Usuario, string Ip)> Encolados => [.. _encolados];

    public void EncolarIpNueva(Guid usuarioId, string ip, DateTimeOffset cuando) => _encolados.Enqueue((usuarioId, ip));
}

/// <summary>Con correo configurado y la cola sustituida.</summary>
public sealed class ApiTestFactoryAvisoIpConCorreo(ContenedorBdFixture bd) : ApiTestFactoryCorreo(bd)
{
    public ColaAvisosAccesoDoble ColaAvisos { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(servicios =>
        {
            servicios.RemoveAll<IColaAvisosAcceso>();
            servicios.AddSingleton<IColaAvisosAcceso>(ColaAvisos);
        });
    }
}

/// <summary>Sin correo configurado y la cola sustituida.</summary>
public sealed class ApiTestFactoryAvisoIpSinCorreo(ContenedorBdFixture bd) : ApiTestFactory(bd)
{
    public ColaAvisosAccesoDoble ColaAvisos { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(servicios =>
        {
            servicios.RemoveAll<IColaAvisosAcceso>();
            servicios.AddSingleton<IColaAvisosAcceso>(ColaAvisos);
        });
    }
}

/// <summary>SEC-023: alerta y correo al administrador por entrada desde IP nueva.</summary>
[Collection(nameof(ColeccionBd))]
public sealed class AvisoIpNuevaAdminConCorreoTests(ContenedorBdFixture bd, ApiTestFactoryAvisoIpConCorreo api)
    : IClassFixture<ApiTestFactoryAvisoIpConCorreo>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    [Fact]
    public async Task IpNuevaDejaAlertaYEncolaElCorreoYUnaSegundaEntradaNoRepite()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        var ipNueva = ContadorInstancias.IpSiguiente();

        await EntrarAsync(usuario, api.IpCliente, ct);
        (await AlertasAsync(usuario.Id, ct)).Should().BeEmpty("el primer login no tiene IP con la que comparar");

        await EntrarAsync(usuario, ipNueva, ct);
        var alertas = await AlertasAsync(usuario.Id, ct);
        alertas.Should().ContainSingle();
        alertas[0].Tipo.Should().Be("seguridad");
        alertas[0].Clave.Should().Be($"ip-nueva:{usuario.Id:D}:{ipNueva}");
        alertas[0].Mensaje.Should().Contain(ipNueva).And.Contain(usuario.NombreAcceso);
        api.ColaAvisos.Encolados.Should().ContainSingle(e => e.Usuario == usuario.Id && e.Ip == ipNueva);

        await EntrarAsync(usuario, ipNueva, ct);
        (await AlertasAsync(usuario.Id, ct)).Should().ContainSingle();
        api.ColaAvisos.Encolados.Count(e => e.Usuario == usuario.Id).Should().Be(1);
    }

    [Fact]
    public async Task ElTrabajoMandaElCorreoALosAdminsSoloConUsuarioIpYFecha()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        var antes = api.Doble.Enviados.Count;

        using (var alcance = api.Services.CreateScope())
        {
            await alcance.ServiceProvider.GetRequiredService<ServicioAvisosAcceso>()
                .EnviarIpNuevaAsync(usuario.Id, "198.51.100.77", DateTimeOffset.UtcNow, ct);
        }

        var nuevos = api.Doble.Enviados.Skip(antes).ToList();
        nuevos.Should().Contain(m => m.DestinatarioEmail == admin.Email);
        nuevos.Should().NotContain(m => m.DestinatarioEmail == usuario.Email);
        var correo = nuevos.First(m => m.DestinatarioEmail == admin.Email);
        correo.Cuerpo.Should().Contain(usuario.NombreAcceso).And.Contain("198.51.100.77").And.Contain("UTC");
        correo.Cuerpo.Should().NotContain(usuario.Nombre);
        correo.Adjunto.Should().BeNull();
    }

    private async Task EntrarAsync(Usuario usuario, string ip, CancellationToken ct)
    {
        api.Resolutor.Ip = ip;
        using var cliente = api.CrearCliente();
        (await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, Contrasena, ct)).EnsureSuccessStatusCode();
    }

    private async Task<List<Alerta>> AlertasAsync(Guid usuarioId, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        return await contexto.Alerta.AsNoTracking()
            .Where(a => a.Clave.StartsWith($"ip-nueva:{usuarioId:D}:"))
            .ToListAsync(ct);
    }
}

[Collection(nameof(ColeccionBd))]
public sealed class AvisoIpNuevaAdminSinCorreoTests(ContenedorBdFixture bd, ApiTestFactoryAvisoIpSinCorreo api)
    : IClassFixture<ApiTestFactoryAvisoIpSinCorreo>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    [Fact]
    public async Task SinCorreoConfiguradoSoloQuedaLaAlerta()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        var ipNueva = ContadorInstancias.IpSiguiente();

        foreach (var ip in new[] { api.IpCliente, ipNueva })
        {
            api.Resolutor.Ip = ip;
            using var cliente = api.CrearCliente();
            (await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, Contrasena, ct)).EnsureSuccessStatusCode();
        }

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Alerta.AsNoTracking().CountAsync(a => a.Clave == $"ip-nueva:{usuario.Id:D}:{ipNueva}", ct))
            .Should().Be(1);
        api.ColaAvisos.Encolados.Should().BeEmpty();
    }
}
