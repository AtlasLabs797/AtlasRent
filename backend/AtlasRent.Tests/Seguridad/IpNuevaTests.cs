using AtlasRent.Api.Data;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Seguridad;

/// <summary>
/// F1-05: aviso al administrador de una entrada desde IP nueva para ese usuario
/// (docs/SEGURIDAD.md A.2).
/// <para>
/// Lo que se comprueba aquí es la detección y el evento. El transporte del aviso (correo) es de
/// fases posteriores y queda como <c>TODO(A#)</c> en <c>ServicioBloqueos</c>.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class IpNuevaTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    [Fact]
    public async Task EntrarDesdeUnaIpQueEseUsuarioNoHaUsadoDejaElEventoDeAviso()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);

        // Primer login: no hay IP previa con la que comparar, así que no es una IP nueva.
        using (var primeraVez = api.CrearCliente())
        {
            (await AyudaAuth.LoginAsync(primeraVez, usuario.NombreAcceso, Contrasena, ct))
                .StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        }

        (await EventosAsync(usuario.Id, ct)).Should().BeEmpty();

        // Mismo usuario, otra IP.
        api.Resolutor.Ip = "198.51.100.201";
        using (var otraIp = api.CrearCliente())
        {
            (await AyudaAuth.LoginAsync(otraIp, usuario.NombreAcceso, Contrasena, ct))
                .StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        }

        var eventos = await EventosAsync(usuario.Id, ct);
        eventos.Should().ContainSingle();
        eventos[0].Ip.Should().Be("198.51.100.201");
        eventos[0].Entidad.Should().Be("usuario");

        // Volver a entrar desde la misma IP nueva no repite el aviso.
        using (var tercera = api.CrearCliente())
        {
            (await AyudaAuth.LoginAsync(tercera, usuario.NombreAcceso, Contrasena, ct))
                .StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        }

        (await EventosAsync(usuario.Id, ct)).Should().ContainSingle();
    }

    /// <summary>
    /// Eventos de auditoría del usuario. La lectura va con el superusuario porque la política de RLS
    /// de <c>auditoria</c> no deja leer a un empleado (docs/DOC_TECNICA.md §4.2).
    /// </summary>
    private async Task<List<Auditoria>> EventosAsync(Guid usuarioId, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaSuperusuario);
        return await contexto.Auditoria
            .AsNoTracking()
            .Where(a => a.UsuarioId == usuarioId && a.Accion == "auth.ip_nueva")
            .ToListAsync(ct);
    }
}