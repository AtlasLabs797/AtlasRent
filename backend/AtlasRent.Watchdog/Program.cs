// AtlasRent.Watchdog: servicio Windows independiente para operaciones críticas (F1-12).
// Restaura copias de seguridad y actualiza la instalación, y en las dos operaciones exige una copia
// anterior válida antes de tocar nada.
// Registro solo por consola: el servicio de Windows ya captura stdout en el visor de eventos.
//
// Va en una clase y no en sentencias de nivel superior a propósito: el tipo implícito Program de un
// programa con sentencias de nivel superior chocaría con el de AtlasRent.Api, que las pruebas de
// integración necesitan para levantar la API.

using AtlasRent.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AtlasRent.Watchdog;

/// <summary>Punto de entrada del Watchdog. Devuelve el código de salida del servicio Windows.</summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using ILoggerFactory fabrica = LoggerFactory.Create(builder => builder
            .AddSimpleConsole(options => options.SingleLine = true)
            .SetMinimumLevel(LogLevel.Information));

        ILogger<OperacionesWatchdog> registro = fabrica.CreateLogger<OperacionesWatchdog>();

        try
        {
            var configuracion = new ConfigurationBuilder().AddEnvironmentVariables().Build();

            // La copia de seguridad es la red de seguridad de estas operaciones, así que sus opciones
            // se validan antes de hacer nada: si falta la clave, es mejor que el Watchdog no empiece
            // a restaurar cualquier cosa.
            var opcionesBackup = OpcionesBackup.Desde(configuracion);
            var opcionesAlmacen = OpcionesAlmacen.Desde(configuracion);

            var copias = new ServicioBackup(
                opcionesBackup,
                VolcadoPgDump.EnProduccion(),
                new AlmacenDocumentos(opcionesAlmacen),
                new ServicioCifradoCopia(opcionesBackup),
                TimeProvider.System);

            var operaciones = new OperacionesWatchdog(copias, registro);

            switch (args.Length > 0 ? args[0] : "comprobar")
            {
                case "copiar":
                {
                    // F1-12: la copia diaria la dispara el trabajo programado de la API
                    // (docs/DOC_TECNICA.md §8). Esto es lo mismo a mano, para cuando hay que
                    // forzar una copia sin esperar a las 02:00 o para comprobar que la cadena de
                    // conexión del volcado sigue viva. Es idempotente por fecha: si la de hoy ya
                    // está y verifica, no se vuelve a generar.
                    var cadena = configuracion.GetConnectionString("Backup")
                        ?? throw new InvalidOperationException(
                            "Falta la cadena de conexión 'Backup' (ConnectionStrings__Backup, el rol "
                            + "rent_backup). Sin ella no se puede volcar la base de datos.");

                    var copia = await copias.EjecutarAsync(cadena, CancellationToken.None);
                    registro.LogInformation(
                        "Copia del {Fecha} lista en {Archivo}: {Bytes} bytes, {Documentos} documentos",
                        copia.Fecha, copia.RutaArchivo, copia.Bytes, copia.Documentos);
                    return 0;
                }

                case "restaurar":
                {
                    if (args.Length < 3)
                    {
                        return Uso(registro, "restaurar <archivo-de-copia> <carpeta-destino>");
                    }

                    var resultado = await operaciones.RestaurarAsync(args[1], args[2], CancellationToken.None);
                    registro.LogInformation(
                        "Restauración terminada: {Documentos} documentos desde la copia del {Fecha}",
                        resultado.Documentos, resultado.Fecha);
                    return 0;
                }

                case "actualizar":
                {
                    if (args.Length < 3)
                    {
                        return Uso(registro, "actualizar <paquete.zip> <carpeta-de-instalacion>");
                    }

                    var resultado = await operaciones.ActualizarAsync(args[1], args[2], CancellationToken.None);
                    registro.LogInformation(
                        "Actualización terminada. Copia de seguridad: {Copia}. Copia previa: {Previa}",
                        resultado.RutaCopiaSeguridad, resultado.CarpetaPrevia);
                    return 0;
                }

                case "comprobar":
                {
                    var copia = await copias.UltimaCopiaValidaAsync(CancellationToken.None);
                    if (copia is null)
                    {
                        registro.LogError(
                            "No hay ninguna copia que verifique en {Carpeta}", copias.Carpeta);
                        return 1;
                    }

                    registro.LogInformation(
                        "La copia más reciente verifica: {Archivo} del {Fecha}, {Documentos} documentos",
                        Path.GetFileName(copia.RutaArchivo), copia.Fecha, copia.Documentos().Count);
                    return 0;
                }

                default:
                    return Uso(registro, "<copiar|comprobar|restaurar|actualizar>");
            }
        }
        catch (WatchdogSinCopiaPreviaException e)
        {
            // Fallo esperado y peligroso: no había red de seguridad. Queda bien dicho en el registro.
            registro.LogCritical("Operación abortada, no se ha tocado nada: {Motivo}", e.Message);
            return 2;
        }
        catch (Exception e)
        {
            registro.LogError("Fallo del Watchdog ({Tipo}): {Motivo}", e.GetType().Name, e.Message);
            return 1;
        }
    }

    private static int Uso(ILogger registro, string forma)
    {
        registro.LogError("Uso: AtlasRent.Watchdog {Forma}", forma);
        return 1;
    }
}