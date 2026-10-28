using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Facturas;

/// <summary>
/// F3-02: la emisión. Número de serie, congelado del tipo de cambio, auditoría con IP, y los tres
/// conflictos que importan: que no falte el cambio del día, que la serie no admita la moneda y que un
/// fallo posterior a gastar el número no deje el número consumido.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class FacturaEmisionTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    /// <summary>
    /// Emitir asigna el número siguiente de la serie, pasa la factura a emitida y congela el cambio
    /// del día. Todo se comprueba leyendo la fila, no el DTO: el DTO es lo que devuelve la
    /// aplicación y sería justo lo que hay que poner en duda.
    /// </summary>
    [Fact]
    public async Task EmitirAsignaNumeroYCongelaElCambioDelDia()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct);
        // Serie de moneda libre, porque el congelado del cambio solo se puede ver en una moneda que
        // no sea el dólar (F3-01: el dólar vale 1 y no tiene fila).
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Fiscal, null, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        // El día de esta prueba es el de la aplicación, que puede estar adelantado por otra prueba.
        var tipoCambioId = await CargarCambioDelDiaAsync(sesion, hoy, 60.123456m);

        var facturaId = await AyudaFacturas.BorradorConLineaAsync(
            sesion, clienteId, serieId, Moneda.DOP, ct);

        var emitida = await sesion.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);
        emitida.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var fila = await contexto.Factura.AsNoTracking().SingleAsync(f => f.Id == facturaId, ct);

        fila.Estado.Should().Be(EstadoFactura.Emitida);
        fila.Numero.Should().Be(1, "es la primera emisión de una serie que arranca en 1");
        fila.FechaEmision.Should().Be(hoy);
        fila.TipoCambioValor.Should().Be(60.123456m, "el cambio queda congelado en el documento");
        fila.TipoCambioFecha.Should().Be(hoy);
        fila.TipoCambioId.Should().Be(tipoCambioId, "y con él se guarda la fila de la que salió");

        (await contexto.NumeroSerie.AsNoTracking().CountAsync(n => n.SerieId == serieId, ct))
            .Should().Be(1, "el número emitido queda en el histórico de la serie");

        await using var contextoSinRls = ContextoSinRls();
        var auditoria = await contextoSinRls.Auditoria.AsNoTracking()
            .SingleAsync(a => a.Accion == "factura.emitida" && a.EntidadId == facturaId.ToString("D"), ct);

        auditoria.Ip.Should().NotBeNullOrWhiteSpace("toda mutación se audita con su IP (AGENTS.md §6)");
        auditoria.Entidad.Should().Be("factura");
        auditoria.Resultado.Should().Be("permitido");
    }

    /// <summary>
    /// Sin tipo de cambio del día no se emite: la salida es la carga manual de §3.12, y el 409 la
    /// dice. La factura sigue en borrador y la serie no gasta el número.
    /// </summary>
    [Fact]
    public async Task SinTipoDeCambioDelDiaLaEmisionFallaYNoGastaElNumero()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);
        var apartado = await ApartarCambioDelDiaAsync(hoy, ct);

        try
        {
            var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct);
            var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Fiscal, null, ct);
            var facturaId = await AyudaFacturas.BorradorConLineaAsync(
                sesion, clienteId, serieId, Moneda.DOP, ct);

            var emitida = await sesion.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);

            emitida.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await emitida.Content.ReadAsStringAsync(ct)).Should().Contain("tipo de cambio");

            await using var contexto = bd.CrearContexto(bd.CadenaApp);
            var fila = await contexto.Factura.AsNoTracking().SingleAsync(f => f.Id == facturaId, ct);
            fila.Estado.Should().Be(EstadoFactura.Borrador);
            fila.Numero.Should().BeNull();
            fila.TipoCambioValor.Should().BeNull();

            (await contexto.Serie.AsNoTracking().SingleAsync(s => s.Id == serieId, ct)).Siguiente
                .Should().Be(1, "el fallo de la fuente de cambios no puede gastar un número");

            (await contexto.NumeroSerie.AsNoTracking().CountAsync(n => n.SerieId == serieId, ct))
                .Should().Be(0);
        }
        finally
        {
            await DevolverCambioApartadoAsync(apartado, ct);
        }
    }

    /// <summary>
    /// El fallo que viene <b>después</b> de gastar el número es el que no se ve en las pruebas
    ///happiest-path: aquí se fuerza con un número que la serie ya tiene en su histórico, así que el
    /// índice único de (serie, número) hace fallar el guardado de la factura con el número ya
    /// reservado. La factura no sale, el contador no se mueve y no queda fila en el histórico: el
    /// número no se consumió.
    /// </summary>
    [Fact]
    public async Task UnFalloDespuesDeReservarElNumeroNoLoConsume()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var facturaId = await AyudaFacturas.BorradorConLineaAsync(
            sesion, clienteId, serieId, Moneda.USD, ct);

        const int NumeroYaUsado = 500;

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            // La serie va a sacar el 500, que ya está ocupado: el guardado de la factura va a chocar
            // con el índice único, y eso pasa después de reservar el número.
            var serie = await contexto.Serie.SingleAsync(s => s.Id == serieId, ct);
            serie.Siguiente = NumeroYaUsado;

            contexto.NumeroSerie.Add(new NumeroSerie
            {
                Id = Guid.NewGuid(),
                SerieId = serieId,
                Numero = NumeroYaUsado,
                Estado = EstadoNumeroSerie.Emitido,
                EmitidoAt = DateTimeOffset.UtcNow,
                EmitidoPor = Guid.NewGuid(),
            });

            await contexto.SaveChangesAsync(ct);
        }

        var emitida = await sesion.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);

        emitida.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var fila = await contexto.Factura.AsNoTracking().SingleAsync(f => f.Id == facturaId, ct);
            fila.Estado.Should().Be(EstadoFactura.Borrador, "la factura no llegó a salir");
            fila.Numero.Should().BeNull();

            (await contexto.Serie.AsNoTracking().SingleAsync(s => s.Id == serieId, ct)).Siguiente
                .Should().Be(NumeroYaUsado, "la transacción entera se deshizo: el contador no se movió");

            (await contexto.NumeroSerie.AsNoTracking().CountAsync(n => n.SerieId == serieId, ct))
                .Should().Be(1, "sigue solo el número que había antes: la emisión no dejó fila");

            await using var contextoSinRls = ContextoSinRls();
            (await contextoSinRls.Auditoria.AsNoTracking().CountAsync(
                a => a.Accion == "factura.emitida" && a.EntidadId == facturaId.ToString("D"), ct))
                .Should().Be(0, "la auditoría va en la misma transacción que la factura");
        }
    }

    /// <summary>
    /// Un reintento con la misma <c>Idempotency-Key</c> devuelve lo que devolvió la primera vez y no
    /// saca un segundo número: emitir dos veces la misma factura es una factura con dos números.
    /// </summary>
    [Fact]
    public async Task EmitirDosVecesConLaMismaClaveEmiteUnaSolaFactura()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var facturaId = await AyudaFacturas.BorradorConLineaAsync(
            sesion, clienteId, serieId, Moneda.USD, ct);

        var clave = $"emitir-{Guid.NewGuid():N}";

        var primera = await sesion.PostAsync(
            $"/api/v1/facturas/{facturaId:D}/emitir", new { }, clave, ct);
        var segunda = await sesion.PostAsync(
            $"/api/v1/facturas/{facturaId:D}/emitir", new { }, clave, ct);

        primera.StatusCode.Should().Be(HttpStatusCode.OK);
        segunda.StatusCode.Should().Be(HttpStatusCode.OK);

        // El reintento devuelve la respuesta guardada. Se comparan los datos y no el texto: la
        // respuesta se guarda en una columna jsonb y PostgreSQL la normaliza (ordena las claves), así
        // que el mismo contenido vuelve con otro orden de propiedades.
        var cuerpoPrimero = await primera.Content.ReadFromJsonAsync<JsonElement>(ct);
        var cuerpoSegundo = await segunda.Content.ReadFromJsonAsync<JsonElement>(ct);

        cuerpoSegundo.GetProperty("id").GetGuid().Should().Be(cuerpoPrimero.GetProperty("id").GetGuid());
        cuerpoSegundo.GetProperty("numero").GetInt32().Should().Be(cuerpoPrimero.GetProperty("numero").GetInt32());
        cuerpoSegundo.GetProperty("estado").GetString().Should().Be(cuerpoPrimero.GetProperty("estado").GetString());
        cuerpoSegundo.GetProperty("total").LeerDecimal().Should().Be(cuerpoPrimero.GetProperty("total").LeerDecimal());

        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        (await contexto.Factura.AsNoTracking().CountAsync(f => f.SerieId == serieId, ct))
            .Should().Be(1);
        (await contexto.NumeroSerie.AsNoTracking().CountAsync(n => n.SerieId == serieId, ct))
            .Should().Be(1, "el reintento no gastó un segundo número");
        (await contexto.Serie.AsNoTracking().SingleAsync(s => s.Id == serieId, ct)).Siguiente
            .Should().Be(2);
    }

    /// <summary>
    /// Una serie con moneda fija no admite otra (D7); una de moneda libre guarda la que le pidan.
    /// Es la regla que hace que una factura no cambie de moneda por un descuido del formulario.
    /// </summary>
    [Fact]
    public async Task UnaSerieDeMonedaFijaRechazaLaMonedaQueNoEsLaSuya()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var clienteId = await AyudaFacturas.CrearClienteAsync(sesion, ct);
        var serieDolar = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Normal, Moneda.USD, ct);
        var serieLibre = await AyudaFacturas.CrearSerieAsync(sesion, TipoSerie.Fiscal, null, ct);
        var hoy = await AyudaFacturas.HoyAsync(sesion, ct);

        var rechazada = await sesion.PostAsync(
            "/api/v1/facturas",
            new
            {
                clienteId,
                serieId = serieDolar,
                moneda = "DOP",
                fechaVencimiento = hoy.AddDays(30).ToString("yyyy-MM-dd"),
                concepto = "Pesos en la serie de dólares",
            },
            ct);

        rechazada.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var aceptada = await sesion.PostAsync(
            "/api/v1/facturas",
            new
            {
                clienteId,
                serieId = serieLibre,
                moneda = "DOP",
                fechaVencimiento = hoy.AddDays(30).ToString("yyyy-MM-dd"),
                concepto = "Pesos en la serie de moneda libre",
            },
            ct);

        aceptada.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = await aceptada.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("moneda").GetString().Should().Be("DOP");
        cuerpo.GetProperty("estado").GetString().Should().Be("Borrador");
    }

    /// <summary>
    /// <c>auditoria</c> tiene RLS y solo la leen gerente y administrador (MigracionInicial). Para
    /// comprobar que la fila está de verdad se lee por la conexión de superusuario, que es la única
    /// forma de mirar la tabla sin montar a mano el contexto de sesión que fija <c>app.rol</c>.
    /// </summary>
    private AtlasRentDbContext ContextoSinRls() => bd.CrearContexto(bd.CadenaSuperusuario);

    /// <summary>
    /// Carga (o recarga) el cambio de un día. Se hace por la API de F3-01, que es la carga manual que
    /// el propio 409 de la emisión le señala a quien la lee.
    /// </summary>
    private async Task<Guid> CargarCambioDelDiaAsync(SesionCliente sesion, DateOnly fecha, decimal valor)
    {
        var response = await sesion.PostAsync(
            "/api/v1/tipos-cambio",
            new { fecha = fecha.ToString("yyyy-MM-dd"), moneda = "DOP", valor },
            ct: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Deja el día «sin tipo de cambio» sin borrar la fila: la mueve a una fecha reservada y
    /// devuelve la fecha apartada para poder devolverla después.
    /// <para>
    /// Antes se borraba con la conexión de superusuario, y eso tenía dos fallos (BUG-006): si
    /// cualquier documento había congelado ese cambio, el borrado reventaba por clave foránea; y
    /// mientras no reventaba, dejaba sin cambio a las pruebas que vinieran después. Mover la fila
    /// no la destruye —los documentos la referencian por identificador, no por fecha— y
    /// <c>DevolverCambioApartadoAsync</c> la deja como estaba, también si el test falla.
    /// </para>
    /// </summary>
    private async Task<DateOnly> ApartarCambioDelDiaAsync(DateOnly fecha, CancellationToken ct)
    {
        var fechaApartada = new DateOnly(fecha.Year - 5, fecha.Month, fecha.Day);

        await using var contexto = bd.CrearContexto(bd.CadenaSuperusuario);
        await contexto.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE tipo_cambio SET fecha = {fechaApartada} WHERE fecha = {fecha} AND moneda = {(int)Moneda.DOP}",
            ct);

        return fechaApartada;
    }

    /// <summary>
    /// Devuelve a su fecha la fila que apartó <see cref="ApartarCambioDelDiaAsync"/>. Si el día no
    /// tenía cambio, no había fila que apartar y aquí no hay nada que hacer.
    /// </summary>
    private async Task DevolverCambioApartadoAsync(DateOnly fechaApartada, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaSuperusuario);
        await contexto.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE tipo_cambio SET fecha = {fechaApartada.AddYears(5)} WHERE fecha = {fechaApartada} AND moneda = {(int)Moneda.DOP}",
            ct);
    }
}
