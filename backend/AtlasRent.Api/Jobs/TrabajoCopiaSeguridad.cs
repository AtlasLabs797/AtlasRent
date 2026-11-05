using AtlasRent.Api.Services;
using Serilog;

namespace AtlasRent.Api.Jobs;

/// <summary>
/// Copia de seguridad cifrada del día (docs/DOC_TECNICA.md §8). Lo programa
/// <see cref="ProgramacionJobs"/> a las 02:00 de Santo Domingo.
/// <para>
/// No hace nada más que llamar a <see cref="ServicioBackup"/>, que ya resuelve el volcado, el
/// cifrado, la comprobación y la rotación. Este trabajo existe para que el calendario de §8 tenga
/// un único sitio donde mirar, y para que se pueda lanzar a mano.
/// </para>
/// </summary>
public sealed class TrabajoCopiaSeguridad(ServicioBackup copias, IConfiguration configuracion)
{
    /// <summary>
    /// Cadena con la que se vuelca la base: <c>ConnectionStrings__Backup</c>, el rol
    /// <c>rent_backup</c>, que solo lee y tiene BYPASSRLS. Nunca <c>Default</c>: con el rol de la
    /// aplicación, el <c>pg_dump</c> sale bloqueado por la RLS o vacío.
    /// </summary>
    private string CadenaDeVolcado =>
        configuracion.GetConnectionString("Backup")
        ?? throw new InvalidOperationException(
            "Falta la cadena de conexión 'Backup' para el trabajo de copia de seguridad. "
            + "Definir ConnectionStrings__Backup (user-secrets en desarrollo y en el servicio en "
            + "producción): sin ella no hay copia.");

    public async Task EjecutarAsync(CancellationToken ct)
    {
        var resultado = await copias.EjecutarAsync(CadenaDeVolcado, ct);

        Log.Information(
            "Copia de seguridad del {Fecha} lista: {Bytes} bytes, {Documentos} documentos"
            + "{Reutilizada}",
            resultado.Fecha,
            resultado.Bytes,
            resultado.Documentos,
            resultado.YaExistia ? " (ya existía y verificó)" : string.Empty);
    }
}