using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace AtlasRent.Tests.Configuracion;

/// <summary>
/// F1-08, criterio de aceptación (D14): los porcentajes de ITBIS y retención son datos, los
/// configura el administrador y el cálculo sale de ellos. Un empleado no llega a verlos ni a
/// tocarlos.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class TiposImpuestoTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// Valores de la prueba. Son datos de prueba, no cifras fiscales de la empresa: no se parecen
    /// a ningún tipo de cambio ni a ningún tipo de impuesto conocido, y su único papel es que el
    /// cálculo dé una cifra que se pueda comprobar a mano.
    /// </summary>
    private const decimal ItbisInicial = 7.5m;

    private const decimal ItbisCambiado = 12.25m;

    private const decimal Retencion = 3m;

    private const decimal SubtotalDePrueba = 1000m;

    [Fact]
    public async Task ElAdminCambiaElPorcentajeDeItbisYElCalculoCambia()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);

        var id = await CrearTipoItbisAsync(sesion, "Impuesto de prueba", ItbisInicial, ct);
        var antes = await CalcularAsync(SubtotalDePrueba, id, ct);

        var configurado = await PorcentajeDeLaRespuestaAsync(sesion, id, ct);
        configurado.Should().Be(ItbisInicial);
        antes.Itbis.Should().Be(75.00m, "7,5 % de 1.000,00 son 75,00");

        var cambio = await sesion.PutAsync(
            $"/api/v1/config/tipos-itbis/{id:D}",
            new { nombre = "Impuesto de prueba", porcentaje = ItbisCambiado, activo = true },
            ct);
        cambio.StatusCode.Should().Be(HttpStatusCode.OK);

        // Lo que el administrador ve después de guardar.
        var despues = await CalcularAsync(SubtotalDePrueba, id, ct);
        despues.Itbis.Should().Be(122.50m, "12,25 % de 1.000,00 son 122,50");
        despues.Itbis.Should().NotBe(antes.Itbis);

        // Y lo que queda guardado, leído por la API.
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.TipoItbis.AsNoTracking().SingleAsync(t => t.Id == id, ct))
            .Porcentaje.Should().Be(ItbisCambiado);

        // La mutación queda auditada con su IP y en la misma transacción.
        await using var conexion = new NpgsqlConnection(bd.CadenaSuperusuario);
        await conexion.OpenAsync(ct);
        await using var comando = new NpgsqlCommand(
            """
            SELECT accion, resultado, usuario_id, ip FROM auditoria
            WHERE entidad = 'tipo_itbis' AND entidad_id = @id
            ORDER BY secuencia
            """,
            conexion);
        comando.Parameters.Add(new NpgsqlParameter("id", NpgsqlTypes.NpgsqlDbType.Text) { Value = id.ToString("D") });
        await using var lector = await comando.ExecuteReaderAsync(ct);

        var acciones = new List<string>();
        while (await lector.ReadAsync(ct))
        {
            acciones.Add(lector.GetString(0));
            lector.GetString(1).Should().Be("permitido");
            lector.GetGuid(2).Should().Be(admin.Id);
            lector.GetString(3).Should().Be(api.IpCliente);
        }

        acciones.Should().Contain("tipo_itbis.creado");
        acciones.Should().Contain("tipo_itbis.actualizado");
    }

    [Fact]
    public async Task ElEmpleadoNoLeeNiCambiaLosTiposDeImpuesto()
    {
        var ct = TestContext.Current.CancellationToken;
        var empleado = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);

        using var sesionEmpleado = await AyudaPermisos.SesionAsync(api, empleado, Contrasena, ct);
        using var sesionAdmin = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);
        var id = await CrearTipoItbisAsync(sesionAdmin, "Impuesto de prueba", ItbisInicial, ct);

        (await sesionEmpleado.GetAsync("/api/v1/config/tipos-itbis", ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sesionEmpleado.PostAsync(
            "/api/v1/config/tipos-itbis",
            new { nombre = "Otro", porcentaje = ItbisCambiado, activo = true },
            ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sesionEmpleado.PutAsync(
            $"/api/v1/config/tipos-itbis/{id:D}",
            new { nombre = "Otro", porcentaje = ItbisCambiado, activo = true },
            ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.TipoItbis.AsNoTracking().SingleAsync(t => t.Id == id, ct))
            .Porcentaje.Should().Be(ItbisInicial, "el empleado no pudo cambiarlo");
    }

    /// <summary>
    /// La base de la retención es elección de la empresa, no una regla del sistema: los mismos
    /// porcentajes sobre subtotal, ITBIS y total dan tres importes distintos.
    /// </summary>
    [Fact]
    public async Task LaBaseDeRetencionDistingueSubtotalItbisYTotal()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);

        var idItbis = await CrearTipoItbisAsync(sesion, "Impuesto de prueba", ItbisInicial, ct);
        var sobreSubtotal = await CrearRetencionAsync(sesion, "Sobre subtotal", BaseRetencion.Subtotal, ct);
        var sobreItbis = await CrearRetencionAsync(sesion, "Sobre ITBIS", BaseRetencion.Itbis, ct);
        var sobreTotal = await CrearRetencionAsync(sesion, "Sobre total", BaseRetencion.Total, ct);

        var resultado = await CalcularAsync(SubtotalDePrueba, idItbis, ct, sobreSubtotal, sobreItbis, sobreTotal);

        resultado.Itbis.Should().Be(75.00m);
        resultado.Total.Should().Be(1075.00m);
        resultado.Retencion.Should().Be(
            Redondeo.Importe(SubtotalDePrueba * Retencion / 100m)
            + Redondeo.Importe(75.00m * Retencion / 100m)
            + Redondeo.Importe(1075.00m * Retencion / 100m));
        resultado.NetoACobrar.Should().Be(resultado.Total - resultado.Retencion);

        // Y con cada base por separado, para que un fallo de una no tape al de las otras.
        (await CalcularAsync(SubtotalDePrueba, idItbis, ct, sobreSubtotal)).Retencion
            .Should().Be(30.00m, "3 % de 1.000,00");
        (await CalcularAsync(SubtotalDePrueba, idItbis, ct, sobreItbis)).Retencion
            .Should().Be(2.25m, "3 % de 75,00");
        (await CalcularAsync(SubtotalDePrueba, idItbis, ct, sobreTotal)).Retencion
            .Should().Be(32.25m, "3 % de 1.075,00");
    }

    /// <summary>Solo un tipo de retención puede llevar el distintivo de por defecto.</summary>
    [Fact]
    public async Task SoloUnaRetencionEsLaDePorDefecto()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);

        var primera = await CrearRetencionAsync(sesion, "Retención A", BaseRetencion.Subtotal, ct, porDefecto: true);
        var segunda = await CrearRetencionAsync(sesion, "Retención B", BaseRetencion.Total, ct, porDefecto: true);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var tipos = await contexto.TipoRetencion.AsNoTracking()
            .Where(t => t.Id == primera || t.Id == segunda)
            .ToListAsync(ct);

        tipos.Single(t => t.Id == primera).EsDefecto.Should().BeFalse();
        tipos.Single(t => t.Id == segunda).EsDefecto.Should().BeTrue();
    }

    /// <summary>El porcentaje se guarda con cuatro decimales (Redondeo.Porcentaje).</summary>
    [Fact]
    public async Task ElPorcentajeSeGuardaConCuatroDecimales()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);

        var id = await CrearTipoItbisAsync(sesion, "Impuesto de prueba", 6.123456m, ct);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.TipoItbis.AsNoTracking().SingleAsync(t => t.Id == id, ct))
            .Porcentaje.Should().Be(6.1235m, "6,123456 redondeado a cuatro decimales, half-up");
    }

    // ---------------------------------------------------------------- auxiliares

    private static async Task<Guid> CrearTipoItbisAsync(
        SesionCliente sesion,
        string nombre,
        decimal porcentaje,
        CancellationToken ct)
    {
        var respuesta = await sesion.PostAsync(
            "/api/v1/config/tipos-itbis", new { nombre, porcentaje, activo = true }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CrearRetencionAsync(
        SesionCliente sesion,
        string nombre,
        BaseRetencion baseCalculo,
        CancellationToken ct,
        bool porDefecto = false)
    {
        var respuesta = await sesion.PostAsync(
            "/api/v1/config/tipos-retencion",
            new { nombre, porcentaje = Retencion, @base = baseCalculo.ToString(), esDefecto = porDefecto, activo = true },
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// El porcentaje que ve el administrador para el tipo dado. Recorre las páginas porque el
    /// endpoint no filtra y la base es la de la colección: el tipo que crea la prueba puede no estar
    /// en la primera página, y leer solo esa hacía que la comprobación dependiera de cuántas filas
    /// hubieran dejado las demás.
    /// </summary>
    private static async Task<decimal> PorcentajeDeLaRespuestaAsync(
        SesionCliente sesion,
        Guid id,
        CancellationToken ct)
    {
        var filas = new List<JsonElement>();
        for (var pagina = 1; ; pagina++)
        {
            var respuesta = await sesion.GetAsync(
                $"/api/v1/config/tipos-itbis?page={pagina}&pageSize=200&sortBy=nombre", ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.OK);

            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
            filas.AddRange(cuerpo.GetProperty("data").EnumerateArray());

            if (pagina >= cuerpo.GetProperty("totalPages").GetInt32())
            {
                break;
            }
        }

        return filas.Should().ContainSingle(t => t.GetProperty("id").GetGuid() == id)
            .Which.GetProperty("porcentaje").LeerDecimal();
    }

    /// <summary>
    /// Calcula leyendo de la base los porcentajes que hay ahora mismo en la configuración y aplicando
    /// el cálculo puro del dominio: los porcentajes salen de la configuración, nunca de una constante
    /// del código (D14). (Antes pasaba por <c>ServicioConfiguracion.CalcularAsync</c>, que solo usaban
    /// estas pruebas y divergía del cálculo real de facturas, que va por línea: BUG-052.)
    /// </summary>
    private async Task<ResultadoImpuestos> CalcularAsync(
        decimal subtotal,
        Guid tipoItbisId,
        CancellationToken ct,
        params Guid[] tipoRetencionIds)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var porcentajeItbis = await contexto.TipoItbis.AsNoTracking()
            .Where(t => t.Id == tipoItbisId && t.Activo)
            .Select(t => t.Porcentaje)
            .SingleAsync(ct);
        var tipos = await contexto.TipoRetencion.AsNoTracking()
            .Where(t => t.Activo && tipoRetencionIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => (t.Porcentaje, t.Base), ct);

        return CalculoImpuestos.Calcular(
            subtotal, porcentajeItbis, tipoRetencionIds.Select(id => tipos[id]).ToList());
    }
}
