using System.ComponentModel.DataAnnotations;
using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// La plantilla congelada de un contrato (docs/ESPECIFICACION.md §3.5: "Plantilla congelada al
/// aprobar").
/// <para>
/// <b>Por qué una tabla propia y no una referencia.</b> F2-05 guardaba el código de la plantilla y su
/// fecha, que es congelar de nombre: el día que alguien edita la plantilla, todos los contratos ya
/// aprobados cambian solos, que es justo lo que la especificación quiere evitar. Aquí se copia el
/// contenido entero —el texto de la plantilla y el de los anexos que le corresponden— en el momento
/// de aprobar, y no lo vuelve a escribir nadie. El PDF sale de aquí, no del catálogo.
/// </para>
/// <para>
/// <b>Por qué en tabla y no en una columna JSON del contrato.</b> El PDF y la auditoría necesitan
/// leer la copia sin cargar el contrato entero y sin joins por las secciones, y una fila por
/// contrato deja el "congelado" visible: <c>EXISTS</c> sobre esta tabla es la prueba de que un
/// contrato tiene o no tiene copia congelada, y un índice único sobre <see cref="ContratoId"/> hace
/// que no pueda haber dos.
/// </para>
/// <para>
/// <b>No se edita ni se borra</b> (AGENTS.md §6): no implementa <see cref="IEntidadTrazada"/> porque
/// no tiene <c>updated_at</c> que mantener; una copia que se pudiera corregir en silencio dejaría de
/// ser la copia de lo que se firmó.
/// </para>
/// </summary>
public sealed class ContratoPlantillaCongelada
{
    /// <summary>Contrato al que pertenece la copia. Uno por contrato: la clave es su identificador.</summary>
    public Guid ContratoId { get; set; }

    public Contrato? Contrato { get; set; }

    /// <summary>
    /// Plantilla del catálogo de la que salió la copia. Referencia al catálogo, no al contrato: si la
    /// plantilla se desactiva o se edita después, esta fila no cambia.
    /// </summary>
    public Guid PlantillaId { get; set; }

    public PlantillaContrato? Plantilla { get; set; }

    /// <summary>Código de la plantilla el día de la aprobación.</summary>
    [MaxLength(60)]
    public string Codigo { get; set; } = string.Empty;

    /// <summary>Nombre de la plantilla el día de la aprobación.</summary>
    [MaxLength(120)]
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Copia del texto de la plantilla. Contenido de usuario, impreso tal cual.</summary>
    public List<SeccionTexto> Secciones { get; set; } = [];

    /// <summary>
    /// Copia de los anexos que la plantilla declaraba y que estaban activos en el catálogo en el
    /// momento de aprobar. Van dentro de la copia y no como referencia al anexo vivo: si el anexo se
    /// edita o se desactiva, el contrato sigue con el texto que tenía cuando se firmó.
    /// </summary>
    public List<AnexoCongelado> Anexos { get; set; } = [];

    /// <summary>Cuándo se copiaron el texto y los anexos. La misma marca que el contrato.</summary>
    public DateTimeOffset CongeladaAt { get; set; }
}