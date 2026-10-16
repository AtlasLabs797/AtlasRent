using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;

namespace AtlasRent.Tests.Facturas;

/// <summary>
/// Apoyo común de las pruebas de facturas (F3-02). Todo se crea por la API real, que es donde
/// viven los permisos, la validación y el contador de series.
/// <para>
/// Cada prueba trae su propia serie, su propio cliente y su propio día: la base es única y la
/// comparten todas las pruebas de la colección, así que nada puede afirmar sobre valores absolutos
/// ni chocar con lo que dejó otra.
/// </para>
/// </summary>
internal static class AyudaFacturas
{
    public const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// Hoy en Santo Domingo tal y como lo ve la aplicación. Se pregunta por la API y no se calcula
    /// en la prueba porque la aplicación usa un reloj de pruebas que otras pruebas adelantan.
    /// <para>
    /// Se pregunta por USD y no por DOP a propósito: el dólar vale 1 sin consultar la base (F3-01), así
    /// que el día se puede leer aunque no haya cambio de ninguna otra moneda cargado, que es
    /// justamente el estado en el que está una de estas pruebas.
    /// </para>
    /// </summary>
    public static async Task<DateOnly> HoyAsync(SesionCliente sesion, CancellationToken ct)
    {
        var actual = await sesion.GetAsync("/api/v1/tipos-cambio/actual?moneda=USD", ct);
        actual.StatusCode.Should().Be(HttpStatusCode.OK);

        var fecha = (await actual.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("fecha")
            .GetString();

        return DateOnly.Parse(fecha!, CultureInfo.InvariantCulture);
    }

    public static async Task<SesionCliente> SesionAsync(
        ContenedorBdFixture bd,
        ApiTestFactory api,
        string rol,
        CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: rol, ct: ct);

        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }

    /// <summary>
    /// Serie propia de la prueba. El número sale de la franja alta de 1..999 para no pisar ni las
    /// series iniciales de §3.7 (1, 2, 3, 4 y 9) ni las que crean otras pruebas.
    /// </summary>
    public static async Task<Guid> CrearSerieAsync(
        SesionCliente sesion,
        TipoSerie tipo,
        Moneda? moneda,
        CancellationToken ct,
        bool generaComprobante = false,
        bool sumaAFacturado = true,
        bool sumaASaldos = true)
    {
        var creada = await sesion.PostAsync(
            "/api/v1/config/series",
            new
            {
                numero = NumeroDeSerieLibre(),
                prefijo = $"F{Guid.NewGuid():N}"[..8],
                tipo = tipo.ToString(),
                moneda = moneda?.ToString(),
                sumaAFacturado,
                sumaASaldos,
                generaComprobante,
                activa = true,
            },
            ct);

        creada.StatusCode.Should().Be(HttpStatusCode.OK, "la serie se crea por la configuración");

        return (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    private static readonly HashSet<int> NumerosUsados = [];

    private static int NumeroDeSerieLibre()
    {
        for (var intento = 0; intento < 50; intento++)
        {
            var candidato = Random.Shared.Next(300, 900);
            if (NumerosUsados.Add(candidato))
            {
                return candidato;
            }
        }

        return Random.Shared.Next(900, 999);
    }

    /// <summary>
    /// Cliente de la prueba. <paramref name="agenteRetencion"/> es la marca de §3.9 y va aquí para que
    /// las pruebas de retención (F3-04) no tengan que escribir a mano el mismo cliente de siempre.
    /// </summary>
    public static async Task<Guid> CrearClienteAsync(
        SesionCliente sesion,
        CancellationToken ct,
        bool agenteRetencion = false)
    {
        var documento = $"001-{Guid.NewGuid():N}"[..13];
        var creada = await sesion.PostAsync(
            "/api/v1/clientes",
            new
            {
                nombre = $"Cliente {Guid.NewGuid():N}"[..20],
                tipo = "PersonaFisica",
                documento,
                email = "cliente@example.test",
                agenteRetencion,
            },
            ct);

        creada.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    /// <summary>Tipo de ITBIS de la prueba, con el porcentaje que le pase el test.</summary>
    public static async Task<Guid> CrearTipoItbisAsync(
        SesionCliente sesion,
        decimal porcentaje,
        CancellationToken ct)
    {
        var creado = await sesion.PostAsync(
            "/api/v1/config/tipos-itbis",
            new
            {
                nombre = $"ITBIS {porcentaje.ToString(CultureInfo.InvariantCulture)}%",
                porcentaje,
                activo = true,
            },
            ct);

        creado.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await creado.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Carga manual del cambio del día (F3-01), que es la salida que señala el 409 de la emisión
    /// cuando a la moneda que no es el dólar le falta el cambio de hoy.
    /// </summary>
    public static async Task<Guid> CargarTipoCambioAsync(
        SesionCliente sesion,
        Moneda moneda,
        decimal valor,
        CancellationToken ct)
    {
        var cargado = await sesion.PostAsync(
            "/api/v1/tipos-cambio",
            new { moneda = moneda.ToString(), valor },
            ct);

        cargado.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await cargado.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    /// <param name="contratoId">
    /// Nulo es lo normal. Solo las pruebas de cobro (F3-06) lo pasan, porque un cobro con
    /// asignaciones explícitas necesita un contrato al que moverle el dinero.
    /// </param>
    public static async Task<Guid> CrearFacturaAsync(
        SesionCliente sesion,
        Guid clienteId,
        Guid serieId,
        Moneda moneda,
        DateOnly vencimiento,
        CancellationToken ct,
        Guid? contratoId = null)
    {
        var creada = await sesion.PostAsync(
            "/api/v1/facturas",
            new
            {
                clienteId,
                contratoId,
                serieId,
                moneda = moneda.ToString(),
                fechaVencimiento = vencimiento.ToString("yyyy-MM-dd"),
                concepto = $"Factura {Guid.NewGuid():N}"[..30],
            },
            ct);

        creada.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    /// <summary>Devuelve la factura entera, que es la respuesta de los endpoints de línea.</summary>
    public static async Task<JsonElement> AgregarLineaAsync(
        SesionCliente sesion,
        Guid facturaId,
        TipoLineaFactura tipo,
        decimal cantidad,
        decimal precioUnitario,
        Guid? tipoItbisId,
        CancellationToken ct)
    {
        var agregada = await sesion.PostAsync(
            $"/api/v1/facturas/{facturaId:D}/lineas",
            new
            {
                tipo = tipo.ToString(),
                concepto = $"Línea {Guid.NewGuid():N}"[..20],
                cantidad,
                precioUnitario,
                tipoItbisId,
            },
            ct);

        agregada.StatusCode.Should().Be(HttpStatusCode.OK, "la línea se da de alta en borrador");

        return await agregada.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    /// <summary>Factura en borrador con una línea de renta, que es el punto de partida de casi todo.</summary>
    public static async Task<Guid> BorradorConLineaAsync(
        SesionCliente sesion,
        Guid clienteId,
        Guid serieId,
        Moneda moneda,
        CancellationToken ct)
    {
        var hoy = await HoyAsync(sesion, ct);
        var facturaId = await CrearFacturaAsync(
            sesion, clienteId, serieId, moneda, hoy.AddDays(30), ct);

        await AgregarLineaAsync(sesion, facturaId, TipoLineaFactura.Renta, 1m, 100m, null, ct);

        return facturaId;
    }
}
