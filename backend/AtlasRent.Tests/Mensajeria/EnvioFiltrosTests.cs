using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Reservas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Mensajeria;

/// <summary>
/// F4-02: los tres filtros de envío de §3.13 <b>filtran de verdad</b>.
/// <para>
/// Hasta que existió la tabla <c>envio</c> estos tres parámetros no se aceptaban, y
/// <c>CuentaFiltrosPendientesTests</c> (F3-10) fijaba exactamente eso: que se aceptaran y se ignoraran.
/// F4-02 borra esa prueba y pone estas, que son la afirmación contraria y la que hace que el filtro
/// valga.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class EnvioFiltrosTests(ContenedorBdFixture bd, ApiTestFactoryCorreo api)
    : IClassFixture<ApiTestFactoryCorreo>
{
    /// <summary>
    /// Un caso de cada: nunca enviada, con un envío que falló, y enviada. Y se mira lo mismo desde los
    /// dos filtros, porque «no enviada» y «envío fallido» son preguntas distintas y si salieran las
    /// dos listas iguales uno de los dos filtros estaría mintiendo.
    /// </summary>
    [Fact]
    public async Task LosFiltrosDeLaVistaUnicaFiltranDeVerdad()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaEnvios.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);

        var nuncaEnviada = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);
        var fallida = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(11), 400m, ct);
        var enviada = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(12), 500m, ct);

        // El envío que falla deja su fila en «Fallido» y no llega al cliente.
        api.Doble.Fallar = new InvalidOperationException("el servidor de correo no responde");
        await AyudaEnvios.EnviarAsync(admin, AyudaEnvios.PeticionFactura(fallida, AyudaEnvios.CorreoDePrueba()), ct);
        api.Doble.Restablecer();

        await AyudaEnvios.EnviarAsync(admin, AyudaEnvios.PeticionFactura(enviada, AyudaEnvios.CorreoDePrueba()), ct);

        var noEnviadas = AyudaCuentas.Filas(
            await AyudaCuentas.VistaAsync(admin, $"clienteId={escenario.ClienteId:D}&noEnviadas=true", ct));
        var fallidos = AyudaCuentas.Filas(
            await AyudaCuentas.VistaAsync(admin, $"clienteId={escenario.ClienteId:D}&envioFallido=true", ct));
        var todas = AyudaCuentas.Filas(
            await AyudaCuentas.VistaAsync(admin, $"clienteId={escenario.ClienteId:D}", ct));

        noEnviadas.Select(f => f.GetProperty("id").GetGuid())
            .Should().Contain(nuncaEnviada, "esa factura no se mandó nunca");
        noEnviadas.Select(f => f.GetProperty("id").GetGuid())
            .Should().NotContain(fallida, "se intentó y falló: eso es «envío fallido», no «no enviada»");
        noEnviadas.Select(f => f.GetProperty("id").GetGuid())
            .Should().NotContain(enviada, "ésta sí salió");

        fallidos.Select(f => f.GetProperty("id").GetGuid())
            .Should().Contain(fallida, "esa tiene un envío que no salió");
        fallidos.Select(f => f.GetProperty("id").GetGuid())
            .Should().NotContain(nuncaEnviada, "nadie lo intentó: no hay fallo que contar");
        fallidos.Select(f => f.GetProperty("id").GetGuid())
            .Should().NotContain(enviada, "ésta sí salió");

        // La columna estadoEnvio sale real y no siempre en null.
        todas.Should().Contain(
            f => f.GetProperty("estadoEnvio").ValueKind != System.Text.Json.JsonValueKind.Null);

        var filaFallida = todas.Single(f => f.GetProperty("id").GetGuid() == fallida);
        filaFallida.GetProperty("estadoEnvio").GetString().Should().Be("Fallido");

        var filaEnviada = todas.Single(f => f.GetProperty("id").GetGuid() == enviada);
        filaEnviada.GetProperty("estadoEnvio").GetString().Should().Be("Enviado");

        var filaNunca = todas.Single(f => f.GetProperty("id").GetGuid() == nuncaEnviada);
        filaNunca.GetProperty("estadoEnvio").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);

        // Y una fianza, que no es un documento que se mande, sale con su estado de envío en null y con
        // la marca de modificada en null también.
        await AyudaCuentas.FianzaAsync(admin, escenario, 700m, ct);

        var conFianza = await AyudaCuentas.VistaAsync(admin, $"clienteId={escenario.ClienteId:D}", ct);
        var fianza = AyudaCuentas.FilaDe(conFianza, "Fianza");
        fianza.GetProperty("estadoEnvio").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        fianza.GetProperty("modificadaTrasEnvio").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);

        // Con cualquiera de los tres filtros, la fianza queda fuera: un filtro que una fianza no puede
        // expresar la deja fuera (docs/DOC_TECNICA.md §6.7).
        foreach (var filtro in new[] { "noEnviadas=true", "envioFallido=true", "modificadasTrasEnvio=true" })
        {
            var filtrada = AyudaCuentas.Filas(
                await AyudaCuentas.VistaAsync(admin, $"clienteId={escenario.ClienteId:D}&{filtro}", ct));

            filtrada.Should().NotContain(f => f.GetProperty("tipo").GetString() == "Fianza", filtro);
        }
    }
}