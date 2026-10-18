using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace AtlasRent.Tests.Configuracion;

/// <summary>
/// AGENTS.md §6: el RNC de la subempresa se guarda cifrado en reposo, igual que el documento de
/// identidad del cliente, y se ve completo en pantalla y en el PDF (decisión D16).
/// <para>
/// Lo del cifrado se comprueba leyendo la fila cruda con la conexión de superusuario, que es como
/// la vería alguien con acceso a la base de datos: por la API el RNC sale descifrado a propósito.
/// El PDF lo comprueba <c>Facturas/FacturaPdfTests</c>, que emite una factura de esa facturadora y
/// mira lo que el compositor imprime.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class SubempresaRncCifradoTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// Lo que dice la regla: en la tabla no está el RNC en claro y por la API sí, entero.
    /// </summary>
    [Fact]
    public async Task ElRncEstaCifradoEnLaBaseDeDatosYLaApiLoDevuelveCompleto()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionDeAdminAsync(ct);

        // RNC inventado para la prueba, con la forma que se teclea en el formulario.
        var rnc = $"1-31-{Random.Shared.Next(10_000, 99_999)}-{Random.Shared.Next(1, 9)}";
        var creada = await CrearSubempresaAsync(sesion, rnc, ct);

        var enLaTabla = await RncEnLaFilaAsync(creada.Id, ct);
        enLaTabla.Should().StartWith("v1:", "lo guardado es el valor cifrado con su versión");
        enLaTabla.Should().NotContain(rnc, "el RNC en claro no está en la base de datos");
        enLaTabla.Should().NotContain(DocumentoIdentidad.Normalizar(rnc));

        // El alta ya devuelve el RNC descifrado…
        creada.Cuerpo.GetProperty("rnc").GetString().Should().Be(rnc);

        // …y el listado también, que es donde se ve en la pantalla de configuración. Se recorre página a
        // página porque la base es la de la colección: leer solo la primera haría que la afirmación
        // dependiera de cuántas subempresas hubieran dejado las demás pruebas.
        var fila = await BuscarEnElListadoAsync(sesion, creada.Id, ct);
        fila.GetProperty("rnc").GetString()
            .Should().Be(rnc, "la lista lo devuelve descifrado y completo");
    }

    /// <summary>
    /// La fila del listado de subempresas con ese identificador, recorriendo las páginas hasta
    /// encontrarla. El listado no tiene filtro por identificador, y el tope de la API son 200 por
    /// página, así que hay que leer más de una cuando la base compartida tiene más de 100.
    /// </summary>
    private static async Task<JsonElement> BuscarEnElListadoAsync(SesionCliente sesion, Guid id, CancellationToken ct)
    {
        for (var pagina = 1; ; pagina++)
        {
            var listada = await sesion.GetAsync($"/api/v1/config/subempresas?page={pagina}&pageSize=100", ct);
            listada.StatusCode.Should().Be(HttpStatusCode.OK, await listada.Cuerpo());

            var cuerpo = await listada.Content.ReadFromJsonAsync<JsonElement>(ct);
            var fila = cuerpo.GetProperty("data").EnumerateArray()
                .SingleOrDefault(s => s.GetProperty("id").GetGuid() == id);

            if (fila.ValueKind != JsonValueKind.Undefined)
            {
                return fila;
            }

            if (pagina >= cuerpo.GetProperty("totalPages").GetInt32())
            {
                throw new InvalidOperationException($"La subempresa '{id:D}' no sale en el listado.");
            }
        }
    }

    /// <summary>
    /// Editar también cifra, y ni la fila ni la auditoría de la mutación guardan el valor en claro.
    /// </summary>
    [Fact]
    public async Task EditarCifraElRncYNiLaFilaNiLaAuditoriaLoGuardanEnClaro()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionDeAdminAsync(ct);

        var rnc = $"1-31-{Random.Shared.Next(10_000, 99_999)}-{Random.Shared.Next(1, 9)}";
        var (id, _) = await CrearSubempresaAsync(sesion, rnc, ct);

        var otro = $"2-22-{Random.Shared.Next(10_000, 99_999)}-{Random.Shared.Next(1, 9)}";
        (await sesion.PutAsync($"/api/v1/config/subempresas/{id:D}", Subempresa(otro), ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var enLaTabla = await RncEnLaFilaAsync(id, ct);
        enLaTabla.Should().StartWith("v1:");
        enLaTabla.Should().NotContain(rnc).And.NotContain(otro);

        await using var conexion = new NpgsqlConnection(bd.CadenaSuperusuario);
        await conexion.OpenAsync(ct);
        await using var auditoria = new NpgsqlCommand(
            "SELECT datos FROM auditoria WHERE entidad = 'subempresa' AND entidad_id = @id",
            conexion);
        auditoria.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Text) { Value = id.ToString("D") });

        var datosAuditoria = (string?)await auditoria.ExecuteScalarAsync(ct);
        datosAuditoria.Should().NotBeNull("la mutación se audita");
        datosAuditoria.Should().NotContain(rnc).And.NotContain(otro);
    }

    // ---------------------------------------------------------------- apoyo

    private static object Subempresa(string? rnc) => new
    {
        nombre = $"Empresa {Guid.NewGuid():N}"[..20],
        rnc,
        direccion = "Calle de prueba 1",
        telefono = "+1 809 000 0000",
        email = "facturacion@ejemplo.test",
        propietaria = false,
        facturadora = true,
        activa = true,
    };

    private static async Task<(Guid Id, JsonElement Cuerpo)> CrearSubempresaAsync(
        SesionCliente sesion,
        string rnc,
        CancellationToken ct)
    {
        var creada = await sesion.PostAsync("/api/v1/config/subempresas", Subempresa(rnc), ct);
        creada.StatusCode.Should().Be(HttpStatusCode.OK);

        var cuerpo = await creada.Content.ReadFromJsonAsync<JsonElement>(ct);
        return (cuerpo.GetProperty("id").GetGuid(), cuerpo);
    }

    /// <summary>La columna tal cual está escrita, leída con la conexión de superusuario.</summary>
    private async Task<string?> RncEnLaFilaAsync(Guid id, CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(bd.CadenaSuperusuario);
        await conexion.OpenAsync(ct);
        await using var comando = new NpgsqlCommand("SELECT rnc FROM subempresa WHERE id = @id", conexion);
        comando.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });

        return (string?)await comando.ExecuteScalarAsync(ct);
    }

    private async Task<SesionCliente> SesionDeAdminAsync(CancellationToken ct)
    {
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        return await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);
    }
}