using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AtlasRent.Api.Jobs;

/// <summary>
/// Avisa de los contratos activos cuyo mes natural ya pasó y que siguen sin factura de ese periodo
/// (F3-05, docs/ESPECIFICACION.md §3.16 «periodos sin facturar»).
/// <para>
/// Avisa del mes anterior, no del que está en curso: un mes que no ha terminado se factura al mes
/// siguiente, y avisar de eso todos los días sería ruido que nadie mira.
/// </para>
/// <para>
/// <b>Tipo de alerta nuevo: <c>factura</c>.</b> Los que ya había son de copia, reserva, contrato,
/// comprobante y tipo de cambio, y cada uno tiene su bandeja. Reutilizar «contrato» metería un
/// problema de facturación entre las bajas y los finales sin liquidar; reutilizar «comprobante»
/// mezclaría dos cosas que se miran por motivos distintos. §3.16 enumera los avisos que espera y
/// entre ellos está «periodos sin facturar», así que el tipo es el que le toca.
/// </para>
/// <para>
/// La clave lleva contrato y periodo, así que correrlo dos veces el mismo día no deja dos alertas y
/// el aviso se cierra solo cuando la factura sale (<see cref="ServicioFacturacionPeriodica"/>), no
/// cuando alguien lo marque.
/// </para>
/// </summary>
public sealed class TrabajoPeriodosSinFacturar(
    AtlasRentDbContext contexto,
    IRegistroAlertas alertas,
    TimeProvider reloj)
{
    /// <summary>Tipo de alerta propio de la facturación periódica.</summary>
    public const string TipoAlerta = "factura";

    /// <summary>Clave de deduplicación: un aviso vivo por contrato y por periodo.</summary>
    public static string Clave(Guid contratoId, DateOnly periodoInicio) =>
        ServicioFacturacionPeriodica.ClaveAlertaSinFacturar(contratoId, periodoInicio);

    public async Task EjecutarAsync(CancellationToken ct)
    {
        var hoy = TiempoNegocio.FechaEnZonaNegocio(reloj.GetUtcNow());

        // El mes natural anterior, que es el último que ya está completo. Se pide el día de hoy y
        // se retrocede un mes, no el día 1 del mes: el 1 de marzo ya no tiene que ver con febrero.
        var periodo = PeriodoFacturacion.DelMes(hoy.AddMonths(-1));

        var contratos = await contexto.Contrato
            .AsNoTracking()
            .Where(ServicioFacturacionPeriodica.TocaElMes(periodo))
            .Select(c => new { c.Id, c.UnidadId, c.Renta, c.Moneda })
            .ToListAsync(ct);

        if (contratos.Count == 0)
        {
            Log.Information("Periodos sin facturar: no hay contratos vigentes para {Periodo:MM/yyyy}", periodo.Inicio);
            return;
        }

        var facturados = await contexto.Factura
            .AsNoTracking()
            .Where(f => f.PeriodoInicio == periodo.Inicio && f.Estado != EstadoFactura.Anulada)
            .Select(f => new { f.ContratoId })
            .ToListAsync(ct);

        // Una factura anulada no cuenta como factura del periodo: el aviso tiene que seguir vivo,
        // porque el mes sigue sin facturarse y lo que hay que hacer es emitir la que la sustituya.
        var yaFacturados = facturados
            .Where(f => f.ContratoId is not null)
            .Select(f => f.ContratoId!.Value)
            .ToHashSet();

        var sinFacturar = contratos.Where(c => !yaFacturados.Contains(c.Id)).ToList();

        foreach (var contrato in sinFacturar)
        {
            await alertas.RegistrarAsync(
                TipoAlerta,
                Clave(contrato.Id, periodo.Inicio),
                $"El contrato {contrato.Id:D} de la unidad {contrato.UnidadId:D} no tiene factura de "
                    + $"{periodo.Inicio:MM/yyyy}, que ya terminó.",
                ct);
        }

        Log.Information(
            "Periodos sin facturar: {Cantidad} contrato(s) sin factura de {Periodo:MM/yyyy} de {Vigentes}",
            sinFacturar.Count,
            periodo.Inicio,
            contratos.Count);
    }
}