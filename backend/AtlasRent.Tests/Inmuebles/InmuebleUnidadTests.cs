using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Inmuebles;

/// <summary>
/// F1-10, inmuebles y unidades (docs/ESPECIFICACION.md §3.1). Las dos reglas que no se pueden
/// violar:
/// <list type="bullet">
/// <item>una unidad solo ofrece las modalidades que tiene permitidas, y su tarifa se elige por modalidad y moneda;</item>
/// <item>el estado manual no es ocupación: ocupada y reservada se calculan por fechas y todavía no existen.</item>
/// </list>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class InmuebleUnidadTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    [Fact]
    public async Task UnaUnidadSoloOfreceLasModalidadesQueTienePermitidasYSuTarifaEsPorModalidadYMoneda()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await SesionAsync(Roles.Gerente, ct);
        var inmueble = await DocumentoInmueblePermisosTests.CrearInmuebleAsync(gerente, ct);

        // Dos modalidades permitidas y una tarifa por modalidad y moneda.
        var alta = await gerente.PostAsync(
            $"/api/v1/inmuebles/{inmueble:D}/unidades",
            new
            {
                nombre = "Oficina 1",
                tipo = "Oficina",
                modalidades = new[] { "Mensual", "MedioDia" },
                tarifas = new object[]
                {
                    new { modalidad = "Mensual", moneda = "USD", importe = 1200.00m },
                    new { modalidad = "Mensual", moneda = "DOP", importe = 70000.00m },
                    new { modalidad = "MedioDia", moneda = "USD", importe = 250.50m },
                },
                metrosCuadrados = 42.5m,
                capacidad = 6,
            },
            ct);
        alta.StatusCode.Should().Be(HttpStatusCode.OK, await DocumentoInmueblePermisosTests.textoDe(alta, ct));

        var unidad = await alta.Content.ReadFromJsonAsync<JsonElement>(ct);
        unidad.GetProperty("modalidades").EnumerateArray()
            .Select(m => m.GetString())
            .Should()
            .BeEquivalentTo(["Mensual", "MedioDia"]);

        // La tarifa se elige por modalidad y por moneda: no hay conversión entre monedas.
        var tarifas = unidad.GetProperty("tarifas").EnumerateArray()
            .ToDictionary(
                t => $"{t.GetProperty("modalidad").GetString()}/{t.GetProperty("moneda").GetString()}",
                t => t.GetProperty("importe").LeerDecimal());
        tarifas["Mensual/USD"].Should().Be(1200.00m);
        tarifas["Mensual/DOP"].Should().Be(70000.00m);
        tarifas["MedioDia/USD"].Should().Be(250.50m);
        tarifas.Should().HaveCount(3);

        // Día completo no está permitido: una tarifa suya se rechaza.
        var noPermitida = await gerente.PostAsync(
            $"/api/v1/inmuebles/{inmueble:D}/unidades",
            new
            {
                nombre = "Oficina 2",
                tipo = "Oficina",
                modalidades = new[] { "Mensual" },
                tarifas = new object[]
                {
                    new { modalidad = "DiaCompleto", moneda = "USD", importe = 900m },
                },
            },
            ct);
        noPermitida.StatusCode.Should().Be(HttpStatusCode.BadRequest, "una modalidad no permitida no tiene tarifa");

        // Y tampoco puede haber dos tarifas de la misma modalidad en la misma moneda.
        var repetida = await gerente.PostAsync(
            $"/api/v1/inmuebles/{inmueble:D}/unidades",
            new
            {
                nombre = "Oficina 3",
                tipo = "Oficina",
                modalidades = new[] { "Mensual" },
                tarifas = new object[]
                {
                    new { modalidad = "Mensual", moneda = "USD", importe = 900m },
                    new { modalidad = "Mensual", moneda = "USD", importe = 950m },
                },
            },
            ct);
        repetida.StatusCode.Should().Be(HttpStatusCode.BadRequest, "una unidad no tiene dos tarifas de lo mismo");

        // Una unidad sin ninguna modalidad no se puede alquilar.
        var sinModalidades = await gerente.PostAsync(
            $"/api/v1/inmuebles/{inmueble:D}/unidades",
            new { nombre = "Oficina 4", tipo = "Oficina", modalidades = Array.Empty<string>() },
            ct);
        sinModalidades.StatusCode.Should().Be(HttpStatusCode.BadRequest, "una unidad sin modalidades no se puede alquilar");
    }

    [Fact]
    public async Task ElEstadoManualSeGuardaYSeFiltraYNoEsOcupacion()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await SesionAsync(Roles.Gerente, ct);
        var inmueble = await DocumentoInmueblePermisosTests.CrearInmuebleAsync(gerente, ct);

        foreach (var (nombre, estado) in new[]
                 {
                     ("Sala A", "Disponible"),
                     ("Sala B", "Mantenimiento"),
                     ("Sala C", "FueraDeServicio"),
                 })
        {
            (await gerente.PostAsync(
                $"/api/v1/inmuebles/{inmueble:D}/unidades",
                new { nombre, tipo = "Sala", estadoManual = estado, modalidades = new[] { "Mensual" } },
                ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // El filtro trae solo lo que se pide.
        var disponibles = await NombresDeUnidadesAsync(gerente, inmueble, "Disponible", ct);
        disponibles.Should().BeEquivalentTo(["Sala A"]);

        var mantenimiento = await NombresDeUnidadesAsync(gerente, inmueble, "Mantenimiento", ct);
        mantenimiento.Should().BeEquivalentTo(["Sala B"]);

        var fuera = await NombresDeUnidadesAsync(gerente, inmueble, "FueraDeServicio", ct);
        fuera.Should().BeEquivalentTo(["Sala C"]);

        // Sin filtro salen las tres: mantenimiento y fuera de servicio son del mismo alquiler.
        (await NombresDeUnidadesAsync(gerente, inmueble, null, ct)).Should().HaveCount(3);

        // El estado manual cambia por la vía normal, sin tocar nada más de la unidad.
        var ficha = await (await gerente.GetAsync($"/api/v1/inmuebles/{inmueble:D}", ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);
        var salaB = ficha.GetProperty("unidades").EnumerateArray()
            .Single(u => u.GetProperty("nombre").GetString() == "Sala B");
        var id = salaB.GetProperty("id").GetGuid();

        var cambio = await gerente.PutAsync(
            $"/api/v1/inmuebles/unidades/{id:D}",
            new
            {
                nombre = "Sala B",
                tipo = "Sala",
                estadoManual = "Disponible",
                modalidades = new[] { "Mensual" },
                medidor = "medidor-2",
            },
            ct);
        cambio.StatusCode.Should().Be(HttpStatusCode.OK, await DocumentoInmueblePermisosTests.textoDe(cambio, ct));

        (await NombresDeUnidadesAsync(gerente, inmueble, "Disponible", ct))
            .Should()
            .BeEquivalentTo(["Sala A", "Sala B"]);

        // Y el estado manual es lo único que hay: ocupada y reservada no son un campo porque se
        // calculan por fechas (AGENTS.md §6). Si un día aparecen, esta comprobación falla.
        var unidadTrasElCambio = await (await gerente.GetAsync($"/api/v1/inmuebles/{inmueble:D}", ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);
        var propiedades = unidadTrasElCambio.GetProperty("unidades").EnumerateArray()
            .SelectMany(u => u.EnumerateObject().Select(p => p.Name))
            .ToList();
        propiedades.Should().NotContain("ocupada");
        propiedades.Should().NotContain("reservada");
        propiedades.Should().NotContain("ocupacion");
    }

    [Fact]
    public async Task ElEmpleadoGestionaLosInmueblesYMantieneLasUnidades()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(Roles.Empleado, ct);

        var alta = await empleado.PostAsync(
            "/api/v1/inmuebles",
            new
            {
                nombre = $"Local {Guid.NewGuid():N}",
                tipo = "Local",
                direccion = "Calle Duarte 1",
                plantillaContratoPorDefecto = "standard",
                renovacionDefectoMeses = 12,
            },
            ct);
        alta.StatusCode.Should().Be(HttpStatusCode.OK);
        var inmueble = (await alta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();

        var unidad = await empleado.PostAsync(
            $"/api/v1/inmuebles/{inmueble:D}/unidades",
            new
            {
                nombre = "Mostrador",
                tipo = "Local",
                modalidades = new[] { "DiaCompleto" },
                tarifas = new object[] { new { modalidad = "DiaCompleto", moneda = "DOP", importe = 5000m } },
                banos = 1.5m,
            },
            ct);
        unidad.StatusCode.Should().Be(HttpStatusCode.OK, await DocumentoInmueblePermisosTests.textoDe(unidad, ct));

        var creada = await unidad.Content.ReadFromJsonAsync<JsonElement>(ct);
        creada.GetProperty("banos").LeerDecimal().Should().Be(1.5m, "los baños van con medios");

        // La baja del inmueble es lógica: desaparece de la lista y no aparece en la ficha.
        (await empleado.DeleteAsync($"/api/v1/inmuebles/{inmueble:D}", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var lista = await (await empleado.GetAsync("/api/v1/inmuebles", ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);
        lista.GetProperty("data").EnumerateArray()
            .Should()
            .NotContain(i => i.GetProperty("id").GetGuid() == inmueble);
    }

    private static async Task<List<string>> NombresDeUnidadesAsync(
        SesionCliente sesion,
        Guid inmueble,
        string? estado,
        CancellationToken ct)
    {
        var filtro = estado is null ? string.Empty : $"?estado={estado}";
        var respuesta = await sesion.GetAsync($"/api/v1/inmuebles/{inmueble:D}/unidades{filtro}", ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);

        var pagina = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);

        return pagina.GetProperty("data").EnumerateArray()
            .Select(u => u.GetProperty("nombre").GetString()!)
            .ToList();
    }

    private async Task<SesionCliente> SesionAsync(string rol, CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: rol, ct: ct);
        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }

    /// <summary>SEC-030: un importe que no cabe en numeric(18,2) es un 400, no un 500 al guardar.</summary>
    [Fact]
    public async Task UnaTarifaQueNoCabeEnLaColumnaSeRechazaConUn400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await SesionAsync(Roles.Gerente, ct);
        var inmueble = await DocumentoInmueblePermisosTests.CrearInmuebleAsync(gerente, ct);

        var respuesta = await gerente.PostAsync(
            $"/api/v1/inmuebles/{inmueble:D}/unidades",
            new
            {
                nombre = "Oficina 2",
                tipo = "Oficina",
                modalidades = new[] { "Mensual" },
                tarifas = new object[] { new { modalidad = "Mensual", moneda = "USD", importe = 1e17m } },
            },
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
