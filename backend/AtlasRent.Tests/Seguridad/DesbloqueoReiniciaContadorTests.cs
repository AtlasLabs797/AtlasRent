using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Seguridad;

/// <summary>
/// Seguimiento de SEC-012: tras un desbloqueo manual los fallos anteriores no vuelven a contar, así
/// que hacen falta otros cinco para bloquear de nuevo y no basta uno.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class DesbloqueoReiniciaContadorTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    private const string Contrasena = "Clave-de-prueba-2026";
    private const string ContrasenaMala = "contrasena-equivocada";

    [Fact]
    public async Task TrasUnDesbloqueoManualUnSoloFalloNoVuelveABloquearYCincoSi()
    {
        var ct = TestContext.Current.CancellationToken;
        var objetivo = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);

        api.Resolutor.Ip = ContadorInstancias.IpSiguiente();
        using var cliente = api.CrearCliente();
        for (var i = 0; i < 5; i++)
        {
            await AyudaAuth.LoginAsync(cliente, objetivo.NombreAcceso, ContrasenaMala, ct);
        }

        (await BloqueadaHastaAsync(objetivo.Id, ct)).Should().NotBeNull();

        api.Resolutor.Ip = ContadorInstancias.IpSiguiente();
        using var sesionAdmin = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);
        api.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        (await sesionAdmin.PostAsync(
            "/api/v1/seguridad/bloqueos/cuenta/desbloquear", new { usuarioId = objetivo.Id }, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await BloqueadaHastaAsync(objetivo.Id, ct)).Should().BeNull();
        api.Reloj.Avanzar(TimeSpan.FromSeconds(1));

        api.Resolutor.Ip = ContadorInstancias.IpSiguiente();
        (await AyudaAuth.LoginAsync(cliente, objetivo.NombreAcceso, ContrasenaMala, ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await BloqueadaHastaAsync(objetivo.Id, ct)).Should().BeNull("un solo fallo tras desbloquear no bloquea");

        for (var i = 0; i < 3; i++)
        {
            await AyudaAuth.LoginAsync(cliente, objetivo.NombreAcceso, ContrasenaMala, ct);
        }

        (await BloqueadaHastaAsync(objetivo.Id, ct)).Should().BeNull("van cuatro fallos desde el desbloqueo");

        await AyudaAuth.LoginAsync(cliente, objetivo.NombreAcceso, ContrasenaMala, ct);
        (await BloqueadaHastaAsync(objetivo.Id, ct)).Should().NotBeNull("el quinto fallo desde el desbloqueo bloquea");
    }

    private async Task<DateTimeOffset?> BloqueadaHastaAsync(Guid usuarioId, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        return (await contexto.Usuario.AsNoTracking().SingleAsync(u => u.Id == usuarioId, ct)).BloqueadaHasta;
    }
}
