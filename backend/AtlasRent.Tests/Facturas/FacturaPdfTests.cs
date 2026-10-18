using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Cobros;
using AtlasRent.Tests.Comprobantes;
using AtlasRent.Tests.Contratos;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Plantillas;
using AtlasRent.Tests.Reservas;
using AtlasRent.Tests.Retenciones;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Facturas;

/// <summary>
/// F4-01: el PDF de la factura y de la proforma con el diseño de <c>docs/DESIGN.md §7</c>.
/// <para>
/// Lo que el PDF <b>dice</b> se comprueba sobre el modelo (<see cref="DocumentoFacturaPdfModelo"/>),
/// no sobre el archivo: QuestPDF incrusta las fuentes con subconjunto y escribe los caracteres como
/// índices de glifo, así que el texto no se puede leer del PDF. Del archivo solo se comprueba lo
/// estructural: que es un PDF, que se sirve como tal y que dos generaciones dan los mismos bytes.
/// </para>
/// <para>
/// Los importes y los totales salen de los datos ya calculados del dominio; estas pruebas no los
/// recalculan, los leen y comparan con lo que dice el modelo.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class FacturaPdfTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Condiciones = "Pago a 30 días contra presentación de la factura.";

    /// <summary>
    /// El archivo que devuelve la API tiene que ser un PDF de verdad y ser reproducible: si dos
    /// descargas del mismo documento dieran bytes distintos, no se podría ni archivar ni comprobar.
    /// Lo único que cambia entre una y otra son las fechas de generación, que se normalizan.
    /// </summary>
    [Fact]
    public async Task ElPdfDeUnaFacturaEmitidaEsValidoYDeterministico()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        // Serie con numerador de comprobante fiscal: así el documento sale con las dos referencias.
        var serieId = await AyudaFacturas.CrearSerieAsync(
            admin, TipoSerie.Normal, Moneda.USD, ct, generaComprobante: true);
        var clienteId = await AyudaFacturas.CrearClienteAsync(admin, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        await AyudaComprobante.NumeradorAsync(admin, serieId, hoy, ct);

        var facturaId = await AyudaFacturas.CrearFacturaAsync(
            admin, clienteId, serieId, Moneda.USD, hoy.AddDays(30), ct);

        await AyudaFacturas.AgregarLineaAsync(
            admin, facturaId, TipoLineaFactura.Renta, 1m, 1_250.50m, null, ct);

        (await admin.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK, "la serie tiene numerador y la factura emite");

        var primera = await admin.GetAsync($"/api/v1/facturas/{facturaId:D}/pdf", ct);
        primera.StatusCode.Should().Be(HttpStatusCode.OK);
        primera.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");

        var bytes = await primera.Content.ReadAsByteArrayAsync(ct);
        Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 5))
            .Should().Be("%PDF-", "un PDF de verdad empieza así");

        var segunda = await admin.GetAsync($"/api/v1/facturas/{facturaId:D}/pdf", ct);
        segunda.StatusCode.Should().Be(HttpStatusCode.OK);

        // Solo se normalizan las fechas de generación: el resto del archivo tiene que salir igual.
        AyudaPlantilla.SinFechaDeGeneracion(bytes)
            .Should()
            .Equal(AyudaPlantilla.SinFechaDeGeneracion(await segunda.Content.ReadAsByteArrayAsync(ct)));

        // El nombre del archivo lleva el identificador de la factura, no el número de serie.
        primera.Content.Headers.ContentDisposition?.FileName
            .Should().Contain($"factura-{facturaId:D}");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.ComprobanteFiscal.AsNoTracking()
            .SingleAsync(c => c.FacturaId == facturaId, ct)).Numero
            .Should().BePositive("la serie genera comprobante y la emisión lo reservó");
    }

    /// <summary>
    /// El contenido del documento: tipo, número, comprobante, cliente con su documento entero, líneas,
    /// totales y pie. Es la prueba que cubre lo que el papel dice, que es lo único que no se puede
    /// leer del PDF.
    /// </summary>
    [Fact]
    public async Task ElModeloDelPdfImprimeLoQueCorresponde()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var serieId = await AyudaFacturas.CrearSerieAsync(
            admin, TipoSerie.Normal, Moneda.USD, ct, generaComprobante: true);

        // Cliente con documento conocido para poder compararlo con el descifrado del modelo.
        const string documento = "001-12345678-9";
        const string nombreCliente = "Cliente con documento conocido";
        var clienteId = await CrearClienteAsync(admin, nombreCliente, documento, agenteRetencion: true, ct);

        // Tipo de ITBIS del 16 % y retención del 10 % sobre el subtotal: así los dos salen distintos
        // de cero y el documento tiene que imprimir las dos filas.
        var tipoItbisId = await AyudaFacturas.CrearTipoItbisAsync(admin, 16m, ct);
        var tipoRetencionId = await AyudaRetenciones.CrearTipoAsync(admin, BaseRetencion.Subtotal, 10m, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        await AyudaComprobante.NumeradorAsync(admin, serieId, hoy, ct);
        await PonerCondicionesAsync(admin, serieId, "estandar", Condiciones, ct);
        var facturaId = await AyudaFacturas.CrearFacturaAsync(
            admin, clienteId, serieId, Moneda.USD, hoy.AddDays(30), ct);

        await AyudaFacturas.AgregarLineaAsync(
            admin, facturaId, TipoLineaFactura.Renta, 1m, 10_000m, tipoItbisId, ct);
        await AyudaRetenciones.AgregarLineaRetencionAsync(admin, facturaId, tipoRetencionId, ct);

        await AyudaRetenciones.EmitirAsync(admin, facturaId, ct);

        const string referencia = "e-factura-2026-000123";
        (await admin.PutAsync(
            $"/api/v1/facturas/{facturaId:D}/referencia-efactura",
            new { referencia },
            ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var modelo = await ModeloAsync(facturaId, ct);
        var (retencion, neto) = await AyudaRetenciones.TotalesAsync(bd, facturaId, ct);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var fila = await contexto.Factura.AsNoTracking().SingleAsync(f => f.Id == facturaId, ct);
        var serie = await contexto.Serie.AsNoTracking().SingleAsync(s => s.Id == serieId, ct);

        modelo.Plantilla.Nombre.Should().Be("estandar");
        modelo.TipoDocumento.Should().Be("Factura");
        modelo.Numero.Should().Be($"{serie.Prefijo}-{fila.Numero}");
        modelo.Moneda.Should().Be(nameof(Moneda.USD));
        modelo.ComprobanteFiscal.Should().NotBeNull("la serie genera comprobante fiscal");
        modelo.MarcaAgua.Should().BeNull("una factura emitida no va anulada");

        // El cliente sale con su documento entero y descifrado (D16): es lo que necesita el
        // expediente y lo que §3.2 deja ver.
        modelo.Cliente.Nombre.Should().Be(nombreCliente);
        modelo.Cliente.Documento.Should().Be(documento);

        modelo.Lineas.Should().HaveCount(2, "la línea de renta y la de retención");
        modelo.Lineas.Should().Contain(l => l.Importe == "10.000,00 USD", "la renta con el formato de §5");

        // Los totales son los del dominio, con el mismo formato. La retención va en negativo, tal
        // y como la guarda F3-04.
        modelo.Totales.Should().Contain(t => t.Etiqueta == "Subtotal" && t.Valor == "10.000,00 USD");
        modelo.Totales.Should().Contain(t => t.Etiqueta == "ITBIS" && t.Valor == "1.600,00 USD");
        modelo.Totales.Should().Contain(t => t.Etiqueta == "Retención" && t.Valor == $"{Numero(retencion)} USD");
        modelo.Totales.Should().Contain(t => t.Etiqueta == "Total" && t.Valor == "11.600,00 USD" && t.Destacado);
        modelo.Totales.Should().Contain(t => t.Etiqueta == "Neto a cobrar" && t.Valor == $"{Numero(neto)} USD");

        // Sin cobros no hay filas de pagado ni pendiente: dos ceros con etiqueta son ruido.
        modelo.Totales.Should().NotContain(t => t.Etiqueta == "Pagado" || t.Etiqueta == "Pendiente");

        modelo.Pie.CondicionesPago.Should().Be(Condiciones, "son las de la serie");
        modelo.Pie.ReferenciaEfactura.Should().Be(referencia);
    }

    /// <summary>
    /// La marca de agua es de estado, no de color: solo la lleva la factura anulada.
    /// <para>
    /// Anulada y descargable a la vez: el papel marcado es lo que necesita el expediente, y lo que
    /// no se puede es emitirla.
    /// </para>
    /// </summary>
    [Fact]
    public async Task UnaFacturaAnuladaSeMarcaComoAnuladaYUnaEmitidaNo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var clienteId = await AyudaFacturas.CrearClienteAsync(admin, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);

        var emitidaId = await AyudaFacturas.CrearFacturaAsync(
            admin, clienteId, serieId, Moneda.USD, hoy.AddDays(30), ct);
        await AyudaFacturas.AgregarLineaAsync(
            admin, emitidaId, TipoLineaFactura.Renta, 1m, 100m, null, ct);
        await admin.PostAsync($"/api/v1/facturas/{emitidaId:D}/emitir", new { }, ct);

        var anuladaId = await AyudaFacturas.CrearFacturaAsync(
            admin, clienteId, serieId, Moneda.USD, hoy.AddDays(30), ct);
        await AyudaFacturas.AgregarLineaAsync(
            admin, anuladaId, TipoLineaFactura.Renta, 1m, 100m, null, ct);
        await admin.PostAsync($"/api/v1/facturas/{anuladaId:D}/emitir", new { }, ct);
        (await admin.PostAsync(
            $"/api/v1/facturas/{anuladaId:D}/anular",
            new { motivo = "Prueba de la marca de agua" },
            ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ModeloAsync(emitidaId, ct)).MarcaAgua.Should().BeNull();

        var anulada = await ModeloAsync(anuladaId, ct);
        anulada.MarcaAgua.Should().Be("ANULADA");

        // Y se descarga: el papel con la marca es el que necesita el cliente.
        var pdf = await admin.GetAsync($"/api/v1/facturas/{anuladaId:D}/pdf", ct);
        pdf.StatusCode.Should().Be(HttpStatusCode.OK, "una factura anulada también se descarga");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Factura.AsNoTracking().SingleAsync(f => f.Id == anuladaId, ct)).Estado
            .Should().Be(EstadoFactura.Anulada, "descargar el papel no la cambia");
    }

    /// <summary>
    /// El texto del usuario se escribe como texto plano. Nunca como marcado, nunca como HTML y
    /// nunca como plantilla: un concepto que parece marcado tiene que salir tal cual en el
    /// documento, y el PDF no puede llevar ni una instrucción ni una acción.
    /// <para>
    /// Es una regla de seguridad y no de estilo (<c>docs/SEGURIDAD.md</c>, SEC-001). La otra mitad
    /// —que ningún compositor use el intérprete de HTML o de SVG— la vigila
    /// <c>InterpretacionEnPdfEnCodigoTests</c>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ElTextoDelUsuarioNuncaSeInterpreta()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var marcahtml = "<b>renta</b>";
        var marcaImagen = "<img src=x onerror=alert(1)>";
        var plantilla = "{{total}}";
        var sustitucion = "{cliente}";
        const string nombreCliente = "<script>alert('cliente')</script>";

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var clienteId = await CrearClienteAsync(admin, nombreCliente, $"001-{Guid.NewGuid():N}"[..13], false, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var facturaId = await AyudaFacturas.CrearFacturaAsync(
            admin, clienteId, serieId, Moneda.USD, hoy.AddDays(30), ct);

        foreach (var concepto in new[] { marcahtml, marcaImagen, plantilla, sustitucion })
        {
            (await admin.PostAsync(
                $"/api/v1/facturas/{facturaId:D}/lineas",
                new { tipo = nameof(TipoLineaFactura.Renta), concepto, cantidad = 1m, precioUnitario = 10m },
                ct)).StatusCode.Should().Be(HttpStatusCode.OK, $"la línea «{concepto}» se guarda tal cual");
        }

        await admin.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);

        // (a) El modelo los lleva literalmente: sin escapado, sin interpretación y sin plantilla.
        var modelo = await ModeloAsync(facturaId, ct);
        modelo.Cliente.Nombre.Should().Be(nombreCliente);
        modelo.Lineas.Select(l => l.Concepto).Should()
            .BeEquivalentTo([marcahtml, marcaImagen, plantilla, sustitucion]);

        var todo = string.Join(
            "\n",
            modelo.Lineas.Select(l => l.Concepto)
                .Append(modelo.Cliente.Nombre)
                .Append(modelo.TipoDocumento));

        foreach (var marca in new[] { marcahtml, marcaImagen, plantilla, sustitucion, nombreCliente })
        {
            todo.Should().Contain(marca, $"«{marca}» sale tal cual, sin transformar");
        }

        // Lo que distingue a un documento plano de uno interpretado es que nada se escapó ni se
        // resolvió: no hay entidades HTML ni la sustitución de una plantilla.
        todo.Should().NotContain("&lt;");
        todo.Should().NotContain("&amp;");

        // (b) Y el PDF es un documento plano: ni guiones, ni acciones, ni archivos incrustados.
        var pdf = await admin.GetAsync($"/api/v1/facturas/{facturaId:D}/pdf", ct);
        pdf.StatusCode.Should().Be(HttpStatusCode.OK);

        var bytes = Encoding.Latin1.GetString(await pdf.Content.ReadAsByteArrayAsync(ct));

        foreach (var prohibido in new[] { "/JavaScript", "/JS", "/OpenAction", "/URI", "/Launch", "/EmbeddedFile" })
        {
            bytes.Should().NotContain(
                prohibido,
                $"un PDF generado por código no lleva «{prohibido}» (docs/SEGURIDAD.md, SEC-001)");
        }
    }

    /// <summary>
    /// Una serie que nombra una plantilla que no existe se rechaza al configurarla, y el mensaje
    /// lleva las válidas. Caer en la de por defecto en silencio imprimiría cada factura de la serie
    /// con un diseño que nadie pidió, y el error saldría meses después.
    /// </summary>
    [Fact]
    public async Task UnaSerieConPlantillaDesconocidaSeRechaza()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var serie = await LeerSerieAsync(admin, serieId, ct);

        var rechazada = await admin.PutAsync(
            $"/api/v1/config/series/{serieId:D}",
            CuerpoSerie(serie, "no-existe", Condiciones),
            ct);

        rechazada.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await rechazada.Cuerpo()).Should().Contain("estandar", "el mensaje dice cuál es la válida");

        // Lo rechazado no se guarda.
        (await LeerSerieAsync(admin, serieId, ct)).GetProperty("plantillaPdf").ValueKind
            .Should().Be(JsonValueKind.Null, "una plantilla desconocida no cae en la de por defecto");

        foreach (var plantilla in new[] { "estandar", null })
        {
            var guardada = await admin.PutAsync(
                $"/api/v1/config/series/{serieId:D}",
                CuerpoSerie(await LeerSerieAsync(admin, serieId, ct), plantilla, Condiciones),
                ct);

            guardada.StatusCode.Should().Be(HttpStatusCode.OK, "«estandar» y nulo son plantillas válidas");

            // El GET devuelve lo guardado tal cual, para que el administrador vea lo que hay.
            (await LeerSerieAsync(admin, serieId, ct)).GetProperty("plantillaPdf").GetString()
                .Should().Be(plantilla);
        }

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Serie.AsNoTracking().SingleAsync(s => s.Id == serieId, ct)).CondicionesPago
            .Should().Be(Condiciones, "las condiciones de pago del pie se guardan con la serie");
    }

    /// <summary>
    /// El PDF es una factura individual: lo ve quien puede ver la factura, y sin ese permiso no.
    /// Los totales agregados son <c>informe.ver</c> y no entran aquí.
    /// </summary>
    [Fact]
    public async Task ElEmpleadoDescargaElPdfYSinFacturaVerNo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        var empleado = await AyudaAuth.CrearUsuarioAsync(
            bd, AyudaFacturas.Contrasena, rol: Roles.Empleado, ct: ct);
        using var conPermiso = await AyudaPermisos.SesionAsync(
            api, empleado, AyudaFacturas.Contrasena, ct);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var clienteId = await AyudaFacturas.CrearClienteAsync(admin, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var facturaId = await AyudaFacturas.CrearFacturaAsync(
            admin, clienteId, serieId, Moneda.USD, hoy.AddDays(30), ct);

        await AyudaFacturas.AgregarLineaAsync(
            admin, facturaId, TipoLineaFactura.Renta, 1m, 100m, null, ct);
        await admin.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);

        (await conPermiso.GetAsync($"/api/v1/facturas/{facturaId:D}/pdf", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK, "el empleado tiene factura.ver");

        // Se le quita solo a este usuario, que es como se revocan permisos: no se toca el rol entero.
        (await admin.PutAsync(
            $"/api/v1/usuarios/{empleado.Id:D}/permisos",
            new { accion = Acciones.FacturaVer, permitido = false },
            ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var sinPermiso = await AyudaPermisos.SesionAsync(
            api, empleado, AyudaFacturas.Contrasena, ct);

        (await sinPermiso.GetAsync($"/api/v1/facturas/{facturaId:D}/pdf", ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "sin factura.ver no hay documento");
    }

    /// <summary>
    /// Los datos de la empresa facturadora salen en la cabecera cuando están configurados, y el
    /// bloque desaparece cuando no hay ninguna de las dos posibles.
    /// </summary>
    [Fact]
    public async Task LaCabeceraSaleConLosDatosDeLaEmpresaFacturadoraYSinEllosDesaparece()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var clienteId = await AyudaFacturas.CrearClienteAsync(admin, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);

        // Con contrato: la facturadora es la del contrato (§3.7), y es la que lleva los datos.
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);
        (await admin.PutAsync(
            $"/api/v1/config/subempresas/{facturadoraId:D}",
            new
            {
                nombre = "Inmobiliaria de prueba",
                rnc = "1-31-12345-6",
                direccion = "Calle Duarte 45, Santo Domingo",
                telefono = "+1 809 000 0000",
                email = "facturacion@ejemplo.test",
                propietaria = false,
                facturadora = true,
                activa = true,
            },
            ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var unidadId = await AyudaReserva.UnidadAsync(admin, ct);
        var contratoId = await AyudaContrato.CrearAsync(
            admin,
            AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, moneda: "USD"),
            ct);

        var conContratoId = await AyudaFacturas.CrearFacturaAsync(
            admin, clienteId, serieId, Moneda.USD, hoy.AddDays(30), ct, contratoId, unidadId);
        await AyudaFacturas.AgregarLineaAsync(
            admin, conContratoId, TipoLineaFactura.Renta, 1m, 100m, null, ct);
        await admin.PostAsync($"/api/v1/facturas/{conContratoId:D}/emitir", new { }, ct);

        var empresa = (await ModeloAsync(conContratoId, ct)).Empresa;
        empresa.Should().NotBeNull("la cabecera lleva la empresa facturadora");
        empresa!.Nombre.Should().Be("Inmobiliaria de prueba");
        empresa.Rnc.Should().Be("1-31-12345-6");
        empresa.Direccion.Should().Be("Calle Duarte 45, Santo Domingo");
        empresa.Telefono.Should().Be("+1 809 000 0000");
        empresa.Email.Should().Be("facturacion@ejemplo.test");

        // Sin contrato y sin facturadora por defecto en la unidad: el bloque entero desaparece, en
        // vez de salirse con líneas vacías.
        var sinContratoId = await AyudaFacturas.CrearFacturaAsync(
            admin, clienteId, serieId, Moneda.USD, hoy.AddDays(30), ct);
        await AyudaFacturas.AgregarLineaAsync(
            admin, sinContratoId, TipoLineaFactura.Renta, 1m, 100m, null, ct);
        await admin.PostAsync($"/api/v1/facturas/{sinContratoId:D}/emitir", new { }, ct);

        (await ModeloAsync(sinContratoId, ct)).Empresa
            .Should().BeNull("sin empresa facturadora la cabecera sale sin el bloque");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var subempresa = await contexto.Subempresa.AsNoTracking()
            .SingleAsync(s => s.Id == facturadoraId, ct);

        // En la fila el RNC va cifrado (AGENTS.md §6); el valor en claro es el del modelo y el de la
        // API, que lo devuelven descifrado a propósito (D16).
        subempresa.Rnc.Should().StartWith("v1:", "lo guardado es el valor cifrado con su versión");
        subempresa.Rnc.Should().NotContain("1-31-12345-6", "el RNC en claro no está en la base de datos");
        Descifrar(subempresa.Rnc).Should().Be("1-31-12345-6");
        subempresa.Direccion.Should().Be("Calle Duarte 45, Santo Domingo");
        subempresa.Telefono.Should().Be("+1 809 000 0000");
        subempresa.Email.Should().Be("facturacion@ejemplo.test");

        // Y el listado de subempresas los devuelve, como el de las series.
        (await ListarAsync(admin, "subempresas", facturadoraId, ct)).GetProperty("rnc").GetString()
            .Should().Be("1-31-12345-6");
    }

    // ---------------------------------------------------------------- ayudas

    /// <summary>
    /// El modelo tal y como lo monta el servicio, con los mismos datos que carga: cliente,
    /// unidad, serie, contrato con su facturadora, líneas y comprobante.
    /// </summary>
    private async Task<DocumentoFacturaPdfModelo> ModeloAsync(Guid facturaId, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        var factura = await contexto.Factura.AsNoTracking()
            .Include(f => f.Cliente)
            .Include(f => f.Unidad)
            .Include(f => f.Serie)
            .Include(f => f.Contrato)
            .ThenInclude(c => c!.Facturadora)
            .Include(f => f.Lineas)
            .SingleAsync(f => f.Id == facturaId, ct);

        var comprobante = await contexto.ComprobanteFiscal.AsNoTracking()
            .FirstOrDefaultAsync(c => c.FacturaId == facturaId, ct);

        factura.Cliente.Should().NotBeNull("toda factura tiene cliente");
        factura.Serie.Should().NotBeNull("toda factura tiene serie");

        // El RNC va cifrado en la fila, así que el compositor lo recibe descifrado. Es lo mismo
        // cambio en memoria que hace `ServicioFacturas` sobre la entidad sin seguimiento.
        var facturadora = factura.Contrato?.Facturadora;
        if (facturadora is not null)
        {
            facturadora.Rnc = Descifrar(facturadora.Rnc);
        }

        return DocumentoFacturaPdf.Modelo(
            factura,
            RegistroPlantillasPdf.Resolver(factura.Serie.PlantillaPdf),
            facturadora,
            Descifrar(factura.Cliente.Documento),
            situacionCobro: null,
            comprobante);
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

    private static async Task<Guid> CrearClienteAsync(
        SesionCliente sesion,
        string nombre,
        string documento,
        bool agenteRetencion,
        CancellationToken ct)
    {
        var creada = await sesion.PostAsync(
            "/api/v1/clientes",
            new { nombre, tipo = "PersonaFisica", documento, email = "cliente@example.test", agenteRetencion },
            ct);

        creada.StatusCode.Should().Be(HttpStatusCode.OK, await creada.Cuerpo());

        return (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    private static async Task PonerCondicionesAsync(
        SesionCliente sesion,
        Guid serieId,
        string? plantilla,
        string condiciones,
        CancellationToken ct) =>
        _ = await sesion.PutAsync(
            $"/api/v1/config/series/{serieId:D}",
            CuerpoSerie(await LeerSerieAsync(sesion, serieId, ct), plantilla, condiciones),
            ct);

    /// <summary>
    /// La serie tal y como la devuelve la API. La configuración no tiene <c>GET</c> por
    /// identificador, solo la lista, así que se busca ahí: lo que se compara es lo que ve el
    /// administrador en pantalla, no la fila de la base.
    /// </summary>
    private static async Task<JsonElement> LeerSerieAsync(
        SesionCliente sesion,
        Guid serieId,
        CancellationToken ct) =>
        (await ListarAsync(sesion, "series", serieId, ct));

    /// <summary>
    /// La fila del recurso de configuración con ese identificador. Se recorre página a página porque la
    /// base es la de la colección y la configuración no tiene <c>GET</c> por identificador: leer solo la
    /// primera página haría que la afirmación dependiera de cuántas filas hubieran dejado las demás
    /// pruebas (mismo criterio que <c>AyudaPlantilla.CatalogoAsync</c>).
    /// </summary>
    private static async Task<JsonElement> ListarAsync(
        SesionCliente sesion,
        string recurso,
        Guid id,
        CancellationToken ct)
    {
        for (var pagina = 1; ; pagina++)
        {
            var listada = await sesion.GetAsync($"/api/v1/config/{recurso}?page={pagina}&pageSize=200", ct);
            listada.StatusCode.Should().Be(HttpStatusCode.OK, await listada.Cuerpo());

            var cuerpo = await listada.Content.ReadFromJsonAsync<JsonElement>(ct);
            var fila = cuerpo.GetProperty("data").EnumerateArray()
                .SingleOrDefault(f => f.GetProperty("id").GetGuid() == id);

            if (fila.ValueKind != JsonValueKind.Undefined)
            {
                return fila;
            }

            if (pagina >= cuerpo.GetProperty("totalPages").GetInt32())
            {
                throw new InvalidOperationException($"El recurso '{recurso}' no tiene la fila '{id:D}'.");
            }
        }
    }

    private static object CuerpoSerie(JsonElement serie, string? plantilla, string condiciones) => new
    {
        numero = serie.GetProperty("numero").GetInt32(),
        prefijo = serie.GetProperty("prefijo").GetString(),
        tipo = serie.GetProperty("tipo").GetString(),
        moneda = serie.GetProperty("moneda").GetString(),
        plantillaPdf = plantilla,
        condicionesPago = condiciones,
        sumaAFacturado = serie.GetProperty("sumaAFacturado").GetBoolean(),
        sumaASaldos = serie.GetProperty("sumaASaldos").GetBoolean(),
        generaComprobante = serie.GetProperty("generaComprobante").GetBoolean(),
        activa = serie.GetProperty("activa").GetBoolean(),
    };

    /// <summary>El mismo formato de número de <c>docs/DESIGN.md §5</c>, para no comparar con otra regla.</summary>
    private static string Numero(decimal valor) => FormatoDocumentoPdf.Numero(valor);

    /// <summary>
    /// Factura y proforma son la misma entidad y salen del mismo compositor. Lo que las distingue es
    /// el tipo de su serie, y eso es lo que dice el título del documento.
    /// </summary>
    [Fact]
    public async Task UnaProformaSeTitulaProformaYUnaFacturaFactura()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var clienteId = await AyudaFacturas.CrearClienteAsync(admin, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);

        var proformaSerie = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Proforma, Moneda.USD, ct);
        var facturaSerie = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);

        var proformaId = await AyudaFacturas.CrearFacturaAsync(
            admin, clienteId, proformaSerie, Moneda.USD, hoy.AddDays(15), ct);
        var facturaId = await AyudaFacturas.CrearFacturaAsync(
            admin, clienteId, facturaSerie, Moneda.USD, hoy.AddDays(30), ct);

        (await ModeloAsync(proformaId, ct)).TipoDocumento.Should().Be("Proforma");
        (await ModeloAsync(facturaId, ct)).TipoDocumento.Should().Be("Factura");

        // En borrador no hay número que imprimir, y el documento lo dice en vez de enseñar un hueco.
        (await ModeloAsync(proformaId, ct)).Numero.Should().Be("Borrador sin número");
    }
}
