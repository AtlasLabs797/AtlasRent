using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Jobs;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;

namespace AtlasRent.Tests.TipoCambio;

/// <summary>
/// Fuente de tipo de cambio de mentira.
/// <para>
/// Existe porque el criterio de aceptación de F3-01 es, literalmente, qué pasa cuando la API falla:
/// una alerta y nada guardado, con el trabajo sin caer. Probarlo contra la API de verdad sería
/// esperar a que un tercero se caiga, y cuando no se cae la prueba pasa sin comprobar nada.
/// </para>
/// </summary>
public sealed class FuenteTipoCambioFalsa : IFuenteTipoCambio
{
    /// <summary>Valor que devuelve cada moneda.</summary>
    public Dictionary<Moneda, decimal> Valores { get; } = [];

    /// <summary>Si está puesto, falla con esta excepción en vez de responder.</summary>
    public Exception? Fallo { get; set; }

    /// <summary>Cuántas veces le han pedido un valor: una ejecución debe consultar una vez.</summary>
    public int Consultas { get; private set; }

    public Task<decimal> ObtenerAsync(Moneda moneda, CancellationToken ct)
    {
        Consultas++;

        if (Fallo is not null)
        {
            return Task.FromException<decimal>(Fallo);
        }

        return Task.FromResult(
            Valores.TryGetValue(moneda, out var valor)
                ? valor
                : throw new FalloTipoCambioException($"la fuente de prueba no trae {moneda}"));
    }

    /// <summary>Fuente que responde bien con el valor indicado para DOP.</summary>
    public static FuenteTipoCambioFalsa ConValor(decimal valor)
    {
        var fuente = new FuenteTipoCambioFalsa();
        fuente.Valores[Moneda.DOP] = valor;

        return fuente;
    }
}

/// <summary>Atajos de F3-01 para las pruebas de la tarea.</summary>
public static class AyudaTipoCambio
{
    /// <summary>
    /// Fechas alejadas de hoy y de las que usan las demás pruebas. El histórico es de DOP y todas
    /// las pruebas de la colección comparten base, así que cada prueba carga en su propia fecha y
    /// compara recuentos antes y después en lugar de contar filas absolutas.
    /// </summary>
    public static DateOnly Dia(int dias) => new DateOnly(2019, 3, 1).AddDays(dias);

    /// <summary>
    /// Reloj adelantado <paramref name="dias"/> días hacia delante, con lo que el día de negocio de
    /// esa prueba es hoy más <paramref name="dias"/>.
    /// <para>
    /// Existe porque la base es una sola para toda la colección: si dos pruebas escribieran "hoy" y
    /// una de ellas dejara un valor manual, la otra se encontraría una fila ajena que la fuente no
    /// puede pisar y comprobaría lo contrario de lo que quiere comprobar. Cada prueba usa su día.
    /// </para>
    /// </summary>
    public static RelojPrueba RelojDePrueba(int dias = 0)
    {
        var reloj = new RelojPrueba();
        reloj.Avanzar(TimeSpan.FromDays(dias));

        return reloj;
    }

    /// <summary>
    /// Servicio montado a mano, como lo montaría el contenedor. Se llama directamente porque
    /// <see cref="ServicioTiposCambio.ActualizarDesdeLaFuenteAsync"/> no lo llama ningún endpoint:
    /// lo llama el trabajo de las 05:00, y probarlo con el trabajo real significaría meter el
    /// planificador en medio.
    /// </summary>
    public static ServicioTiposCambio ServicioDe(
        AtlasRentDbContext contexto,
        IFuenteTipoCambio fuente,
        RelojPrueba reloj) =>
        new(
            contexto,
            new AuditoriaService(contexto),
            new ServicioIdempotencia(contexto, reloj),
            new ServicioAlertas(contexto, reloj),
            fuente,
            reloj);

    /// <summary>
    /// Contexto de aplicación con rol de administrador, que es con el que corren los trabajos: la
    /// auditoría que escribe el trabajo va con <c>usuario_id</c> nulo y necesita poder insertar.
    /// </summary>
    public static AtlasRentDbContext ContextoDelTrabajo(ContenedorBdFixture bd) =>
        bd.CrearContexto(bd.CadenaApp, new ContextoSesion { Rol = Roles.Admin });

    /// <summary>El trabajo de las 05:00, montado como lo monta el contenedor.</summary>
    public static TrabajoTipoCambio Trabajo(ServicioTiposCambio servicio) => new(servicio);
}
