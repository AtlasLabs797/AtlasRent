using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Comprobantes;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Periodicas;

/// <summary>
/// F3-05: doble ejecución no duplica (criterio de aceptación de <c>docs/PLAN.md</c>).
/// <para>
/// La prueba no mira lo que la aplicación devuelve y se detiene: mira las filas de
/// <c>factura</c> y la definición del índice en <c>pg_indexes</c>. «No duplica» tiene que ser una
/// propiedad de la base, porque la segunda ejecución puede venir de otra persona, de otro día o de
/// un reintento que ni siquiera pasó por la simulación.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class PeriodicaNoDuplicaTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    /// <summary>
    /// Dos ejecuciones del mismo mes para el mismo contrato dejan una sola factura, y la segunda
    /// responde qué se saltó, de quién y por qué. El número de serie se gasta una vez: si la
    /// segunda ejecución hubiera emitido, la serie habría avanzado dos posiciones.
    /// </summary>
    [Fact]
    public async Task DosEjecucionesDelMismoMesDejanUnaSolaFactura()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var mes = PeriodoFacturacion.DelMes(hoy);

        var (contratoId, _) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, mes.Inicio, duracionMeses: 24);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);

        var primera = await AyudaPeriodicas.GenerarAsync(
            admin, AyudaPeriodicas.Peticion(mes.Inicio, serieId, [contratoId]), ct);

        primera.GetProperty("creadas").GetInt32().Should().Be(1);
        primera.GetProperty("omitidas").GetInt32().Should().Be(0);
        var facturaId = primera.GetProperty("facturas")[0].GetProperty("facturaId").GetGuid();

        // La segunda ejecución no falla: es una operación normal y su respuesta dice qué ya estaba.
        var segunda = await AyudaPeriodicas.GenerarAsync(
            admin, AyudaPeriodicas.Peticion(mes.Inicio, serieId, [contratoId]), ct);

        segunda.GetProperty("creadas").GetInt32().Should().Be(0);
        segunda.GetProperty("omitidas").GetInt32().Should().Be(1);

        var omitido = segunda.GetProperty("omitidos")[0];
        omitido.GetProperty("contratoId").GetGuid().Should().Be(contratoId);
        omitido.GetProperty("facturaExistenteId").GetGuid().Should().Be(facturaId);
        omitido.GetProperty("motivo").GetString().Should().Contain("ya tiene la factura");

        var facturas = await AyudaPeriodicas.FacturasDelPeriodoAsync(bd, contratoId, mes.Inicio, ct);
        facturas.Should().ContainSingle("una factura del periodo, ni una más ni una menos");
        facturas[0].Id.Should().Be(facturaId);

        // Y la serie solo gastó un número: la segunda ejecución ni siquiera llegó a crear nada.
        (await AyudaComprobante.NumerosDeSerieAsync(bd, serieId, ct))
            .Should().ContainSingle();
        (await AyudaComprobante.SiguienteDeSerieAsync(bd, serieId, ct)).Should().Be(2);
    }

    /// <summary>
    /// La red que lo hace cierto está en el índice único parcial, y el índice es el que dice —no el
    /// código— que dos facturas del mismo contrato, periodo y concepto no pueden existir.
    /// <para>
    /// Se comprueba también <c>NULLS NOT DISTINCT</c>: sin eso, la factura de una reserva corta
    /// (que no tiene contrato) quedaría fuera de la protección, porque en PostgreSQL cada nulo es
    /// distinto de los demás.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ElIndiceUnicoDePeriodoEsLaRedYAdmiteNulosNoDistintos()
    {
        var ct = TestContext.Current.CancellationToken;

        var definicion = await AyudaPeriodicas.IndiceDePeriodoAsync(bd, ct);

        definicion.Should().Contain("CREATE UNIQUE INDEX");
        definicion.Should().Contain("NULLS NOT DISTINCT");
        definicion.Should().Contain("contrato_id, periodo_inicio, concepto");
        definicion.Should().Contain("periodo_inicio IS NOT NULL");
        definicion.Should().Contain("estado <> 3",
            "el filtro deja fuera las facturas que no son periódicas y las anuladas");
    }

    /// <summary>
    /// La base, no el código: dos filas con la misma clave no pueden existir. Se prueba Writing en
    /// la tabla directamente, sin pasar por la API, porque es la condición que tiene que ser
    /// imposible aunque alguien escriba por fuera.
    /// </summary>
    [Fact]
    public async Task LaBaseNoAdmiteDosFacturasDelMismoContratoPeriodoYConcepto()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var mes = PeriodoFacturacion.DelMes(hoy);

        var (contratoId, clienteId) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, mes.Inicio, duracionMeses: 24);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var concepto = $"Renta {mes.Inicio:yyyy-MM}";

        await AyudaPeriodicas.GenerarAsync(
            admin, AyudaPeriodicas.Peticion(mes.Inicio, serieId, [contratoId]), ct);

        await using var contexto = bd.CrearContexto(bd.CadenaSuperusuario);

        var repetida = new Factura
        {
            Id = Guid.NewGuid(),
            ClienteId = clienteId,
            ContratoId = contratoId,
            SerieId = serieId,
            Estado = EstadoFactura.Borrador,
            Moneda = Moneda.USD,
            FechaVencimiento = mes.Inicio.AddDays(4),
            Concepto = concepto,
            PeriodoInicio = mes.Inicio,
            PeriodoFin = mes.Fin,
            Subtotal = 0m,
            Itbis = 0m,
            Total = 0m,
            Retencion = 0m,
            NetoACobrar = 0m,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        contexto.Factura.Add(repetida);

        var guardar = () => contexto.SaveChangesAsync(ct);

        await guardar.Should().ThrowAsync<DbUpdateException>(
            "el índice único parcial es la red que impide el duplicado, exista o no el código que lo compruebe");
    }

    /// <summary>
    /// Una factura anulada libera su clave: la que la sustituye sí se puede emitir (F3-08), y su
    /// número no vuelve atrás (D28). El filtro del índice es lo que lo permite.
    /// </summary>
    [Fact]
    public async Task UnaFacturaAnuladaLiberaLaClaveDelPeriodo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var mes = PeriodoFacturacion.DelMes(hoy);

        var (contratoId, _) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, mes.Inicio, duracionMeses: 24);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);

        await AyudaPeriodicas.GenerarAsync(
            admin, AyudaPeriodicas.Peticion(mes.Inicio, serieId, [contratoId]), ct);

        var facturas = await AyudaPeriodicas.FacturasDelPeriodoAsync(bd, contratoId, mes.Inicio, ct);
        var factura = facturas.Should().ContainSingle().Subject;

        await using (var contexto = bd.CrearContexto(bd.CadenaSuperusuario))
        {
            await contexto.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE factura SET estado = 3, updated_at = {DateTimeOffset.UtcNow} WHERE id = {factura.Id}", ct);
        }

        // Con la clave liberada, la simulación vuelve a decir que se emite...
        var item = AyudaPeriodicas.UnItem(await AyudaPeriodicas.SimularAsync(
            admin, AyudaPeriodicas.Peticion(mes.Inicio, serieId, [contratoId]), ct));
        item.GetProperty("yaExiste").GetBoolean().Should().BeFalse("una factura anulada no cuenta como factura del periodo");

        // ...y la ejecución emite otra, con el número siguiente de la serie y no con el anterior.
        var segunda = await AyudaPeriodicas.GenerarAsync(
            admin, AyudaPeriodicas.Peticion(mes.Inicio, serieId, [contratoId]), ct);

        segunda.GetProperty("creadas").GetInt32().Should().Be(1);

        var numeros = await AyudaComprobante.NumerosDeSerieAsync(bd, serieId, ct);
        numeros.Should().BeEquivalentTo([1, 2], "el número de la anulada no vuelve al contador");
    }

    /// <summary>
    /// Facturar dos contratos del mismo mes da dos facturas, no una: la clave es por contrato, y
    /// una ejecución que se saltara uno por llevar el mismo periodo dejaría a un inquilino sin
    /// facturar.
    /// </summary>
    [Fact]
    public async Task DosContratosDelMismoMesDanDosFacturas()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var mes = PeriodoFacturacion.DelMes(hoy);

        var (primero, _) = await AyudaPeriodicas.ContratoActivoAsync(bd, api, ct, mes.Inicio, duracionMeses: 24);
        var (segundo, _) = await AyudaPeriodicas.ContratoActivoAsync(bd, api, ct, mes.Inicio, duracionMeses: 24);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);

        var resultado = await AyudaPeriodicas.GenerarAsync(
            admin, AyudaPeriodicas.Peticion(mes.Inicio, serieId, [primero, segundo]), ct);

        resultado.GetProperty("creadas").GetInt32().Should().Be(2);

        (await AyudaPeriodicas.FacturasDelPeriodoAsync(bd, primero, mes.Inicio, ct)).Should().ContainSingle();
        (await AyudaPeriodicas.FacturasDelPeriodoAsync(bd, segundo, mes.Inicio, ct)).Should().ContainSingle();
    }

    /// <summary>
    /// Un reintento con la misma <c>Idempotency-Key</c> devuelve lo que ya se emitió en vez de
    /// ejecutar otra vez el lote entero.
    /// </summary>
    [Fact]
    public async Task UnReintentoConLaMismaClaveNoEjecutaElLoteDeNuevo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var mes = PeriodoFacturacion.DelMes(hoy);

        var (contratoId, _) = await AyudaPeriodicas.ContratoActivoAsync(
            bd, api, ct, mes.Inicio, duracionMeses: 24);

        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var clave = $"periodicas-{Guid.NewGuid():N}";

        var primera = await admin.PostAsync(
            "/api/v1/facturas/periodicas/generar",
            AyudaPeriodicas.Peticion(mes.Inicio, serieId, [contratoId]),
            clave,
            ct);

        var segunda = await admin.PostAsync(
            "/api/v1/facturas/periodicas/generar",
            AyudaPeriodicas.Peticion(mes.Inicio, serieId, [contratoId]),
            clave,
            ct);

        primera.StatusCode.Should().Be(HttpStatusCode.OK);
        segunda.StatusCode.Should().Be(HttpStatusCode.OK);

        (await AyudaPeriodicas.FacturasDelPeriodoAsync(bd, contratoId, mes.Inicio, ct))
            .Should().ContainSingle();
        (await AyudaComprobante.NumerosDeSerieAsync(bd, serieId, ct)).Should().ContainSingle();
    }
}