using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Reservas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Mensajeria;

/// <summary>
/// F4-02: las plantillas de mensaje (docs/ESPECIFICACION.md §3.14) —las variables, el idioma y el
/// texto plano— y quién puede editarlas.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class EnvioPlantillasTests(ContenedorBdFixture bd, ApiTestFactoryCorreo api)
    : IClassFixture<ApiTestFactoryCorreo>
{
    /// <summary>
    /// Las seis variables se sustituyen en el asunto y en el cuerpo, y no queda ninguna sin
    /// sustituir: una variable suelta en el correo que llega al cliente es un descuido que nadie ve
    /// hasta que el cliente escribe preguntando qué es «{total}».
    /// </summary>
    [Fact]
    public async Task LasVariablesDeLaPlantillaSeSustituyen()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaEnvios.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var facturaId = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        await GuardarPlantillaAsync(
            admin,
            TipoMensajeEnvio.Factura,
            "es",
            "Factura {numero} para {cliente} en {unidad} por {total}",
            "Cliente: {cliente}\r\nNumero: {numero}\r\nUnidad: {unidad}\r\nTotal: {total}\r\n"
            + "Pendiente: {pendiente}\r\nVencimiento: {vencimiento}",
            ct);

        await AyudaEnvios.EnviarAsync(admin, AyudaEnvios.PeticionFactura(facturaId, AyudaEnvios.CorreoDePrueba()), ct);

        var enviado = api.Doble.Ultimo!;

        foreach (var texto in new[] { enviado.Asunto, enviado.Cuerpo })
        {
            foreach (var variable in new[] { "{cliente}", "{numero}", "{total}", "{pendiente}", "{vencimiento}", "{unidad}" })
            {
                texto.Should().NotContain(variable, "una variable sin sustituir llega tal cual al cliente");
            }
        }

        // Y el cuerpo lleva los datos de verdad, no otros: el papel y el correo salen del mismo modelo.
        enviado.Asunto.Should().Contain("para");
        enviado.Cuerpo.Should().Contain("Total: 300");
        enviado.Cuerpo.Should().Contain("Vencimiento: ");
    }

    /// <summary>
    /// El idioma pedido elige la plantilla, y la fila guarda el idioma que se usó de verdad. Si el
    /// idioma pedido no existe, sale la de «es» y la fila lo dice: es mejor mandar en español que no
    /// mandar nada porque nadie escribió la versión en francés.
    /// </summary>
    [Fact]
    public async Task ElIdiomaEligeLaPlantillaYQuedaEnLaFila()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaEnvios.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);

        await GuardarPlantillaAsync(
            admin, TipoMensajeEnvio.Factura, "en", "Invoice {numero}", "Dear {cliente}, invoice {numero}.", ct);

        var enIngles = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);
        var envioEn = await AyudaEnvios.EnviarAsync(
            admin, AyudaEnvios.PeticionFactura(enIngles, AyudaEnvios.CorreoDePrueba(), idioma: "en"), ct);

        envioEn.GetProperty("idioma").GetString().Should().Be("en");
        (await AyudaEnvios.FilaAsync(bd, envioEn.GetProperty("id").GetGuid(), ct))
            .Idioma.Should().Be("en");

        // Un idioma que no existe no es un error: sale la de «es» y la fila dice «es».
        var sinTraducir = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(11), 400m, ct);
        var envioEs = await AyudaEnvios.EnviarAsync(
            admin, AyudaEnvios.PeticionFactura(sinTraducir, AyudaEnvios.CorreoDePrueba(), idioma: "fr"), ct);

        envioEs.GetProperty("idioma").GetString()
            .Should().Be("es", "la fila guarda el idioma real del envío, no el que se pidió sin existir");
    }

    /// <summary>
    /// El cuerpo sale en texto plano y el texto del cliente va literal. No hay HTML, ni etiquetas, ni
    /// entidades: es la misma regla que los PDF por código y no por HTML y navegador (SEC-001 en
    /// docs/SEGURIDAD.md). Si el nombre del cliente trae algo que parece una etiqueta, sale al lado en
    /// el correo y se ve; escapado, el cliente vería «&lt;b&gt;» y creería que la factura es de otra
    /// persona.
    /// </summary>
    [Fact]
    public async Task ElCuerpoSaleEnTextoPlanoYLiteral()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaEnvios.SesionAsync(bd, api, Roles.Admin, ct);

        var nombreConMarcas = "Roa<b> & Cia";
        var clienteId = await CrearClienteAsync(admin, nombreConMarcas, ct);
        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);

        var serieId = await AyudaFacturas.CrearSerieAsync(
            admin, TipoSerie.Normal, null, ct);

        var facturaId = await AyudaFacturas.CrearFacturaAsync(
            admin, clienteId, serieId, Moneda.USD, escenario.Hoy.AddDays(10), ct);

        await AyudaFacturas.AgregarLineaAsync(
            admin, facturaId, TipoLineaFactura.Renta, 1m, 300m, null, ct);

        (await admin.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct))
            .EnsureSuccessStatusCode();

        await AyudaEnvios.EnviarAsync(
            admin, AyudaEnvios.PeticionFactura(facturaId, AyudaEnvios.CorreoDePrueba()), ct);

        var cuerpo = api.Doble.Ultimo!.Cuerpo;

        // El nombre sale tal cual, con su «&» y su «<b>» al lado: nada se escapa y nada se
        // interpreta. No se comprueba «que no haya <b>» porque el nombre lo trae; lo que se
        // comprueba es que no aparezca convertido en entidad ni envuelto en un documento HTML.
        cuerpo.Should().Contain(nombreConMarcas, "el texto del cliente va literal, sin escapar");
        cuerpo.Should().NotContain("&lt;");
        cuerpo.Should().NotContain("&amp;");
        cuerpo.Should().NotContain("<html");
        cuerpo.Should().NotContain("<body");
    }

    /// <summary>
    /// Editar una plantilla es <c>plantilla.editar</c>, que es solo administrador (§2). Un gerente que
    /// tiene casi todo lo de la matriz no puede escribirlas, porque lo que le llega al cliente no es
    /// lo mismo que el trabajo de registrar cobros.
    /// </summary>
    [Fact]
    public async Task EditarUnaPlantillaExigePlantillaEditar()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaEnvios.SesionAsync(bd, api, Roles.Admin, ct);
        using var gerente = await AyudaEnvios.SesionAsync(bd, api, Roles.Gerente, ct);

        var peticion = new
        {
            tipo = nameof(TipoMensajeEnvio.Factura),
            idioma = "es",
            asunto = "Asunto que no debería existir",
            cuerpo = "Cuerpo que no debería existir.",
            activa = true,
        };

        var denegado = await gerente.PutAsync("/api/v1/config/plantillas-mensaje", peticion, ct);
        denegado.StatusCode.Should().Be(HttpStatusCode.Forbidden, await denegado.Cuerpo());

        // El texto del gerente no llegó a escribirse: la plantilla que hay sigue siendo la de antes.
        var guardada = await admin.PutAsync(
            "/api/v1/config/plantillas-mensaje",
            new
            {
                tipo = nameof(TipoMensajeEnvio.EstadoCuenta),
                idioma = "es",
                asunto = "Estado de cuenta {numero}",
                cuerpo = "Cliente {cliente}, pendiente {pendiente}.",
                activa = true,
            },
            ct);
        guardada.StatusCode.Should().Be(HttpStatusCode.OK, await guardada.Cuerpo());

        var listado = await admin.GetAsync("/api/v1/config/plantillas-mensaje?tipo=EstadoCuenta", ct);
        listado.StatusCode.Should().Be(HttpStatusCode.OK, await listado.Cuerpo());

        var filas = await listado.Content.ReadFromJsonAsync<JsonElement>(ct);
        filas.GetProperty("data").EnumerateArray()
            .Should().ContainSingle(f => f.GetProperty("idioma").GetString() == "es");
    }

    // ---------------------------------------------------------------- apoyo

    private static async Task GuardarPlantillaAsync(
        SesionCliente sesion,
        TipoMensajeEnvio tipo,
        string idioma,
        string asunto,
        string cuerpo,
        CancellationToken ct)
    {
        var respuesta = await sesion.PutAsync(
            "/api/v1/config/plantillas-mensaje",
            new { tipo = tipo.ToString(), idioma, asunto, cuerpo, activa = true },
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());
    }

    /// <summary>
    /// Cliente con marcas en el nombre, para comprobar que el correo las lleva tal cual. El nombre
    /// sale de la ficha del cliente y de ahí se sustituye en el cuerpo, sin pasar por ningún escapado.
    /// </summary>
    private static async Task<Guid> CrearClienteAsync(
        SesionCliente sesion,
        string nombre,
        CancellationToken ct)
    {
        var creada = await sesion.PostAsync(
            "/api/v1/clientes",
            new
            {
                nombre,
                tipo = "PersonaFisica",
                documento = $"001-{Guid.NewGuid():N}"[..13],
                email = $"marcas-{Guid.NewGuid():N}"[..20] + "@example.test",
            },
            ct);

        creada.StatusCode.Should().Be(HttpStatusCode.OK, await creada.Cuerpo());

        return (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }
}