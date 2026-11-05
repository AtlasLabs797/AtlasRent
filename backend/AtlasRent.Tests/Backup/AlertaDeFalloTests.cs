using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Backup;

/// <summary>
/// F1-12: el registro de alertas que cumple el "fallo → alerta" del criterio de aceptación.
/// <para>
/// Se prueba contra PostgreSQL de verdad, y no con un doble en memoria, por dos motivos concretos:
/// la restricción de unicidad sobre <c>alerta.clave</c> solo existe en la base, y los permisos de
/// <c>rent_app</c> los concede la migración. Un doble en memoria pasaría las dos cosas.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class AlertaDeFalloTests(ContenedorBdFixture bd)
{
    /// <summary>Clave propia de este test: la tabla es compartida por todos los de la colección.</summary>
    private const string Clave = "test-alerta-f1-12";

    [Fact]
    public async Task ElMismoFalloNoSeRegistraDosVecesYAcumulaRepeticiones()
    {
        var ct = TestContext.Current.CancellationToken;
        var sesion = new ContextoSesion { Rol = Roles.Admin };
        await using var contexto = bd.CrearContexto(bd.CadenaApp, sesion);
        var alertas = new ServicioAlertas(contexto, new RelojPrueba());

        await alertas.RegistrarAsync("backup", Clave, "la copia falló", ct);
        await alertas.RegistrarAsync("backup", Clave, "la copia volvió a fallar", ct);
        await alertas.RegistrarAsync("backup", Clave, "la copia falló otra vez", ct);

        var guardadas = await contexto.Alerta.Where(a => a.Clave == Clave).ToListAsync(ct);

        guardadas.Should().HaveCount(1, "la clave es única: el fallo del día es uno, no tres");
        guardadas[0].Repeticiones.Should().Be(3, "lo que importa es cuántas veces lleva fallando");
        guardadas[0].Mensaje.Should().Be("la copia falló otra vez", "el motivo es el último visto");
        guardadas[0].Tipo.Should().Be("backup");
    }

    /// <summary>
    /// Dos fallos de tipos distintos son dos alertas. Si compartieran clave, el fallo del tipo de
    /// cambio taparía el del backup y solo se vería uno.
    /// </summary>
    [Fact]
    public async Task FallosDeTiposDistintosSonAlertasDistintas()
    {
        var ct = TestContext.Current.CancellationToken;
        var sesion = new ContextoSesion { Rol = Roles.Admin };
        await using var contexto = bd.CrearContexto(bd.CadenaApp, sesion);
        var alertas = new ServicioAlertas(contexto, new RelojPrueba());

        await alertas.RegistrarAsync("backup", $"{Clave}-a", "falló el backup", ct);
        await alertas.RegistrarAsync("tipo_cambio", $"{Clave}-b", "falló el tipo de cambio", ct);

        var tipos = await contexto.Alerta
            .Where(a => a.Clave == $"{Clave}-a" || a.Clave == $"{Clave}-b")
            .Select(a => a.Tipo)
            .ToListAsync(ct);

        tipos.Should().BeEquivalentTo(["backup", "tipo_cambio"]);
    }

    /// <summary>
    /// El job de backup corre desde Hangfire <b>sin rol de sesión</b>: no hay usuario, no hay
    /// <c>app.rol</c>. Si la escritura dependiera de un rol, la alerta se perdería exactamente en el
    /// caso para el que existe —el respaldo diario— y el fallo volvería a ser solo una línea de log.
    /// Se escribe con el rol real de la aplicación y con la sesión vacía.
    /// </summary>
    [Fact]
    public async Task LaAlertaSeEscribeSinRolDeSesionPorqueElJobNoTiene()
    {
        var ct = TestContext.Current.CancellationToken;

        // Sin rol: es como lo ejecuta Hangfire.
        await using var contexto = bd.CrearContexto(bd.CadenaApp, new ContextoSesion());
        await new ServicioAlertas(contexto, new RelojPrueba())
            .RegistrarAsync("backup", $"{Clave}-sin-rol", "escrita sin app.rol", ct);

        // Con el rol de la API, por si algún día la alerta se escribe desde una petición.
        var sesion = new ContextoSesion { Rol = Roles.Admin };
        await using var contextoConRol = bd.CrearContexto(bd.CadenaApp, sesion);
        await new ServicioAlertas(contextoConRol, new RelojPrueba())
            .RegistrarAsync("backup", $"{Clave}-con-rol", "escrita con app.rol", ct);

        // Si rent_app no tuviera el INSERT, ServicioAlertas se tragaría la excepción y estas filas no
        // existirían: por eso se mira la fila y no que la llamada no rompiera.
        (await contexto.Alerta.CountAsync(a => a.Clave.StartsWith($"{Clave}-sin-rol")
            || a.Clave.StartsWith($"{Clave}-con-rol"), ct)).Should().Be(2,
            "el INSERT lo concede la migración; sin ese GRANT la alerta se perdía en silencio");
    }
}