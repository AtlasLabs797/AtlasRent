using AtlasRent.Api.Services;
using Microsoft.Extensions.Logging;

namespace AtlasRent.Watchdog;

/// <summary>La operación crítica no puede empezar porque no hay una copia anterior válida.</summary>
public sealed class WatchdogSinCopiaPreviaException(string mensaje) : Exception(mensaje);

/// <summary>
/// Operaciones críticas del Watchdog: restaurar una copia y actualizar la instalación.
/// <para>
/// La regla que comparten las dos es la que las hace un watchdog y no un script: <strong>antes de
/// tocar nada, tiene que existir una copia anterior que verifique</strong>. Si no la hay, se para y
/// dice por qué. Un watchdog que actualiza sin red de seguridad convierte un despliegue malo en una
/// pérdida de datos, que es justo lo contrario de lo que se le pide.
/// </para>
/// </summary>
public sealed class OperacionesWatchdog(ServicioBackup copias, ILogger<OperacionesWatchdog> registro)
{
    /// <summary>
    /// Restaura una copia en una carpeta: los documentos en su sitio y el volcado de la base en un
    /// archivo aparte.
    /// <para>
    /// La copia se abre y se comprueba <strong>antes</strong> de escribir nada. Aplicar el volcado
    /// sobre una base en marcha es cosa del runbook, no de aquí.
    /// </para>
    /// </summary>
    /// <exception cref="WatchdogSinCopiaPreviaException">
    /// La copia no existe, no se descifra o no trae datos. No se ha escrito nada.
    /// </exception>
    public async Task<RestauracionResultada> RestaurarAsync(
        string nombreCopia,
        string carpetaDestino,
        CancellationToken ct)
    {
        var ruta = Path.Combine(copias.Carpeta, nombreCopia);
        var copia = await copias.VerificarAsync(ruta, ct);
        if (copia is null)
        {
            throw new WatchdogSinCopiaPreviaException(
                $"La copia '{nombreCopia}' no verifica (no existe, no se descifra o no trae datos). "
                + "No se ha tocado nada.");
        }

        var documentos = copia.Documentos();
        var carpetaDocumentos = Path.Combine(carpetaDestino, "documentos");
        Directory.CreateDirectory(carpetaDocumentos);

        foreach (var (nombre, datos) in documentos)
        {
            var destino = Path.Combine(carpetaDocumentos, nombre.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            await File.WriteAllBytesAsync(destino, datos, ct);
        }

        var rutaDump = Path.Combine(carpetaDestino, "bd.sql");
        await File.WriteAllBytesAsync(rutaDump, copia.Dump(), ct);

        registro.LogInformation(
            "Copia del {Fecha} restaurada: {Documentos} documentos y el volcado en {Destino}",
            copia.Fecha, documentos.Count, carpetaDestino);

        // TODO(A#): aplicar el volcado sobre PostgreSQL (psql) es el paso siguiente y va en el
        // runbook FP-03 de DOC_TECNICA §11, que decide si se restaura sobre una base nueva o en
        // caliente. Aquí solo se deja el volcado listo y verificado.
        registro.LogWarning(
            "El volcado está restaurado en {Dump} pero NO se ha aplicado a la base de datos; "
            + "eso lo hace el runbook FP-03",
            rutaDump);

        return new RestauracionResultada(copia.Fecha, rutaDump, documentos.Count);
    }

    /// <summary>
    /// Actualiza la instalación con un paquete, y solo si antes ha hecho una copia que verificar.
    /// <para>
    /// Son dos redes, en este orden y sin saltos: primero la copia de seguridad de la base de datos
    /// (que es la que <see cref="ServicioBackup"/> ya verifica), y después la copia de los archivos
    /// de la instalación. Si cualquiera de las dos falla, se aborta sin haber movido un archivo.
    /// </para>
    /// </summary>
    /// <exception cref="WatchdogSinCopiaPreviaException">
    /// No hay copia de seguridad válida o no se pudo dejar la copia de la instalación. No se ha
    /// tocado nada.
    /// </exception>
    public async Task<ActualizacionResultada> ActualizarAsync(
        string rutaPaquete,
        string carpetaInstalacion,
        CancellationToken ct)
    {
        if (!File.Exists(rutaPaquete))
        {
            throw new FileNotFoundException($"No existe el paquete '{rutaPaquete}'.", rutaPaquete);
        }

        var copiaPrevia = await copias.UltimaCopiaValidaAsync(ct)
            ?? throw new WatchdogSinCopiaPreviaException(
                "No hay ninguna copia de seguridad que verifique. No se actualiza: sin red de seguridad "
                + "una actualización puede dejar el sistema sin base de datos recuperable.");

        registro.LogInformation(
            "Copia de seguridad previa verificada: {Archivo} del {Fecha}",
            Path.GetFileName(copiaPrevia.RutaArchivo), copiaPrevia.Fecha);

        var carpetaPrevia = carpetaInstalacion + ".prev";
        CopiaDeArchivos(carpetaInstalacion, carpetaPrevia);
        ComprobarCopiaDeArchivos(carpetaInstalacion, carpetaPrevia);

        registro.LogInformation(
            "Copia previa de la instalación lista en {Carpeta}", carpetaPrevia);

        // TODO(A#): validar la firma y la integridad del paquete antes de descomprimirlo. Llega con
        // F1-13, que es donde se publican los releases de GitHub con firma. Hasta entonces el
        // Watchdog solo se lanza desde el procedimiento de instalación, con un paquete de origen
        // conocido, y no desde ningún sitio que acepte un paquete de fuera.
        registro.LogWarning(
            "El paquete {Paquete} se descomprime sin comprobar firma: la validación de firma llega con F1-13",
            Path.GetFileName(rutaPaquete));

        System.IO.Compression.ZipFile.ExtractToDirectory(rutaPaquete, carpetaInstalacion, overwriteFiles: true);

        registro.LogInformation("Paquete aplicado en {Carpeta}", carpetaInstalacion);

        return new ActualizacionResultada(copiaPrevia.RutaArchivo, carpetaPrevia);
    }

    /// <summary>
    /// Copia los archivos de la instalación a una carpeta hermana. Se rehace entera en vez de
    /// incrementarse: una copia anterior parcial es justo la copia que no sirve.
    /// </summary>
    private static void CopiaDeArchivos(string origen, string destino)
    {
        if (Directory.Exists(destino))
        {
            Directory.Delete(destino, recursive: true);
        }

        Directory.CreateDirectory(destino);

        if (!Directory.Exists(origen))
        {
            return;
        }

        foreach (var archivo in Directory.EnumerateFiles(origen, "*", SearchOption.AllDirectories))
        {
            var ruta = Path.Combine(destino, Path.GetRelativePath(origen, archivo));
            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
            File.Copy(archivo, ruta, overwrite: true);
        }
    }

    /// <summary>
    /// La copia de archivos vale si tiene los mismos archivos que la instalación. Comparar el
    /// recuento y el tamaño total es lo que hace falta para saber que no se quedó por el camino la
    /// mitad, sin reescribir un hash de cada archivo en cada actualización.
    /// </summary>
    private static void ComprobarCopiaDeArchivos(string origen, string copia)
    {
        if (!Directory.Exists(copia))
        {
            throw new WatchdogSinCopiaPreviaException(
                "No se pudo crear la copia previa de la instalación. No se actualiza.");
        }

        var esperado = Medir(origen);
        var copiado = Medir(copia);

        if (esperado != copiado)
        {
            throw new WatchdogSinCopiaPreviaException(
                $"La copia previa de la instalación no cuadra (se esperaban {esperado.Archivos} archivos "
                + $"y {esperado.Bytes} bytes; hay {copiado.Archivos} y {copiado.Bytes}). No se actualiza.");
        }
    }

    private static (int Archivos, long Bytes) Medir(string carpeta) =>
        Directory.Exists(carpeta)
            ? Directory
                .EnumerateFiles(carpeta, "*", SearchOption.AllDirectories)
                .Aggregate((Archivos: 0, Bytes: 0L),
                    (total, archivo) => (total.Archivos + 1, total.Bytes + new FileInfo(archivo).Length))
            : (0, 0L);
}

/// <summary>Lo que dejó una restauración.</summary>
/// <param name="Fecha">Fecha de la copia restaurada.</param>
/// <param name="RutaDump">Dónde quedó el volcado de la base de datos.</param>
/// <param name="Documentos">Cuántos documentos se restauraron.</param>
public sealed record RestauracionResultada(DateOnly Fecha, string RutaDump, int Documentos);

/// <summary>Lo que dejó una actualización.</summary>
/// <param name="RutaCopiaSeguridad">Copia de seguridad verificada que respalda esta actualización.</param>
/// <param name="CarpetaPrevia">Carpeta con los archivos de la instalación antes de actualizarla.</param>
public sealed record ActualizacionResultada(string RutaCopiaSeguridad, string CarpetaPrevia);