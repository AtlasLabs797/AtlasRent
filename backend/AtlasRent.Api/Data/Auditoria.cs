using System.Text.Json;

namespace AtlasRent.Api.Data;

/// <summary>
/// Registro de auditoría. Append-only a nivel de base de datos (<c>db/scripts/03_auditoria_append_only.sql</c>):
/// no tiene <c>deleted_at</c> ni <c>deleted_by</c> porque un registro de auditoría nunca se borra.
/// <para>
/// La fila está firmada (<see cref="Firma"/>) encadenando el hash de la anterior (<see cref="FirmaAnterior"/>).
/// </para>
/// </summary>
public sealed class Auditoria
{
    public Guid Id { get; set; }

    /// <summary>Autor de la acción. Nulo en acciones de sistema (jobs, mantenimiento).</summary>
    public Guid? UsuarioId { get; set; }

    /// <summary>Acción declarada por el endpoint o por el job.</summary>
    public string Accion { get; set; } = string.Empty;

    /// <summary>Entidad afectada, en singular y en español sin tildes (<c>factura</c>, <c>cobro</c>).</summary>
    public string? Entidad { get; set; }

    /// <summary>Identificador visible de la fila afectada, no su <c>uuid</c>.</summary>
    public string? EntidadId { get; set; }

    /// <summary>Carga útil en <c>jsonb</c>. Nunca contiene secretos ni datos personales.</summary>
    public JsonDocument? Datos { get; set; }

    /// <summary>IP de origen. Obligatoria en toda mutación (AGENTS.md §6).</summary>
    public string Ip { get; set; } = string.Empty;

    /// <summary><c>permitido</c> o <c>denegado</c>.</summary>
    public string? Resultado { get; set; }

    public string? Origen { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string Firma { get; set; } = string.Empty;

    public string FirmaAnterior { get; set; } = string.Empty;

    /// <summary>
    /// Posición en la cadena. La asigna <c>nextval</c> dentro del mismo bloqueo que firma, antes
    /// de insertar, para que orden de firma y orden de inserción coincidan siempre.
    /// <para>
    /// No la genera la base con una identidad a propósito: EF Core usaría <c>RETURNING</c> y
    /// PostgreSQL filtra esa cláusula con la política de SELECT, con lo que un empleado no podría
    /// registrar su propia auditoría.
    /// </para>
    /// </summary>
    public long Secuencia { get; set; }
}