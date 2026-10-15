namespace AtlasRent.Api.Dominio;

/// <summary>
/// Estado de un cobro (F3-06, docs/ESPECIFICACION.md §3.10.1).
/// <para>
/// Un cobro tiene solo dos estados porque solo tiene dos hechos posibles: se registró con un motivo
/// y una forma de pago, o se anuló con otro motivo. No hay borradores ni estados intermedios: un
/// cobro es dinero que entró, y o entra o se corrige.
/// </para>
/// <para>
/// <b>Anulado no borra nada</b> (AGENTS.md §6): sus asignaciones siguen en la tabla y lo que
/// cambia es que dejan de contar, que es lo mismo que hace una factura anulada con su número.
/// </para>
/// </summary>
public enum EstadoCobro
{
    /// <summary>Registrado: el dinero entró y sus asignaciones cuentan.</summary>
    Registrado = 1,

    /// <summary>Anulado con motivo. Sus asignaciones dejan de contar y el crédito vuelve al contrato.</summary>
    Anulado = 2,
}

/// <summary>
/// Estado de cobro **derivado** de una factura (docs/ESPECIFICACION.md §3.8). No se guarda: sale de
/// comparar lo que se tiene que cobrar con lo que está cobrado.
/// <para>
/// No existe aquí un valor <c>Vencida</c> porque vencido no sustituye al estado: una factura
/// vencida sigue siendo pendiente, y por eso <c>vencida</c> es una bandera aparte en
/// <see cref="CalculoCobro"/>.
/// </para>
/// </summary>
public enum EstadoCobroFactura
{
    /// <summary>No se ha cobrado nada.</summary>
    Pendiente = 1,

    /// <summary>Se ha cobrado parte, pero queda algo pendiente.</summary>
    Parcial = 2,

    /// <summary>Cobros más retenciones llegan al total: no queda nada pendiente.</summary>
    Pagada = 3,
}