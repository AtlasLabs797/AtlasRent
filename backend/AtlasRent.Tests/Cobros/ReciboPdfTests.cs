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
using Xunit;

namespace AtlasRent.Tests.Cobros;

/// <summary>
/// F4-01: el recibo del cobro en PDF, con el diseño de <c>docs/DESIGN.md §7</c> (§3.10.1).
/// <para>
/// Lo que el PDF <b>dice</b> se comprueba sobre el modelo (<see cref="DocumentoReciboPdfModelo"/>), no
/// sobre el archivo: QuestPDF incrusta las fuentes con subconjunto y escribe los caracteres como índices
/// de glifo, así que el texto no se puede leer del PDF. Del archivo solo se comprueba lo estructural: que
/// es un PDF, que se sirve como tal y que dos generaciones dan los mismos bytes.
/// </para>
/// <para>
/// El identificador del recibo (<c>REC-XXXXXXXX</c>) sale del identificador del cobro porque un cobro no
/// tiene número y la especificación no le pone ninguno: lo que hace falta es que el mismo cobro
/// reimprimido diga lo mismo.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ReciboPdfTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    /// <summary>
    /// El archivo que devuelve la API tiene que ser un PDF de verdad y ser reproducible: si dos
    /// descargas del mismo cobro dieran bytes distintos, no se podría ni archivar ni comprobar. Lo único
    /// que cambia entre una y otra son las fechas de generación, que se normalizan.
    /// </summary>
    [Fact]
    public async Task ElReciboEsValidoYDeterministico()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await EscenarioAsync(admin, ct);
        var cobroId = await RegistrarCobroAsync(
            admin, escenario, 300m, Moneda.USD, ct, referencia: "TRANSFERENCIA-001");

        var primera = await admin.GetAsync($"/api/v1/cobros/{cobroId:D}/pdf", ct);
        primera.StatusCode.Should().Be(HttpStatusCode.OK);
        primera.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");

        var bytes = await primera.Content.ReadAsByteArrayAsync(ct);
        Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 5))
            .Should().Be("%PDF-", "un PDF de verdad empieza así");

        var segunda = await admin.GetAsync($"/api/v1/cobros/{cobroId:D}/pdf", ct);
        segunda.StatusCode.Should().Be(HttpStatusCode.OK);

        // Solo se normalizan las fechas de generación: el resto del archivo tiene que salir igual.
        AyudaPlantilla.SinFechaDeGeneracion(bytes)
            .Should()
            .Equal(AyudaPlantilla.SinFechaDeGeneracion(await segunda.Content.ReadAsByteArrayAsync(ct)));

        // El nombre del archivo lleva el identificador del cobro, no un número de serie.
        primera.Content.Headers.ContentDisposition?.FileName
            .Should().Contain($"recibo-{cobroId:D}");

        // Un cobro que no existe es un 404, no un PDF vacío.
        (await admin.GetAsync($"/api/v1/cobros/{Guid.NewGuid():D}/pdf", ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// El contenido del recibo: identificador, fecha, moneda e importe recibidos, cliente con su
    /// documento entero, datos del cobro y la tabla de asignaciones. Es la prueba que cubre lo que el
    /// papel dice, que es lo único que no se puede leer del PDF.
    /// </summary>
    [Fact]
    public async Task ElModeloDelReciboImprimeLosDatosDelCobro()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await EscenarioAsync(admin, ct);
        var cobroId = await RegistrarCobroAsync(
            admin, escenario, 300m, Moneda.USD, ct, referencia: "TRANSFERENCIA-002");

        var modelo = await ModeloAsync(cobroId, ct);

        // El identificador es estable y derivado del cobro: «REC-» y ocho caracteres en mayúsculas.
        modelo.Numero.Should().Be($"REC-{cobroId.ToString("N")[..8].ToUpperInvariant()}");
        modelo.Numero.Should().HaveLength(12);

        modelo.MonedaRecibida.Should().Be("USD");
        modelo.ImporteRecibido.Should().Be("300,00 USD");
        modelo.FechaEfectiva.Should().Be(escenario.Hoy.ToString("dd/MM/yyyy"));
        modelo.MarcaAgua.Should().BeNull("un cobro registrado no lleva marca de agua");

        // El cliente sale con su documento descifrado y entero (D16).
        modelo.Cliente.Nombre.Should().NotBeNullOrWhiteSpace();
        modelo.Cliente.Documento.Should().NotBeNullOrWhiteSpace("el documento del cliente va en el papel");
        modelo.Cliente.Documento.Should().NotStartWith("v1:", "el documento del papel va descifrado");

        // Los datos del cobro: forma de pago, cuenta receptora y referencia.
        modelo.Datos.Should().Contain(d => d.Etiqueta == "Forma de pago" && d.Valor.Length > 0);
        modelo.Datos.Should().Contain(d => d.Etiqueta == "Cuenta receptora" && d.Valor.Length > 0);
        modelo.Datos.Should().Contain(d => d.Etiqueta == "Referencia" && d.Valor == "TRANSFERENCIA-002");

        // La asignación dice a qué factura se aplicó y cuánto, en la moneda de esa factura.
        var asignacion = modelo.Asignaciones.Should().ContainSingle().Which;
        asignacion.Destino.Should().Contain("-", "el destino es el serie-número de la factura");
        asignacion.Importe.Should().Be("300,00 USD");
        asignacion.Moneda.Should().Be("USD");
        asignacion.Vigente.Should().BeTrue();
        asignacion.ImporteRecibido.Should().BeNull("sin conversión no hay segundo importe");
        modelo.ConConversion.Should().BeFalse();

        // Y el total aplicado, que es lo que el gerente quiere ver de un vistazo.
        modelo.Totales.Should().Contain(t => t.Etiqueta == "Total aplicado (USD)" && t.Valor == "300,00 USD");
    }

    /// <summary>
    /// El caso de §3.10.2: una factura de 1.000 dólares pagada con 59.500 pesos. El papel tiene que decir
    /// los dos importes y el cambio, que es lo único que explica el pago (§3.10.2, punto 4).
    /// </summary>
    [Fact]
    public async Task ElReciboDeUnCobroEnOtraMonedaImprimeElCambioYLosDosImportes()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await EscenarioAsync(admin, ct);

        // El cambio del día de la base compartida; al terminar se deshace (BUG-006).
        await using var limpieza = AyudaCobros.SinRastroDeCambio(bd, escenario.Hoy, Moneda.DOP);
        await AyudaCobros.CargarCambioAsync(
            admin, escenario.Hoy, Moneda.DOP, AyudaCobros.CambioDelDia, ct);

        var formaPagoId = await AyudaCobros.FormaPagoAsync(admin, ct);
        var cuentaPesos = await AyudaCobros.CuentaAsync(admin, Moneda.DOP, ct);

        // La deuda que se paga: 1.000 dólares. El cobro entra en pesos.
        await AyudaCobros.EmitidaAsync(
            admin, escenario.ClienteId, escenario.ContratoId, escenario.SerieId, Moneda.USD,
            escenario.Hoy.AddDays(10), 1_000m, ct);

        var registrado = await admin.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                escenario.ClienteId,
                escenario.ContratoId,
                escenario.Hoy,
                Moneda.DOP,
                59_500m,
                formaPagoId,
                cuentaPesos,
                tipoCambioAplicado: AyudaCobros.CambioExacto),
            ct);
        registrado.StatusCode.Should().Be(HttpStatusCode.OK, await registrado.Cuerpo());

        var cobroId = (await registrado.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var modelo = await ModeloAsync(cobroId, ct);

        // El documento es del dinero que entró: pesos.
        modelo.MonedaRecibida.Should().Be("DOP");
        modelo.ImporteRecibido.Should().Be("59.500,00 DOP");

        // El cambio del día va en los datos del cobro, con su fecha (D9).
        modelo.Datos.Should().Contain(d => d.Etiqueta.StartsWith("Tipo de cambio del ")
            && d.Valor == "60,123456");

        // Y la línea dice las dos monedas y el cambio aplicado (§3.10.2).
        modelo.ConConversion.Should().BeTrue();
        var asignacion = modelo.Asignaciones.Should().ContainSingle().Which;
        asignacion.Importe.Should().Be("1.000,00 USD");
        asignacion.ImporteRecibido.Should().Be("59.500,00 DOP");
        asignacion.Cambio.Should().Be("59,50");
        asignacion.Moneda.Should().Be("USD");

        // Los totales: lo aplicado en la moneda de la factura y lo recibido en la del cobro, sin
        // convertir nada entre ellas.
        modelo.Totales.Should().Contain(t => t.Etiqueta == "Total aplicado (USD)" && t.Valor == "1.000,00 USD");
        modelo.Totales.Should().Contain(t => t.Etiqueta == "Total recibido (DOP)" && t.Valor == "59.500,00 DOP");
    }

    /// <summary>
    /// Un cobro anulado se descarga marcado, y sus asignaciones salen con su marca: lo que deja de
    /// contar no se esconde (AGENTS.md §6, y es lo mismo que hace <c>GET /api/v1/cobros/{id}</c>).
    /// </summary>
    [Fact]
    public async Task ElReciboDeUnCobroAnuladoLlevaMarcaYSusAsignacionesAnuladasSalenConSuMarca()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await EscenarioAsync(admin, ct);
        var cobroId = await RegistrarCobroAsync(admin, escenario, 300m, Moneda.USD, ct);

        var anulado = await admin.PostAsync(
            $"/api/v1/cobros/{cobroId:D}/anular",
            new { motivo = "Se registró en el contrato equivocado" },
            ct);
        anulado.StatusCode.Should().Be(HttpStatusCode.OK, await anulado.Cuerpo());

        var modelo = await ModeloAsync(cobroId, ct);

        modelo.MarcaAgua.Should().Be("ANULADO");
        modelo.MotivoAnulacion.Should().Be("Se registró en el contrato equivocado");

        var asignacion = modelo.Asignaciones.Should().ContainSingle().Which;
        asignacion.Vigente.Should().BeFalse("anular el cobro desactiva sus asignaciones");
        asignacion.Destino.Should().Contain("anulada", "la fila que no cuenta sale con su marca");

        // Y los totales no suman lo que no cuenta.
        modelo.Totales.Should().BeEmpty("un cobro anulado no tiene total aplicado que valga");

        // El PDF de un cobro anulado también se descarga: es el papel que necesita el expediente.
        var descarga = await admin.GetAsync($"/api/v1/cobros/{cobroId:D}/pdf", ct);
        descarga.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// El identificador del recibo no cambia entre descargas: es el mismo cobro, y el papel que se
    /// archiva tiene que decir lo mismo la segunda vez que se imprime.
    /// </summary>
    [Fact]
    public async Task ElIdentificadorDelReciboEsEstableEntreDosDescargas()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await EscenarioAsync(admin, ct);
        var cobroId = await RegistrarCobroAsync(admin, escenario, 300m, Moneda.USD, ct);

        var primera = await ModeloAsync(cobroId, ct);
        var segunda = await ModeloAsync(cobroId, ct);

        primera.Numero.Should().Be(segunda.Numero);
        segunda.Numero.Should().Be($"REC-{cobroId.ToString("N")[..8].ToUpperInvariant()}");
    }

    /// <summary>
    /// El recibo es un documento individual: lleva <c>factura.ver</c>, que el empleado tiene. Lo que no
    /// puede es cuando a ese usuario se le recorta el permiso: entonces es un 403 y queda auditado
    /// (mismo criterio que <c>CobroPermisosTests</c>).
    /// </summary>
    [Fact]
    public async Task ElReciboLoPuedeDescargarQuienTieneFacturaVerYNoQuienSeLoHanRecortado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await EscenarioAsync(admin, ct);
        var cobroId = await RegistrarCobroAsync(admin, escenario, 300m, Moneda.USD, ct);

        // El empleado tiene factura.ver en el techo de su rol: puede ver el cobro y su papel.
        using var empleado = await AyudaCobros.SesionAsync(bd, api, Roles.Empleado, ct);
        (await empleado.GetAsync($"/api/v1/cobros/{cobroId:D}/pdf", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK, "el recibo es un documento individual");

        // A un gerente se le recorta factura.ver: los permisos se recortan, nunca se amplían. Los
        // recorta el administrador, que es quien puede tocar los permisos de otro usuario.
        var usuarioGerente = await AyudaAuth.CrearUsuarioAsync(
            bd, AyudaFacturas.Contrasena, rol: Roles.Gerente, ct: ct);
        using var gerente = await AyudaPermisos.SesionAsync(
            api, usuarioGerente, AyudaFacturas.Contrasena, ct);

        (await admin.PutAsync(
            $"/api/v1/usuarios/{usuarioGerente.Id:D}/permisos",
            new { accion = Acciones.FacturaVer, permitido = false },
            ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var denegado = await gerente.GetAsync($"/api/v1/cobros/{cobroId:D}/pdf", ct);
        denegado.StatusCode.Should().Be(HttpStatusCode.Forbidden, await denegado.Cuerpo());

        await using var sinRls = bd.CrearContexto(bd.CadenaSuperusuario);
        var denegadas = await sinRls.Auditoria.AsNoTracking()
            .Where(a => a.Accion == $"permiso.denegado.{Acciones.FacturaVer}" && a.UsuarioId == usuarioGerente.Id)
            .ToListAsync(ct);

        denegadas.Should().NotBeEmpty("el intento denegado queda en la auditoría firmada");
        denegadas.Should().OnlyContain(a => a.Resultado == "denegado" && a.Ip != null);
    }

    // ---------------------------------------------------------------- ayudas

    /// <summary>
    /// Cliente, contrato, serie y día: las piezas de partida, las mismas que usa el resto de pruebas de
    /// cobros.
    /// </summary>
    private async Task<Escenario> EscenarioAsync(SesionCliente sesion, CancellationToken ct)
    {
        var (clienteId, contratoId) = await AyudaCobros.ContratoAsync(sesion, bd, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        return new Escenario(clienteId, contratoId, serieId, hoy);
    }

    /// <summary>
    /// Factura emitida y cobro registrado contra ella, con la referencia que le pase la prueba.
    /// </summary>
    private async Task<Guid> RegistrarCobroAsync(
        SesionCliente sesion,
        Escenario escenario,
        decimal importe,
        Moneda moneda,
        CancellationToken ct,
        string? referencia = null)
    {
        await AyudaCobros.EmitidaAsync(
            sesion, escenario.ClienteId, escenario.ContratoId, escenario.SerieId, moneda,
            escenario.Hoy.AddDays(10), importe, ct);

        var formaPagoId = await AyudaCobros.FormaPagoAsync(sesion, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(sesion, moneda, ct);

        var registrado = await sesion.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                escenario.ClienteId,
                escenario.ContratoId,
                escenario.Hoy,
                moneda,
                importe,
                formaPagoId,
                cuentaId,
                referencia: referencia),
            ct);
        registrado.StatusCode.Should().Be(HttpStatusCode.OK, await registrado.Cuerpo());

        return (await registrado.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// El modelo tal y como lo monta el servicio, con los mismos datos que carga: cliente, contrato con
    /// su facturadora, forma de pago, cuenta receptora y asignaciones.
    /// </summary>
    private async Task<DocumentoReciboPdfModelo> ModeloAsync(Guid cobroId, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        var cobro = await contexto.Cobro.AsNoTracking()
            .Include(c => c.Cliente)
            .Include(c => c.Contrato)
            .ThenInclude(c => c!.Facturadora)
            .Include(c => c.Contrato)
            .ThenInclude(c => c!.Unidad)
            .Include(c => c.FormaPago)
            .Include(c => c.CuentaReceptora)
            .Include(c => c.Asignaciones)
            .ThenInclude(a => a.Factura)
            .ThenInclude(f => f!.Serie)
            .Include(c => c.Asignaciones)
            .ThenInclude(a => a.Fianza)
            .SingleAsync(c => c.Id == cobroId, ct);

        // El RNC va cifrado en la fila, así que el compositor lo recibe descifrado. Es el mismo cambio en
        // memoria que hace `ServicioCobros` sobre la entidad sin seguimiento.
        var facturadora = cobro.Contrato?.Facturadora;
        if (facturadora is not null && facturadora.Rnc is { } rnc)
        {
            facturadora.Rnc = Descifrar(rnc);
        }

        return DocumentoReciboPdf.Modelo(
            cobro,
            RegistroPlantillasPdf.Resolver(nombre: null),
            facturadora,
            Descifrar(cobro.Cliente?.Documento));
    }

    /// <summary>
    /// El descifrado con la clave de esta instancia de pruebas, el mismo camino que usa la API.
    /// </summary>
    private string? Descifrar(string? documentoCifrado) =>
        documentoCifrado is null
            ? null
            : new ServicioCifrado(new OpcionesSeguridad
            {
                ClaveJwt = [],
                ClaveCifrado = ApiTestFactory.ClaveCifrado,
                ClaveHmacDocumento = api.ClaveHmacDocumento,
            }).Descifrar(documentoCifrado);

    private sealed record Escenario(Guid ClienteId, Guid ContratoId, Guid SerieId, DateOnly Hoy);
}
