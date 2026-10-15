using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace AtlasRent.Tests.Clientes;

/// <summary>
/// F1-09, criterio de aceptación: el documento de identidad está cifrado en la base de datos y la
/// API lo devuelve completo (decisión D16).
/// <para>
/// Las comprobaciones del cifrado se hacen leyendo la fila cruda con la conexión de superusuario,
/// que es como la vería alguien con acceso a la base de datos: por la API el documento sale
/// descifrado a propósito.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ClienteCifradoYBusquedaTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// Lo que dice el criterio de aceptación: en la tabla no está el documento y por la API sí,
    /// entero.
    /// </summary>
    [Fact]
    public async Task ElDocumentoEstaCifradoEnLaBaseDeDatosYLaApiLoDevuelveCompleto()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionDeAdminAsync(ct);

        var documento = $"001-{Guid.NewGuid():N}"[..13];
        var id = await CrearClienteAsync(sesion, documento, ct);

        var enLaTabla = await DocumentoEnLaFilaAsync(id, ct);
        enLaTabla.Should().StartWith("v1:", "lo guardado es el valor cifrado con su versión");
        enLaTabla.Should().NotContain(documento, "el documento en claro no está en la base de datos");
        enLaTabla.Should().NotContain(DocumentoIdentidad.Normalizar(documento));

        var ficha = await (await sesion.GetAsync($"/api/v1/clientes/{id:D}", ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);

        ficha.GetProperty("documento").GetString().Should().Be(documento);
    }

    /// <summary>
    /// Ni el documento en claro ni su versión normalizada aparecen en la fila cifrada, ni en la
    /// auditoría (que se escribe en la misma mutación).
    /// </summary>
    [Fact]
    public async Task NiElDocumentoNiLaAuditoriaGuardanElValorEnClaro()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionDeAdminAsync(ct);

        var documento = $"001-{Guid.NewGuid():N}"[..13];
        var normalizado = DocumentoIdentidad.Normalizar(documento);
        var id = await CrearClienteAsync(sesion, documento, ct);

        var enLaTabla = await DocumentoEnLaFilaAsync(id, ct);
        enLaTabla.Should().NotContain(documento).And.NotContain(normalizado);

        await using var conexion = new NpgsqlConnection(bd.CadenaSuperusuario);
        await conexion.OpenAsync(ct);
        await using var auditoria = new NpgsqlCommand(
            "SELECT datos FROM auditoria WHERE entidad = 'cliente' AND entidad_id = @id",
            conexion);
        auditoria.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Text) { Value = id.ToString("D") });

        var datosAuditoria = (string?)await auditoria.ExecuteScalarAsync(ct);
        datosAuditoria.Should().NotBeNull("la mutación se audita");
        datosAuditoria.Should().NotContain(documento).And.NotContain(normalizado);
    }

    /// <summary>
    /// El índice único del HMAC es lo que hace que "001-1234567-8" y "00112345678" no puedan ser dos
    /// clientes, aunque se escriban distinto.
    /// </summary>
    [Fact]
    public async Task DosClientesConElMismoDocumentoSeRechazanAunqueSeEscribanDistinto()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionDeAdminAsync(ct);

        var documento = $"001-{Guid.NewGuid():N}"[..13];
        await CrearClienteAsync(sesion, documento, ct);

        var mismoTexto = await sesion.PostAsync("/api/v1/clientes", Cliente(documento), ct);
        mismoTexto.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await mismoTexto.Content.ReadAsStringAsync(ct)).Should().Contain(Problemas.ValorDuplicado);

        var otraEscritura = await sesion.PostAsync(
            "/api/v1/clientes", Cliente(DocumentoIdentidad.Normalizar(documento)), ct);
        otraEscritura.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Buscar por documento va contra el índice ciego: el SQL que genera el servicio compara
    /// <c>documento_hmac</c> y ni siquiera selecciona la columna cifrada.
    /// </summary>
    [Fact]
    public async Task BuscarPorDocumentoNoDescifraLaTabla()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionDeAdminAsync(ct);

        var documento = $"001-{Guid.NewGuid():N}"[..13];
        var id = await CrearClienteAsync(sesion, documento, ct);

        using (var ambito = api.Services.CreateScope())
        {
            var servicio = ambito.ServiceProvider.GetRequiredService<ServicioClientes>();
            var consulta = servicio.ConsultaPorDocumento(DocumentoIdentidad.Normalizar(documento));

            var sql = consulta.ToQueryString();
            sql.Should().Contain("documento_hmac", "el filtro es el índice ciego");
            sql.Should().NotContain("\"documento\"", "la columna cifrada ni se lee ni se descifra");
        }

        // Y solo con la huella basta para encontrar la fila, sin ninguna clave de descifrado.
        using (var ambito = api.Services.CreateScope())
        {
            var indice = ambito.ServiceProvider.GetRequiredService<IndiceCiegoDocumento>();
            var huella = indice.Huella(documento)!;

            await using var conexion = new NpgsqlConnection(bd.CadenaSuperusuario);
            await conexion.OpenAsync(ct);
            await using var comando = new NpgsqlCommand(
                "SELECT id FROM cliente WHERE documento_hmac = @huella", conexion);
            comando.Parameters.Add(new NpgsqlParameter("huella", NpgsqlDbType.Varchar) { Value = huella });

            (await comando.ExecuteScalarAsync(ct)).Should().Be(id);
        }

        var encontrado = await (await sesion.GetAsync(
            $"/api/v1/clientes?documento={Uri.EscapeDataString(DocumentoIdentidad.Normalizar(documento))}", ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);

        var filas = encontrado.GetProperty("data").EnumerateArray().ToList();
        filas.Should().ContainSingle();
        filas[0].GetProperty("id").GetGuid().Should().Be(id);
    }

    [Fact]
    public async Task BuscarPorNombreDevuelveLaListaYUnDocumentoInexistenteNoTraeNada()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionDeAdminAsync(ct);

        var marca = $"Nombre{Guid.NewGuid():N}"[..20];
        await CrearClienteAsync(sesion, $"002-{Guid.NewGuid():N}"[..13], marca, ct);

        var lista = await (await sesion.GetAsync($"/api/v1/clientes?q={marca}", ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);

        lista.GetProperty("total").GetInt32().Should().Be(1);
        lista.GetProperty("data")[0].GetProperty("nombre").GetString().Should().StartWith(marca);

        var sinNada = await (await sesion.GetAsync("/api/v1/clientes?documento=%20--", ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);
        sinNada.GetProperty("total").GetInt32().Should().Be(0);
    }

    // ---------------------------------------------------------------- apoyo

    private static object Cliente(string documento, string? nombre = null) => new
    {
        nombre = nombre ?? $"Cliente {Guid.NewGuid():N}"[..20],
        tipo = "PersonaFisica",
        documento,
        email = "cliente@example.test",
    };

    private static async Task<Guid> CrearClienteAsync(
        SesionCliente sesion,
        string documento,
        CancellationToken ct) =>
        await CrearClienteAsync(sesion, documento, null, ct);

    private static async Task<Guid> CrearClienteAsync(
        SesionCliente sesion,
        string documento,
        string? nombre,
        CancellationToken ct)
    {
        var creada = await sesion.PostAsync("/api/v1/clientes", Cliente(documento, nombre), ct);
        creada.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    /// <summary>La columna tal cual está escrita, leída con la conexión de superusuario.</summary>
    private async Task<string?> DocumentoEnLaFilaAsync(Guid id, CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(bd.CadenaSuperusuario);
        await conexion.OpenAsync(ct);
        await using var comando = new NpgsqlCommand("SELECT documento FROM cliente WHERE id = @id", conexion);
        comando.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });

        return (string?)await comando.ExecuteScalarAsync(ct);
    }

    private async Task<SesionCliente> SesionDeAdminAsync(CancellationToken ct)
    {
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        return await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);
    }
}