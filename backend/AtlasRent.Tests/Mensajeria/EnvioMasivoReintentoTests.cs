using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Mensajeria;

/// <summary>
/// F4-03, <b>criterio de aceptación</b>: «Reintenta solo fallidos; sin duplicados»
/// (docs/ESPECIFICACION.md §3.14).
/// <para>
/// Es la prueba que decide si la tarea está hecha: reintentar no vuelve a mandar lo que ya salió, no
/// sube el contador de intentos de lo que ya salió, no crea filas nuevas y manda lo que falló.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class EnvioMasivoReintentoTests(ContenedorBdFixture bd, ApiTestFactoryMasivo api)
    : IClassFixture<ApiTestFactoryMasivo>
{
    /// <summary>
    /// Tres documentos, el doble falla en el segundo, y el reintento solo vuelve a encolar ese. Los
    /// otros dos no se tocan: ni sube su contador de intentos ni aparecen filas nuevas.
    /// </summary>
    [Fact]
    public async Task ReintentarSoloLosFallidos()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, usuario) = await AyudaEnviosMasivo.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(11), 400m, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(12), 500m, ct);

        api.Cola.Vaciar();

        var loteId = await AyudaEnviosMasivo.ConfirmarAsync(
            admin, AyudaEnviosMasivo.Peticion(escenario.ClienteId), ct);

        api.Cola.Encolados.Should().HaveCount(3);

        // El orden de la cola es el de la vista, que desempata por identificador: no dice cuál de las
        // tres es la segunda, así que el que se hace fallar es «el segundo de la cola» y no «la segunda
        // factura».
        var salePrimero = api.Cola.Encolados[0];
        var seQuedaEnElTintero = api.Cola.Encolados[1];
        var saleTercero = api.Cola.Encolados[2];

        await AyudaEnviosMasivo.EjecutarTrabajoAsync(api, salePrimero, usuario, ct);

        api.Doble.Fallar = new InvalidOperationException("El servidor de correo no responde.");
        await AyudaEnviosMasivo.EjecutarTrabajoAsync(api, seQuedaEnElTintero, usuario, ct);
        api.Doble.Restablecer();

        await AyudaEnviosMasivo.EjecutarTrabajoAsync(api, saleTercero, usuario, ct);

        var trasElLote = await AyudaEnviosMasivo.FilasDelLoteAsync(bd, loteId, ct);
        trasElLote.Should().HaveCount(3, "una fila por documento, ni una más");
        trasElLote.Count(e => e.Estado == EstadoEnvio.Enviado).Should().Be(2);
        trasElLote.Count(e => e.Estado == EstadoEnvio.Fallido).Should().Be(1);

        var fallido = trasElLote.Should().ContainSingle(e => e.Id == seQuedaEnElTintero).Which;
        fallido.Intentos.Should().Be(1);
        fallido.UltimoError.Should().Be("InvalidOperationException sin detalle.", "y el motivo, sin detalle");

        // El progreso cuenta lo que pasó, y el fallido sale con su motivo para decidir el reintento.
        var progreso = await AyudaEnviosMasivo.ProgresoAsync(admin, loteId, ct);
        progreso.GetProperty("total").GetInt32().Should().Be(3);
        progreso.GetProperty("enviados").GetInt32().Should().Be(2);
        progreso.GetProperty("fallidos").GetInt32().Should().Be(1);
        progreso.GetProperty("pendientes").GetInt32().Should().Be(0);

        var detalle = AyudaEnviosMasivo.Lista(progreso, "fallidosDetalle").Should().ContainSingle().Which;
        detalle.GetProperty("documentoId").GetGuid().Should().Be(fallido.DocumentoId);
        detalle.GetProperty("intentos").GetInt32().Should().Be(1);
        detalle.GetProperty("motivo").GetString().Should().Be("InvalidOperationException sin detalle.");

        // Ahora el reintento, que es lo que hay que comprobar.
        api.Cola.Vaciar();
        var correosAntes = api.Doble.Enviados.Count;

        var respuesta = await AyudaEnviosMasivo.ReintentarAsync(admin, loteId, ct);

        respuesta.GetProperty("reintentados").GetInt32().Should().Be(1);
        api.Cola.Encolados.Should().ContainSingle("solo se reencola lo que falló").Which.Should().Be(fallido.Id);

        await AyudaEnviosMasivo.EjecutarTrabajoAsync(api, api.Cola.Encolados[0], usuario, ct);

        api.Doble.Enviados.Should().HaveCount(correosAntes + 1, "reintentar manda una vez lo que falló");

        var trasElReintento = await AyudaEnviosMasivo.FilasDelLoteAsync(bd, loteId, ct);
        trasElReintento.Should().HaveCount(3, "el reintento sube intentos en la fila que había: no crea otra");

        var reintentado = trasElReintento.Should().ContainSingle(e => e.Id == fallido.Id).Which;
        reintentado.Estado.Should().Be(EstadoEnvio.Enviado);
        reintentado.Intentos.Should().Be(2, "el contador de intentos sube, y por eso dice dos");
        reintentado.UltimoError.Should().BeNull();

        foreach (var salida in trasElReintento.Where(e => e.Id != fallido.Id))
        {
            salida.Estado.Should().Be(EstadoEnvio.Enviado);
            salida.Intentos.Should().Be(1, "lo que ya salió no ve subir su contador de intentos");
            (await AyudaEnviosMasivo.FilasDelDocumentoAsync(bd, salida.DocumentoId, ct))
                .Should().ContainSingle("ni se le abre una fila nueva");
        }

        var final = await AyudaEnviosMasivo.ProgresoAsync(admin, loteId, ct);
        final.GetProperty("enviados").GetInt32().Should().Be(3);
        final.GetProperty("fallidos").GetInt32().Should().Be(0);
    }

    /// <summary>
    /// Un lote que no tiene fallidos no es un error: no hay nada que reintentar y la respuesta lo
    /// dice con un cero, sin tocar ninguna fila.
    /// </summary>
    [Fact]
    public async Task ReintentarUnLoteSinFallidosNoEncolaNada()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, usuario) = await AyudaEnviosMasivo.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        api.Cola.Vaciar();
        var loteId = await AyudaEnviosMasivo.ConfirmarAsync(
            admin, AyudaEnviosMasivo.Peticion(escenario.ClienteId), ct);

        await AyudaEnviosMasivo.EjecutarTrabajoAsync(api, api.Cola.Encolados[0], usuario, ct);

        api.Cola.Vaciar();
        var respuesta = await AyudaEnviosMasivo.ReintentarAsync(admin, loteId, ct);

        respuesta.GetProperty("reintentados").GetInt32().Should().Be(0);
        api.Cola.Encolados.Should().BeEmpty("no hay nada fallido que reencolar");

        var filas = await AyudaEnviosMasivo.FilasDelLoteAsync(bd, loteId, ct);
        filas.Should().ContainSingle().Which.Intentos.Should().Be(1, "y el que salió no se toca");
    }

    /// <summary>Un lote que no existe es un 404, no un cero.</summary>
    [Fact]
    public async Task UnLoteQueNoExisteEsNoEncontrado()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaEnviosMasivo.SesionAsync(bd, api, Roles.Admin, ct);

        var inexistente = Guid.NewGuid();

        (await admin.GetAsync($"/api/v1/envios/masivo/{inexistente:D}", ct))
            .StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        (await admin.PostAsync($"/api/v1/envios/masivo/{inexistente:D}/reintentar", new { }, ct))
            .StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);

        (await AyudaEnviosMasivo.TotalEnviosAsync(bd, ct))
            .Should().BeGreaterThan(0, "la base compartida ya tiene envíos de otras pruebas");
    }

    /// <summary>
    /// El reintento reutiliza la copia del intento anterior: es el mismo archivo el que se manda dos
    /// veces, y no una regeneración que cambiaría los bytes de lo que ya se probó.
    /// </summary>
    [Fact]
    public async Task ElReintentoMandaLaMismaCopiaDelIntentoAnterior()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, usuario) = await AyudaEnviosMasivo.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        api.Cola.Vaciar();
        var loteId = await AyudaEnviosMasivo.ConfirmarAsync(
            admin, AyudaEnviosMasivo.Peticion(escenario.ClienteId), ct);

        var envioId = api.Cola.Encolados[0];

        api.Doble.Fallar = new InvalidOperationException("Cayó a mitad.");
        await AyudaEnviosMasivo.EjecutarTrabajoAsync(api, envioId, usuario, ct);
        api.Doble.Restablecer();

        api.Cola.Vaciar();
        await AyudaEnviosMasivo.ReintentarAsync(admin, loteId, ct);
        await AyudaEnviosMasivo.EjecutarTrabajoAsync(api, api.Cola.Encolados[0], usuario, ct);

        var fila = (await AyudaEnviosMasivo.FilasDelLoteAsync(bd, loteId, ct)).Should().ContainSingle().Which;
        fila.Estado.Should().Be(EstadoEnvio.Enviado);

        // El PDF que sale es el que compuso el primer intento, que es el que quedó guardado.
        var referencia = await AyudaEnvios.PdfDeFacturaAsync(admin, fila.DocumentoId, ct);
        fila.PdfCopia.Should().Equal(api.Doble.Ultimo!.Adjunto, "lo que salió ahora es lo que se guardó");
        fila.PdfCopia.Should().NotBeEmpty();
        AyudaEnviosMasivo.EsPdf(referencia).Should().BeTrue("el papel de la factura es un PDF");
    }
}