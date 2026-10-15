using System.Globalization;
using AtlasRent.Api.Data;

namespace AtlasRent.Api.Services;

/// <summary>
/// Huella del contenido de un contrato (docs/ESPECIFICACION.md §3.5).
/// <para>
/// Es lo que convierte "el empleado lo manda y lo aprueba un gerente" en algo con Verification:
/// cuando el contrato pasa a pendiente se guarda esta huella, y si el contrato cambia mientras
/// espera, la huella deja de valer, la solicitud caduca y aprobar da 409.
/// </para>
/// <para>
/// <b>Qué entra y qué no, y por qué.</b> Entra el contenido que el gerente va a aprobar: cliente,
/// unidad, facturadora, inicio, duración inicial, renta, moneda, día de pago, fianza, regla y
/// duración de renovación, plantilla y observaciones. <b>No entra el estado ni las marcas del cambio
/// de estado</b>, y esa es la parte que hay que acertar: si la huella incluyera <c>estado</c>,
/// guardar el estado "pendiente" cambiaría la huella que se acaba de calcular y el contrato se
/// caducaría solo en el instante de enviarlo, sin que nadie hubiera tocado nada. Ese es el test
/// <c>AprobarRecienEnviadoFunciona</c>: aprobar sin haber editado nada tiene que funcionar.
/// </para>
/// <para>
/// No lleva clave ni sal porque no es un secreto: es un checksum para detectar que el documento
/// cambió. Se calcula como SHA-256 de una cadena canónica de formato fijo, en cultura invariante, con
/// los importes ya redondeados a dos decimales, de modo que el mismo contrato produzca siempre la
/// misma cadena.
/// </para>
/// </summary>
internal static class HuellaContrato
{
    /// <summary>
    /// Versión del formato. Si algún día se añade un campo a la huella, esta versión cambia: las
    /// solicitudes pendientes que se enviaron con el formato viejo se caducan (el contenido ya no se
    /// puede comparar) en vez de aprobarse por una coincidencia que ya no significa nada.
    /// </summary>
    private const string Version = "contrato.v1";

    public static string Calcular(Contrato contrato) => EmisorTokens.HashToken(Contenido(contrato));

    /// <summary>La cadena canónica, separada por saltos de línea.</summary>
    private static string Contenido(Contrato contrato) => string.Join('\n',
    [
        Version,
        $"cliente={contrato.ClienteId:N}",
        $"unidad={contrato.UnidadId:N}",
        $"facturadora={contrato.FacturadoraId:N}",
        $"inicio={contrato.FechaInicio:yyyy-MM-dd}",
        $"duracion_inicial={contrato.DuracionInicialMeses.ToString(CultureInfo.InvariantCulture)}",
        $"renta={Importe(contrato.Renta)}",
        $"moneda={(int)contrato.Moneda}",
        $"dia_pago={contrato.DiaPago.ToString(CultureInfo.InvariantCulture)}",
        $"fianza={Importe(contrato.Fianza)}",
        $"regla_renovacion={(int)contrato.ReglaRenovacion}",
        $"renovacion_meses={contrato.RenovacionMeses.ToString(CultureInfo.InvariantCulture)}",
        $"plantilla={contrato.PlantillaContratoId}",
        $"observaciones={contrato.Observaciones}",
    ]);

    /// <summary>Importe con dos decimales fijos, en cultura invariante: 1200 y 1200,00 son la misma cifra.</summary>
    private static string Importe(decimal importe) => importe.ToString("0.00", CultureInfo.InvariantCulture);
}