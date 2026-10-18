using System.Net;
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
/// F4-03: la pantalla de resumen del envío masivo (docs/ESPECIFICACION.md §3.14: «cuántos, excluidos
/// con motivo, vista previa»).
/// <para>
/// Lo que se comprueba aquí es que preparar <b>cuenta bien y no manda nada</b>. Un resumen que cuelgue
/// de lo que se mandó es un resumen sin valor, y uno que mande algo al prepararlo es un botón con
/// «confirmar» en otro sitio.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class EnvioMasivoResumenTests(ContenedorBdFixture bd, ApiTestFactoryMasivo api)
    : IClassFixture<ApiTestFactoryMasivo>
{
    /// <summary>
    /// Cada exclusión con su motivo: la ya enviada, la que no tiene a quién mandarle, la anulada y la
    /// fianza, que no es un documento. Y ninguna cuenta como enviada.
    /// </summary>
    [Fact]
    public async Task ElResumenCuentaLosDocumentosYExcluyeLosYaEnviadosConMotivo()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaEnviosMasivo.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);

        var enviada = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);
        var pendiente = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(11), 400m, ct);
        var anulada = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(12), 500m, ct);
        await AyudaEnviosMasivo.AnularAsync(admin, anulada, ct);
        await AyudaCuentas.FianzaAsync(admin, escenario, 600m, ct);

        // El doble de correo es único por instancia de la fábrica y lo comparten las pruebas de la
        // clase, así que el punto de partida se cuenta antes y no se supone en cero.
        var correosAntes = api.Doble.Enviados.Count;

        // La primera factura ya salió a ese cliente, así que el lote no la vuelve a coger.
        await AyudaEnvios.EnviarAsync(
            admin,
            AyudaEnvios.PeticionFactura(enviada, AyudaEnvios.CorreoEjemplo),
            ct);

        var resumen = await AyudaEnviosMasivo.PrepararAsync(
            admin, AyudaEnviosMasivo.Peticion(escenario.ClienteId), ct);

        // Las que entran son las que se pueden mandar: la pendiente y la ya enviada... no. Solo la
        // pendiente, y ni la fianza ni la anulada.
        resumen.GetProperty("total").GetInt32().Should().Be(1);
        resumen.GetProperty("totalExcluidos").GetInt32().Should().Be(3);

        var documento = AyudaEnviosMasivo.Lista(resumen, "documentos").Should().ContainSingle().Which;
        documento.GetProperty("documentoId").GetGuid().Should().Be(pendiente);
        documento.GetProperty("destinatarioEmail").GetString().Should().Be(AyudaEnvios.CorreoEjemplo);

        AyudaEnviosMasivo.ExcluidoDe(resumen, enviada)
            .GetProperty("motivo").GetString().Should().Be(nameof(MotivoExclusionEnvio.YaEnviado));
        AyudaEnviosMasivo.ExcluidoDe(resumen, anulada)
            .GetProperty("motivo").GetString().Should().Be(nameof(MotivoExclusionEnvio.FacturaAnulada));

        var fianza = AyudaEnviosMasivo.ExcluidoDe(resumen, await FanzaDelEscenarioAsync(escenario, ct));
        fianza.GetProperty("motivo").GetString().Should().Be(nameof(MotivoExclusionEnvio.NoGeneraDocumento));
        fianza.GetProperty("tipo").GetString().Should().Be("Fianza", "es una fianza, no una factura");

        // Ninguna exclusión ha creado un envío, y la que ya se había mandado sigue sin tocarse.
        (await AyudaEnviosMasivo.FilasDelDocumentoAsync(bd, enviada, ct)).Should().HaveCount(1);
        (await AyudaEnviosMasivo.FilasDelDocumentoAsync(bd, pendiente, ct)).Should().BeEmpty();
        (await AyudaEnviosMasivo.FilasDelDocumentoAsync(bd, anulada, ct)).Should().BeEmpty();

        api.Doble.Enviados.Should().HaveCount(correosAntes + 1, "lo único que salió es la factura mandada antes");
        api.Doble.Ultimo!.DestinatarioEmail.Should().Be(AyudaEnvios.CorreoEjemplo);
    }

    /// <summary>
    /// Un cliente sin casilla en ningún sitio no tiene a quién mandarle, y el motivo lo dice. Es la
    /// exclusión que evita que un lote se quede «pendiente de poner correos» para siempre.
    /// </summary>
    [Fact]
    public async Task ElResumenExcluyeAlClienteQueNoTieneDondeMandarle()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaEnviosMasivo.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaId = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        await AyudaEnviosMasivo.QuitarCorreoAlClienteAsync(admin, escenario.ClienteId, ct);

        var resumen = await AyudaEnviosMasivo.PrepararAsync(
            admin, AyudaEnviosMasivo.Peticion(escenario.ClienteId), ct);

        resumen.GetProperty("total").GetInt32().Should().Be(0);
        resumen.GetProperty("totalExcluidos").GetInt32().Should().Be(1);

        var excluido = AyudaEnviosMasivo.ExcluidoDe(resumen, facturaId);
        excluido.GetProperty("motivo").GetString().Should().Be(nameof(MotivoExclusionEnvio.SinDireccionCorreo));
        excluido.GetProperty("detalle").GetString().Should().Contain("no hay a quién mandarle");
    }

    /// <summary>
    /// Preparar cuenta y enseña, y no manda ni escribe una fila. Es el criterio de §3.14: la
    /// pantalla de resumen es el punto en el que alguien lee lo que va a salir antes de que salga.
    /// </summary>
    [Fact]
    public async Task PrepararNoEnviaNada()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaEnviosMasivo.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        var antes = await AyudaEnviosMasivo.TotalEnviosAsync(bd, ct);
        var correosAntes = api.Doble.Enviados.Count;

        var resumen = await AyudaEnviosMasivo.PrepararAsync(
            admin, AyudaEnviosMasivo.Peticion(escenario.ClienteId), ct);

        // La vista previa trae el asunto y el cuerpo con las variables ya sustituidas: no es una
        // descripción del correo, es el correo.
        var documento = AyudaEnviosMasivo.Lista(resumen, "documentos").Should().ContainSingle().Which;
        var asunto = documento.GetProperty("asunto").GetString();
        var cuerpo = documento.GetProperty("cuerpo").GetString();

        asunto.Should().NotBeNullOrWhiteSpace().And.NotContain("{cliente}").And.NotContain("{numero}");
        cuerpo.Should().NotBeNullOrWhiteSpace().And.NotContain("{cliente}").And.NotContain("{numero}");
        cuerpo.Should().NotBeNullOrWhiteSpace().And.Contain("Le adjuntamos la factura");
        cuerpo.Should().NotBeNullOrWhiteSpace().And.NotContain("{total}").And.Contain("300", "el total va sustituido");
        documento.GetProperty("nombreArchivo").GetString().Should().EndWith(".pdf");

        // Y no se ha escrito ni mandado nada.
        (await AyudaEnviosMasivo.TotalEnviosAsync(bd, ct)).Should().Be(antes, "preparar no escribe filas");
        api.Doble.Enviados.Should().HaveCount(correosAntes, "preparar no llama al servidor de correo");
        api.Cola.Encolados.Should().BeEmpty("preparar no encola nada: eso es de confirmar");
        resumen.GetProperty("aviso").GetString().Should().Contain("no manda nada");
    }

    /// <summary>
    /// Sin duplicados: la factura ya enviada a ese destinatario queda fuera del lote con su motivo, y
    /// con <c>forzar</c> entra. El antidiplicado es el de §3.14 y aquí se ve en lote, no en un envío
    /// suelto.
    /// </summary>
    [Fact]
    public async Task SinDuplicados()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaEnviosMasivo.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaId = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        await AyudaEnvios.EnviarAsync(
            admin,
            AyudaEnvios.PeticionFactura(facturaId, AyudaEnvios.CorreoEjemplo),
            ct);

        var sinForzar = await AyudaEnviosMasivo.PrepararAsync(
            admin, AyudaEnviosMasivo.Peticion(escenario.ClienteId), ct);

        sinForzar.GetProperty("total").GetInt32().Should().Be(0);
        AyudaEnviosMasivo.ExcluidoDe(sinForzar, facturaId)
            .GetProperty("motivo").GetString().Should().Be(nameof(MotivoExclusionEnvio.YaEnviado));

        var forzando = await AyudaEnviosMasivo.PrepararAsync(
            admin, AyudaEnviosMasivo.Peticion(escenario.ClienteId, forzar: true), ct);

        forzando.GetProperty("total").GetInt32().Should().Be(1, "con «forzar» el reenvío entra");
        forzando.GetProperty("totalExcluidos").GetInt32().Should().Be(0);
    }

    /// <summary>
    /// Pedir un idioma sin plantilla no manda el correo en otro idioma sin avisar: en un lote eso
    /// serían cientos de clientes recibiendo un idioma que nadie pidió. Se excluye y se dice cuál
    /// falta.
    /// </summary>
    [Fact]
    public async Task ElResumenExcluyeCuandoNoHayPlantillaDelIdiomaPedido()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaEnviosMasivo.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaId = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        var resumen = await AyudaEnviosMasivo.PrepararAsync(
            admin, AyudaEnviosMasivo.Peticion(escenario.ClienteId, idioma: "fr"), ct);

        resumen.GetProperty("total").GetInt32().Should().Be(0);
        AyudaEnviosMasivo.ExcluidoDe(resumen, facturaId)
            .GetProperty("motivo").GetString().Should().Be(nameof(MotivoExclusionEnvio.SinPlantilla));

        // En «es», que sí está sembrado, entra.
        var enEspanol = await AyudaEnviosMasivo.PrepararAsync(
            admin, AyudaEnviosMasivo.Peticion(escenario.ClienteId), ct);

        enEspanol.GetProperty("total").GetInt32().Should().Be(1);
    }

    /// <summary>La fianza del escenario, leída de la vista para tener su identificador.</summary>
    private async Task<Guid> FanzaDelEscenarioAsync(
        AyudaCuentas.Escenario escenario,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        return await contexto.Fianza.AsNoTracking()
            .Where(f => f.ContratoId == escenario.ContratoId)
            .Select(f => f.Id)
            .SingleAsync(ct);
    }
}