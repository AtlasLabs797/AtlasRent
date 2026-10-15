using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Permisos;

/// <summary>
/// F1-07: desde esta tarea <c>app.rol</c> lleva el rol real del usuario, no un valor por defecto.
/// Las políticas de RLS de <c>auditoria</c> (F1-03) siguen distinguiendo con esos mismos valores:
/// administrador y gerente leen, el empleado no.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class RlsAuditoriaConRolRealTests(ContenedorBdFixture bd)
{
    private const string Ip = "203.0.113.99";

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Gerente, true)]
    [InlineData(Roles.Empleado, false)]
    public async Task LaLecturaDeAuditoriaSigueDependiendoDelRol(string rol, bool esperaFilas)
    {
        var ct = TestContext.Current.CancellationToken;
        await InsertarComoAppAsync($"permisos.rls.{rol}", ct);

        // El interceptor de transacción escribe app.rol desde ContextoSesion, igual que en una
        // petición con el rol real ya resuelto.
        var sesion = new ContextoSesion { UsuarioId = Guid.NewGuid(), Rol = rol };
        await using var contexto = bd.CrearContexto(bd.CadenaApp, sesion);
        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        var filas = await contexto.Auditoria
            .AsNoTracking()
            .CountAsync(a => a.Entidad == $"permisos.rls.{rol}", ct);

        filas.Should().Be(esperaFilas ? 1 : 0);
        await transaccion.RollbackAsync(ct);
    }

    /// <summary>
    /// El rol sale de <c>usuario_rol</c>, no de un valor constante: si un usuario cambia de rol, el
    /// que ve la base de datos en la siguiente petición es el nuevo.
    /// </summary>
    [Fact]
    public async Task ElRolSeLeeDeLaTablaYNoDeUnValorPorDefecto()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(
            bd, "Clave-de-prueba-2026", rol: Roles.Empleado, ct: ct);

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var politica = new PolicyService(
                contexto,
                new ContextoSesion { UsuarioId = usuario.Id, Rol = string.Empty },
                new AuditoriaService(contexto),
                new ResolutorIpPeticion(),
                new SinHttpContext());

            (await politica.RolDeAsync(usuario.Id, ct)).Should().Be(Roles.Empleado);
        }
    }

    private async Task InsertarComoAppAsync(string entidad, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var servicio = new AuditoriaService(contexto);

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);
        await servicio.RegistrarAsync(new RegistroAuditoria
        {
            Accion = entidad,
            Ip = Ip,
            Entidad = entidad,
            DatosJson = """{"estado":"nuevo"}""",
            Resultado = "permitido",
            Origen = "test",
        }, ct);
        await transaccion.CommitAsync(ct);
    }
}

/// <summary>Accesor de <see cref="HttpContext"/> vacío para las pruebas que no van por HTTP.</summary>
public sealed class SinHttpContext : IHttpContextAccessor
{
    public HttpContext? HttpContext { get; set; }
}