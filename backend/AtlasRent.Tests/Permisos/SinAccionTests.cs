using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AtlasRent.Tests.Permisos;

/// <summary>
/// F1-07: política global de denegar por omisión. Un endpoint de controlador sin <c>[Accion]</c> se
/// rechaza, para que el próximo endpoint que se añada no quede abierto por descuido.
/// <para>
/// El controlador sin atributo vive aquí, no en la API: en producción un endpoint sin
/// <c>[Accion]</c> sería un fallo, y no puede uno esconderse en el código de la aplicación.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class SinAccionTests(ContenedorBdFixture bd, ApiTestFactoryConPrueba api)
    : IClassFixture<ApiTestFactoryConPrueba>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    [Fact]
    public async Task UnEndpointSinAccionSeRechazaAunqueLaSesionSeaValida()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);

        using var sesion = await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);

        var respuesta = await sesion.GetAsync("/api/v1/prueba/sin-accion", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await respuesta.Content.ReadAsStringAsync(ct)).Should().Contain(Problemas.PermisoDenegado);
    }

    [Fact]
    public async Task UnEndpointConAccionDelRolPasa()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);

        using var sesion = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);

        (await sesion.GetAsync("/api/v1/prueba/con-accion", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

/// <summary>
/// Controlador de prueba: el primero sin <c>[Accion]</c> (debe rechazarse) y el segundo con ella
/// (debe pasar).
/// </summary>
[ApiController]
[Route("api/v1/prueba")]
public sealed class ControladorDePrueba : ControllerBase
{
    [HttpGet("sin-accion")]
    public IActionResult SinAccion() => Ok(new { abierto = true });

    [HttpGet("con-accion")]
    [Accion(Acciones.ClienteVer)]
    public IActionResult ConAccion() => Ok(new { abierto = true });
}

/// <summary>
/// Fábrica que añade el assembly de las pruebas como parte de la aplicación, para que sus
/// controladores existan en el sistema de rutas.
/// </summary>
public sealed class ApiTestFactoryConPrueba(ContenedorBdFixture bd) : ApiTestFactory(bd)
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(servicios =>
            servicios.AddControllers().AddApplicationPart(typeof(ControladorDePrueba).Assembly));
    }
}