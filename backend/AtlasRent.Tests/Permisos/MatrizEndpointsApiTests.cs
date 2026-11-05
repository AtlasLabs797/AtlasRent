using System.Net;
using System.Reflection;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Controllers;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace AtlasRent.Tests.Permisos;

/// <summary>
/// BUG-051 (c) / F1-07: la matriz de permisos probada <b>contra la API real</b>. Para cada controlador se
/// toma un endpoint de lectura y uno de escritura (el primero de cada tipo con <c>[Accion]</c>), se llama
/// con un empleado, un gerente y un administrador y se comprueba el código según
/// <see cref="MatrizPermisos"/>: 403 si el rol no tiene en su techo alguna de las acciones del endpoint,
/// y cualquier otra cosa (400, 404… lo que diga el negocio con datos que no existen) si las tiene.
/// <para>
/// Los casos salen de la reflexión sobre los controladores y de la matriz de C#, no de una lista a mano:
/// un controlador nuevo entra solo en la prueba. Que la matriz de C# coincida con lo sembrado lo prueba
/// <see cref="MatrizPermisosTests"/>.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class MatrizEndpointsApiTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    // Actúan sobre la propia sesión (logout, MFA): llamarlos mataría la sesión con la que se prueba el resto.
    private static readonly string[] ControladoresDeSesion = [nameof(AuthController), nameof(MfaController)];

    /// <summary>Un endpoint de muestra: verbo, ruta con marcadores sustituidos y acciones que exige.</summary>
    internal sealed record Muestra(string Controlador, HttpMethod Verbo, string Ruta, string[] Acciones)
    {
        public override string ToString() => $"{Controlador} {Verbo} {Ruta}";
    }

    [Fact]
    public void LaMuestraCubreCadaControladorConAccion()
    {
        var controladores = ControladoresDeclaranAccionTests.Endpoints()
            .Where(m => m.GetCustomAttribute<AccionAttribute>() is not null
                && !ControladoresDeSesion.Contains(m.DeclaringType!.Name))
            .Select(m => m.DeclaringType!)
            .Distinct()
            .ToList();
        var cubiertos = Muestras().Select(m => m.Controlador).Distinct().ToList();

        cubiertos.Should().BeEquivalentTo(controladores.Select(c => c.Name));
        Muestras().Any(m => m.Verbo == HttpMethod.Get).Should().BeTrue();
        Muestras().Any(m => m.Verbo != HttpMethod.Get).Should().BeTrue();
    }

    [Theory]
    [InlineData(Roles.Empleado)]
    [InlineData(Roles.Gerente)]
    [InlineData(Roles.Admin)]
    public async Task CadaEndpointDeMuestraRespondeSegunLaMatrizDelRol(string rol)
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: rol, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);

        var fallos = new List<string>();
        foreach (var muestra in Muestras())
        {
            var permitido = muestra.Acciones.All(a => MatrizPermisos.EnTecho(rol, a));

            using var respuesta = muestra.Verbo == HttpMethod.Get
                ? await sesion.GetAsync(muestra.Ruta, ct)
                : muestra.Verbo == HttpMethod.Delete
                    ? await sesion.DeleteAsync(muestra.Ruta, ct)
                    : muestra.Verbo == HttpMethod.Put
                        ? await sesion.PutAsync(muestra.Ruta, new { }, Guid.NewGuid().ToString("N"), ct)
                        : await sesion.PostAsync(muestra.Ruta, new { }, Guid.NewGuid().ToString("N"), ct);

            var codigo = respuesta.StatusCode;
            var esperadoDenegado = !permitido;
            var obtenidoDenegado = codigo == HttpStatusCode.Forbidden;
            if (esperadoDenegado != obtenidoDenegado || codigo == HttpStatusCode.Unauthorized)
            {
                fallos.Add($"{rol}: {muestra} -> {(int)codigo}, esperaba {(esperadoDenegado ? "403" : "algo distinto de 403")}");
            }
        }

        fallos.Should().BeEmpty();
    }

    internal static IReadOnlyList<Muestra> Muestras()
    {
        var porControlador = ControladoresDeclaranAccionTests.Endpoints()
            .Where(m => m.GetCustomAttribute<AccionAttribute>() is not null
                && !ControladoresDeSesion.Contains(m.DeclaringType!.Name)
                && m.GetCustomAttribute<ConsumesAttribute>() is null
                && !m.GetParameters().Any(p => p.ParameterType == typeof(IFormFile)))
            .GroupBy(m => m.DeclaringType!)
            .OrderBy(g => g.Key.Name);

        var muestras = new List<Muestra>();
        foreach (var controlador in porControlador)
        {
            var prefijo = controlador.Key.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;
            var lectura = controlador.FirstOrDefault(m => m.GetCustomAttribute<HttpGetAttribute>() is not null);
            var escritura = controlador.FirstOrDefault(m => m.GetCustomAttribute<HttpGetAttribute>() is null);
            foreach (var endpoint in new[] { lectura, escritura }.OfType<MethodInfo>())
            {
                var http = endpoint.GetCustomAttributes().OfType<HttpMethodAttribute>().First();
                var ruta = $"/{prefijo}/{http.Template}".TrimEnd('/');
                muestras.Add(new Muestra(
                    controlador.Key.Name,
                    new HttpMethod(http.HttpMethods.First()),
                    Sustituir(ruta, endpoint),
                    endpoint.GetCustomAttribute<AccionAttribute>()!.Acciones));
            }
        }

        return muestras;
    }

    private static string Sustituir(string ruta, MethodInfo endpoint) =>
        System.Text.RegularExpressions.Regex.Replace(ruta, @"\{([^}:?]+)[^}]*\}", m =>
        {
            var tipo = endpoint.GetParameters().FirstOrDefault(p => p.Name == m.Groups[1].Value)?.ParameterType;
            return tipo == typeof(int) || tipo == typeof(long) ? "1"
                : tipo == typeof(string) ? "x"
                : Guid.Empty.ToString("D");
        });
}
