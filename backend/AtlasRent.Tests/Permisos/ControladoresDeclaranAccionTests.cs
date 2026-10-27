using System.Reflection;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Services;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace AtlasRent.Tests.Permisos;

/// <summary>
/// BUG-051 (c): recorre por reflexión TODOS los controladores reales y comprueba que ningún endpoint
/// queda sin declarar. <see cref="MatrizPermisosTests"/> solo prueba lo sembrado en BD; esto prueba que el
/// código de la API declara su acción en cada endpoint (denegar por omisión, docs/DOC_TECNICA.md §6).
/// </summary>
public sealed class ControladoresDeclaranAccionTests
{
    internal static IEnumerable<MethodInfo> Endpoints() =>
        typeof(AccionAttribute).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsPublic: true } && typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => !m.IsSpecialName
                && m.GetCustomAttribute<NonActionAttribute>() is null
                && m.GetCustomAttributes().OfType<HttpMethodAttribute>().Any());

    [Fact]
    public void ElDescubrimientoEncuentraLosEndpointsReales()
    {
        // Si la reflexión no ve nada, las demás comprobaciones pasarían en vacío.
        Endpoints().Count().Should().BeGreaterThan(100);
    }

    [Fact]
    public void TodoEndpointDeclaraAccionOEsAnonimoExplicito()
    {
        var sinDeclarar = Endpoints()
            .Where(m =>
                m.GetCustomAttribute<AccionAttribute>() is null
                && m.DeclaringType!.GetCustomAttribute<AccionAttribute>() is null
                && m.GetCustomAttribute<AllowAnonymousAttribute>() is null
                && m.DeclaringType!.GetCustomAttribute<AllowAnonymousAttribute>() is null)
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        sinDeclarar.Should().BeEmpty("un endpoint sin [Accion] ni [AllowAnonymous] se rechazaría en ejecución");
    }

    [Fact]
    public void NingunControladorDeclaraAccionEnLaClase()
    {
        // La acción va en cada método: una de clase se mezclaría con la del método y el permiso real
        // del endpoint dependería del orden de los metadatos.
        typeof(AccionAttribute).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && t.GetCustomAttribute<AccionAttribute>() is not null)
            .Select(t => t.Name)
            .Should().BeEmpty();
    }

    [Fact]
    public async Task SiHayAccionEnClaseYEnMetodoManda_ElMetodo()
    {
        var politica = new PoliticaQueAnota();
        var filtro = new FiltroAccionGlobal(politica);
        var descriptor = new ActionDescriptor
        {
            // El orden real de ASP.NET Core: primero los atributos de la clase, después los del método.
            EndpointMetadata = [new AccionAttribute("clase.accion"), new AccionAttribute("metodo.accion")],
        };
        var contexto = new AuthorizationFilterContext(
            new ActionContext(new DefaultHttpContext(), new RouteData(), descriptor),
            []);

        await filtro.OnAuthorizationAsync(contexto);

        politica.Exigidas.Should().Equal("metodo.accion");
    }

    private sealed class PoliticaQueAnota : IPolicyService
    {
        public List<string> Exigidas { get; } = [];

        public IReadOnlySet<string> Permisos => new HashSet<string>();

        public Task CargarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public bool Puede(string accion) => true;

        public void Exigir(string accion, string? recurso = null) => Exigidas.Add(accion);

        public Task<bool> PuedeAsync(string accion, Guid usuarioId, CancellationToken ct = default) => Task.FromResult(true);

        public Task<IReadOnlySet<string>> PermisosDeAsync(Guid usuarioId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task<string?> RolDeAsync(Guid usuarioId, CancellationToken ct = default) => Task.FromResult<string?>(null);

        public Task ExigirDentroDelTechoAsync(Guid usuarioId, string accion, bool activar, CancellationToken ct = default) => Task.CompletedTask;

        public Task AuditarDenegadoAsync(string accion, string? recurso, CancellationToken ct = default) => Task.CompletedTask;
    }
}
