using System.Linq.Expressions;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;

namespace AtlasRent.Api.Services;

/// <summary>
/// El pendiente de una factura —neto a cobrar menos lo cobrado menos lo ajustado— como expresión
/// que la base puede evaluar (docs/ESPECIFICACION.md §3.13, <see cref="CalculoCobro"/>).
/// <para>
/// Existe en dos versiones porque hay dos motores y no se llevan bien: <see cref="CalculoCobro"/> la
/// escribe para <c>C#</c> y esta para SQL. EF Core traduce el cuerpo de la lambda, no una llamada a
/// un método del servicio, así que quien filtra en la base tiene que tener la expresión a mano.
/// </para>
/// <para>
/// Los dos conjuntos se leen del contexto del servicio que la pide y EF los traduce como subconsultas
/// correlacionadas: por eso filtrar por pendiente o por vencida no obliga a traer antes todas las
/// facturas. El <c>?? 0</c> no se puede quitar: en SQL la suma de una tabla vacía es nula y una factura
/// sin cobros no tiene pendiente <c>null</c>.
/// </para>
/// </summary>
internal static class PendienteDeFactura
{
    public static Expression<Func<Factura, decimal>> Expresion(AtlasRentDbContext contexto) =>
        f => f.Total - f.Retencion
            - (contexto.CobroAsignacion
                .Where(a => a.FacturaId == f.Id && a.Vigente && a.Cobro!.Estado == EstadoCobro.Registrado)
                .Select(a => (decimal?)a.Importe)
                .Sum() ?? 0m)
            - (contexto.AjusteDiferenciaCambio
                .Where(a => a.FacturaId == f.Id && a.AnuladoAt == null)
                .Select(a => (decimal?)a.Importe)
                .Sum() ?? 0m);

    /// <summary>
    /// «Esta factura tiene algo pendiente», como filtro y no como resultado. Es
    /// <see cref="Expresion"/> comparada con cero: quien trae facturas vencidas de la base lo necesita
    /// filtrando, porque filtrar con la cifra ya calculada obligaría a traerlas todas.
    /// </summary>
    public static Expression<Func<Factura, bool>> ConPendiente(AtlasRentDbContext contexto)
    {
        var pendiente = Expresion(contexto);
        var destino = Expression.Parameter(typeof(Factura), "f");
        var cuerpo = new SustituirParametro(pendiente.Parameters[0], destino).Visit(pendiente.Body)!;

        return Expression.Lambda<Func<Factura, bool>>(
            Expression.GreaterThan(cuerpo, Expression.Constant(0m)),
            destino);
    }

    /// <summary>Sustituye una referencia a un parámetro de expresión por su equivalente en otra.</summary>
    private sealed class SustituirParametro(ParameterExpression origen, Expression destino) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression nodo) =>
            nodo == origen ? destino : base.VisitParameter(nodo);
    }
}