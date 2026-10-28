using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Mensajeria;

/// <summary>
/// BUG-052: <c>modificadasTrasEnvio</c> compara con el envío «cuando salió». Un envío que sigue
/// <c>Pendiente</c> (en cola) todavía no salió, así que no dice nada de si el documento cambió después.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class EnvioPendienteNoCuentaComoSalidoTests(ContenedorBdFixture bd, ApiTestFactoryCorreo api)
    : IClassFixture<ApiTestFactoryCorreo>
{
    [Fact]
    public async Task UnEnvioPendienteNoMarcaLaFacturaComoModificadaTrasElEnvio()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaEnvios.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var factura = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);
        await AyudaEnvios.EnviarAsync(admin, AyudaEnvios.PeticionFactura(factura, AyudaEnvios.CorreoDePrueba()), ct);

        // El envío vuelve a estar en cola: no ha salido.
        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            await contexto.Envio
                .Where(e => e.DocumentoId == factura)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Estado, EstadoEnvio.Pendiente), ct);
        }

        api.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        await AyudaEnvios.ModificarFacturaAsync(admin, factura, ct);

        var filas = AyudaCuentas.Filas(await AyudaCuentas.VistaAsync(
            admin, $"clienteId={escenario.ClienteId:D}&modificadasTrasEnvio=true", ct));

        filas.Should().NotContain(
            f => f.GetProperty("id").GetGuid() == factura,
            "un envío pendiente no salió: no se puede decir que la factura cambió después de enviarla");
    }
}
