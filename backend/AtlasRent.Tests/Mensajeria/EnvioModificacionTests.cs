using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Mensajeria;

/// <summary>
/// F4-02: mandar el correo y luego cambiar la factura (F3-08) deja la fila marcada como «modificada
/// después de enviar», y el filtro <c>modificadasTrasEnvio</c> la devuelve.
/// <para>
/// La comparación no es contra la fecha del envío sino contra el <c>modificada_at</c> que la factura
/// tenía cuando salió: comparar con la fecha daría «modificada tras envío» a una factura que se tocó
/// antes de mandarla y solo se emitió después. Y un envío que <b>no salió</b> tampoco cuenta: no llegó
/// a haber un papel en manos del cliente que después se pudiera cambiar.
/// </para>
/// <para>
/// Va en su propia clase y con su propia fábrica a propósito: comparte reloj de pruebas con el resto
/// del archivo y avanzar ese reloj en un método afectaba a la sesión del siguiente.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class EnvioModificacionTests(ContenedorBdFixture bd, ApiTestFactoryCorreo api)
    : IClassFixture<ApiTestFactoryCorreo>
{
    /// <summary>
    /// Enviar, modificar la factura (F3-08) y comprobar que la vista la marca y que el filtro la
    /// devuelve. Sin modificar, el filtro no la devuelve.
    /// </summary>
    [Fact]
    public async Task ElDocumentoModificadoDespuesDelEnvioQuedaMarcado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaEnvios.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var modificada = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);
        var intacta = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(11), 400m, ct);

        await AyudaEnvios.EnviarAsync(admin, AyudaEnvios.PeticionFactura(modificada, AyudaEnvios.CorreoDePrueba()), ct);
        await AyudaEnvios.EnviarAsync(admin, AyudaEnvios.PeticionFactura(intacta, AyudaEnvios.CorreoDePrueba()), ct);

        var filtro = $"clienteId={escenario.ClienteId:D}&modificadasTrasEnvio=true";

        // Antes de tocar nada, ninguna de las dos sale: se mandaron y no se han tocado desde entonces.
        var antes = AyudaCuentas.Filas(
            await AyudaCuentas.VistaAsync(admin, filtro, ct));
        antes.Should().NotContain(f => f.GetProperty("id").GetGuid() == modificada);

        // El reloj de pruebas no se mueve solo: F3-08 escribe «modificada_at» con el reloj de la
        // aplicación, así que sin avanzar un minuto la modificación tendría la misma marca que el
        // envío y la comparación «mayor que» no se cumpliría. Se avanza un minuto y no más porque
        // cruzar la medianoche cambiaría el día de negocio de todo lo demás (BUG-009).
        api.Reloj.Avanzar(TimeSpan.FromMinutes(1));

        await AyudaEnvios.ModificarFacturaAsync(admin, modificada, ct);

        var despues = AyudaCuentas.Filas(
            await AyudaCuentas.VistaAsync(admin, filtro, ct));

        despues.Should().Contain(
            f => f.GetProperty("id").GetGuid() == modificada,
            "la factura se tocó después de que saliera su correo (F3-08 y F4-02)");
        despues.Should().NotContain(
            f => f.GetProperty("id").GetGuid() == intacta,
            "la otra se mandó y no se ha tocado: no hay nada que marcarla");

        // La marca de la fila y el filtro tienen que decir lo mismo, o uno de los dos está mintiendo.
        var fila = AyudaCuentas.Filas(
                await AyudaCuentas.VistaAsync(admin, $"clienteId={escenario.ClienteId:D}", ct))
            .Single(f => f.GetProperty("id").GetGuid() == modificada);

        fila.GetProperty("modificadaTrasEnvio").GetBoolean().Should().BeTrue();
        fila.GetProperty("estadoEnvio").GetString().Should().Be(nameof(EstadoEnvio.Enviado));
    }
}