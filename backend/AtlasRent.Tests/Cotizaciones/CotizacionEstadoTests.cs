using System.Net;
using System.Net.Http.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Cotizaciones;

/// <summary>
/// F2-02: el flujo de estados de una cotización (docs/ESPECIFICACION.md §3.3, D18).
/// <c>Borrador → Enviada → Aceptada / Rechazada</c> y ningún otro camino.
/// <para>
/// Van contra la API porque lo que hay que comprobar es de quién es el permiso de cada cambio de
/// estado, no solo que la tabla acepte el nuevo valor.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class CotizacionEstadoTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>Los dos desenlaces del flujo: aceptar y rechazar.</summary>
    [Fact]
    public async Task ElFlujoDeEstadosEsBorradorEnviadaAceptada()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await SesionAsync(Roles.Gerente, ct);

        var id = await AyudaCotizacion.CrearAsync(gerente, await AyudaCotizacion.ClienteAsync(gerente, ct), ct: ct);
        (await AyudaCotizacion.Json(await gerente.GetAsync($"/api/v1/cotizaciones/{id:D}", ct), ct))
            .GetProperty("estado").GetString().Should().Be("Borrador", "una cotización nace en borrador");

        (await AyudaCotizacion.CambiarEstadoAsync(gerente, id, "Enviada", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await AyudaCotizacion.CambiarEstadoAsync(gerente, id, "Aceptada", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await AyudaCotizacion.Json(await gerente.GetAsync($"/api/v1/cotizaciones/{id:D}", ct), ct))
            .GetProperty("estado").GetString().Should().Be("Aceptada");
    }

    /// <summary>El otro desenlace del mismo camino.</summary>
    [Fact]
    public async Task ElFlujoDeEstadosEsBorradorEnviadaRechazada()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await SesionAsync(Roles.Gerente, ct);

        var id = await AyudaCotizacion.CrearAsync(gerente, await AyudaCotizacion.ClienteAsync(gerente, ct), ct: ct);

        (await AyudaCotizacion.CambiarEstadoAsync(gerente, id, "Enviada", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await AyudaCotizacion.CambiarEstadoAsync(gerente, id, "Rechazada", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await AyudaCotizacion.Json(await gerente.GetAsync($"/api/v1/cotizaciones/{id:D}", ct), ct))
            .GetProperty("estado").GetString().Should().Be("Rechazada");
    }

    /// <summary>
    /// De borrador no se pasa a aceptada ni a rechazada: hay que enviarla antes. Un 409, no un 403:
    /// el permiso está bien, lo que no existe es el camino.
    /// </summary>
    [Theory]
    [InlineData("Aceptada")]
    [InlineData("Rechazada")]
    [InlineData("Borrador")]
    public async Task DesdeBorradorSoloSePuedePasarAEnviada(string destino)
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await SesionAsync(Roles.Gerente, ct);

        var id = await AyudaCotizacion.CrearAsync(gerente, await AyudaCotizacion.ClienteAsync(gerente, ct), ct: ct);

        (await AyudaCotizacion.CambiarEstadoAsync(gerente, id, destino, ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await AyudaCotizacion.Json(await gerente.GetAsync($"/api/v1/cotizaciones/{id:D}", ct), ct))
            .GetProperty("estado").GetString().Should().Be("Borrador", "el cambio rechazado no se guarda");
    }

    /// <summary>De enviada no se vuelve a borrador: lo que se envió, se quedó enviado.</summary>
    [Fact]
    public async Task DesdeEnviadaNoSeVuelveABorrador()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await SesionAsync(Roles.Gerente, ct);

        var id = await AyudaCotizacion.CrearAsync(gerente, await AyudaCotizacion.ClienteAsync(gerente, ct), ct: ct);
        (await AyudaCotizacion.CambiarEstadoAsync(gerente, id, "Enviada", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await AyudaCotizacion.CambiarEstadoAsync(gerente, id, "Borrador", ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await AyudaCotizacion.Json(await gerente.GetAsync($"/api/v1/cotizaciones/{id:D}", ct), ct))
            .GetProperty("estado").GetString().Should().Be("Enviada");
    }

    /// <summary>Aceptada y rechazada son finales: no tienen salida.</summary>
    [Theory]
    [InlineData("Aceptada")]
    [InlineData("Rechazada")]
    public async Task LosEstadosFinalesNoTienenSalida(string final)
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await SesionAsync(Roles.Gerente, ct);

        var id = await AyudaCotizacion.CrearAsync(gerente, await AyudaCotizacion.ClienteAsync(gerente, ct), ct: ct);
        (await AyudaCotizacion.CambiarEstadoAsync(gerente, id, "Enviada", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await AyudaCotizacion.CambiarEstadoAsync(gerente, id, final, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        foreach (var destino in new[] { "Borrador", "Enviada", "Aceptada", "Rechazada" })
        {
            (await AyudaCotizacion.CambiarEstadoAsync(gerente, id, destino, ct))
                .StatusCode.Should().Be(HttpStatusCode.Conflict, $"desde {final} no se puede pasar a {destino}");
        }
    }

    /// <summary>Una cotización enviada ya no se edita: se edita una nueva.</summary>
    [Fact]
    public async Task UnaCotizacionEnviadaNoSeEdita()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await SesionAsync(Roles.Gerente, ct);

        var clienteId = await AyudaCotizacion.ClienteAsync(gerente, ct);
        var id = await AyudaCotizacion.CrearAsync(gerente, clienteId, ct: ct);
        (await AyudaCotizacion.CambiarEstadoAsync(gerente, id, "Enviada", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await gerente.PutAsync(
            $"/api/v1/cotizaciones/{id:D}",
            AyudaCotizacion.Cuerpo(clienteId, lineas: new[] { AyudaCotizacion.Linea("Otro precio", 1, 9999m) }),
            ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Tampoco se da de baja: lo que ya salió a un cliente no se retira.
        (await gerente.DeleteAsync($"/api/v1/cotizaciones/{id:D}", ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// D15: el empleado recibe <c>cotizacion.enviar</c> desactivado, así que puede trabajar el
    /// borrador pero no marcarlo como enviada. La usuario se crea por la API, que es donde se
    /// escriben las filas de <c>usuario_permiso</c> con <c>permitido = false</c>.
    /// </summary>
    [Fact]
    public async Task ElEmpleadoSinPermisoNoMarcaEnviada()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await SesionAsync(Roles.Admin, ct);
        var empleadoId = await CrearEmpleadoAsync(admin, ct);
        using var empleado = await AyudaPermisos.SesionAsync(api, await UsuarioAsync(empleadoId, ct), Contrasena, ct);

        // El mismo empleado redacta el borrador sin problema: lo que no tiene es enviar.
        var id = await AyudaCotizacion.CrearAsync(
            empleado, await AyudaCotizacion.ClienteAsync(empleado, ct), ct: ct);

        (await AyudaCotizacion.CambiarEstadoAsync(empleado, id, "Enviada", ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.UsuarioPermiso.AsNoTracking()
            .SingleAsync(p => p.UsuarioId == empleadoId && p.Accion == Acciones.CotizacionEnviar, ct))
            .Permitido.Should().BeFalse("D15 lo deja desactivado al crear el usuario");

        // Y el gerente, que sí lo trae en su techo, sí puede.
        using var gerente = await SesionAsync(Roles.Gerente, ct);
        (await AyudaCotizacion.CambiarEstadoAsync(gerente, id, "Enviada", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// El mismo permiso, activado por el administrador: con esa fila el empleado sí envía. Es el
    /// camino de D15 entero, no un atajo por la base de datos.
    /// </summary>
    [Fact]
    public async Task ElEmpleadoConElPermisoActivadoSi()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await SesionAsync(Roles.Admin, ct);
        var empleadoId = await CrearEmpleadoAsync(admin, ct);

        (await admin.PutAsync(
            $"/api/v1/usuarios/{empleadoId:D}/permisos",
            new { accion = Acciones.CotizacionEnviar, permitido = true },
            ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var empleado = await AyudaPermisos.SesionAsync(api, await UsuarioAsync(empleadoId, ct), Contrasena, ct);
        var id = await AyudaCotizacion.CrearAsync(
            empleado, await AyudaCotizacion.ClienteAsync(empleado, ct), ct: ct);

        (await AyudaCotizacion.CambiarEstadoAsync(empleado, id, "Enviada", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---------------------------------------------------------------- ayudantes

    private async Task<SesionCliente> SesionAsync(string rol, CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: rol, ct: ct);
        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }

    /// <summary>El usuario creado por la API, leído de la base para poder iniciar sesión con él.</summary>
    private async Task<Usuario> UsuarioAsync(Guid id, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        return await contexto.Usuario.AsNoTracking().SingleAsync(u => u.Id == id, ct);
    }

    /// <summary>Alta por la API: así el usuario nace con las acciones desactivadas de D15.</summary>
    private async Task<Guid> CrearEmpleadoAsync(SesionCliente admin, CancellationToken ct)
    {
        var creado = await admin.PostAsync(
            "/api/v1/usuarios",
            new
            {
                nombreAcceso = $"empleado{Guid.NewGuid():N}"[..20],
                nombre = "Empleado de prueba",
                contrasena = Contrasena,
                rol = Roles.Empleado,
            },
            ct);
        creado.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await creado.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct))
            .GetProperty("id").GetGuid();
    }
}