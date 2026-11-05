using AtlasRent.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Cerrojo de transacción que serializa todo lo que lee y cambia la deuda de un contrato (BUG-019):
/// registrar o anular un cobro, el ajuste por diferencia de cambio y la anulación de facturas.
/// <para>
/// Se toma <b>antes de leer los pendientes</b>, dentro de la transacción de la mutación, y PostgreSQL lo
/// suelta al confirmar o deshacer. La clave es el contrato porque ahí vive la deuda y el saldo a favor;
/// un cobro sin contrato no toca ninguna factura ni ningún saldo, y se serializa por cliente solo para
/// no depender de nada más. Una colisión de los 32 bits de la clave solo serializa de más.
/// </para>
/// </summary>
public static class CerrojoCobros
{
    private const int Espacio = 0x434F4252; // "COBR": no choca con los cerrojos de bigint de auditoría e IA.

    public static Task TomarAsync(AtlasRentDbContext contexto, Guid? contratoId, Guid clienteId, CancellationToken ct)
    {
        var bytes = (contratoId ?? clienteId).ToByteArray();
        var clave = BitConverter.ToInt32(bytes, 0) ^ BitConverter.ToInt32(bytes, 4)
            ^ BitConverter.ToInt32(bytes, 8) ^ BitConverter.ToInt32(bytes, 12);

        return contexto.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({Espacio}, {clave})", ct);
    }
}
