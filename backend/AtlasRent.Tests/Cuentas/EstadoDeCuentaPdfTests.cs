using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Plantillas;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AtlasRent.Tests.Cuentas;

/// <summary>
/// F4-01: el estado de cuenta del cliente en PDF, con el diseño de <c>docs/DESIGN.md §7</c> (§3.14).
/// <para>
/// Lo que el PDF <b>dice</b> se comprueba sobre el modelo (<see cref="DocumentoEstadoCuentaPdfModelo"/>),
/// no sobre el archivo: el texto de un PDF de QuestPDF no se puede leer del archivo. Del archivo solo se
/// comprueba lo estructural.
/// </para>
/// <para>
/// <b>La prueba que importa es la del cuadre</b>: el estado de cuenta lleva las filas de la vista única
/// (§3.13) y sus totales tienen que ser los mismos que devuelve <c>GET /api/v1/cuentas</c> para ese
/// cliente. §3.15 lo exige —«los informes deben cuadrar con la vista única»— y un informe con su propia
/// cuenta de saldos es exactamente cómo se rompe.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class EstadoDeCuentaPdfTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    /// <summary>
    /// El archivo que devuelve la API tiene que ser un PDF de verdad y ser reproducible: si dos
    /// descargas del mismo estado de cuenta dieran bytes distintos, no se podría archivar ni comparar.
    /// </summary>
    [Fact]
    public async Task ElEstadoDeCuentaEsValidoYDeterministico()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuarioAdmin = await AyudaAuth.CrearUsuarioAsync(
            bd, AyudaFacturas.Contrasena, rol: Roles.Admin, ct: ct);
        using var admin = await AyudaPermisos.SesionAsync(api, usuarioAdmin, AyudaFacturas.Contrasena, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        var primera = await admin.GetAsync(
            $"/api/v1/clientes/{escenario.ClienteId:D}/estado-de-cuenta.pdf", ct);
        primera.StatusCode.Should().Be(HttpStatusCode.OK);
        primera.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");

        var bytes = await primera.Content.ReadAsByteArrayAsync(ct);
        Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 5))
            .Should().Be("%PDF-", "un PDF de verdad empieza así");

        var segunda = await admin.GetAsync(
            $"/api/v1/clientes/{escenario.ClienteId:D}/estado-de-cuenta.pdf", ct);
        segunda.StatusCode.Should().Be(HttpStatusCode.OK);

        // Solo se normalizan las fechas de generación: el resto del archivo tiene que salir igual.
        AyudaPlantilla.SinFechaDeGeneracion(bytes)
            .Should()
            .Equal(AyudaPlantilla.SinFechaDeGeneracion(await segunda.Content.ReadAsByteArrayAsync(ct)));

        primera.Content.Headers.ContentDisposition?.FileName
            .Should().Contain($"estado-de-cuenta-{escenario.ClienteId:D}");
    }

    /// <summary>
    /// Las filas y los totales del estado de cuenta, y el cuadre con la vista única: las mismas filas
    /// que devuelve <c>GET /api/v1/cuentas</c> para ese cliente y los mismos totales por moneda.
    /// </summary>
    [Fact]
    public async Task ElEstadoDeCuentaImprimeLasFilasYSusTotalesCuadranConLaVistaUnica()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuarioAdmin = await AyudaAuth.CrearUsuarioAsync(
            bd, AyudaFacturas.Contrasena, rol: Roles.Admin, ct: ct);
        using var admin = await AyudaPermisos.SesionAsync(api, usuarioAdmin, AyudaFacturas.Contrasena, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        var pagada = await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(-5), 500m, ct);

        // Un pago parcial sobre la primera: la deja en «parcial» con 200 dólares de pendiente.
        await AyudaCuentas.CobrarAsync(admin, escenario, pagada, 100m, escenario.Hoy, ct);

        // La vista única, tal y como la ve la pantalla.
        var vista = await AyudaCuentas.VistaAsync(admin, $"clienteId={escenario.ClienteId:D}", ct);
        var filas = AyudaCuentas.Filas(vista);
        filas.Should().HaveCount(2, "el cliente tiene dos facturas emitidas");

        var modelo = await ModeloAsync(escenario.ClienteId, usuarioAdmin.Id, ct);

        // Las filas del papel son las de la vista, las mismas y en el mismo orden.
        modelo.Filas.Should().HaveCount(filas.Length);
        modelo.Periodo.Should().Be("Todos los años");
        modelo.Aviso.Should().BeNull("caben todas las filas: no hay nada que avisar");
        modelo.Cliente.Nombre.Should().NotBeNullOrWhiteSpace();
        modelo.Cliente.Documento.Should().NotBeNullOrWhiteSpace("el cliente va con su documento");

        foreach (var (fila, esperada) in modelo.Filas.Zip(filas))
        {
            fila.Documento.Should().NotBeNullOrWhiteSpace();
            fila.Moneda.Should().Be(esperada.GetProperty("moneda").GetString());
            fila.Total.Should().Be(Importe(esperada, "total", Moneda.USD));
            fila.Cobrado.Should().Be(Importe(esperada, "cobrado", Moneda.USD));
            fila.Pendiente.Should().Be(Importe(esperada, "pendiente", Moneda.USD));
        }

        // Y los totales por moneda son los de la vista, sin recalcular nada.
        var totalesApi = vista.GetProperty("totales").EnumerateArray()
            .Single(t => t.GetProperty("moneda").GetString() == "USD");

        var total = modelo.Totales.Should().ContainSingle(t => t.Moneda == "USD").Which;
        total.Total.Should().Be(Importe(totalesApi, "total", Moneda.USD));
        total.Cobrado.Should().Be(Importe(totalesApi, "cobrado", Moneda.USD));
        total.Pendiente.Should().Be(Importe(totalesApi, "pendiente", Moneda.USD));
        total.Retenido.Should().Be(Importe(totalesApi, "retencion", Moneda.USD));

        // Lo vencido es la parte del pendiente de las filas que la vista marcó como vencidas: la misma
        // cuenta que hace una persona al pie de la tabla, hecha con los números de la vista.
        var vencidoDeLaVista = filas
            .Where(f => f.GetProperty("vencida").ValueKind != JsonValueKind.Null && f.GetProperty("vencida").GetBoolean())
            .Sum(f => f.GetProperty("pendiente").LeerDecimal());

        total.Vencido.Should().Be(FormatoDocumentoPdf.Importe(vencidoDeLaVista, Moneda.USD));
        vencidoDeLaVista.Should().Be(500m, "la factura vencida sigue debiendo 500 dólares");
    }

    /// <summary>
    /// El estado de cuenta lleva totales agregados de dinero de un cliente, así que es de gerente y
    /// administrador: el empleado recibe un 403 y el intento queda auditado. Es la regla de AGENTS.md §6
    /// y el caso es obligatorio, no opcional.
    /// </summary>
    [Fact]
    public async Task ElEmpleadoNoRecibeElEstadoDeCuentaYElGerenteYElAdministradorSi()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuarioAdmin = await AyudaAuth.CrearUsuarioAsync(
            bd, AyudaFacturas.Contrasena, rol: Roles.Admin, ct: ct);
        var usuarioGerente = await AyudaAuth.CrearUsuarioAsync(
            bd, AyudaFacturas.Contrasena, rol: Roles.Gerente, ct: ct);
        var usuarioEmpleado = await AyudaAuth.CrearUsuarioAsync(
            bd, AyudaFacturas.Contrasena, rol: Roles.Empleado, ct: ct);

        using var admin = await AyudaPermisos.SesionAsync(api, usuarioAdmin, AyudaFacturas.Contrasena, ct);
        using var gerente = await AyudaPermisos.SesionAsync(api, usuarioGerente, AyudaFacturas.Contrasena, ct);
        using var empleado = await AyudaPermisos.SesionAsync(api, usuarioEmpleado, AyudaFacturas.Contrasena, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        var ruta = $"/api/v1/clientes/{escenario.ClienteId:D}/estado-de-cuenta.pdf";

        var denegado = await empleado.GetAsync(ruta, ct);
        denegado.StatusCode.Should().Be(HttpStatusCode.Forbidden, await denegado.Cuerpo());

        (await gerente.GetAsync(ruta, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync(ruta, ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var sinRls = bd.CrearContexto(bd.CadenaSuperusuario);
        var denegadas = await sinRls.Auditoria.AsNoTracking()
            .Where(a => a.Accion == $"permiso.denegado.{Acciones.InformeVer}" && a.UsuarioId == usuarioEmpleado.Id)
            .ToListAsync(ct);

        denegadas.Should().NotBeEmpty("el intento denegado queda en la auditoría firmada");
        denegadas.Should().OnlyContain(a => a.Resultado == "denegado" && a.Ip != null);
    }

    // ---------------------------------------------------------------- ayudas

    /// <summary>
    /// El modelo tal y como lo monta el servicio. Se pide al servicio y no a mano porque lo que se
    /// comprueba es que el papel dice lo que dice la vista, y eso solo se sabe si las dos cosas salen
    /// del mismo camino.
    /// </summary>
    private async Task<DocumentoEstadoCuentaPdfModelo> ModeloAsync(
        Guid clienteId,
        Guid usuario,
        CancellationToken ct)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var cuentas = scope.ServiceProvider.GetRequiredService<ServicioCuentas>();

        return await cuentas.ModeloEstadoCuentaPdfAsync(clienteId, anio: null, usuario, ct);
    }

    /// <summary>
    /// El importe de una fila de la vista con el formato del documento (§5), para comparar el papel con
    /// lo que dice la pantalla sin cambiar la regla del número.
    /// </summary>
    private static string? Importe(JsonElement fila, string columna, Moneda moneda) =>
        fila.GetProperty(columna).ValueKind == JsonValueKind.Null
            ? null
            : FormatoDocumentoPdf.Importe(fila.GetProperty(columna).LeerDecimal(), moneda);
}
