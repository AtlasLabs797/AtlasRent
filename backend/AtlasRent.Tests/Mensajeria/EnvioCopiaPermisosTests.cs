using System.Net.Http.Json;
using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Mensajeria;

/// <summary>
/// El permiso de la copia del PDF enviado lo decide <b>el contenido del adjunto</b>, no solo el tipo
/// de documento ni el permiso de envío (AGENTS.md §6, docs/ESPECIFICACION.md §2).
/// <para>
/// El defecto era este: <c>GET /api/v1/envios/{id}/pdf</c> se leía con <c>factura.ver</c> y el
/// comentario lo justificaba con «quien puede ver una factura puede ver la copia que se le mandó».
/// Eso es cierto para una factura y falso para un estado de cuenta, porque <b>la copia de un envío de
/// tipo <c>EstadoCuenta</c> es el estado de cuenta</b>: filas, totales por moneda y saldo pendiente.
/// Como el empleado sí tiene <c>factura.ver</c> (§2), un gerente podía mandar el archivo —o un
/// recordatorio lo mandaba solo con <c>documento.enviar</c>— y el empleado lo descargaba con un
/// permiso de informe que nunca le dio nadie.
/// </para>
/// <para>
/// Y al revés también: que el contenido exija <c>informe.ver</c> es lo que deja de hacer que un
/// permiso de envío sea, sin querer, un permiso de informe.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class EnvioCopiaPermisosTests(ContenedorBdFixture bd, ApiTestFactoryCorreo api)
    : IClassFixture<ApiTestFactoryCorreo>
{
    /// <summary>
    /// La prueba del defecto: un empleado recibe 403 al descargar la copia de un estado de cuenta,
    /// aunque el envío lo haya hecho un administrador y aunque lo pueda ver en la lista.
    /// <para>
    /// Antes de BUG-014 esto devolvía 200 con los bytes enteros del estado de cuenta, que es un
    /// informe con todos los totales del cliente.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ElEmpleadoNoDescargaLaCopiaDeUnEstadoDeCuenta()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaEnvios.SesionAsync(bd, api, Roles.Admin, ct);
        using var empleado = await AyudaEnvios.SesionAsync(bd, api, Roles.Empleado, ct);

        var escenario = await EscenarioConFacturaAsync(admin, ct);
        var envio = await EnviarEstadoCuentaAsync(admin, escenario, ct);

        // Lo que sí puede hacer el empleado con ese envío: verlo en la lista. Eso es metadato —a quién,
        // cuándo y en qué estado— y este cambio no lo toca; lo que no puede es bajarse el archivo.
        var lista = await empleado.GetAsync($"/api/v1/envios?documentoId={escenario.ClienteId:D}", ct);
        lista.StatusCode.Should().Be(HttpStatusCode.OK, await lista.Cuerpo());
        (await lista.Content.ReadAsStringAsync(ct))
            .Should().Contain(envio.ToString("D"), "el envío se ve en la lista: es lo que el empleado recibe");

        var descarga = await empleado.GetAsync($"/api/v1/envios/{envio:D}/pdf", ct);

        descarga.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            $"un estado de cuenta lleva totales agregados y es de informe.ver, no de factura.ver: {await descarga.Cuerpo()}");

        // Y el archivo que se le niega es el mismo que sí se puede leer con el permiso correcto: el
        // 403 no viene de un envío vacío ni de un adjunto que no se compusiera.
        (await admin.GetAsync($"/api/v1/envios/{envio:D}/pdf", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK, "el 403 es por el permiso, no por el archivo");
    }

    /// <summary>
    /// El mismo hueco por el otro lado: mandar un estado de cuenta exige <c>informe.ver</c>, no solo
    /// el permiso de envío del documento.
    /// <para>
    /// Al empleado le negan las dos cosas, porque no trae <c>documento.enviar</c> (§2 solo se lo da a
    /// gerente y administrador). La segunda comprobación no es la que salta en ese caso, y por eso
    /// <b>tampoco se puede probar con un rol</b>: los dos roles que pueden mandar un estado de cuenta
    /// traen también <c>informe.ver</c>. Lo que protege es el recorte por usuario
    /// (<c>usuario_permiso</c>, D17): un gerente al que le apaguen <c>informe.ver</c> puede seguir
    /// mandando facturas y deja de poder mandar ni reenviar un estado de cuenta.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ElEmpleadoTampocoEnviaUnEstadoDeCuenta()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaEnvios.SesionAsync(bd, api, Roles.Admin, ct);
        using var empleado = await AyudaEnvios.SesionAsync(bd, api, Roles.Empleado, ct);

        var escenario = await EscenarioConFacturaAsync(admin, ct);

        var denegado = await empleado.PostAsync(
            "/api/v1/envios",
            AyudaEnvios.PeticionEstadoCuenta(
                escenario.ClienteId, escenario.Hoy.Year, AyudaEnvios.CorreoDePrueba()),
            ct);

        denegado.StatusCode.Should().Be(HttpStatusCode.Forbidden, await denegado.Cuerpo());
        (await AyudaEnvios.UltimoEnvioAsync(bd, escenario.ClienteId, ct))
            .Should().BeNull("un permiso denegado no llega a mandar nada ni a escribir una fila");
    }

    /// <summary>
    /// La copia de una factura <b>sí</b> se sigue descargando con <c>factura.ver</c>: el contenido es
    /// un documento individual y no lleva totales agregados, así que el criterio nuevo no lo castiga.
    /// <para>
    /// Si esto pasara a 403, el arreglo de BUG-014 habría roto el caso de uso legítimo de F4-02 —
    ///«verificamos que al cliente le llegó tal cual el papel que le mandamos»— para todo el que no
    /// tenga <c>informe.ver</c>. No es el caso y por eso el test sigue exigiendo 200.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ElEmpleadoSiguePudiendoDescargarLaCopiaDeUnaFactura()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaEnvios.SesionAsync(bd, api, Roles.Admin, ct);
        using var empleado = await AyudaEnvios.SesionAsync(bd, api, Roles.Empleado, ct);

        var escenario = await EscenarioConFacturaAsync(admin, ct);
        var envio = await AyudaEnvios.EnviarAsync(
            admin,
            AyudaEnvios.PeticionFactura(await FacturaAsync(admin, escenario, ct), AyudaEnvios.CorreoDePrueba()),
            ct);

        var descarga = await empleado.GetAsync($"/api/v1/envios/{envio.GetProperty("id").GetGuid():D}/pdf", ct);

        descarga.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "una factura es un documento individual y se lee con factura.ver: " + await descarga.Cuerpo());
        (await descarga.Content.ReadAsByteArrayAsync(ct))
            .Take(5).Should().Equal("%PDF-"u8.ToArray());
    }

    /// <summary>
    /// Gerente y administrador sí descargan la copia del estado de cuenta: los dos tienen
    /// <c>informe.ver</c> (§2) y son los únicos que pueden verlo por <c>GET</c> o mandarlo.
    /// </summary>
    [Fact]
    public async Task GerenteYAdministradorSiDescarganLaCopiaDelEstadoDeCuenta()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaEnvios.SesionAsync(bd, api, Roles.Admin, ct);
        using var gerente = await AyudaEnvios.SesionAsync(bd, api, Roles.Gerente, ct);

        var escenario = await EscenarioConFacturaAsync(admin, ct);
        var envio = await EnviarEstadoCuentaAsync(admin, escenario, ct);

        foreach (var sesion in new[] { admin, gerente })
        {
            var descarga = await sesion.GetAsync($"/api/v1/envios/{envio:D}/pdf", ct);

            descarga.StatusCode.Should().Be(HttpStatusCode.OK, await descarga.Cuerpo());
            (await descarga.Content.ReadAsByteArrayAsync(ct))
                .Take(5).Should().Equal("%PDF-"u8.ToArray(), "es el estado de cuenta, no una respuesta de error");
        }
    }

    // ---------------------------------------------------------------- apoyo

    private async Task<AyudaCuentas.Escenario> EscenarioConFacturaAsync(
        SesionCliente admin,
        CancellationToken ct)
    {
        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await FacturaAsync(admin, escenario, ct);

        return escenario;
    }

    /// <summary>Una factura emitida del escenario, que es lo que llena el estado de cuenta.</summary>
    private async Task<Guid> FacturaAsync(
        SesionCliente admin,
        AyudaCuentas.Escenario escenario,
        CancellationToken ct) =>
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

    /// <summary>
    /// El envío del estado de cuenta, hecho con un administrador. El archivo se compone contra el
    /// permiso del que lo manda, que es el camino real del defecto: el archivo existe porque alguien
    /// con <c>informe.ver</c> lo generó, no porque el que lo descarga lo tenga.
    /// </summary>
    private async Task<Guid> EnviarEstadoCuentaAsync(
        SesionCliente admin,
        AyudaCuentas.Escenario escenario,
        CancellationToken ct)
    {
        var envio = await AyudaEnvios.EnviarAsync(
            admin,
            AyudaEnvios.PeticionEstadoCuenta(
                escenario.ClienteId, escenario.Hoy.Year, AyudaEnvios.CorreoDePrueba()),
            ct);

        return envio.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// SEC-030: el asunto de un estado de cuenta puede llevar el saldo pendiente del cliente entero
    /// (un total agregado). Sin <c>informe.ver</c> la fila sale en la lista, pero sin asunto.
    /// </summary>
    [Fact]
    public async Task ElEmpleadoNoVeElAsuntoDeUnEstadoDeCuentaEnLaLista()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaEnvios.SesionAsync(bd, api, Roles.Admin, ct);
        using var empleado = await AyudaEnvios.SesionAsync(bd, api, Roles.Empleado, ct);

        var escenario = await EscenarioConFacturaAsync(admin, ct);
        var envio = await EnviarEstadoCuentaAsync(admin, escenario, ct);

        string AsuntoDe(System.Text.Json.JsonElement lista) => lista.GetProperty("data").EnumerateArray()
            .Single(f => f.GetProperty("id").GetGuid() == envio).GetProperty("asunto").GetString()!;

        var url = $"/api/v1/envios?documentoId={escenario.ClienteId:D}";
        var comoAdmin = await (await admin.GetAsync(url, ct)).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
        var comoEmpleado = await (await empleado.GetAsync(url, ct)).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);

        AsuntoDe(comoAdmin).Should().NotBeNullOrWhiteSpace("quien tiene informe.ver ve el asunto");
        AsuntoDe(comoEmpleado).Should().BeEmpty("el empleado no recibe lo que puede llevar totales agregados");
    }
}
