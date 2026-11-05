using System.Net;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Seguridad;

/// <summary>
/// F1-05: la IP de la oficina está en la lista blanca y nunca se bloquea
/// (<c>Seguridad__IpListaBlanca</c>, docs/SEGURIDAD.md A.2). La cuenta sí se bloquea: la lista
/// blanca es de IP, no de cuentas.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ListaBlancaIpTests(ContenedorBdFixture bd, ApiTestFactoryIpPermitida api)
    : IClassFixture<ApiTestFactoryIpPermitida>
{
    private const string Contrasena = "Clave-de-prueba-2026";
    private const string ContrasenaMala = "contrasena-equivocada";

    [Fact]
    public async Task UnaIpEnLaListaBlancaNoSeBloqueaAunqueSupereElUmbralDeFallos()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, ct: ct);
        using var cliente = api.CrearCliente();

        // Doce fallos: por encima del umbral de IP (10) y del de cuenta (5).
        for (var intento = 1; intento <= 12; intento++)
        {
            var respuesta = await AyudaAuth.LoginAsync(cliente, usuario.NombreAcceso, ContrasenaMala, ct);

            // Nunca 403: para esta IP el bloqueo no existe.
            respuesta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.BloqueoIp.AnyAsync(b => b.Ip == api.IpCliente, ct)).Should().BeFalse();

        // La cuenta, en cambio, sí quedó bloqueada por sus cinco intentos fallidos.
        var fila = await contexto.Usuario.SingleAsync(u => u.Id == usuario.Id, ct);
        fila.BloqueadaHasta.Should().NotBeNull();
    }
}