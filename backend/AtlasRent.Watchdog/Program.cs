// AtlasRent.Watchdog: servicio Windows independiente para operaciones críticas
// (restauración de backup, actualización con copia previa y recuperación).
// F1-01 solo deja el proceso en pie y registrándose; las operaciones llegan en F1-12.
// Registro solo por consola: el servicio de Windows ya captura stdout en el visor de eventos.

using Microsoft.Extensions.Logging;

using ILoggerFactory fabrica = LoggerFactory.Create(builder => builder
    .AddSimpleConsole(options => options.SingleLine = true)
    .SetMinimumLevel(LogLevel.Information));

ILogger registro = fabrica.CreateLogger("AtlasRent.Watchdog");

registro.LogInformation("Watchdog iniciado. Zona horaria de negocio: {Zona}", "America/Santo_Domingo");
registro.LogInformation("Operaciones críticas habilitadas en F1-12; por ahora no hay nada que supervisar.");

return 0;