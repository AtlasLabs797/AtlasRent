using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace AtlasRent.Tests.Datos;

/// <summary>
/// F1-03, aceptación: UPDATE/DELETE/TRUNCATE en <c>auditoria</c> con el rol de la app fallan y
/// la verificación de integridad detecta una fila alterada.
/// <para>
/// Todos los tests hablan con el contenedor como <c>rent_app</c>, que es como habla la API.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class AuditoriaAppendOnlyTests(ContenedorBdFixture bd)
{
    private const string Ip = "203.0.113.24";

    [Fact]
    public async Task RentAppPuedeInsertarEnAuditoria()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var servicio = new AuditoriaService(contexto);

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);
        var fila = await servicio.RegistrarAsync(new RegistroAuditoria
        {
            Accion = "factura.emitir",
            Ip = Ip,
            Entidad = "factura",
            EntidadId = "F-0001",
            DatosJson = """{"total":"100.00","moneda":"USD"}""",
            Resultado = "permitido",
            Origen = "api",
        }, ct);
        await transaccion.CommitAsync(ct);

        fila.Firma.Should().NotBeNullOrWhiteSpace();
        fila.FirmaAnterior.Should().NotBeNullOrWhiteSpace();
        fila.Secuencia.Should().BeGreaterThan(0);

        await using var conexion = await AbrirAsync(bd.CadenaSuperusuario);
        await using var consulta = new NpgsqlCommand(
            "SELECT count(*) FROM auditoria WHERE id = @id", conexion);
        consulta.Parameters.AddWithValue("id", fila.Id);
        var existentes = (long)(await consulta.ExecuteScalarAsync(ct) ?? 0L);
        existentes.Should().Be(1);
    }

    [Fact]
    public async Task RentAppNoPuedeActualizarAuditoria()
    {
        var ct = TestContext.Current.CancellationToken;
        await InsertarComoApp("auditoria.actualizar", ct);

        await using var conexion = await AbrirAsync(bd.CadenaApp);
        await using var comando = new NpgsqlCommand(
            "UPDATE auditoria SET resultado = 'denegado' WHERE entidad = @entidad", conexion);
        comando.Parameters.AddWithValue("entidad", "auditoria.actualizar");

        var excepcion = await ObtenerPostgresErrorAsync(() => comando.ExecuteNonQueryAsync(ct));
        excepcion.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task RentAppNoPuedeDesactivarElTriggerDeAuditoria()
    {
        var ct = TestContext.Current.CancellationToken;

        // SEC-020: el rol con el que corre la API (y no solo el de la fábrica) no puede quitar el
        // append-only. Hace falta ser dueño de la tabla.
        await using var conexion = await AbrirAsync(bd.CadenaApp);
        await using var comando = new NpgsqlCommand("ALTER TABLE auditoria DISABLE TRIGGER ALL", conexion);

        var excepcion = await ObtenerPostgresErrorAsync(() => comando.ExecuteNonQueryAsync(ct));
        excepcion.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task RentAppNoPuedeBorrarAuditoria()
    {
        var ct = TestContext.Current.CancellationToken;
        await InsertarComoApp("auditoria.borrar", ct);

        await using var conexion = await AbrirAsync(bd.CadenaApp);
        await using var comando = new NpgsqlCommand(
            "DELETE FROM auditoria WHERE entidad = @entidad", conexion);
        comando.Parameters.AddWithValue("entidad", "auditoria.borrar");

        var excepcion = await ObtenerPostgresErrorAsync(() => comando.ExecuteNonQueryAsync(ct));
        excepcion.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task RentAppNoPuedeTruncarAuditoria()
    {
        var ct = TestContext.Current.CancellationToken;
        await InsertarComoApp("auditoria.truncar", ct);

        await using var conexion = await AbrirAsync(bd.CadenaApp);
        await using var comando = new NpgsqlCommand("TRUNCATE auditoria", conexion);

        var excepcion = await ObtenerPostgresErrorAsync(() => comando.ExecuteNonQueryAsync(ct));
        excepcion.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task ElTriggerRechazaModificarAuditoriaAunConPermisos()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await InsertarComoApp("auditoria.trigger", ct);

        // Superusuario: tiene permisos de sobra, pero el trigger le para los pies igual.
        await using var conexion = await AbrirAsync(bd.CadenaSuperusuario);
        await using var comando = new NpgsqlCommand(
            "UPDATE auditoria SET resultado = 'denegado' WHERE id = @id", conexion);
        comando.Parameters.AddWithValue("id", id);

        var excepcion = await ObtenerPostgresErrorAsync(() => comando.ExecuteNonQueryAsync(ct));
        excepcion.Message.Should().Contain("append-only");
    }

    [Fact]
    public async Task LaVerificacionDeIntegridadDetectaUnaFilaAlteradaPorDebajo()
    {
        var ct = TestContext.Current.CancellationToken;
        var idAlterada = await InsertarComoApp("auditoria.integridad", ct);
        await InsertarComoApp("auditoria.integridad", ct);
        await InsertarComoApp("auditoria.integridad", ct);

        await using (var contexto = bd.CrearContexto(bd.CadenaSuperusuario))
        {
            var integridad = new IntegridadAuditoria(contexto);

            var antes = await integridad.VerificarAsync(ct);
            antes.Integra.Should().BeTrue();
            antes.TotalRegistros.Should().BeGreaterThanOrEqualTo(3);
        }

        // Manipulación por debajo: se apaga el trigger, se edita la fila y se vuelve a encender.
        await using (var conexion = await AbrirAsync(bd.CadenaSuperusuario))
        {
            await EjecutarAsync(conexion, "ALTER TABLE auditoria DISABLE TRIGGER trg_auditoria_inmutable", ct);
            await using var edicion = new NpgsqlCommand(
                "UPDATE auditoria SET resultado = 'denegado' WHERE id = @id", conexion);
            edicion.Parameters.AddWithValue("id", idAlterada);
            await edicion.ExecuteNonQueryAsync(ct);
            await EjecutarAsync(conexion, "ALTER TABLE auditoria ENABLE TRIGGER trg_auditoria_inmutable", ct);
        }

        await using var contextoVerificacion = bd.CrearContexto(bd.CadenaSuperusuario);
        var despues = await new IntegridadAuditoria(contextoVerificacion).VerificarAsync(ct);

        despues.Integra.Should().BeFalse();
        despues.IdRota.Should().Be(idAlterada);
    }

    [Theory]
    [InlineData("gerente", "auditoria.rls.gerente", true)]
    [InlineData("admin", "auditoria.rls.admin", true)]
    [InlineData("empleado", "auditoria.rls.empleado", false)]
    [InlineData("", "auditoria.rls.sinrol", false)]
    public async Task LaLecturaDeAuditoriaDependeDelRol(string rol, string marcador, bool esperaFilas)
    {
        var ct = TestContext.Current.CancellationToken;
        await InsertarComoApp(marcador, ct);

        await using var conexion = await AbrirAsync(bd.CadenaApp);
        await using var transaccion = await conexion.BeginTransactionAsync(ct);

        // Lo mismo que hace el interceptor al abrir la transacción.
        await EjecutarAsync(conexion, "SELECT set_config('app.usuario_id', '', true)", ct);
        await EjecutarAsync(conexion, $"SELECT set_config('app.rol', '{rol}', true)", ct);

        await using var consulta = new NpgsqlCommand(
            "SELECT count(*) FROM auditoria WHERE entidad = @entidad", conexion, transaccion);
        consulta.Parameters.AddWithValue("entidad", marcador);

        var filas = (long)(await consulta.ExecuteScalarAsync(ct) ?? 0L);
        filas.Should().Be(esperaFilas ? 1 : 0);
    }

    /// <summary>Inserta una fila firmada por la app, como en una mutación real.</summary>
    private async Task<Guid> InsertarComoApp(string entidad, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var servicio = new AuditoriaService(contexto);

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);
        var fila = await servicio.RegistrarAsync(new RegistroAuditoria
        {
            Accion = entidad,
            Ip = Ip,
            Entidad = entidad,
            DatosJson = """{"estado":"nuevo"}""",
            Resultado = "permitido",
            Origen = "test",
        }, ct);
        await transaccion.CommitAsync(ct);

        return fila.Id;
    }

    private static async Task<NpgsqlConnection> AbrirAsync(string cadenaConexion)
    {
        var conexion = new NpgsqlConnection(cadenaConexion);
        await conexion.OpenAsync(TestContext.Current.CancellationToken);
        return conexion;
    }

    private static async Task EjecutarAsync(NpgsqlConnection conexion, string sql, CancellationToken ct)
    {
        await using var comando = new NpgsqlCommand(sql, conexion);
        await comando.ExecuteNonQueryAsync(ct);
    }

    private static async Task<PostgresException> ObtenerPostgresErrorAsync(Func<Task> operacion) =>
        (await operacion.Should().ThrowAsync<PostgresException>()).Which;
}