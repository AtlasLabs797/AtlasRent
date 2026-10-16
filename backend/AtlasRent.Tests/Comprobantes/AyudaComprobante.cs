using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Tests.Comprobantes;

/// <summary>
/// Apoyo común de las pruebas de comprobante fiscal (F3-03). Todo se crea por la API real, que es
/// donde viven los permisos, la validación y el contador.
/// <para>
/// <b>Ningún rango, tipo ni prefijo de comprobante está escrito aquí</b>: los inventa la propia
/// prueba, cada vez distinto. Son una autorización que da alguien fuera del sistema, y un valor fijo
/// en el código —aunque sea de prueba— sería inventar una autorización fiscal (D14). La serie 3 de la
/// base tampoco viene con ninguno: lo configura el administrador.
/// </para>
/// </summary>
internal static class AyudaComprobante
{
    /// <summary>
    /// Tamaño de franja que se pide por defecto, de sobra para las emisiones concurrentes de la
    /// prueba grande. No es un rango: cada prueba decide el suyo y el que sale es distinto cada vez.
    /// </summary>
    public const int Amplio = 500;

    /// <summary>Serie fiscal con «genera comprobante» activo, que es lo que obliga al numerador.</summary>
    public static async Task<Guid> SerieFiscalAsync(SesionCliente sesion, CancellationToken ct) =>
        await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Fiscal, null, ct, generaComprobante: true);

    /// <summary>
    /// Valores de un numerador, todos inventados en el momento. Se devuelven como un objeto anónimo
    /// porque van directos al cuerpo de la API: si el contrato cambia, lo dice la compilación de la
    /// llamada y no una prueba verde que ya no comprueba lo que dice comprobar.
    /// </summary>
    public static object Peticion(
        DateOnly hoy,
        DateOnly? vencimiento = null,
        int? rangoDesde = null,
        int? rangoHasta = null,
        int? siguienteNumero = null,
        int? umbralAlerta = null)
    {
        var desde = rangoDesde ?? Random.Shared.Next(1, Amplio);
        return new
        {
            // El tipo es un dato de la autorización, no una constante: cada numerador de prueba lleva
            // uno distinto y ninguno significa nada fuera de esta base.
            tipoPrefijo = $"T{Guid.NewGuid():N}"[..6],
            rangoDesde = desde,
            rangoHasta = rangoHasta ?? desde + Amplio,
            fechaVencimiento = (vencimiento ?? hoy.AddDays(30)).ToString("yyyy-MM-dd"),
            siguienteNumero = siguienteNumero ?? desde,
            umbralAlerta = umbralAlerta ?? 10,
        };
    }

    /// <summary>
    /// Crea el numerador de una serie por la API de configuración y devuelve su identificador.
    /// <para>
    /// Existe porque desde F3-03 una serie fiscal <b>no emite sin numerador</b>: las pruebas de
    /// factura, cobro y tipo de cambio que emiten en serie fiscal tienen que configurarlo antes, y lo
    /// hacen por aquí en vez de por la base para que sigan pasando por los permisos y por la
    /// validación que se aplican a una persona.
    /// </para>
    /// </summary>
    public static async Task<Guid> NumeradorAsync(
        SesionCliente sesion,
        Guid serieId,
        DateOnly hoy,
        CancellationToken ct,
        DateOnly? vencimiento = null,
        int? rangoDesde = null,
        int? rangoHasta = null,
        int? siguienteNumero = null,
        int? umbralAlerta = null)
    {
        var guardado = await sesion.PutAsync(
            $"/api/v1/config/numeradores-fiscales/{serieId:D}",
            Peticion(hoy, vencimiento, rangoDesde, rangoHasta, siguienteNumero, umbralAlerta),
            ct);

        guardado.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "sin numerador una serie fiscal no emite, así que hay que configurarlo antes");

        return (await guardado.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    /// <summary>Factura en borrador en una serie fiscal, que es de donde sale el comprobante.</summary>
    public static async Task<Guid> BorradorAsync(
        SesionCliente sesion,
        Guid clienteId,
        Guid serieId,
        CancellationToken ct) =>
        await AyudaFacturas.BorradorConLineaAsync(sesion, clienteId, serieId, Moneda.USD, ct);

    // ---------------------------------------------------------------- lecturas

    /// <summary>
    /// Una serie tal y como la devuelve la configuración, para poder reenviarla en un cambio sin
    /// inventar sus valores. Recorre las páginas porque la base es compartida con el resto de pruebas
    /// y la serie de esta puede no estar en la primera.
    /// </summary>
    public static async Task<JsonElement> SerieAsync(SesionCliente sesion, Guid serieId, CancellationToken ct)
    {
        for (var pagina = 1; ; pagina++)
        {
            var respuesta = await sesion.GetAsync(
                $"/api/v1/config/series?page={pagina}&pageSize=200&sortBy=numero", ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.OK);

            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
            var elementos = cuerpo.GetProperty("data").EnumerateArray();
            var encontrada = elementos.FirstOrDefault(s => s.GetProperty("id").GetGuid() == serieId);

            if (encontrada.ValueKind != JsonValueKind.Undefined)
            {
                return encontrada;
            }

            pagina.Should().BeLessThan(
                cuerpo.GetProperty("totalPages").GetInt32(),
                "la serie de esta prueba está en el catálogo");
        }
    }

    public static Task<ComprobanteFiscal?> ComprobanteAsync(
        ContenedorBdFixture bd,
        Guid facturaId,
        CancellationToken ct) =>
        EnContextoAsync(bd, c => c.ComprobanteFiscal.AsNoTracking()
            .SingleOrDefaultAsync(x => x.FacturaId == facturaId, ct));

    public static Task<NumeradorFiscal?> NumeradorDeAsync(
        ContenedorBdFixture bd,
        Guid serieId,
        CancellationToken ct) =>
        EnContextoAsync(bd, c => c.NumeradorFiscal.AsNoTracking()
            .SingleOrDefaultAsync(n => n.SerieId == serieId, ct));

    public static Task<List<ComprobanteFiscal>> ComprobantesAsync(
        ContenedorBdFixture bd,
        Guid serieId,
        CancellationToken ct) =>
        EnContextoAsync(bd, c => c.ComprobanteFiscal.AsNoTracking()
            .Where(x => x.SerieId == serieId)
            .OrderBy(x => x.Numero)
            .ToListAsync(ct));

    /// <summary>Números de factura que han salido de la serie, en orden.</summary>
    public static Task<List<int>> NumerosDeSerieAsync(
        ContenedorBdFixture bd,
        Guid serieId,
        CancellationToken ct) =>
        EnContextoAsync(bd, c => c.NumeroSerie.AsNoTracking()
            .Where(n => n.SerieId == serieId)
            .Select(n => n.Numero)
            .OrderBy(n => n)
            .ToListAsync(ct));

    /// <summary>Contador de la serie, tal y como queda después de lo que hizo la prueba.</summary>
    public static Task<int> SiguienteDeSerieAsync(
        ContenedorBdFixture bd,
        Guid serieId,
        CancellationToken ct) =>
        EnContextoAsync(bd, c => c.Serie.AsNoTracking().Where(s => s.Id == serieId)
            .Select(s => s.Siguiente)
            .SingleAsync(ct));

    /// <summary>
    /// Alerta viva de una clave. Por defecto devuelve la que no está cerrada; con
    /// <paramref name="otra"/> devuelve cualquiera, que es lo que hace falta para comprobar que una
    /// alerta cerrada sigue en la base (cerrar no es borrar).
    /// </summary>
    public static Task<Alerta?> AlertaAsync(
        ContenedorBdFixture bd,
        string clave,
        CancellationToken ct,
        bool otra = false) =>
        EnContextoAsync(bd, c => otra
            ? c.Alerta.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == clave, ct)
            : c.Alerta.AsNoTracking().FirstOrDefaultAsync(a => a.Clave == clave && a.ResueltaAt == null, ct));

    private static async Task<T> EnContextoAsync<T>(
        ContenedorBdFixture bd,
        Func<AtlasRentDbContext, Task<T>> consulta)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        return await consulta(contexto);
    }
}