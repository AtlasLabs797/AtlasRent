using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Permisos;

/// <summary>
/// F1-07: la matriz de permisos de docs/ESPECIFICACION.md §2 va como datos sembrados en
/// <c>permiso_rol</c>, no como un <c>switch</c> en el código. Este test transcribe la tabla de la
/// especificación y la compara fila a fila con lo que hay en la base de datos.
/// <para>
/// Lee con <c>rent_app</c>, que es como habla la API: así el test comprueba también los permisos
/// que concede la migración.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class MatrizPermisosTests(ContenedorBdFixture bd)
{
    // Fila 1 a 19 de la especificación. "Sí" es true; la columna "Permiso" del empleado es true
    // en el techo (viene desactivada por usuario, y eso lo comprueba otro test).
    [Theory]
    [InlineData(Roles.Admin, Acciones.ConfiguracionEditar, true)]
    [InlineData(Roles.Admin, Acciones.ClienteVer, true)]
    [InlineData(Roles.Admin, Acciones.ClienteEditar, true)]
    [InlineData(Roles.Admin, Acciones.InmuebleDocumento, true)]
    [InlineData(Roles.Admin, Acciones.ContratoBorrador, true)]
    [InlineData(Roles.Admin, Acciones.ContratoEstado, true)]
    [InlineData(Roles.Admin, Acciones.FacturaVer, true)]
    [InlineData(Roles.Admin, Acciones.FacturaBorrador, true)]
    [InlineData(Roles.Admin, Acciones.FacturaEmitir, true)]
    [InlineData(Roles.Admin, Acciones.FacturaAnular, true)]
    [InlineData(Roles.Admin, Acciones.CobroRegistrar, true)]
    [InlineData(Roles.Admin, Acciones.FianzaMover, true)]
    [InlineData(Roles.Admin, Acciones.DocumentoEnviar, true)]
    [InlineData(Roles.Admin, Acciones.CotizacionEnviar, true)]
    [InlineData(Roles.Admin, Acciones.MensajeriaMasivo, true)]
    [InlineData(Roles.Admin, Acciones.PlantillaEditar, true)]
    [InlineData(Roles.Admin, Acciones.InformeVer, true)]
    [InlineData(Roles.Admin, Acciones.AuditoriaVer, true)]
    [InlineData(Roles.Admin, Acciones.IpDesbloquear, true)]
    [InlineData(Roles.Admin, Acciones.CuentaDesbloquear, true)]
    [InlineData(Roles.Gerente, Acciones.ConfiguracionEditar, false)]
    [InlineData(Roles.Gerente, Acciones.ClienteVer, true)]
    [InlineData(Roles.Gerente, Acciones.ClienteEditar, true)]
    [InlineData(Roles.Gerente, Acciones.InmuebleDocumento, true)]
    [InlineData(Roles.Gerente, Acciones.ContratoBorrador, true)]
    [InlineData(Roles.Gerente, Acciones.ContratoEstado, true)]
    [InlineData(Roles.Gerente, Acciones.FacturaVer, true)]
    [InlineData(Roles.Gerente, Acciones.FacturaBorrador, true)]
    [InlineData(Roles.Gerente, Acciones.FacturaEmitir, true)]
    [InlineData(Roles.Gerente, Acciones.FacturaAnular, true)]
    [InlineData(Roles.Gerente, Acciones.CobroRegistrar, true)]
    [InlineData(Roles.Gerente, Acciones.FianzaMover, true)]
    [InlineData(Roles.Gerente, Acciones.DocumentoEnviar, true)]
    [InlineData(Roles.Gerente, Acciones.CotizacionEnviar, true)]
    [InlineData(Roles.Gerente, Acciones.MensajeriaMasivo, true)]
    [InlineData(Roles.Gerente, Acciones.PlantillaEditar, false)]
    [InlineData(Roles.Gerente, Acciones.InformeVer, true)]
    [InlineData(Roles.Gerente, Acciones.AuditoriaVer, true)]
    [InlineData(Roles.Gerente, Acciones.IpDesbloquear, false)]
    [InlineData(Roles.Gerente, Acciones.CuentaDesbloquear, false)]
    [InlineData(Roles.Empleado, Acciones.ConfiguracionEditar, false)]
    [InlineData(Roles.Empleado, Acciones.ClienteVer, true)]
    [InlineData(Roles.Empleado, Acciones.ClienteEditar, true)]
    [InlineData(Roles.Empleado, Acciones.InmuebleDocumento, false)]
    [InlineData(Roles.Empleado, Acciones.ContratoBorrador, true)]
    [InlineData(Roles.Empleado, Acciones.ContratoEstado, false)]
    [InlineData(Roles.Empleado, Acciones.FacturaVer, true)]
    [InlineData(Roles.Empleado, Acciones.FacturaBorrador, true)]
    [InlineData(Roles.Empleado, Acciones.FacturaEmitir, false)]
    [InlineData(Roles.Empleado, Acciones.FacturaAnular, false)]
    [InlineData(Roles.Empleado, Acciones.CobroRegistrar, false)]
    [InlineData(Roles.Empleado, Acciones.FianzaMover, false)]
    [InlineData(Roles.Empleado, Acciones.DocumentoEnviar, false)]
    // "Permiso" en la especificación: dentro del techo del empleado, pero desactivado por usuario.
    [InlineData(Roles.Empleado, Acciones.CotizacionEnviar, true)]
    [InlineData(Roles.Empleado, Acciones.MensajeriaMasivo, false)]
    [InlineData(Roles.Empleado, Acciones.PlantillaEditar, false)]
    [InlineData(Roles.Empleado, Acciones.InformeVer, false)]
    [InlineData(Roles.Empleado, Acciones.AuditoriaVer, false)]
    [InlineData(Roles.Empleado, Acciones.IpDesbloquear, false)]
    [InlineData(Roles.Empleado, Acciones.CuentaDesbloquear, false)]
    public async Task LaMatrizSembradaCoincideConLaEspecificacion(string rol, string accion, bool enElTecho)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        var hay = await contexto.PermisoRol
            .AsNoTracking()
            .AnyAsync(p => p.RolClave == rol && p.Accion == accion, ct);

        hay.Should().Be(enElTecho);
    }

    /// <summary>Los tres roles de la especificación existen en la tabla <c>rol</c>.</summary>
    [Fact]
    public async Task LosTresRolesEstanSembrados()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        var roles = await contexto.Rol.AsNoTracking().Select(r => r.Clave).ToListAsync(ct);

        roles.Should().BeEquivalentTo([Roles.Admin, Roles.Gerente, Roles.Empleado]);
    }

    /// <summary>
    /// Toda acción que el usuario nuevo recibe desactivada tiene que estar dentro del techo del rol:
    /// si no lo estuviera, el permiso por usuario intentaría ampliar por encima (D17).
    /// </summary>
    [Fact]
    public async Task LasAccionesDesactivadasAlCrearUnUsuarioEstanDentroDeSuTecho()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        foreach (var accion in MatrizPermisos.DesactivadasAlCrearUsuario)
        {
            Acciones.Todas.Should().Contain(accion);
            (await contexto.PermisoRol.AsNoTracking()
                .AnyAsync(p => p.Accion == accion, ct))
                .Should().BeTrue($"si no, desactivar '{accion}' al crear el usuario no significaría nada");
        }
    }

    /// <summary>
    /// Sin filas de <c>usuario_permiso</c> nuevos, un usuario con rol toma el techo tal cual; solo se
    /// guarda lo que difiere.
    /// </summary>
    [Fact]
    public async Task ElRolSeAplicaAlCrearElUsuarioYNoAlUsar()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(
            bd, "Clave-de-prueba-2026", rol: Roles.Gerente, ct: ct);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        (await contexto.UsuarioRol.AsNoTracking().SingleAsync(r => r.UsuarioId == usuario.Id, ct))
            .RolClave.Should().Be(Roles.Gerente);
        (await contexto.UsuarioPermiso.AsNoTracking().CountAsync(p => p.UsuarioId == usuario.Id, ct))
            .Should().Be(0);
    }
}