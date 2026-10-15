using System.Text.Json;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AtlasRent.Api.Services;

/// <summary>Lo que hizo una pasada del trabajo de la fuente.</summary>
/// <param name="Cargados">Valores guardados o actualizados desde la fuente.</param>
/// <param name="Respetados">Filas manuales que la fuente no ha pisado.</param>
/// <param name="Fallo">Si la fuente no respondió y no se guardó nada.</param>
public sealed record ResumenTipoCambio(int Cargados, int Respetados, bool Fallo);

/// <summary>
/// El tipo de cambio diario de cada moneda (F3-01, docs/ESPECIFICACION.md §3.12, D9 y D22).
/// <para>
/// <b>Solo para informes consolidados.</b> No convierte nada: la factura y el cobro congelan el
/// cambio en el momento de emitirse o de registrarse (D9) y no lo vuelven a mirar, así que este
/// servicio no toca importes de documentos (docs/ESPECIFICACION.md §3.12).
/// </para>
/// <para>
/// <b>El dólar no se guarda.</b> <see cref="ObtenerAsync"/> devuelve 1 sin tocar la base, y por eso
/// el historical es de DOP solamente y una fila de USD sería ruido con poder de contradecir.
/// </para>
/// <para>
/// <b>La carga manual manda sobre la fuente.</b> Es lo que dice la especificación al hablar del
/// fallo de la API, y es la única forma de que "carga manual" signifique algo: si el trabajo de la
/// mañana siguiente la sobrescribiera, quien la cargó se habría quedado con un día sin cambio y sin
/// enterarse. Al revés, una fila que vino de la fuente sí se actualiza con el valor del día, porque
/// la fuente es la que manda mientras nadie la sustituya.
/// </para>
/// </summary>
public sealed class ServicioTiposCambio(
    AtlasRentDbContext contexto,
    AuditoriaService auditoria,
    ServicioIdempotencia idempotencia,
    IRegistroAlertas alertas,
    IFuenteTipoCambio fuente,
    TimeProvider reloj)
{
    private const string Entidad = "tipo_cambio";

    /// <summary>Tipo de alerta del fallo de la fuente (docs/ESPECIFICACION.md §3.12 y §3.16).</summary>
    public const string TipoAlerta = "tipo_cambio";

    /// <summary>La auditoría es obligatoria y un trabajo no tiene IP de dónde venga (AGENTS.md §6).</summary>
    private const string IpDelTrabajo = "127.0.0.1";

    private const string OrigenDelTrabajo = "job";

    private const string AccionRegistrada = "tipo_cambio.registrado";

    private const string AccionActualizada = "tipo_cambio.actualizado";

    /// <summary>
    /// Clave de la alerta de fallo. Lleva la fecha a propósito y por el motivo contrario al de las
    /// alertas de F2-10: aquí el problema es "hoy no hay cambio", que mañana deja de ser cierto. Con
    /// fecha en la clave, cada día fallido es un aviso y no una fila que se refresca para siempre; y
    /// volver a fallar el mismo día actualiza el mismo aviso en vez de llenar la bandeja.
    /// </summary>
    public static string ClaveFallo(DateOnly fecha) => $"fallo:{fecha:D}";

    private static readonly Dictionary<string, Func<IQueryable<TipoCambio>, bool, IOrderedQueryable<TipoCambio>>>
        Ordenes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["fecha"] = (q, desc) => desc ? q.OrderByDescending(t => t.Fecha) : q.OrderBy(t => t.Fecha),
            ["moneda"] = (q, desc) => desc
                ? q.OrderByDescending(t => t.Moneda).ThenByDescending(t => t.Fecha)
                : q.OrderBy(t => t.Moneda).ThenBy(t => t.Fecha),
            ["valor"] = (q, desc) => desc ? q.OrderByDescending(t => t.Valor) : q.OrderBy(t => t.Valor),
            ["createdAt"] = (q, desc) => desc
                ? q.OrderByDescending(t => t.CreatedAt)
                : q.OrderBy(t => t.CreatedAt),
        };

    /// <summary>Hoy en Santo Domingo: el día al que se aplica el valor que se acaba de pedir.</summary>
    public DateOnly FechaDeHoy() => TiempoNegocio.FechaEnZonaNegocio(reloj.GetUtcNow());

    /// <summary>Valor de esa moneda ese día, o <c>null</c> si no hay. USD devuelve 1 sin consultar.</summary>
    public async Task<decimal?> BuscarAsync(Moneda moneda, DateOnly fecha, CancellationToken ct)
    {
        if (moneda == Moneda.USD)
        {
            return 1m;
        }

        return await contexto.TipoCambio.AsNoTracking()
            .Where(t => t.Moneda == moneda && t.Fecha == fecha)
            .Select(t => (decimal?)t.Valor)
            .SingleOrDefaultAsync(ct);
    }

    /// <summary>
    /// Igual que <see cref="BuscarAsync"/>, pero sin <c>null</c>: el valor o un 409 con el motivo.
    /// <para>
    /// El mensaje dice las dos cosas que hacen falta para arreglarlo —qué fecha y qué moneda— y no
    /// insinúa que se invente uno: la salida es la carga manual, que es lo que la especificación
    /// deja escrito para el fallo de la fuente.
    /// </para>
    /// </summary>
    public async Task<decimal> ObtenerAsync(Moneda moneda, DateOnly fecha, CancellationToken ct)
    {
        if (moneda == Moneda.USD)
        {
            return 1m;
        }

        return await BuscarAsync(moneda, fecha, ct)
            ?? throw new InvalidOperationException(
                $"No hay tipo de cambio de {moneda} para el {fecha:dd/MM/yyyy}. Se carga a mano desde "
                    + "«tipos de cambio» o se espera al trabajo diario.");
    }

    /// <summary>
    /// Histórico paginado. Sin filtro devuelve toda la tabla, que son unos cientos de filas al año:
    /// es el histórico completo que piden los informes consolidados y el que se puede pegar en una
    /// hoja sin recortar.
    /// </summary>
    public Task<Pagina<TipoCambioRespuesta>> ListarAsync(
        ConsultaLista paginacion,
        Moneda? moneda,
        DateOnly? desde,
        DateOnly? hasta,
        CancellationToken ct)
    {
        var consulta = contexto.TipoCambio.AsNoTracking();

        if (moneda is not null)
        {
            consulta = consulta.Where(t => t.Moneda == moneda);
        }

        if (desde is not null)
        {
            consulta = consulta.Where(t => t.Fecha >= desde);
        }

        if (hasta is not null)
        {
            consulta = consulta.Where(t => t.Fecha <= hasta);
        }

        return ConsultaPaginada.PaginarAsync(consulta, Ordenes, "fecha", paginacion, ARespuesta, ct);
    }

    /// <summary>Una fila concreta del histórico.</summary>
    public async Task<TipoCambioRespuesta> LeerAsync(Guid id, CancellationToken ct) =>
        ARespuesta(await contexto.TipoCambio.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new KeyNotFoundException($"El tipo de cambio {id:D} no existe."));

    /// <summary>
    /// Valor de una fecha concreta. Es lo que van a leer la factura (F3-02) y el cobro (F3-08) para
    /// congelar el cambio, así que devuelve también la fila cuando existe: quien congela quiere
    /// guardar de dónde salió el número, no solo el número.
    /// </summary>
    public async Task<TipoCambioActualRespuesta> ActualAsync(
        Moneda moneda,
        DateOnly fecha,
        CancellationToken ct)
    {
        if (moneda == Moneda.USD)
        {
            return new TipoCambioActualRespuesta(fecha, moneda, 1m, null, null);
        }

        var fila = await contexto.TipoCambio.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Moneda == moneda && t.Fecha == fecha, ct);

        return fila is null
            ? throw new InvalidOperationException(
                $"No hay tipo de cambio de {moneda} para el {fecha:dd/MM/yyyy}. Se carga a mano desde "
                    + "«tipos de cambio» o se espera al trabajo diario.")
            : new TipoCambioActualRespuesta(fecha, moneda, fila.Valor, fila.Id, fila.Fuente);
    }

    /// <summary>
    /// Guarda el valor de un día, o actualiza el que ya había.
    /// <para>
    /// Alta y actualización van por el mismo camino porque el índice único (fecha, moneda) es lo que
    /// decide cuál de las dos es, y decidirlo en el servicio antes de mirar la base solo añadiría una
    /// forma de equivocarse.
    /// </para>
    /// </summary>
    public async Task<TipoCambioRespuesta> RegistrarAsync(
        DateOnly fecha,
        Moneda moneda,
        decimal valor,
        string fuente,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        if (moneda == Moneda.USD)
        {
            throw new InvalidOperationException(
                "El tipo de cambio del dólar es 1 por definición (docs/ESPECIFICACION.md §3.12) y no "
                    + "se guarda: se resuelve sin consultar.");
        }

        if (valor <= 0m)
        {
            throw new InvalidOperationException(
                $"El tipo de cambio tiene que ser mayor que cero; se ha enviado {valor}.");
        }

        var existente = await contexto.TipoCambio
            .SingleOrDefaultAsync(t => t.Moneda == moneda && t.Fecha == fecha, ct);

        var fila = existente ?? new TipoCambio
        {
            Id = Guid.NewGuid(),
            Fecha = fecha,
            Moneda = moneda,
        };
        fila.Valor = Redondeo.TipoCambio(valor);
        fila.Fuente = fuente;

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.TipoCambio, fila,
            esAlta: existente is null,
            accion: existente is null ? AccionRegistrada : AccionActualizada,
            entidad: Entidad,
            usuario,
            ip,
            mutacion,
            proyectar: ARespuesta,
            datosAuditoria: DatosAuditoria,
            ct: ct);
    }

    /// <summary>
    /// Una pasada del trabajo diario: pide a la fuente el valor de hoy de cada moneda que la
    /// aplicación maneja menos la base y lo guarda.
    /// <para>
    /// <b>No lanza si la fuente falla</b>, que es el criterio de aceptación de la tarea: la fuente
    /// es una dependencia externa y no puede ser la razón de que caiga el trabajo. El fallo queda en
    /// <c>alerta</c> con clave <c>fallo:{fecha}</c> y la carga manual es el camino de recuperación
    /// (docs/ESPECIFICACION.md §3.12). Se devuelve en cuanto falla, sin probar el resto de monedas:
    /// si la fuente no responde, no va a responder para la siguiente.
    /// </para>
    /// </summary>
    public async Task<ResumenTipoCambio> ActualizarDesdeLaFuenteAsync(CancellationToken ct)
    {
        var hoy = FechaDeHoy();
        var cargados = 0;
        var respetados = 0;

        foreach (var moneda in Enum.GetValues<Moneda>().Where(m => m != Moneda.USD))
        {
            decimal valor;
            try
            {
                valor = await fuente.ObtenerAsync(moneda, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                await alertas.RegistrarAsync(
                    TipoAlerta,
                    ClaveFallo(hoy),
                    $"No se pudo consultar el tipo de cambio: {MotivoDe(e)}",
                    ct);

                Log.Warning("Fallo consultando el tipo de cambio del {Fecha}: {Motivo}", hoy, MotivoDe(e));

                return new ResumenTipoCambio(cargados, respetados, Fallo: true);
            }

            if (await GuardarDesdeLaFuenteAsync(hoy, moneda, valor, ct))
            {
                cargados++;
            }
            else
            {
                respetados++;
            }
        }

        // El cambio llegó: la alerta de este día, si la hubo, ya dice algo que no es cierto. Se
        // cierra (docs/DOC_TECNICA.md §8.0 y la nota de F2-10 sobre alertas que sobreviven al
        // problema que las creó).
        await alertas.ResolverAsync(ClaveFallo(hoy), ct);

        return new ResumenTipoCambio(cargados, respetados, Fallo: false);
    }

    /// <summary>
    /// Guarda el valor que trae la fuente, o se hace a un lado si la fila es manual.
    /// <para>
    /// La transacción y la auditoría van aquí y no en <see cref="GuardadoAuditado"/> porque esta
    /// escritura no viene de la API: no hay <c>Idempotency-Key</c> que guardar y la auditoría
    /// lleva <c>usuario_id</c> nulo y <c>origen = "job"</c>, como los demás trabajos
    /// (docs/DOC_TECNICA.md §8.0).
    /// </para>
    /// </summary>
    private async Task<bool> GuardarDesdeLaFuenteAsync(
        DateOnly fecha,
        Moneda moneda,
        decimal valor,
        CancellationToken ct)
    {
        var existente = await contexto.TipoCambio
            .SingleOrDefaultAsync(t => t.Moneda == moneda && t.Fecha == fecha, ct);

        if (existente is not null && existente.Fuente == FuentesTipoCambio.Manual)
        {
            Log.Information(
                "El tipo de cambio manual de {Moneda} del {Fecha} se respeta: la fuente no lo pisa",
                moneda,
                fecha);

            return false;
        }

        var ahora = reloj.GetUtcNow();
        var alta = existente is null;
        var fila = existente ?? new TipoCambio
        {
            Id = Guid.NewGuid(),
            Fecha = fecha,
            Moneda = moneda,
            CreatedAt = ahora,
            CreatedBy = null,
        };
        fila.Valor = Redondeo.TipoCambio(valor);
        fila.Fuente = FuentesTipoCambio.Api;
        fila.UpdatedAt = ahora;
        fila.UpdatedBy = null;

        if (alta)
        {
            await contexto.TipoCambio.AddAsync(fila, ct);
        }

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        await contexto.SaveChangesAsync(ct);

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = alta ? AccionRegistrada : AccionActualizada,
            Ip = IpDelTrabajo,
            UsuarioId = null,
            Entidad = Entidad,
            EntidadId = fila.Id.ToString("D"),
            DatosJson = JsonSerializer.Serialize(new
            {
                Fecha = fila.Fecha,
                Moneda = fila.Moneda.ToString(),
                Valor = fila.Valor,
                Fuente = fila.Fuente,
            }),
            Resultado = "permitido",
            Origen = OrigenDelTrabajo,
        }, ct);

        await transaccion.CommitAsync(ct);

        return true;
    }

    /// <summary>
    /// El motivo que va a la alerta. Solo el de <see cref="FalloTipoCambioException"/>, que está
    /// escrito para que alguien lo lea; de cualquier otra excepción solo el tipo, porque su mensaje
    /// puede traer la URL con la clave o una cadena de conexión y la alerta se lee fuera
    /// (mismo criterio que el fallo de la copia en F1-12).
    /// </summary>
    private static string MotivoDe(Exception e) => e switch
    {
        FalloTipoCambioException fallo => fallo.Message,
        _ => $"{e.GetType().Name} sin detalle.",
    };

    private static TipoCambioRespuesta ARespuesta(TipoCambio fila) => new(
        fila.Id,
        fila.Fecha,
        fila.Moneda,
        fila.Valor,
        fila.Fuente,
        fila.CreatedAt,
        fila.CreatedBy,
        fila.UpdatedAt);

    /// <summary>
    /// Lo que va al campo <c>datos</c> de la auditoría. Sin el valor entero de otras fuentes ni
    /// nada más: el valor que se guardó ya está en la fila, y la auditoría guarda el hecho.
    /// </summary>
    private static object DatosAuditoria(TipoCambio fila) => new
    {
        Fecha = fila.Fecha,
        Moneda = fila.Moneda.ToString(),
        Valor = fila.Valor,
        Fuente = fila.Fuente,
    };
}
