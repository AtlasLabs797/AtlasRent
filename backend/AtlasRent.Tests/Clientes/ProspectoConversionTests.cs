using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Tests.Cotizaciones;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Npgsql;
using Xunit;

namespace AtlasRent.Tests.Clientes;

/// <summary>
/// Conversión de un prospecto en cliente (docs/ESPECIFICACION.md §3.2: "recibe número de cliente al
/// convertirse"). Una transacción: cliente nuevo con número de la secuencia, prospecto marcado y
/// vinculado, y sus cotizaciones y reservas pasan al cliente para poder seguir el flujo (§3.3).
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ProspectoConversionTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    private static string Documento() => $"001-{Guid.NewGuid():N}"[..13];

    private static object Alta(string? documento = null, int? numero = null) => new
    {
        nombre = $"Convertido {Guid.NewGuid():N}"[..24],
        tipo = "PersonaFisica",
        documento = documento ?? Documento(),
        telefono = "8090000000",
        numero,
    };

    [Theory]
    [InlineData(Roles.Empleado)]
    [InlineData(Roles.Gerente)]
    [InlineData(Roles.Admin)]
    public async Task CualquierRolConClienteEditarConvierteYTodoSeMueveAlCliente(string rol)
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(rol, ct);
        var prospectoId = await AyudaReserva.ProspectoAsync(sesion, ct);
        var cotizacionId = await AyudaCotizacion.CrearAsync(sesion, null, prospectoId, ct: ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var reservaId = await AyudaReserva.CrearAsync(
            sesion, AyudaReserva.Mensual(null, unidadId, AyudaReserva.Dia(400), prospectoId: prospectoId), ct);

        var documento = Documento();
        var respuesta = await sesion.PostAsync(
            $"/api/v1/prospectos/{prospectoId:D}/convertir", Alta(documento), ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));

        var cliente = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        var clienteId = cliente.GetProperty("id").GetGuid();
        cliente.GetProperty("numero").GetInt32().Should().BeGreaterThan(0);
        cliente.TryGetProperty("documento", out _).Should().BeFalse("la respuesta de mutación no lleva el documento");

        var ficha = await (await sesion.GetAsync($"/api/v1/clientes/{clienteId:D}", ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);
        ficha.GetProperty("documento").GetString().Should().Be(documento);

        var prospecto = await (await sesion.GetAsync($"/api/v1/prospectos/{prospectoId:D}", ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);
        prospecto.GetProperty("clienteId").GetGuid().Should().Be(clienteId);
        prospecto.GetProperty("convertidoAt").ValueKind.Should().NotBe(JsonValueKind.Null);

        var cotizacion = await (await sesion.GetAsync($"/api/v1/cotizaciones/{cotizacionId:D}", ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);
        cotizacion.GetProperty("clienteId").GetGuid().Should().Be(clienteId);
        cotizacion.GetProperty("prospectoId").ValueKind.Should().Be(JsonValueKind.Null);

        var reserva = await (await sesion.GetAsync($"/api/v1/reservas/{reservaId:D}", ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);
        reserva.GetProperty("clienteId").GetGuid().Should().Be(clienteId);
        reserva.GetProperty("prospectoId").ValueKind.Should().Be(JsonValueKind.Null);

        // Un prospecto convertido deja de ofrecerse como destinatario de cotizaciones.
        var lista = await (await sesion.GetAsync("/api/v1/prospectos?pageSize=100", ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);
        lista.GetProperty("data").EnumerateArray()
            .Should().NotContain(p => p.GetProperty("id").GetGuid() == prospectoId);

        (await AuditoriasAsync("prospecto.convertido", prospectoId, ct)).Should().Be(1);
    }

    [Fact]
    public async Task ConvertirDosVecesDa409()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(Roles.Empleado, ct);
        var prospectoId = await AyudaReserva.ProspectoAsync(sesion, ct);

        (await sesion.PostAsync($"/api/v1/prospectos/{prospectoId:D}/convertir", Alta(), ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await sesion.PostAsync($"/api/v1/prospectos/{prospectoId:D}/convertir", Alta(), ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UnReintentoConLaMismaIdempotencyKeyDevuelveElMismoClienteSinDuplicar()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(Roles.Gerente, ct);
        var prospectoId = await AyudaReserva.ProspectoAsync(sesion, ct);
        var cuerpo = Alta();
        var clave = Guid.NewGuid().ToString("N");

        var primera = await sesion.PostAsync($"/api/v1/prospectos/{prospectoId:D}/convertir", cuerpo, clave, ct);
        var repetida = await sesion.PostAsync($"/api/v1/prospectos/{prospectoId:D}/convertir", cuerpo, clave, ct);

        primera.StatusCode.Should().Be(HttpStatusCode.OK);
        repetida.StatusCode.Should().Be(HttpStatusCode.OK);
        var a = (await primera.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var b = (await repetida.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        b.Should().Be(a);
        (await AuditoriasAsync("prospecto.convertido", prospectoId, ct)).Should().Be(1);
    }

    [Fact]
    public async Task UnDocumentoYaUsadoPorOtroClienteDa409YElProspectoQuedaIntacto()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(Roles.Admin, ct);
        var documento = Documento();
        (await sesion.PostAsync(
            "/api/v1/clientes",
            new { nombre = $"Previo {Guid.NewGuid():N}"[..20], tipo = "PersonaFisica", documento },
            ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var prospectoId = await AyudaReserva.ProspectoAsync(sesion, ct);
        var cotizacionId = await AyudaCotizacion.CrearAsync(sesion, null, prospectoId, ct: ct);

        (await sesion.PostAsync($"/api/v1/prospectos/{prospectoId:D}/convertir", Alta(documento), ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var prospecto = await (await sesion.GetAsync($"/api/v1/prospectos/{prospectoId:D}", ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);
        prospecto.GetProperty("clienteId").ValueKind.Should().Be(JsonValueKind.Null);
        var cotizacion = await (await sesion.GetAsync($"/api/v1/cotizaciones/{cotizacionId:D}", ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);
        cotizacion.GetProperty("prospectoId").GetGuid().Should().Be(prospectoId);
    }

    [Fact]
    public async Task ElNumeroDeClienteNoSePuedeFijarEnLaConversion()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(Roles.Admin, ct);
        var prospectoId = await AyudaReserva.ProspectoAsync(sesion, ct);

        (await sesion.PostAsync($"/api/v1/prospectos/{prospectoId:D}/convertir", Alta(numero: 98765), ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ConvertirUnProspectoQueNoExisteDa404()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(Roles.Empleado, ct);

        (await sesion.PostAsync($"/api/v1/prospectos/{Guid.NewGuid():D}/convertir", Alta(), ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SinClienteEditarLaConversionEs403()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await SesionAsync(Roles.Admin, ct);
        var prospectoId = await AyudaReserva.ProspectoAsync(admin, ct);

        using var recortado = await SesionRecortadaAsync(ct);
        (await recortado.PostAsync($"/api/v1/prospectos/{prospectoId:D}/convertir", Alta(), ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<int> AuditoriasAsync(string accion, Guid entidadId, CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(bd.CadenaSuperusuario);
        await conexion.OpenAsync(ct);
        await using var comando = new NpgsqlCommand(
            "SELECT count(*) FROM auditoria WHERE accion = @a AND entidad_id = @id", conexion);
        comando.Parameters.AddWithValue("a", accion);
        comando.Parameters.AddWithValue("id", entidadId.ToString("D"));

        return (int)(long)(await comando.ExecuteScalarAsync(ct))!;
    }

    private async Task<SesionCliente> SesionAsync(string rol, CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: rol, ct: ct);
        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }

    /// <summary>Empleado al que se le quita <c>cliente.editar</c> (conserva <c>cliente.ver</c>).</summary>
    private async Task<SesionCliente> SesionRecortadaAsync(CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);
        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            contexto.UsuarioPermiso.Add(new UsuarioPermiso
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuario.Id,
                Accion = Acciones.ClienteEditar,
                Permitido = false,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await contexto.SaveChangesAsync(ct);
        }

        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }
}
