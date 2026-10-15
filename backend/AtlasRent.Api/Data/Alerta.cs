namespace AtlasRent.Api.Data;

/// <summary>
/// Algo que ha fallado y que alguien tiene que enterarse (docs/ESPECIFICACION.md §3.16,
/// <c>docs/SEGURIDAD.md</c> A.8).
/// <para>
/// Hoy la consume el backup, que es quien tiene un criterio de aceptación que dice literalmente
/// "fallo → alerta". La bandeja de la interfaz es de F2-10; esta tabla es el sitio donde queda
/// registrado el fallo, no el sitio donde se lee.
/// </para>
/// <para>
/// <see cref="Clave"/> es lo que evita el duplicado: dos fallos del mismo tipo el mismo día dejan una
/// sola alerta. Sin eso, un job que reintenta cada hora genera veinticuatro filas idénticas y la
/// alerta deja de ser una señal.
/// </para>
/// <para>
/// <see cref="ResueltaAt"/> es la otra mitad de lo mismo (F2-09): una alerta persistente se mantiene
/// viva mientras el problema siga vivo y se cierra cuando deja de estarlo. Nula significa "abierta".
/// Cerrar no es borrar —la fila dice que pasó, cuándo y cuántas veces se vio— y una alerta cerrada no
/// vuelve a abrirse con el refresco diario: si el problema regresa, se inserta una fila nueva.
/// </para>
/// </summary>
public sealed class Alerta
{
    public Guid Id { get; set; }

    /// <summary>
    /// Identidad estable de la alerta dentro de su tipo, por ejemplo <c>backup:2026-10-10</c>. Única:
    /// es la que impide que el mismo fallo se registre una y otra vez.
    /// </summary>
    public string Clave { get; set; } = string.Empty;

    /// <summary>Qué falló: <c>backup</c>, <c>tipo_cambio</c>, <c>comprobante</c>…</summary>
    public string Tipo { get; set; } = string.Empty;

    /// <summary>
    /// El motivo, en texto que se pueda leer sin abrir el log. No lleva datos de la base: si el
    /// respaldo falló porque la base está caída, el motivo es eso, no el error de PostgreSQL.
    /// </summary>
    public string Mensaje { get; set; } = string.Empty;

    public DateTimeOffset OcurridoAt { get; set; }

    /// <summary>
    /// Cuántas veces se ha visto este mismo fallo desde la primera vez. Se incrementa en vez de
    /// insertar otra fila: el valor importante es "lleva tres días fallando", que con filas nuevas
    /// no se vería.
    /// </summary>
    public int Repeticiones { get; set; }

    /// <summary>
    /// Cuándo se cerró la alerta, si está cerrada. Nula = abierta.
    /// <para>
    /// Es el reloj del cierre, no un estado: cerrarla dos veces no cambia la fecha de la primera, para
    /// que "se resolvió el martes" siga siendo cierto al leerlo dentro de un mes.
    /// </para>
    /// </summary>
    public DateTimeOffset? ResueltaAt { get; set; }
}