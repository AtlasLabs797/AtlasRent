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
    /// La aplicación escribe alertas pero no las borra: el registro de un fallo de seguridad tiene
    /// que sobrevivir. Se comprueba con el rol real de la API, que es quien escribe.
    /// </summary>
    [Fact]
    public async Task LaAplicacionPuedeEscribirLaAlertaConSuPropioRol()
    {
        var ct = TestContext.Current.CancellationToken;
        var sesion = new ContextoSesion { Rol = Roles.Admin };
        await using var contexto = bd.CrearContexto(bd.CadenaApp, sesion);

        // Si rent_app no tuviera el INSERT, la escritura lanzaría y ServicioAlertas se la tragaría
        // dejando solo el log: por eso se mira la fila, no que la llamada no Rompa.
        await new ServicioAlertas(contexto, new RelojPrueba())
            .RegistrarAsync("backup", $"{Clave}-permisos", "escrita con rent_app", ct);

        (await contexto.Alerta.AnyAsync(a => a.Clave == $"{Clave}-permisos", ct)).Should().BeTrue(
            "el INSERT lo concede la migración; sin ese GRANT la alerta se perdía en silencio");
    }
}