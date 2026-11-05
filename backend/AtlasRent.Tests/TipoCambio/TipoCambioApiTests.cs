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

namespace AtlasRent.Tests.TipoCambio;

/// <summary>
/// F3-01 por la API: el histórico con filtros y paginación, quién puede cargarlo y qué responde la
/// consulta del valor de un día.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class TipoCambioApiTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// El histórico se pagina y se filtra por moneda y por rango de fechas, que es como lo va a
    /// necesitar un informe consolidado.
    /// <para>
    /// Las fechas se cargan en días propios de esta prueba y la comparación es por recuento, no por
    /// número absoluto de filas: la base es una sola para toda la colección y otras pruebas pueden
    /// haber cargado antes.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ElHistoricoSePaginaYSeFiltraPorMonedaYFechas()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);

        var desde = AyudaTipoCambio.Dia(20);
        var hasta = AyudaTipoCambio.Dia(23);
        var cargados = 0;

        for (var dia = desde; dia <= hasta; dia = dia.AddDays(1))
        {
            var respuesta = await sesion.PostAsync(
                "/api/v1/tipos-cambio",
                new { fecha = dia.ToString("yyyy-MM-dd"), moneda = "DOP", valor = 60m + cargados },
                ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
            cargados++;
        }

        // Fuera del rango, para que el filtro tenga algo que excluir.
        (await sesion.PostAsync(
            "/api/v1/tipos-cambio",
            new { fecha = AyudaTipoCambio.Dia(60).ToString("yyyy-MM-dd"), moneda = "DOP", valor = 59m },
            ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var pagina = await sesion.GetAsync(
            $"/api/v1/tipos-cambio?moneda=DOP&desde={desde:yyyy-MM-dd}&hasta={hasta:yyyy-MM-dd}"
                + "&page=1&pageSize=2&sortBy=fecha&sortDir=asc",
            ct);

        pagina.StatusCode.Should().Be(HttpStatusCode.OK);

        var cuerpo = await pagina.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("total").GetInt32().Should().Be(4, "solo los cuatro días del rango");
        cuerpo.GetProperty("totalPages").GetInt32().Should().Be(2, "dos por página y cuatro filas");
        cuerpo.GetProperty("pageSize").GetInt32().Should().Be(2);

        var filas = cuerpo.GetProperty("data").EnumerateArray().ToList();
        filas.Should().HaveCount(2, "la primera página trae dos filas");
        filas.Should().OnlyContain(f => f.GetProperty("moneda").GetString() == "DOP");
        filas.Should().OnlyContain(f => f.GetProperty("fuente").GetString() == "manual",
            "las cargó una persona");
        filas.Select(f => f.GetProperty("fecha").GetString())
            .Should().Equal([desde.ToString("yyyy-MM-dd"), desde.AddDays(1).ToString("yyyy-MM-dd")],
                "orden ascendente por fecha");

        // El filtro por fecha funciona por el otro lado: un día suelto.
        var unDia = await sesion.GetAsync(
            $"/api/v1/tipos-cambio?desde={hasta:yyyy-MM-dd}&hasta={hasta:yyyy-MM-dd}", ct);
        (await unDia.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("total").GetInt32().Should().Be(1);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.TipoCambio.CountAsync(t => t.Moneda == Moneda.DOP && t.Fecha == hasta, ct))
            .Should().Be(1, "cargar dos veces el mismo día actualiza la fila, no la duplica");
    }

    /// <summary>
    /// El empleado ve el histórico pero no carga el tipo de cambio.
    /// <para>
    /// Se quita y se prueba por HTTP, no mirando el servicio: el permiso es una de las cuatro
    /// paredes del sistema y lo que importa es que el 403 llegue de la API.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ElEmpleadoNoCargaElTipoDeCambio()
    {
        var ct = TestContext.Current.CancellationToken;
        var empleado = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, empleado, Contrasena, ct);

        int antes;
        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            antes = await contexto.TipoCambio.CountAsync(ct);
        }

        (await sesion.PostAsync(
            "/api/v1/tipos-cambio",
            new { moneda = "DOP", valor = 61m },
            ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "cargar un tipo de cambio es configuracion.editar, y el empleado no la tiene");

        await using (var comprobacion = bd.CrearContexto(bd.CadenaApp))
        {
            (await comprobacion.TipoCambio.CountAsync(ct)).Should().Be(antes,
                "el 403 es antes de tocar nada: no hay fila a medias ni auditoría de un cambio que no ocurrió");
        }

        // Pero sí lo ve: leer el histórico es factura.ver, que el empleado tiene.
        (await sesion.GetAsync("/api/v1/tipos-cambio?page=1&pageSize=5", ct)).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// El dólar vale 1 sin fila en la tabla, y una moneda sin tipo de cambio para ese día da un 409
    /// con el motivo y no un 500.
    /// <para>
    /// Lo primero es lo que hace que el histórico no se llene de filas que dicen siempre lo mismo; lo
    /// segundo es el error de negocio que la factura se va a encontrar el día que falte un cambio, y
    /// tiene que ser un 409 legible —con la fecha y la moneda, para que quien lo lea sepa qué
    /// cargar— y no una excepción sin traducir.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ElDolarValUnoSinFilaYUnaMonedaSinValorDa409()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);

        var dolar = await sesion.GetAsync("/api/v1/tipos-cambio/actual?moneda=USD", ct);
        dolar.StatusCode.Should().Be(HttpStatusCode.OK);

        var cuerpo = await dolar.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("valor").LeerDecimal().Should().Be(1m, "el dólar es la base de informes (D9)");
        cuerpo.GetProperty("id").ValueKind.Should().Be(JsonValueKind.Null, "y no tiene fila");

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            (await contexto.TipoCambio.CountAsync(t => t.Moneda == Moneda.USD, ct)).Should().Be(0,
                "el dólar no se guarda: se resuelve sin consultar");
        }

        // Una fecha en la que no se ha cargado nada. El CHECK de la base no deja ni un USD colarse.
        var sinValor = await sesion.GetAsync(
            $"/api/v1/tipos-cambio/actual?moneda=DOP&fecha={AyudaTipoCambio.Dia(80):yyyy-MM-dd}", ct);

        sinValor.StatusCode.Should().Be(HttpStatusCode.Conflict, "no hay cambio para ese día");

        var problema = await sinValor.Content.ReadFromJsonAsync<JsonElement>(ct);
        problema.GetProperty("detail").GetString().Should().Contain("No hay tipo de cambio");
        problema.GetProperty("detail").GetString().Should().Contain("DOP");

        // Cargar un tipo de cambio del dólar tampoco: vale 1 por definición y no se guarda.
        (await sesion.PostAsync(
            "/api/v1/tipos-cambio",
            new { moneda = "USD", valor = 1m },
            ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await sesion.PostAsync(
            "/api/v1/tipos-cambio",
            new { moneda = "DOP", valor = 0m },
            ct)).StatusCode.Should().Be(HttpStatusCode.Conflict,
            "un tipo de cambio no positivo no es un dato que pueda quedar en la tabla");

        // Sin sesión tampoco hay nada.
        using var anonimo = api.CrearCliente();
        (await anonimo.GetAsync("/api/v1/tipos-cambio/actual?moneda=USD", ct)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }
}
