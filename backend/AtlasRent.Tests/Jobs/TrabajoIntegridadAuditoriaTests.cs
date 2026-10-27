using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Jobs;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace AtlasRent.Tests.Jobs;

/// <summary>
/// F1-12: el trabajo diario de integridad de auditoría solo sirve si lee la cadena entera.
/// <para>
/// Esta clase es la que protege contra el fallo más caro y más silencioso de todo F1-12: la RLS de
/// <c>auditoria</c> esconde las filas a quien no es admin o gerente, y una consulta que no ve filas
/// no da error, devuelve una cadena vacía y <c>Integra</c> dice que todo está bien. Es decir: el
/// trabajo de detectar una auditoría manipulada se quedaría en verde precisamente cuando hay una.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class TrabajoIntegridadAuditoriaTests(ContenedorBdFixture bd)
{
    /// <summary>Fila propia de este test, para que la comprobación no dependa de otros tests.</summary>
    private const string AccionDePrueba = "trabajo-integridad-f1-12";

    [Fact]
    public async Task ElTrabajoCorreConRolDeGerenciaYPorEsoLeeLaCadenaEntera()
    {
        var ct = TestContext.Current.CancellationToken;
        await SembrarAuditoriaDeEmpleadoAsync(ct);

        var sesion = new ContextoSesion();
        await using var contexto = bd.CrearContexto(bd.CadenaApp, sesion);

        var trabajo = new TrabajoIntegridadAuditoria(sesion, contexto, new IntegridadAuditoria(contexto), new OpcionesLimites(), new ServicioAlertas(contexto, TimeProvider.System));
        var resultado = await trabajo.EjecutarAsync(ct);

        TrabajoIntegridadAuditoria.RolRequisito.Should().Be(Roles.Admin);
        sesion.Rol.Should().Be(Roles.Admin, "es lo que le da a la RLS la lectura de auditoría");
        resultado.TotalRegistros.Should().BeGreaterThan(0,
            "la fila sembrada es de un empleado, y el trabajo tiene que verla igual");

        // Y el motivo de que el rol no sea opcional: el mismo trabajo sin rol no ve nada, y aun así
        // diría que la cadena está íntegra.
        var sesionSinRol = new ContextoSesion();
        await using var contextoSinRol = bd.CrearContexto(bd.CadenaApp, sesionSinRol);
        var sinRol = await new IntegridadAuditoria(contextoSinRol).VerificarAsync(ct);

        sinRol.TotalRegistros.Should().Be(0, "la RLS esconde auditoría a quien no es admin ni gerente");
        sinRol.Integra.Should().BeTrue("y por eso, sin el rol del trabajo, el resultado es un falso íntegro");
    }

    /// <summary>BUG-045: una cadena rota deja alerta en la tabla, y repetirla no duplica la fila.</summary>
    [Fact]
    public async Task UnaCadenaRotaDejaUnaSolaAlertaViva()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = new NpgsqlConnection(bd.CadenaSuperusuario);
        await conexion.OpenAsync(ct);
        await Ejecutar(conexion, "DELETE FROM alerta WHERE clave = 'integridad-auditoria'", ct);

        await SembrarAuditoriaDeEmpleadoAsync(ct);
        await Ejecutar(conexion, "ALTER TABLE auditoria DISABLE TRIGGER trg_auditoria_inmutable", ct);
        await Ejecutar(
            conexion,
            "UPDATE auditoria SET resultado = 'denegado' WHERE accion = 'trabajo-integridad-f1-12'",
            ct);
        await Ejecutar(conexion, "ALTER TABLE auditoria ENABLE TRIGGER trg_auditoria_inmutable", ct);

        for (var i = 0; i < 2; i++)
        {
            var sesion = new ContextoSesion();
            await using var contexto = bd.CrearContexto(bd.CadenaApp, sesion);
            var trabajo = new TrabajoIntegridadAuditoria(
                sesion, contexto, new IntegridadAuditoria(contexto), new OpcionesLimites(),
                new ServicioAlertas(contexto, TimeProvider.System));

            (await trabajo.EjecutarAsync(ct)).Integra.Should().BeFalse();
        }

        await using var consulta = new NpgsqlCommand(
            "SELECT repeticiones FROM alerta WHERE tipo = 'auditoria' AND clave = 'integridad-auditoria' "
            + "AND resuelta_at IS NULL",
            conexion);
        var repeticiones = new List<int>();
        await using (var lector = await consulta.ExecuteReaderAsync(ct))
        {
            while (await lector.ReadAsync(ct))
            {
                repeticiones.Add(lector.GetInt32(0));
            }
        }

        repeticiones.Should().Equal(2);

        // Se deja la fila como estaba para no dejar la cadena rota a los demas tests.
        await Ejecutar(conexion, "ALTER TABLE auditoria DISABLE TRIGGER trg_auditoria_inmutable", ct);
        await Ejecutar(
            conexion,
            "UPDATE auditoria SET resultado = 'permitido' WHERE accion = 'trabajo-integridad-f1-12'",
            ct);
        await Ejecutar(conexion, "ALTER TABLE auditoria ENABLE TRIGGER trg_auditoria_inmutable", ct);
    }

    private static async Task Ejecutar(NpgsqlConnection conexion, string sql, CancellationToken ct)
    {
        await using var orden = new NpgsqlCommand(sql, conexion);
        await orden.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Una fila de auditoría escrita por un empleado. Se escribe con el rol de la aplicación y sin
    /// transactions ajenas, como cualquier mutación de la API.
    /// </summary>
    private async Task SembrarAuditoriaDeEmpleadoAsync(CancellationToken ct)
    {
        var sesion = new ContextoSesion { Rol = Roles.Empleado };
        await using var contexto = bd.CrearContexto(bd.CadenaApp, sesion);
        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        await new AuditoriaService(contexto).RegistrarAsync(new RegistroAuditoria
        {
            Accion = AccionDePrueba,
            Ip = "198.51.100.10",
            Entidad = "prueba",
            Resultado = "permitido",
            Origen = "tests",
        }, ct);

        await transaccion.CommitAsync(ct);
    }
}