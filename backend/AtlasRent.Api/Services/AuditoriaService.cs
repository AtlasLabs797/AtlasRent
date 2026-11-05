using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AtlasRent.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Huella de una fila de auditoría: <c>SHA-256(firma_anterior || datos_de_la_fila)</c>
/// (docs/DOC_TECNICA.md §4.3 y <c>db/scripts/03_auditoria_append_only.sql</c>).
/// <para>
/// Los campos se unen con el separador ASCII 0x1F, que no puede aparecer en los datos, y la
/// fecha se escribe con seis decimales (microsegundos) porque es lo que PostgreSQL guarda en
/// <c>timestamptz</c>: si se firmara con más decimales, la verificación nunca cuadraría.
/// </para>
/// <para>
/// <see cref="FirmaGenesis"/> es la firma anterior de la primera fila de la cadena.
/// </para>
/// </summary>
internal static class HuellaAuditoria
{
    /// <summary>Firma anterior de la primera fila: 64 ceros.</summary>
    internal static readonly string FirmaGenesis = new('0', 64);

    // Separador ASCII 0x1F ("Unit Separator"). Se escribe como conversion y no como el caracter
    // literal porque en el fuente el literal es invisible, y cualquier herramienta que
    // normalice el archivo cambiaria el formato de la firma e invalidaria la cadena ya escrita.
    private static readonly char Separador = (char)0x1F;

    private const string FormatoMarcaTiempo = "yyyy-MM-ddTHH:mm:ss.ffffff+00:00";

    /// <summary>
    /// Calcula la firma de <paramref name="fila"/> encadenada con <paramref name="firmaAnterior"/>.
    /// </summary>
    /// <param name="datosTexto">
    /// El <c>jsonb</c> tal y como queda almacenado. PostgreSQL normaliza el JSON (ordena claves,
    /// quita espacios), así que se firma su forma normalizada y no la que envió la aplicación.
    /// </param>
    internal static string Calcular(string firmaAnterior, Auditoria fila, string? datosTexto) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            firmaAnterior + Separador + DatosParaFirmar(fila, datosTexto))));

    private static string DatosParaFirmar(Auditoria fila, string? datosTexto) => string.Join(Separador,
        fila.Id.ToString("D"),
        fila.UsuarioId?.ToString("D") ?? string.Empty,
        fila.Accion,
        fila.Entidad ?? string.Empty,
        fila.EntidadId ?? string.Empty,
        datosTexto ?? string.Empty,
        fila.Ip,
        fila.Resultado ?? string.Empty,
        fila.Origen ?? string.Empty,
        fila.CreatedAt.ToUniversalTime().ToString(FormatoMarcaTiempo, CultureInfo.InvariantCulture));
}

/// <summary>Datos de la acción que se quiere auditar.</summary>
public sealed record RegistroAuditoria
{
    public required string Accion { get; init; }

    /// <summary>IP de origen. Obligatoria en toda mutación (AGENTS.md §6).</summary>
    public required string Ip { get; init; }

    public Guid? UsuarioId { get; init; }

    public string? Entidad { get; init; }

    public string? EntidadId { get; init; }

    /// <summary>Carga útil como texto JSON. Sin secretos ni datos personales.</summary>
    public string? DatosJson { get; init; }

    /// <summary><c>permitido</c> o <c>denegado</c>.</summary>
    public string? Resultado { get; init; }

    public string? Origen { get; init; }
}

/// <summary>
/// Escribe el registro de auditoría firmado dentro de la transacción de la mutación
/// (AGENTS.md §6: toda mutación escribe auditoría con IP en la misma transacción).
/// </summary>
public sealed class AuditoriaService(AtlasRentDbContext contexto)
{
    /// <summary>
    /// Clave del bloqueo consultivo que serializa a todo el que inserta en la cadena:
    /// "AUDI" en hexadecimal. Una sola clave para toda la tabla.
    /// </summary>
    private const long ClaveCadenaAuditoria = 0x41554449;

    public async Task<Auditoria> RegistrarAsync(RegistroAuditoria registro, CancellationToken ct = default)
    {
        if (contexto.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "La auditoría se escribe dentro de la transacción de la mutación (AGENTS.md §6).");
        }

        if (string.IsNullOrWhiteSpace(registro.Ip))
        {
            throw new ArgumentException("Toda mutación audita su IP de origen.", nameof(registro));
        }

        // 1) Orden de la cadena: nadie firma contra una firma anterior que todavía va a cambiar.
        await contexto.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({ClaveCadenaAuditoria})", ct);

        // 2) Posición en la cadena, con el bloqueo ya tomado: el número acompaña a la firma.
        var secuencia = await contexto.Database
            .SqlQuery<long>($"SELECT nextval('auditoria_secuencia_seq') AS \"Value\"")
            .SingleAsync(ct);

        // 3) El jsonb se normaliza en PostgreSQL para poder firmarlo tal y como se guarda.
        var datosTexto = registro.DatosJson is null
            ? null
            : await contexto.Database
                .SqlQuery<string>($"SELECT {registro.DatosJson}::jsonb::text AS \"Value\"")
                .SingleAsync(ct);

        // 4) La firma anterior se lee por una función SECURITY DEFINER: RLS no deja leer
        //    auditoría a un empleado, pero la cadena no puede romperse por el rol de quien muta.
        var firmaAnterior = (await contexto.Database
            .SqlQuery<string>($"SELECT firma AS \"Value\" FROM auditoria_ultima_firma()")
            .SingleOrDefaultAsync(ct)) ?? HuellaAuditoria.FirmaGenesis;

        var fila = new Auditoria
        {
            Id = Guid.NewGuid(),
            UsuarioId = registro.UsuarioId,
            Accion = registro.Accion,
            Entidad = registro.Entidad,
            EntidadId = registro.EntidadId,
            Datos = datosTexto is null ? null : JsonDocument.Parse(datosTexto),
            Ip = registro.Ip,
            Resultado = registro.Resultado,
            Origen = registro.Origen,
            CreatedAt = DateTimeOffset.UtcNow,
            Secuencia = secuencia,
            FirmaAnterior = firmaAnterior,
        };
        fila.Firma = HuellaAuditoria.Calcular(fila.FirmaAnterior, fila, datosTexto);

        contexto.Auditoria.Add(fila);
        // Se inserta ya, con el bloqueo tomado: la posición en la cadena (secuencia) y la firma
        // tienen que quedar fijadas antes de soltarlo. Lo que la mutación tenga pendiente se
        // escribe en esta misma transacción.
        await contexto.SaveChangesAsync(ct);

        return fila;
    }
}