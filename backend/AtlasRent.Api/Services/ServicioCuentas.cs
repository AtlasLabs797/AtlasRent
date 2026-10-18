using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace AtlasRent.Api.Services;

/// <summary>
/// Vista única de cuentas y cobros (F3-10, docs/ESPECIFICACION.md §3.13): la pantalla principal del
/// día a día, que sustituye a Factusol.
/// <para>
/// <b>Una sola lista con facturas, proformas y fianzas</b> y una insignia de tipo en cada fila, en
/// vez de tres listados y tres totales: quien cobra necesita ver la deuda y la fianza del mismo
/// contrato en la misma tabla, y partirlo obligaría a mirar dos pantallas para responder «¿cuánto me
/// debe este cliente?».
/// </para>
/// <para>
/// <b>Las columnas de dinero se calculan con las funciones de dominio que ya existen</b>
/// (<see cref="CalculoCobro"/>, <see cref="CalculoFianza"/>), nunca con una fórmula propia: la vista
/// es la cuarta tarea que lee esos números, y el día que cada una tenga su propia copia dirán cosas
/// distintas sobre la misma factura.
/// </para>
/// <para>
/// <b>Un filtro que no se puede aplicar deja fuera el tipo de fila que no puede expresar.</b> Pedir
/// <c>serie</c> no devuelve fianzas: una fianza no tiene serie, y devolverla «por si acaso» sería una
/// fila que el filtro no tocó. Lo mismo con <c>vencidas</c>, <c>modificadas</c> y los estados.
/// </para>
/// <para>
/// <b>Los totales son de <see cref="Acciones.InformeVer"/></b> y vienen en <c>null</c> sin ese
/// permiso. No es un campo vacío con ceros: cero es un total, y un total que el empleado no puede ver
/// tiene que ser un nulo, porque con ceros se puede sumar, imprimir y archivar.
/// </para>
/// </summary>
public sealed class ServicioCuentas(
    AtlasRentDbContext contexto,
    ServicioCobros cobros,
    ServicioFianzas fianzas,
    ServicioCifrado cifrado,
    IPolicyService politica,
    TimeProvider reloj)
{
    /// <summary>Prefijo del concepto con el que F3-05 marca la factura de una reserva corta (§3.4).</summary>
    public const string PrefijoConceptoReserva = "Reserva corta ";

    private DateOnly Hoy() => TiempoNegocio.FechaEnZonaNegocio(reloj.GetUtcNow());

    /// <summary>
    /// Las filas de la vista con sus filtros y sus totales.
    /// <para>
    /// La paginación es <b>obligatoria y estable</b>: facturas y fianzas se ordenan por la misma clave
    /// —fecha y, a igualdad de fecha, identificador— y la página se arma mezclando las dos listas. Cada
    /// tipo se trae hasta el final de la página pedida y de la mezcla se corta la ventana, porque traer
    /// solo una página de cada uno perdería filas: la última factura de la página 2 caería junto a una
    /// fianza que la desplaza y no saldría nunca. La clave acaba en el identificador y no hay dos
    /// documentos con el mismo entre dos tablas distintas, así que la ventana no puede repetir ninguna.
    /// </para>
    /// </summary>
    public async Task<CuentasRespuesta> ListarAsync(
        ConsultaLista paginacion,
        int? anio,
        Guid? serieId,
        Moneda? moneda,
        EstadoFactura? estado,
        EstadoCobroFactura? estadoCobro,
        Guid? clienteId,
        Guid? unidadId,
        bool vencidas,
        bool modificadas,
        bool noEnviadas,
        bool envioFallido,
        bool modificadasTrasEnvio,
        bool incluirProformas,
        bool incluirFianzas,
        CancellationToken ct)
    {
        ExigirOrdenConocido(paginacion.SortBy);

        var hoy = Hoy();

        var consultaFacturas = FacturasFiltradas(
            anio, serieId, moneda, estado, estadoCobro, clienteId, unidadId, vencidas, modificadas,
            noEnviadas, envioFallido, modificadasTrasEnvio, incluirProformas);

        // Las fianzas solo están cuando ningún filtro es de factura. No es una decisión de comodidad:
        // una fianza no tiene serie ni estado de factura ni vencimiento ni documento que mandar, así
        // que un filtro de esos no puede decir nada de ella y devolverla sería devolver una fila que
        // el filtro no tocó. Los tres de envío entran aquí por lo mismo (DOC_TECNICA §6.7).
        var consultaFianzas = incluirFianzas && SinFiltroDeFactura(
                serieId, estado, estadoCobro, vencidas, modificadas, noEnviadas, envioFallido, modificadasTrasEnvio)
            ? FianzasFiltradas(anio, moneda, clienteId, unidadId)
            : null;

        var totalFacturas = await consultaFacturas.CountAsync(ct);
        var totalFianzas = consultaFianzas is null ? 0 : await consultaFianzas.CountAsync(ct);
        var total = totalFacturas + totalFianzas;

        var saltadas = (paginacion.Page - 1) * paginacion.PageSize;

        // Cada tipo se trae hasta el final de la página pedida —y no solo `pageSize`— porque la mezcla
        // puede empujar filas del otro tipo fuera de la ventana. Traer solo `pageSize` de cada uno y
        // quedarse con los primeros `pageSize` de la unión pierde filas: la tercera de facturas puede
        // caer en la página 2 junto a una fianza que la desplaza, y entonces nunca sale.
        var hasta = paginacion.Page * paginacion.PageSize;

        var paginaFacturas = await (paginacion.EsDescendente
            ? consultaFacturas.OrderByDescending(f => f.FechaEmision).ThenByDescending(f => f.Id)
            : consultaFacturas.OrderBy(f => f.FechaEmision).ThenBy(f => f.Id))
            .Take(hasta)
            .ToListAsync(ct);

        var filas = await FilasDeFacturasAsync(paginaFacturas, hoy, ct);

        if (consultaFianzas is not null)
        {
            var paginaFianzas = await (paginacion.EsDescendente
                ? consultaFianzas.OrderByDescending(f => f.CreatedAt).ThenByDescending(f => f.Id)
                : consultaFianzas.OrderBy(f => f.CreatedAt).ThenBy(f => f.Id))
                .Take(hasta)
                .ToListAsync(ct);

            filas.AddRange(await FilasDeFianzasAsync(paginaFianzas, ct));
        }

        // La página es la ventana [saltadas, saltadas + pageSize) de la mezcla. Cada tipo viene ya
        // ordenado por la misma clave y ninguno pasa de `hasta`, así que la ventana se puede cortar
        // sin que falte ninguna fila que le tocara.
        IReadOnlyList<CuentaFila> data = paginacion.EsDescendente
            ? [.. filas.OrderByDescending(OrdenDeFila).Skip(saltadas).Take(paginacion.PageSize)]
            : [.. filas.OrderBy(OrdenDeFila).Skip(saltadas).Take(paginacion.PageSize)];

        return new CuentasRespuesta(
            data,
            total,
            paginacion.Page,
            paginacion.PageSize,
            (int)Math.Ceiling(total / (double)paginacion.PageSize),
            await TotalesAsync(consultaFacturas, consultaFianzas, ct),
            new FiltroCuentas(
                anio, serieId, moneda, estado, estadoCobro, clienteId, unidadId, vencidas, modificadas,
                noEnviadas, envioFallido, modificadasTrasEnvio, incluirProformas, incluirFianzas));
    }

    // ---------------------------------------------------------------- estado de cuenta (F4-01)

    /// <summary>
    /// Estado de cuenta del cliente con el diseño de <c>docs/DESIGN.md §7</c> (§3.14): las filas de la
    /// vista única y los totales por moneda.
    /// <para>
    /// <b>Las filas salen de <see cref="ListarAsync"/>, la vista única de F3-10, y no de una consulta
    /// propia.</b> §3.15 exige que los informes cuadren con la vista, y un segundo cálculo de pendiente
    /// es exactamente cómo se rompe eso: dos fórmulas que un día dicen cosas distintas.
    /// </para>
    /// <para>
    /// <b>El permiso se comprueba aquí y no solo en el endpoint</b>, porque es el método que usará F4-05
    /// desde un trabajo de Hangfire, sin sesión HTTP ni filtro <c>[Accion]</c> (como en F3-06). El
    /// permiso es <c>informe.ver</c> y no <c>factura.ver</c>: el documento lleva totales agregados de
    /// dinero de un cliente, y AGENTS.md §6 dice que el empleado no los recibe por ningún endpoint.
    /// </para>
    /// <para>
    /// <b>El <c>totales</c> de la respuesta no se usa.</b> Sale de <c>IPolicyService.Puede</c>, que en un
    /// trabajo sin sesión no tiene permisos cargados y devolvería nulo. Los totales del papel se suman
    /// sobre las filas, que son las de la vista: la misma cuenta que hace una persona al pie de la tabla,
    /// y la que la prueba de cuadre compara con el <c>totales</c> de la API.
    /// </para>
    /// </summary>
    /// <param name="usuario">
    /// Usuario al que se le comprueba <c>informe.ver</c>. Es el que inicia el trabajo, no el de la
    /// sesión HTTP, que en un trabajo no existe.
    /// </param>
    public async Task<byte[]> PdfAsync(
        Guid clienteId,
        int? anio,
        Guid usuario,
        CancellationToken ct) =>
        DocumentoEstadoCuentaPdf.Generar(
            await ModeloEstadoCuentaPdfAsync(clienteId, anio, usuario, ct));

    /// <summary>
    /// Lo que el estado de cuenta va a decir, separado del render (F4-01), por la misma razón que
    /// <see cref="ServicioFacturas.ModeloPdfAsync"/>: el texto de un PDF de QuestPDF no se puede leer del
    /// archivo, así que las pruebas afirman sobre el modelo y del archivo solo comprueban lo estructural.
    /// </summary>
    internal async Task<DocumentoEstadoCuentaPdfModelo> ModeloEstadoCuentaPdfAsync(
        Guid clienteId,
        int? anio,
        Guid usuario,
        CancellationToken ct)
    {
        await ExigirAsync(Acciones.InformeVer, $"cliente/{clienteId:D}", usuario, ct);

        // Sin proformas —no generan deuda (§3.8)— y con fianzas, que en esta vista son filas de dinero
        // (§3.13). El año es el que ya tiene la vista única.
        var vista = await ListarAsync(
            new ConsultaLista(1, TopeFilasEstadoCuenta, SortBy: null, ConsultaLista.OrdenAscendente),
            anio,
            serieId: null,
            moneda: null,
            estado: null,
            estadoCobro: null,
            clienteId,
            unidadId: null,
            vencidas: false,
            modificadas: false,
            noEnviadas: false,
            envioFallido: false,
            modificadasTrasEnvio: false,
            incluirProformas: false,
            incluirFianzas: true,
            ct);

        var cliente = await contexto.Cliente.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == clienteId, ct)
            ?? throw new KeyNotFoundException($"El cliente '{clienteId:D}' no existe.");

        return DocumentoEstadoCuentaPdf.Modelo(
            cliente,
            // El documento va descifrado solo para el papel (D16); en la fila sigue cifrado.
            cliente.Documento is { } documentoCifrado ? cifrado.Descifrar(documentoCifrado) : null,
            anio,
            vista.Data,
            vista.Total,
            // El estado de cuenta no pertenece a ninguna serie, así que usa la de por defecto.
            RegistroPlantillasPdf.Resolver(nombre: null));
    }

    /// <summary>
    /// Tope de filas del detalle de un estado de cuenta.
    /// <para>
    /// <b>El tope recorta filas y lo dice en el papel</b> (<c>DocumentoEstadoCuentaPdf</c> avisa de cuántas
    /// se muestran de cuántas hay), y los totales se calculan sobre las mostradas. Un cliente con cientos
    /// de documentos en un año es raro pero legal, y un papel que recorta en silencio es peor que uno que
    /// no se genera. <c>TODO(A#)</c>: si el responsable lo pide, la salida es un anexo con el resto del año
    /// o partir por periodo.
    /// </para>
    /// </summary>
    public const int TopeFilasEstadoCuenta = 500;

    /// <summary>
    /// Segunda puerta del permiso: el endpoint lo pide con <c>[Accion]</c> y el atributo protege la
    /// entrada por HTTP y nada más. Un trabajo entra por el servicio, y el intento denegado se audita
    /// antes de lanzar, para que quede constancia de quién lo intentó y no pudo.
    /// </summary>
    private async Task ExigirAsync(string accion, string recurso, Guid usuario, CancellationToken ct)
    {
        if (await politica.PuedeAsync(accion, usuario, ct))
        {
            return;
        }

        await politica.AuditarDenegadoAsync(accion, recurso, ct);

        throw new PermisoDenegadoException(accion, recurso);
    }

    /// <summary>
    /// Orden estable de la mezcla: fecha y, a igualdad de fecha, identificador. El desempate por
    /// identificador es lo que hace estable la paginación; sin él, dos filas con la misma fecha pueden
    /// cambiar de posición entre dos peticiones y una aparecer dos veces y otra ninguna.
    /// </summary>
    private static readonly Func<CuentaFila, (DateOnly Fecha, Guid Id)> OrdenDeFila =
        fila => (fila.Fecha ?? DateOnly.MinValue, fila.Id);

    // ---------------------------------------------------------------- filtros

    /// <summary>
    /// Las facturas y proformas que entran en la vista con los filtros que se pueden aplicar en la
    /// base, incluidos los dos que son derivados —estado de cobro y vencidas— resueltos con la misma
    /// cuenta que usa <see cref="CalculoCobro"/>.
    /// </summary>
    private IQueryable<Factura> FacturasFiltradas(
        int? anio,
        Guid? serieId,
        Moneda? moneda,
        EstadoFactura? estado,
        EstadoCobroFactura? estadoCobro,
        Guid? clienteId,
        Guid? unidadId,
        bool vencidas,
        bool modificadas,
        bool noEnviadas,
        bool envioFallido,
        bool modificadasTrasEnvio,
        bool incluirProformas)
    {
        IQueryable<Factura> consulta = contexto.Factura.AsNoTracking()
            .Include(f => f.Cliente)
            .Include(f => f.Unidad)
            .Include(f => f.Serie);

        // Un borrador no sale: no tiene número, ni fecha de emisión, ni vencimiento, y no es una deuda.
        // La pantalla de borradores es otra (§3.8) y esta es la de las cuentas. Tampoco sale una factura
        // sin fecha de emisión, porque una tabla ordenada por fecha con filas sin fecha no se puede
        // ordenar de dos maneras a la vez.
        consulta = consulta.Where(f => f.Estado != EstadoFactura.Borrador && f.FechaEmision != null);

        // Las proformas van con filtro (§3.13) y por defecto no entran. Una proforma convertida deja de
        // contar por sí misma desde F3-08: la factura en la que se convirtió es la que está en la tabla.
        consulta = incluirProformas
            ? consulta.Where(f => f.Serie!.Tipo != TipoSerie.Proforma || f.Estado != EstadoFactura.Convertida)
            : consulta.Where(f => f.Serie!.Tipo != TipoSerie.Proforma);

        if (serieId is { } serie)
        {
            consulta = consulta.Where(f => f.SerieId == serie);
        }

        if (moneda is { } filtroMoneda)
        {
            consulta = consulta.Where(f => f.Moneda == filtroMoneda);
        }

        if (estado is { } filtroEstado)
        {
            consulta = consulta.Where(f => f.Estado == filtroEstado);
        }

        if (clienteId is { } cliente)
        {
            consulta = consulta.Where(f => f.ClienteId == cliente);
        }

        if (unidadId is { } unidad)
        {
            consulta = consulta.Where(f => f.UnidadId == unidad);
        }

        if (anio is { } anyo)
        {
            // El año es el de la fecha de emisión, que es lo que se ve en la columna «fecha».
            consulta = consulta.Where(f => f.FechaEmision!.Value.Year == anyo);
        }

        if (modificadas)
        {
            consulta = consulta.Where(f => f.ModificadaAt != null);
        }

        consulta = ConFiltrosDeEnvio(consulta, noEnviadas, envioFallido, modificadasTrasEnvio);

        return ConFiltrosDerivados(consulta, estadoCobro, vencidas);
    }

    /// <summary>
    /// Los tres filtros de envío de §3.13, que F4-02 dejó de no aceptar porque ya hay tabla
    /// <c>envio</c>.
    /// <para>
    /// Los tres leen <c>envio</c> con el tipo <see cref="TipoDocumentoEnvio.Factura"/>, que es como se
    /// registra el envío de una factura y de una proforma (las dos viven en la misma tabla).
    /// <para>
    /// <b>«No enviada» y «envío fallido» no se solapan</b>, y es lo que las hace dos preguntas
    /// distintas: <c>noEnviadas</c> es «nadie lo intentó» (no hay ninguna fila de envío) y
    /// <c>envioFallido</c> es «se intentó y no salió». Si «no enviada» fuera «no llegó a salir», sería
    /// las dos listas la misma con dos nombres y la segunda no diría nada que la primera no dijera.
    /// </para>
    /// <para>
    /// <c>modificadasTrasEnvio</c> compara el <c>modificada_at</c> de ahora con el que tenía la
    /// factura cuando salió (F3-08 y §3.14). El <c>NULL</c> del envío significa «cuando salió, aún no
    /// se había tocado»: eso también es «tocada después», y por eso se compara el envío con o sin
    /// fecha. Comparar solo con los que tienen fecha dejaría fuera justamente la factura que nunca se
    /// había tocado, que es el caso normal.
    /// </para>
    /// </summary>
    private IQueryable<Factura> ConFiltrosDeEnvio(
        IQueryable<Factura> consulta,
        bool noEnviadas,
        bool envioFallido,
        bool modificadasTrasEnvio)
    {
        IQueryable<Envio> envio = contexto.Envio.Where(e => e.TipoDocumento == TipoDocumentoEnvio.Factura);

        var salidos = envio.Where(e => e.Estado != EstadoEnvio.Fallido);

        if (noEnviadas)
        {
            consulta = consulta.Where(f => !envio.Any(e => e.DocumentoId == f.Id));
        }

        if (envioFallido)
        {
            consulta = consulta.Where(f =>
                envio.Any(e => e.DocumentoId == f.Id && e.Estado == EstadoEnvio.Fallido)
                && !salidos.Any(e => e.DocumentoId == f.Id));
        }

        if (modificadasTrasEnvio)
        {
            // Un envío que no salió no dice nada de si el documento cambió después.
            consulta = consulta.Where(f => f.ModificadaAt != null
                && salidos.Any(e => e.DocumentoId == f.Id
                    && (e.DocumentoModificadoAt == null || f.ModificadaAt > e.DocumentoModificadoAt)));
        }

        return consulta;
    }

    /// <summary>
    /// Estado de cobro y vencidas. Se filtran en la base y no sobre la página ya traída: filtrar
    /// después de paginar daría páginas vacías y un pie de totales que no cuadra con lo que hay en
    /// pantalla.
    /// <para>
    /// <b>El pendiente entra como expresión y no como método</b>, y no por gusto: EF Core traduce el
    /// cuerpo de la lambda pero no una llamada a un método del servicio —«could not be translated»—, de
    /// modo que la cuenta se escribe una vez en <see cref="ExpresionDePendiente"/> y de ahí sale cada
    /// comparación. Es la misma cuenta que <see cref="CalculoCobro"/>, escrita en el árbol de
    /// expresiones para que la pueda evaluar la base.
    /// </para>
    /// </summary>
    private IQueryable<Factura> ConFiltrosDerivados(
        IQueryable<Factura> consulta,
        EstadoCobroFactura? estadoCobro,
        bool vencidas)
    {
        if (estadoCobro is { } estado)
        {
            var pendiente = ExpresionDePendiente();

            // El neto a cobrar del filtro —total menos retención— es contra lo que se compara el
            // pendiente, porque pendiente = neto − cubierto: «cubierto es cero» es «pendiente es igual
            // al neto» y «cubierto es algo» es «pendiente es menor». Con una sola cuenta y ninguna
            // más, que es lo que evita que dos filtros se desincronicen entre sí.
            switch (estado)
            {
                case EstadoCobroFactura.Anulada:
                    consulta = consulta.Where(f => f.Estado == EstadoFactura.Anulada);
                    break;

                // Una factura anulada tiene pendiente cero por decisión de dominio (CalculoCobro), así
                // que sin esta condición saldría además como Pagada.
                case EstadoCobroFactura.Pagada:
                    consulta = consulta.Where(Comparar(
                        pendiente,
                        (p, f) => Expression.AndAlso(
                            Expression.LessThanOrEqual(p, Expression.Constant(0m)),
                            NoAnulada(f))));
                    break;

                case EstadoCobroFactura.Parcial:
                    consulta = consulta.Where(Comparar(
                        pendiente,
                        (p, f) => Expression.AndAlso(
                            Expression.AndAlso(
                                Expression.GreaterThan(p, Expression.Constant(0m)),
                                Expression.LessThan(p, NetoDe(f))),
                            NoAnulada(f))));
                    break;

                default:
                    consulta = consulta.Where(Comparar(
                        pendiente,
                        (p, f) => Expression.AndAlso(
                            Expression.AndAlso(
                                Expression.GreaterThan(p, Expression.Constant(0m)),
                                Expression.Equal(p, NetoDe(f))),
                            NoAnulada(f))));
                    break;
            }
        }

        if (vencidas)
        {
            // Vencimiento el mismo día no está vencido (§3.8): el día se acaba al final.
            var hoy = Hoy();
            consulta = consulta.Where(Comparar(
                ExpresionDePendiente(),
                (p, f) => Expression.AndAlso(
                    Expression.AndAlso(
                        Expression.GreaterThan(p, Expression.Constant(0m)),
                        Expression.LessThan(
                            Expression.Property(f, nameof(Factura.FechaVencimiento)),
                            Expression.Constant(hoy))),
                    NoAnulada(f))));
        }

        return consulta;
    }

    /// <summary>Lo que la factura tiene que cobrar: su total menos lo retenido (§3.8).</summary>
    private static Expression NetoDe(Expression f) =>
        Expression.Subtract(
            Expression.Property(f, nameof(Factura.Total)),
            Expression.Property(f, nameof(Factura.Retencion)));

    /// <summary>El documento no está anulado, que es lo que lo saca del filtro de cobros.</summary>
    private static Expression NoAnulada(Expression f) =>
        Expression.NotEqual(
            Expression.Property(f, nameof(Factura.Estado)),
            Expression.Constant(EstadoFactura.Anulada));

    /// <summary>
    /// El pendiente de una factura —neto a cobrar menos lo cobrado menos lo ajustado— como expresión
    /// que la base puede evaluar, con las dos sumas como subconsultas correlacionadas. Es la misma
    /// cuenta que <see cref="CalculoCobro"/>, escrita dentro de la lambda porque EF Core traduce el
    /// cuerpo de la lambda y no una llamada a un método del servicio.
    /// <para>
    /// Los dos conjuntos se leen del contexto del propio servicio y EF los traduce como subconsultas
    /// correlacionadas: por eso filtrar por estado de cobro o por vencida no obliga a traer antes
    /// todas las facturas. El <c>?? 0</c> no se puede quitar: en SQL la suma de una tabla vacía es
    /// nula y una factura sin cobros no tiene pendiente <c>null</c>.
    /// </para>
    /// </summary>
    private Expression<Func<Factura, decimal>> ExpresionDePendiente() => PendienteDeFactura.Expresion(contexto);

    /// <summary>
    /// Reemplaza el parámetro de una expresión de importe por el de la consulta, para poder compararla
    /// con lo que haga falta —cero, el neto, el vencimiento— sin volver a escribir la cuenta de los
    /// cobros en cada filtro.
    /// </summary>
    private static Expression<Func<Factura, bool>> Comparar(
        Expression<Func<Factura, decimal>> importe,
        Func<Expression, ParameterExpression, Expression> comparacion)
    {
        var destino = Expression.Parameter(typeof(Factura), "f");

        var cuerpo = new SustituirParametro(importe.Parameters[0], destino).Visit(importe.Body)!;

        return Expression.Lambda<Func<Factura, bool>>(comparacion(cuerpo, destino), destino);
    }

    /// <summary>Sustituye una referencia a un parámetro de expresión por su equivalente en otra.</summary>
    private sealed class SustituirParametro(ParameterExpression origen, Expression destino) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression nodo) =>
            nodo == origen ? destino : base.VisitParameter(nodo);
    }

    /// <summary>
    /// Si ningún filtro es de factura y por tanto las fianzas pueden salir.
    /// <para>
    /// Los tres de envío también son filtros de factura (F4-02): una fianza no tiene documento que
    /// mandar, así que no puede decir «no enviada» ni «envío fallido» ni «modificada tras envío», y
    /// devolverla «por si acaso» sería una fila que el filtro no tocó —el mismo criterio que ya
    /// aplicaban <c>serie</c>, <c>estado</c>, <c>vencidas</c> y <c>modificadas</c>.
    /// </para>
    /// </summary>
    private static bool SinFiltroDeFactura(
        Guid? serieId,
        EstadoFactura? estado,
        EstadoCobroFactura? estadoCobro,
        bool vencidas,
        bool modificadas,
        bool noEnviadas,
        bool envioFallido,
        bool modificadasTrasEnvio) =>
        serieId is null && estado is null && estadoCobro is null && !vencidas && !modificadas
        && !noEnviadas && !envioFallido && !modificadasTrasEnvio;

    private IQueryable<Fianza> FianzasFiltradas(
        int? anio,
        Moneda? moneda,
        Guid? clienteId,
        Guid? unidadId)
    {
        IQueryable<Fianza> consulta = contexto.Fianza.AsNoTracking()
            .Include(f => f.Contrato)
            .ThenInclude(c => c!.Cliente)
            .Include(f => f.Contrato)
            .ThenInclude(c => c!.Unidad);

        if (moneda is { } filtroMoneda)
        {
            consulta = consulta.Where(f => f.Moneda == filtroMoneda);
        }

        // El cliente y la unidad de una fianza son los de su contrato: es la única relación que tiene.
        if (clienteId is { } cliente)
        {
            consulta = consulta.Where(f => f.Contrato.ClienteId == cliente);
        }

        if (unidadId is { } unidad)
        {
            consulta = consulta.Where(f => f.Contrato.UnidadId == unidad);
        }

        if (anio is { } anyo)
        {
            // Una fianza no tiene fecha de emisión: la fecha de su fila es la del alta.
            consulta = consulta.Where(f => f.CreatedAt.Year == anyo);
        }

        return consulta;
    }

    // ---------------------------------------------------------------- cifras de factura

    private async Task<List<CuentaFila>> FilasDeFacturasAsync(
        List<Factura> pagina,
        DateOnly hoy,
        CancellationToken ct)
    {
        var filas = new List<CuentaFila>(pagina.Count);

        if (pagina.Count == 0)
        {
            return filas;
        }

        var ids = pagina.Select(f => f.Id).ToArray();

        // Cobrado y ajustado van en sus propias consultas agregadas para toda la página, como en el
        // listado de facturas de F3-06: son datos de otra tabla y una vista de 25 filas no puede costar
        // 26 viajes.
        var cobrados = await cobros.CobradoPorFacturaAsync(ids, ct);
        var ajustados = await cobros.AjustadoPorFacturaAsync(ids, ct);
        var conReserva = await FacturasConReservaConAnticipoAsync(pagina, ct);

        // F4-02: el estado del último envío de cada documento de la página (§3.13). Una consulta para
        // toda la página, por el mismo motivo que el cobrado: es otra tabla y una fila de la vista no
        // puede ser una consulta.
        var estadosEnvio = await UltimoEnvioPorFacturaAsync(ids, ct);

        // Y qué facturas se tocaron después de que saliera el correo (F4-02, §3.13). Va en su propia
        // consulta y con la misma condición que el filtro `modificadasTrasEnvio`, para que la marca de
        // la fila y el filtro no puedan discrepar: si la marca dijera «no» y el filtro la sacara, uno
        // de los dos estaría mintiendo y no habría forma de saber cuál.
        var modificadasTrasEnvio = await ModificadasTrasEnvioAsync(ids, ct);

        // El comprobante fiscal (F3-03, §3.7) va en su propia consulta para toda la página, por el mismo
        // motivo que el cobrado: es otra tabla y una fila de la vista no puede ser una consulta.
        var comprobantes = await contexto.ComprobanteFiscal.AsNoTracking()
            .Where(c => ids.Contains(c.FacturaId))
            .ToDictionaryAsync(c => c.FacturaId, ct);

        foreach (var factura in pagina)
        {
            var proforma = factura.Serie?.Tipo == TipoSerie.Proforma;

            // Solo la serie fiscal genera comprobante (F3-03). Una proforma nunca lo tiene, y decirlo con
            // un nulo en vez de con un «0» o un vacío es lo que evita que se lea como que sí.
            var tieneComprobante = comprobantes.TryGetValue(factura.Id, out var comprobante);

            var situacion = CalculoCobro.Calcular(
                factura.Total,
                factura.Retencion,
                cobrados.GetValueOrDefault(factura.Id),
                ajustados.GetValueOrDefault(factura.Id),
                factura.FechaVencimiento,
                hoy,
                factura.Estado);

            filas.Add(new CuentaFila(
                proforma ? TipoFilaCuenta.Proforma : TipoFilaCuenta.Factura,
                factura.Id,
                factura.SerieId,
                factura.Serie?.Numero,
                factura.Numero,
                tieneComprobante ? comprobante!.Tipo : null,
                tieneComprobante ? comprobante!.Numero : null,
                factura.FechaEmision,
                factura.ClienteId,
                factura.Cliente?.Nombre,
                factura.UnidadId,
                factura.Unidad?.Nombre,
                factura.ContratoId,
                factura.Concepto,
                factura.Moneda,
                Redondeo.Importe(factura.Total),

                // Una proforma no genera deuda (§3.8): de su importe se ve el total y lo demás no
                // existe. Es <c>null</c> y no cero, porque un cero se lee como «no ha cobrado nada de una
                // deuda que existe» y una deuda que no existe no vale ni un cero.
                proforma ? null : Redondeo.Importe(factura.Retencion),
                proforma ? null : situacion.Cobrado,
                proforma ? null : situacion.Pendiente,
                null,
                proforma ? null : factura.FechaVencimiento,
                proforma ? null : situacion.DiasVencida,
                proforma ? null : situacion.Vencida,
                proforma ? factura.Estado.ToString() : situacion.Estado.ToString(),
                factura.ModificadaAt is not null,
                conReserva.Contains(factura.Id),

                // F4-02: el estado del último envío del documento, o <c>null</c> si nunca se mandó.
                // No es «Pendiente» para lo que no se ha mandado: no hay nada en la cola, no se ha
                // intentado nada, y «Pendiente» affirmaría que hay un envío esperando.
                estadosEnvio.TryGetValue(factura.Id, out var estadoEnvio)
                    ? estadoEnvio.ToString()
                    : null,
                modificadasTrasEnvio.Contains(factura.Id),
                !proforma && situacion.Pendiente > 0m && factura.Estado != EstadoFactura.Anulada));
        }

        return filas;
    }

    /// <summary>
    /// Estado del <b>último</b> envío de cada una de esas facturas, por <c>EnviadoAt</c> (y a igualdad
    /// por identificador, que es lo que hace estable el orden). Los envíos que nunca salieron tienen
    /// <c>EnviadoAt</c> nulo y salen por su <c>CreatedAt</c>, que es cuando se intentó.
    /// </summary>
    private async Task<Dictionary<Guid, EstadoEnvio>> UltimoEnvioPorFacturaAsync(Guid[] ids, CancellationToken ct)
    {
        var envios = await contexto.Envio.AsNoTracking()
            .Where(e => e.TipoDocumento == TipoDocumentoEnvio.Factura && ids.Contains(e.DocumentoId))
            .OrderByDescending(e => e.EnviadoAt ?? e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Select(e => new { e.DocumentoId, e.Estado })
            .ToListAsync(ct);

        // GroupBy conserva el orden de aparición, así que el primero de cada grupo es el último envío.
        // Se hace en memoria porque la consulta ya viene ordenada y solo trae dos columnas.
        return envios
            .GroupBy(e => e.DocumentoId)
            .ToDictionary(g => g.Key, g => g.First().Estado);
    }

    /// <summary>
    /// Las facturas de la página a las que alguien las tocó después de que saliera el último envío que
    /// sí salió (F4-02, §3.14 y F3-08).
    /// <para>
    /// <b>Un envío <c>Fallido</c> no cuenta.</b> No llegó a salir nada, así que no puede haber un
    /// documento en manos del cliente que después se haya cambiado.
    /// </para>
    /// </summary>
    private async Task<HashSet<Guid>> ModificadasTrasEnvioAsync(Guid[] ids, CancellationToken ct)
    {
        var tocadas = await contexto.Factura.AsNoTracking()
            .Where(f => ids.Contains(f.Id)
                && f.ModificadaAt != null
                && contexto.Envio.Any(e => e.TipoDocumento == TipoDocumentoEnvio.Factura
                    && e.DocumentoId == f.Id
                    && e.Estado != EstadoEnvio.Fallido
                    && (e.DocumentoModificadoAt == null || f.ModificadaAt > e.DocumentoModificadoAt)))
            .Select(f => f.Id)
            .ToListAsync(ct);

        return tocadas.ToHashSet();
    }

    /// <summary>
    /// Los identificadores de las facturas de la página cuya reserva tiene el anticipo anotado
    /// (§3.13: icono informativo, no fila de dinero).
    /// <para>
    /// <b>La relación es por concepto y no por clave ajena</b>, porque así dejó F3-05 la factura de
    /// una reserva corta: el concepto lleva el identificador de la reserva porque no hay columna que
    /// los una, y una factura de reserva corta no tiene contrato con el que distinguirlas.
    /// <c>TODO(A#)</c>: si el icono tiene que ser un dato y no una convención, la salida es una columna
    /// <c>factura.reserva_id</c>. Hasta entonces, una factura tecleada a mano con el mismo concepto y un
    /// identificador que exista en <c>reserva</c> mostraría el icono sin ser su factura.
    /// </para>
    /// </summary>
    private async Task<HashSet<Guid>> FacturasConReservaConAnticipoAsync(
        List<Factura> pagina,
        CancellationToken ct)
    {
        var conConceptoDeReserva = pagina
            .Where(f => f.Concepto.StartsWith(PrefijoConceptoReserva, StringComparison.Ordinal))
            .ToArray();

        if (conConceptoDeReserva.Length == 0)
        {
            return [];
        }

        // Las reservas con anticipo anotado son las pocas que lo tienen anotado, así que se traen
        // enteras y se cruzan con las de la página. Traer solo las de la página con un `IN` ahorraría
        // una lista de identificadores a cambio de un plan distinto para cada página, y esto no es lo
        // bastante seguido como para pagar esa diferencia.
        var reservasConAnticipo = (await contexto.Reserva.AsNoTracking()
            .Where(r => r.AnticipoImporte != null
                || (r.AnticipoNota != null && r.AnticipoNota != string.Empty))
            .Select(r => r.Id)
            .ToListAsync(ct))
            .ToHashSet();

        // Se devuelven los identificadores de las **facturas**, que es lo que la fila lleva: la cuenta
        // va del concepto a la reserva, y quien pregunta es la fila, no la reserva.
        return [.. conConceptoDeReserva
            .Where(f => Guid.TryParseExact(
                    f.Concepto[PrefijoConceptoReserva.Length..], "D", out var reservaId)
                && reservasConAnticipo.Contains(reservaId))
            .Select(f => f.Id)];
    }

    // ---------------------------------------------------------------- cifras de fianza

    private async Task<List<CuentaFila>> FilasDeFianzasAsync(List<Fianza> pagina, CancellationToken ct)
    {
        if (pagina.Count == 0)
        {
            return [];
        }

        var cifras = await fianzas.CifrasAsync([.. pagina.Select(f => f.Id)], ct);

        return
        [
            .. pagina.Select(f =>
            {
                var c = cifras[f.Id];
                var estado = CalculoFianza.Estado(f.Estado, c.Cobrado);
                var pendiente = CalculoFianza.PendienteDeCobro(f.ImporteExigido, c.Cobrado);

                return new CuentaFila(
                    TipoFilaCuenta.Fianza,
                    f.Id,
                    null,
                    null,
                    null,
                    null,
                    null,
                    TiempoNegocio.FechaEnZonaNegocio(f.CreatedAt),
                    f.Contrato.ClienteId,
                    f.Contrato.Cliente?.Nombre,
                    f.Contrato.UnidadId,
                    f.Contrato.Unidad?.Nombre,
                    f.ContratoId,
                    $"Fianza de {f.NumeroFianzas} {(f.NumeroFianzas == 1 ? "fianza" : "fianzas")}",
                    f.Moneda,

                    // El total de una fianza es lo exigido (§3.11): es lo que se pidió, no una venta.
                    Redondeo.Importe(f.ImporteExigido),

                    // Una fianza no retiene: es una garantía que se devuelve, no un servicio facturado.
                    null,
                    c.Cobrado,

                    // «Pendiente» en una fianza es lo que falta por exigir (exigido − cobrado), que es lo
                    // mismo que limita un cobro de fianza en CalculoFianza. El saldo a devolver va en su
                    // propia columna porque son dos preguntas distintas: una se pide y la otra se
                    // devuelve, y una fianza recién dada de alta tiene la primera en verde y la segunda
                    // en cero.
                    pendiente,
                    c.Saldo,
                    null,
                    null,
                    null,
                    estado.ToString(),
                    null,
                    false,
                    null,

                    // Una fianza no es un documento que se mande: no tiene PDF ni destinatario, así que
                    // no hay envío ni «modificada después de enviar» que decir de ella. Es <c>null</c>
                    // y no <c>false</c>, por la misma razón que el resto de columnas de fianza.
                    null,
                    pendiente > 0m && !CalculoFianza.NoCobrable(estado));
            }),
        ];
    }

    // ---------------------------------------------------------------- totales

    /// <summary>
    /// Los totales por moneda, y <c>null</c> si quien pregunta no puede verlos.
    /// <para>
    /// Se calculan sobre <b>todo</b> el conjunto filtrado, no sobre la página: el pie de la tabla dice
    /// «de estos 431 documentos», no «de estos 25». Y con la misma cuenta que las filas, para que lo que
    /// dice el pie sea lo que se ve arriba fila a fila.
    /// </para>
    /// <para>
    /// <b>Por moneda y sin convertir</b> (§3.13 y D9): consolidar en dólares es de F5-01, con el tipo de
    /// cambio congelado de cada documento. Sumar dólares y pesos en una misma celda sería inventar un
    /// número.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<TotalesCuenta>?> TotalesAsync(
        IQueryable<Factura> facturas,
        IQueryable<Fianza>? fianzasFiltradas,
        CancellationToken ct)
    {
        if (!politica.Puede(Acciones.InformeVer))
        {
            return null;
        }

        var porMoneda = new Dictionary<Moneda, TotalesCuenta>();

        // La proyección trae **solo columnas**. Lo cobrado y lo ajustado no pueden ser una llamada a un
        // método del servicio dentro de la proyección —EF la rechaza: arrastraría al servicio entero
        // hacia la consulta—, así que se traen con las dos consultas agregadas que ya usan el listado
        // de facturas (F3-06) y el de fianzas (F3-09), y la cuenta se hace aquí con la misma fórmula que
        // la fila. Lo que sale de esa cuenta es exactamente lo que sale de la fila, que es lo que hace
        // que el pie cuadre con lo que hay arriba.
        var sumas = await facturas
            .Select(f => new
            {
                f.Id,
                f.Moneda,
                f.Total,
                f.Retencion,
                f.Estado,
                EsProforma = f.Serie!.Tipo == TipoSerie.Proforma,
            })
            .ToListAsync(ct);

        var cobrados = await cobros.CobradoPorFacturaAsync([.. sumas.Select(f => f.Id)], ct);
        var ajustados = await cobros.AjustadoPorFacturaAsync([.. sumas.Select(f => f.Id)], ct);

        foreach (var f in sumas)
        {
            // Los nulos de una proforma suman cero, que es lo mismo que no aportar: el pie cuadra con
            // lo que hay en pantalla y una proforma no es dinero.
            var retencion = f.EsProforma ? 0m : Redondeo.Importe(f.Retencion);
            var pendiente = f.EsProforma || f.Estado == EstadoFactura.Anulada
                ? 0m
                : Math.Max(
                    0m,
                    Redondeo.Importe(
                        f.Total - f.Retencion - cobrados.GetValueOrDefault(f.Id) - ajustados.GetValueOrDefault(f.Id)));

            Acumular(
                porMoneda,
                f.Moneda,
                Redondeo.Importe(f.Total),
                retencion,
                cobrados.GetValueOrDefault(f.Id),
                pendiente);
        }

        if (fianzasFiltradas is not null)
        {
            var exigidas = await fianzasFiltradas
                .Select(f => new { f.Id, f.ImporteExigido, f.Moneda })
                .ToListAsync(ct);

            var cifras = await fianzas.CifrasAsync([.. exigidas.Select(f => f.Id)], ct);

            foreach (var f in exigidas)
            {
                var cobrado = cifras[f.Id].Cobrado;

                Acumular(
                    porMoneda,
                    f.Moneda,
                    Redondeo.Importe(f.ImporteExigido),
                    0m,
                    cobrado,
                    CalculoFianza.PendienteDeCobro(f.ImporteExigido, cobrado));
            }
        }

        return [.. porMoneda.Values.OrderBy(t => t.Moneda)];
    }

    private static void Acumular(
        Dictionary<Moneda, TotalesCuenta> porMoneda,
        Moneda moneda,
        decimal total,
        decimal retencion,
        decimal cobrado,
        decimal pendiente)
    {
        if (!porMoneda.TryGetValue(moneda, out var actual))
        {
            porMoneda[moneda] = new TotalesCuenta(moneda, total, retencion, cobrado, pendiente);
            return;
        }

        porMoneda[moneda] = actual with
        {
            Total = Redondeo.Importe(actual.Total + total),
            Retencion = Redondeo.Importe(actual.Retencion + retencion),
            Cobrado = Redondeo.Importe(actual.Cobrado + cobrado),
            Pendiente = Redondeo.Importe(actual.Pendiente + pendiente),
        };
    }

    // ---------------------------------------------------------------- varios

    /// <summary>
    /// Solo se admite ordenar por <c>fecha</c>. El mensaje es el del resto de la API para un campo de
    /// orden desconocido, porque es el mismo problema: una errata que no se ve. Los demás campos que
    /// se podrían añadir —pendiente, días vencida— no son columnas, y ordenar por ellos exigiría traerse
    /// el conjunto entero para ordenarlo en memoria; eso es de F5-01, que ya tiene que consolidar.
    /// </summary>
    private static void ExigirOrdenConocido(string? sortBy)
    {
        if (sortBy is not null && !string.Equals(sortBy, "fecha", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"No se puede ordenar por '{sortBy}'. Campos: fecha.", nameof(sortBy));
        }
    }
}
