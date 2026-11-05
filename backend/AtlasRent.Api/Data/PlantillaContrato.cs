using System.ComponentModel.DataAnnotations;
using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Data;

/// <summary>
/// Plantilla de contrato (docs/ESPECIFICACION.md §3.5).
/// <para>
/// Es un <b>catálogo</b>, no un documento vivo: el contrato apunta a una plantilla por su
/// <see cref="Codigo"/>, y al aprobar copia su contenido a
/// <see cref="ContratoPlantillaCongelada"/>. Editar una plantilla no cambia los contratos ya
/// aprobados; solo afecta a los que se aprueben después.
/// </para>
/// <para>
/// <b>Nada se borra</b> (AGENTS.md §6): una plantilla que se quiere retirar se desactiva, y el
/// contrato que la tenía congelada sigue con su copia.
/// </para>
/// </summary>
public sealed class PlantillaContrato : IEntidadTrazada
{
    /// <summary>
    /// Código con el que se identifica la plantilla. Es lo que guarda <c>contrato.plantilla_contrato_id</c>
    /// y lo que trae por defecto <c>inmueble.plantilla_contrato_por_defecto</c> (F1-10), así que es
    /// único y no se reutiliza: el código de una plantilla retirada es suyo para siempre, porque
    /// los contratos que lo guardan siguen nombrándolo.
    /// </summary>
    [MaxLength(60)]
    public string Codigo { get; set; } = string.Empty;

    /// <summary>Nombre que ve el usuario al elegir plantilla.</summary>
    [MaxLength(120)]
    public string Nombre { get; set; } = string.Empty;

    /// <summary>
    /// Texto de la plantilla, por bloques. Contenido de usuario: se imprime tal cual y nunca se
    /// interpreta (ver <see cref="SeccionTexto"/>).
    /// </summary>
    public List<SeccionTexto> Secciones { get; set; } = [];

    /// <summary>
    /// Tipos de anexo que esta plantilla necesita. Al aprobar se copia el anexo activo de cada uno
    /// de estos tipos al contrato (<see cref="ContratoPlantillaCongelada"/>), que es lo que sale en
    /// el PDF.
    /// <para>
    /// TODO(A#) F2-07: la especificación dice que los anexos son un catálogo con uno activo por
    /// tipo, pero no dice qué anexo va con qué plantilla. Declararlo aquí es la lectura menos
    /// inventada: es la plantilla, que es el documento, la que dice qué lleva. Un tipo declarado que
    /// no tiene anexo activo no bloquea la aprobación y simplemente no se copia; si el negocio
    /// quiere que sí la bloquee, es una línea aquí.
    /// </para>
    /// </summary>
    public List<TipoAnexo> AnexosTipos { get; set; } = [];

    /// <summary>Si la plantilla se puede usar en contratos nuevos.</summary>
    public bool Activa { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid Id { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}