namespace AtlasRent.Api.Dominio;

/// <summary>
/// Estados de un contrato (docs/ESPECIFICACION.md §3.5):
/// <c>Borrador → PendienteAprobacion → Aprobado → Activo → BajaProgramada → Finalizado →
/// Liquidado</c>, con el rechazo como vuelta a <see cref="Borrador"/>.
/// <para>
/// Los valores numéricos son los que guarda la columna, como el resto de enums del dominio, y no se
/// reordenan.
/// </para>
/// </summary>
public enum EstadoContrato
{
    /// <summary>
    /// Se está redactando. Lo edita el empleado (docs/ESPECIFICACION.md §2: "crear y editar
    /// borradores") y de aquí sale hacia aprobación.
    /// </summary>
    Borrador = 1,

    /// <summary>
    /// Hay una solicitud esperando al gerente o al administrador. En este estado el contrato ya no
    /// se puede editar sin que la solicitud caduque: lo que cambió ya no es lo que se pidió aprobar.
    /// </summary>
    PendienteAprobacion = 2,

    /// <summary>Aprobado y con la plantilla congelada. Todavía no bloquea la unidad.</summary>
    Aprobado = 3,

    /// <summary>
    /// Vigente: es el que bloquea la unidad en <c>ocupacion_rango</c> con origen
    /// <see cref="OrigenOcupacion.Contrato"/> y el único en el que un cambio crea versión.
    /// </summary>
    Activo = 4,

    /// <summary>
    /// Baja anunciada con fecha efectiva de fin (docs/ESPECIFICACION.md §3.5: preaviso recomendado
    /// de 30 días, sin penalización). Sigue bloqueando la unidad hasta que se finalice, y se puede
    /// anular mientras no llegue la fecha.
    /// </summary>
    BajaProgramada = 5,

    /// <summary>
    /// Terminada la relación: la unidad queda libre (docs/ESPECIFICACION.md §3.5: "el fin libera la
    /// unidad"). Falta la liquidación, que cierra inventario de salida, fianza y saldos.
    /// </summary>
    Finalizado = 6,

    /// <summary>Liquidado: cerrado. Nada más se hace con el contrato y nada se borra de él.</summary>
    Liquidado = 7,
}