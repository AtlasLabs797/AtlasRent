using System.Text;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Npgsql;
using Xunit;

namespace AtlasRent.Tests.Mensajeria;

/// <summary>
/// SEC-018: el PDF de <c>envio.pdf_copia</c> lleva la cédula/RNC del cliente, así que no puede estar
/// en claro en la base de datos (AGENTS.md §6). Se guarda cifrado con AES-256-GCM y la descarga
/// devuelve el PDF original byte a byte.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class EnvioCopiaCifradaTests(ContenedorBdFixture bd, ApiTestFactoryCorreo api)
    : IClassFixture<ApiTestFactoryCorreo>
{
    [Fact]
    public async Task LaColumnaNoContieneElPdfYLaDescargaDevuelveElOriginal()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaEnvios.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaId = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);
        var envio = await AyudaEnvios.EnviarAsync(
            admin, AyudaEnvios.PeticionFactura(facturaId, AyudaEnvios.CorreoDePrueba()), ct);
        var envioId = envio.GetProperty("id").GetGuid();
        var adjunto = api.Doble.Ultimo!.Adjunto;

        byte[] crudo;
        await using (var conexion = new NpgsqlConnection(bd.CadenaApp))
        {
            await conexion.OpenAsync(ct);
            await using var orden = new NpgsqlCommand("SELECT pdf_copia FROM envio WHERE id = @id", conexion);
            orden.Parameters.AddWithValue("id", envioId);
            crudo = (byte[])(await orden.ExecuteScalarAsync(ct))!;
        }

        crudo.Should().NotBeEmpty();
        Encoding.Latin1.GetString(crudo).Should().NotContain("%PDF-", "la copia va cifrada en la base de datos");
        crudo.Should().NotEqual(adjunto);

        (await AyudaEnvios.PdfDeEnvioAsync(admin, envioId, ct))
            .Should().Equal(adjunto, "la descarga descifra y devuelve el PDF que salió, byte a byte");
    }

    [Fact]
    public void UnaFilaAnteriorAlCifradoEnClaroSigueSiendoLegible()
    {
        var pdf = "%PDF-1.7 contenido"u8.ToArray();

        CopiaEnvio.Cifrado.DescifrarBytes(pdf).Should().Equal(pdf);
        CopiaEnvio.Cifrado.DescifrarBytes(CopiaEnvio.Cifrado.CifrarBytes(pdf)).Should().Equal(pdf);
        CopiaEnvio.Cifrado.CifrarBytes([]).Should().BeEmpty();
    }
}
