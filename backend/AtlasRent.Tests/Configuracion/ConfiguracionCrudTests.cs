using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace AtlasRent.Tests.Configuracion;

/// <summary>
/// F1-08: el resto de la pantalla de configuración. Paginación y orden con el envoltorio de
/// <c>docs/DOC_TECNICA.md §6</c>, <c>Idempotency-Key</c> en las mutaciones, series iniciales de la
/// especificación, baja lógica y errores con código de referencia.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ConfiguracionCrudTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>Las listas vienen paginadas y se pueden ordenar por el campo que pida el cliente.</summary>
    [Fact]
    public async Task LasListasVienenPaginadasYSePuedenOrdenar()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionDeAdminAsync(ct);

        foreach (var nombre in new[] { "Gamma", "Alfa", "Delta", "Beta" })
        {
            (await sesion.PostAsync(
                "/api/v1/config/subempresas",
                new { nombre, propietaria = true, facturadora = false, activa = true },
                ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var cuerpo = await (await sesion.GetAsync("/api/v1/config/subempresas?page=1&pageSize=2", ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);

        cuerpo.GetProperty("page").GetInt32().Should().Be(1);
        cuerpo.GetProperty("pageSize").GetInt32().Should().Be(2);
        cuerpo.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(4);
        cuerpo.GetProperty("totalPages").GetInt32().Should().BeGreaterThan(1);

        var primeraPagina = cuerpo.GetProperty("data").EnumerateArray()
            .Select(s => s.GetProperty("nombre").GetString()!)
            .ToList();
        primeraPagina.Should().BeInAscendingOrder("sin sortBy se ordena por nombre");

        var segundaPagina = await (await sesion.GetAsync("/api/v1/config/subempresas?page=2&pageSize=2", ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);
        var nombresSegunda = segundaPagina.GetProperty("data").EnumerateArray()
            .Select(s => s.GetProperty("nombre").GetString()!)
            .ToList();

        nombresSegunda.Should().NotIntersectWith(primeraPagina, "las páginas no se repiten");

        var descendente = await (await sesion.GetAsync(
            "/api/v1/config/subempresas?page=1&pageSize=4&sortBy=nombre&sortDir=desc", ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);

        descendente.GetProperty("data").EnumerateArray()
            .Select(s => s.GetProperty("nombre").GetString()!)
            .Should().BeInDescendingOrder();

        // Un campo que no existe se rechaza, para que una errata no pase por un orden válido.
        (await sesion.GetAsync("/api/v1/config/subempresas?sortBy=noExiste", ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Repetir la misma mutación con la misma clave no la duplica (AGENTS.md §6).</summary>
    [Fact]
    public async Task LaIdempotenciaNoDuplicaLaMutacion()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionDeAdminAsync(ct);

        var clave = $"clave-{Guid.NewGuid():N}";
        var cuerpo = new { nombre = $"Forma {clave}", activa = true };

        var primera = await sesion.PostAsync(
            "/api/v1/config/formas-pago", cuerpo, clave, ct);
        var segunda = await sesion.PostAsync(
            "/api/v1/config/formas-pago", cuerpo, clave, ct);

        primera.StatusCode.Should().Be(HttpStatusCode.OK);
        segunda.StatusCode.Should().Be(HttpStatusCode.OK);

        var id = (await primera.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var cuerpoSegunda = await segunda.Content.ReadAsStringAsync(ct);
        cuerpoSegunda.Should().StartWith("{", "el reintento devuelve el cuerpo de la primera respuesta");

        var idSegunda = JsonDocument.Parse(cuerpoSegunda).RootElement.GetProperty("id").GetGuid();
        idSegunda.Should().Be(id, "el reintento devuelve la misma respuesta");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.FormaPago.AsNoTracking().CountAsync(f => f.Nombre == cuerpo.nombre, ct))
            .Should().Be(1);
    }

    /// <summary>La misma clave con otro cuerpo no es un reintento: es un conflicto.</summary>
    [Fact]
    public async Task LaMismaClaveConOtroCuerpoEsUnConflicto()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionDeAdminAsync(ct);

        var clave = $"clave-{Guid.NewGuid():N}";

        (await sesion.PostAsync(
            "/api/v1/config/formas-pago", new { nombre = "Uno", activa = true }, clave, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var repetida = await sesion.PostAsync(
            "/api/v1/config/formas-pago", new { nombre = "Otro", activa = true }, clave, ct);

        repetida.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await repetida.Content.ReadAsStringAsync(ct)).Should().Contain(Problemas.IdempotenciaEnConflicto);
    }

    /// <summary>Las series iniciales son las que fija la especificación §3.7.</summary>
    [Fact]
    public async Task LasSeriesInicialesSonLasDeLaEspecificacion()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionDeAdminAsync(ct);

        var series = await (await sesion.GetAsync("/api/v1/config/series?pageSize=100", ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);

        var porNumero = series.GetProperty("data").EnumerateArray()
            .ToDictionary(s => s.GetProperty("numero").GetInt32());

        porNumero.Keys.Should().Contain([1, 2, 3, 4, 9]);

        porNumero[1].GetProperty("moneda").GetString().Should().Be("USD");
        porNumero[1].GetProperty("sumaAFacturado").GetBoolean().Should().BeTrue();

        porNumero[2].GetProperty("moneda").GetString().Should().Be("DOP");

        porNumero[3].GetProperty("tipo").GetString().Should().Be("Fiscal");
        porNumero[3].GetProperty("generaComprobante").GetBoolean().Should().BeTrue();
        porNumero[3].GetProperty("moneda").ValueKind.Should().Be(JsonValueKind.Null, "la serie fiscal es de moneda libre");

        porNumero[4].GetProperty("tipo").GetString().Should().Be("Proforma");
        porNumero[4].GetProperty("sumaAFacturado").GetBoolean().Should().BeFalse();

        porNumero[9].GetProperty("tipo").GetString().Should().Be("Historica");
    }

    /// <summary>
    /// El contador de una serie no lo toca la edición de la configuración: solo lo mueve la
    /// emisión (docs/DOC_TECNICA.md §4.3).
    /// </summary>
    [Fact]
    public async Task EditarUnaSerieNoMoveElContador()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionDeAdminAsync(ct);

        var prefijo = $"T{Guid.NewGuid():N}"[..8];
        var creada = await sesion.PostAsync(
            "/api/v1/config/series",
            new
            {
                numero = 71,
                prefijo,
                tipo = "Normal",
                moneda = "USD",
                sumaAFacturado = true,
                sumaASaldos = true,
                generaComprobante = false,
                activa = true,
            },
            ct);
        creada.StatusCode.Should().Be(HttpStatusCode.OK);
        var id = (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var servicio = new ServicioSeries(contexto, new AuditoriaService(contexto), TimeProvider.System);
            await servicio.ReservarNumeroAsync(id, Guid.NewGuid(), ct);
        }

        var edicion = await sesion.PutAsync(
            $"/api/v1/config/series/{id:D}",
            new
            {
                numero = 71,
                prefijo,
                tipo = "Normal",
                moneda = "USD",
                sumaAFacturado = true,
                sumaASaldos = false,
                generaComprobante = false,
                activa = true,
            },
            ct);

        edicion.StatusCode.Should().Be(HttpStatusCode.OK);
        (await edicion.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("siguiente").GetInt32()
            .Should().Be(2, "se emitió un número y la edición no lo deshace");
    }

    /// <summary>
    /// Nada se borra: la baja de una configuración la deja inactiva y conservada. Aquí una forma de
    /// pago que los cobros ya usan.
    /// </summary>
    [Fact]
    public async Task LaBajaLogicaNoBorraYQuedaAuditada()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionDeAdminAsync(ct);

        var nombre = $"Cuenta {Guid.NewGuid():N}";
        var creada = await sesion.PostAsync(
            "/api/v1/config/cuentas",
            new { nombre, banco = "Banco de prueba", moneda = "DOP", activa = true },
            ct);
        creada.StatusCode.Should().Be(HttpStatusCode.OK);
        var id = (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();

        (await sesion.DeleteAsync($"/api/v1/config/cuentas/{id:D}", ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.CuentaReceptora.AsNoTracking().SingleAsync(c => c.Id == id, ct)).Activa
            .Should().BeFalse();

        await using var conexion = new NpgsqlConnection(bd.CadenaSuperusuario);
        await conexion.OpenAsync(ct);
        await using var comando = new NpgsqlCommand(
            "SELECT accion FROM auditoria WHERE entidad = 'cuenta_receptora' AND entidad_id = @id", conexion);
        comando.Parameters.Add(new NpgsqlParameter("id", NpgsqlTypes.NpgsqlDbType.Text) { Value = id.ToString("D") });
        var acciones = new List<string>();
        await using (var lector = await comando.ExecuteReaderAsync(ct))
        {
            while (await lector.ReadAsync(ct))
            {
                acciones.Add(lector.GetString(0));
            }
        }

        acciones.Should().BeEquivalentTo(["cuenta_receptora.creada", "cuenta_receptora.desactivada"]);
    }

    /// <summary>Los errores llevan su código de referencia (docs/DOC_TECNICA.md §6).</summary>
    [Fact]
    public async Task LosErroresLlevanCodigoDeReferencia()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionDeAdminAsync(ct);

        // Dos series con el mismo número no pueden existir.
        var primera = await sesion.PostAsync(
            "/api/v1/config/series",
            new { numero = 91, prefijo = $"X{Guid.NewGuid():N}"[..8], tipo = "Normal", moneda = "USD", activa = true },
            ct);
        primera.StatusCode.Should().Be(HttpStatusCode.OK);

        var duplicada = await sesion.PostAsync(
            "/api/v1/config/series",
            new { numero = 91, prefijo = $"Y{Guid.NewGuid():N}"[..8], tipo = "Normal", moneda = "USD", activa = true },
            ct);
        duplicada.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await duplicada.Content.ReadAsStringAsync(ct)).Should().Contain(Problemas.ValorDuplicado);

        // Un id que no existe.
        (await sesion.PutAsync(
            $"/api/v1/config/formas-pago/{Guid.NewGuid():D}", new { nombre = "No existe", activa = true }, ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Un porcentaje fuera de rango.
        (await sesion.PostAsync("/api/v1/config/tipos-itbis", new { nombre = "Fuera", porcentaje = 140m }, ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Las tolerancias y el modo de recordatorios son una fila sola y configurable.</summary>
    [Fact]
    public async Task LasToleranciasYElModoDeRecordatoriosSonConfigurables()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionDeAdminAsync(ct);

        var inicial = await (await sesion.GetAsync("/api/v1/config/ajustes", ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);

        // Nacen sin valor: la especificación los da como supuesto, y un supuesto no es un dato
        // (AGENTS.md §5). Nadie envía mensajes sin que el administrador lo pida.
        inicial.GetProperty("toleranciaCambioUsd").ValueKind.Should().Be(JsonValueKind.Null);
        inicial.GetProperty("modoRecordatorios").GetString().Should().Be("Apagado");

        var cambio = await sesion.PutAsync(
            "/api/v1/config/ajustes",
            new { toleranciaCambioUsd = 2.345m, toleranciaCambioDop = 60m, modoRecordatorios = "Manual" },
            ct);

        cambio.StatusCode.Should().Be(HttpStatusCode.OK);
        var guardado = await cambio.Content.ReadFromJsonAsync<JsonElement>(ct);
        guardado.GetProperty("toleranciaCambioUsd").GetDecimal().Should().Be(2.35m, "tolerancia de importe: 2 decimales");
        guardado.GetProperty("toleranciaCambioDop").GetDecimal().Should().Be(60m);
        guardado.GetProperty("modoRecordatorios").GetString().Should().Be("Manual");

        var releido = await (await sesion.GetAsync("/api/v1/config/ajustes", ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);
        releido.GetProperty("modoRecordatorios").GetString().Should().Be("Manual");
    }

    private async Task<SesionCliente> SesionDeAdminAsync(CancellationToken ct)
    {
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        return await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);
    }
}