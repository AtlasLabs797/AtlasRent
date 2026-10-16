using System.Text.Json;
using AtlasRent.Api.Data;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Dominio;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Alta, cambio, baja lógica y lectura de la configuración (docs/ESPECIFICACION.md §3.7, §3.9 y
/// §3.10). Solo el administrador llega aquí: lo comprueba el filtro global de acciones.
/// <para>
/// Reglas que valen para todo lo de este servicio:
/// </para>
/// <list type="bullet">
/// <item>Toda mutación escribe auditoría con IP en la misma transacción (AGENTS.md §6).</item>
/// <item>Nada se borra: lo que se da de baja pasa a inactivo y se conserva.</item>
/// <item>
/// Los porcentajes de ITBIS y retención son datos (D14). Aquí solo se guardan, redondeados a cuatro
/// decimales; el cálculo vive en <see cref="CalculoImpuestos"/> y no tiene ninguna cifra fiscal.
/// </para>
/// </list>
/// </summary>
public sealed class ServicioConfiguracion(
    AtlasRentDbContext contexto,
    AuditoriaService auditoria,
    ServicioIdempotencia idempotencia,
    TimeProvider reloj)
{
    private static readonly Dictionary<string, Func<IQueryable<Serie>, bool, IOrderedQueryable<Serie>>> OrdenesSerie =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["numero"] = (q, desc) => desc ? q.OrderByDescending(s => s.Numero) : q.OrderBy(s => s.Numero),
            ["prefijo"] = (q, desc) => desc ? q.OrderByDescending(s => s.Prefijo) : q.OrderBy(s => s.Prefijo),
            ["tipo"] = (q, desc) => desc ? q.OrderByDescending(s => s.Tipo) : q.OrderBy(s => s.Tipo),
            ["siguiente"] = (q, desc) => desc ? q.OrderByDescending(s => s.Siguiente) : q.OrderBy(s => s.Siguiente),
        };

    private static readonly Dictionary<string, Func<IQueryable<NumeradorFiscal>, bool, IOrderedQueryable<NumeradorFiscal>>>
        OrdenesNumerador = new(StringComparer.OrdinalIgnoreCase)
        {
            // Los que quedan primero: es el orden en el que hay que revisar la pantalla.
            ["restantes"] = (q, desc) => desc
                ? q.OrderByDescending(n => n.RangoHasta - n.SiguienteNumero + 1)
                : q.OrderBy(n => n.RangoHasta - n.SiguienteNumero + 1),
            ["vencimiento"] = (q, desc) => desc
                ? q.OrderByDescending(n => n.FechaVencimiento)
                : q.OrderBy(n => n.FechaVencimiento),
            ["actualizado"] = (q, desc) => desc
                ? q.OrderByDescending(n => n.UpdatedAt)
                : q.OrderBy(n => n.UpdatedAt),
        };

    private static readonly Dictionary<string, Func<IQueryable<TipoItbis>, bool, IOrderedQueryable<TipoItbis>>>
        OrdenesItbis = new(StringComparer.OrdinalIgnoreCase)
        {
            ["nombre"] = (q, desc) => desc ? q.OrderByDescending(t => t.Nombre) : q.OrderBy(t => t.Nombre),
            ["porcentaje"] = (q, desc) => desc ? q.OrderByDescending(t => t.Porcentaje) : q.OrderBy(t => t.Porcentaje),
        };

    private static readonly Dictionary<string, Func<IQueryable<TipoRetencion>, bool, IOrderedQueryable<TipoRetencion>>>
        OrdenesRetencion = new(StringComparer.OrdinalIgnoreCase)
        {
            ["nombre"] = (q, desc) => desc ? q.OrderByDescending(t => t.Nombre) : q.OrderBy(t => t.Nombre),
            ["porcentaje"] = (q, desc) => desc ? q.OrderByDescending(t => t.Porcentaje) : q.OrderBy(t => t.Porcentaje),
        };

    private static readonly Dictionary<string, Func<IQueryable<FormaPago>, bool, IOrderedQueryable<FormaPago>>>
        OrdenesFormaPago = new(StringComparer.OrdinalIgnoreCase)
        {
            ["nombre"] = (q, desc) => desc ? q.OrderByDescending(f => f.Nombre) : q.OrderBy(f => f.Nombre),
        };

    private static readonly Dictionary<string, Func<IQueryable<CuentaReceptora>, bool, IOrderedQueryable<CuentaReceptora>>>
        OrdenesCuenta = new(StringComparer.OrdinalIgnoreCase)
        {
            ["nombre"] = (q, desc) => desc ? q.OrderByDescending(c => c.Nombre) : q.OrderBy(c => c.Nombre),
            ["moneda"] = (q, desc) => desc ? q.OrderByDescending(c => c.Moneda) : q.OrderBy(c => c.Moneda),
        };

    private static readonly Dictionary<string, Func<IQueryable<Subempresa>, bool, IOrderedQueryable<Subempresa>>>
        OrdenesSubempresa = new(StringComparer.OrdinalIgnoreCase)
        {
            ["nombre"] = (q, desc) => desc ? q.OrderByDescending(s => s.Nombre) : q.OrderBy(s => s.Nombre),
        };

    // ---------------------------------------------------------------- series

    public Task<Pagina<SerieRespuesta>> ListarSeriesAsync(ConsultaLista paginacion, CancellationToken ct) =>
        ConsultaPaginada.PaginarAsync(
            contexto.Serie.AsNoTracking(), OrdenesSerie, "numero", paginacion, ASerie, ct);

    public async Task<SerieRespuesta> CrearSerieAsync(
        SeriePeticion peticion,
        Guid admin,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirNumeroYPrefijoLibresAsync(peticion.Numero, peticion.Prefijo, null, ct);

        var serie = new Serie();
        Aplicar(serie, peticion);

        // El contador de una serie nueva arranca en 1: el primer número emitido es el 1
        // (docs/DOC_TECNICA.md §4.3).
        serie.Siguiente = 1;

        return await GuardarAsync(
            contexto.Serie,
            serie,
            ASerie,
            esAlta: true,
            "serie.creada",
            "serie",
            admin,
            ip,
            mutacion,
            datos => JsonSerializer.Serialize(new
            {
                serie.Numero,
                Prefijo = serie.Prefijo,
                Tipo = serie.Tipo.ToString(),
                Moneda = serie.Moneda?.ToString(),
            }),
            antesDeGuardar: null,
            ct);
    }

    public async Task<SerieRespuesta> ActualizarSerieAsync(
        Guid id,
        SeriePeticion peticion,
        Guid admin,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var serie = await CargarAsync(contexto.Serie, id, "serie", ct);

        await ExigirNumeroYPrefijoLibresAsync(peticion.Numero, peticion.Prefijo, id, ct);

        var antes = new SerieRespuesta(
            serie.Id, serie.Numero, serie.Prefijo, serie.Moneda, serie.Tipo, serie.PlantillaPdf,
            serie.SumaAFacturado, serie.SumaASaldos, serie.GeneraComprobante, serie.Siguiente, serie.Activa);

        // El contador no lo toca la edición: solo lo mueve la emisión (docs/DOC_TECNICA.md §4.3).
        var siguiente = serie.Siguiente;
        Aplicar(serie, peticion);
        serie.Siguiente = siguiente;

        // §3.7: "no se puede cambiar genera comprobante en una serie con facturas emitidas". La razón
        // es que las facturas ya salieron con o sin comprobante y esa decisión ya está en el
        // histórico: pasarla por debajo dejaría facturas de una serie fiscal sin comprobante, que es
        // justo el documento que no se puede declarar. La salida es crear una serie nueva.
        if (serie.GeneraComprobante != antes.GeneraComprobante
            && await contexto.Factura.AsNoTracking().AnyAsync(f => f.SerieId == id, ct))
        {
            throw new InvalidOperationException(
                $"La serie {serie.Numero} ya tiene facturas emitidas y no admite cambiar «genera "
                    + $"comprobante» (de {(antes.GeneraComprobante ? "sí" : "no")} a "
                    + $"{(serie.GeneraComprobante ? "sí" : "no")}). Crea una serie nueva.");
        }

        return await GuardarAsync(
            contexto.Serie,
            serie,
            ASerie,
            esAlta: false,
            "serie.actualizada",
            "serie",
            admin,
            ip,
            mutacion,
            datos => JsonSerializer.Serialize(new
            {
                serie.Numero,
                Prefijo = serie.Prefijo,
                Activa = serie.Activa,
                GeneraComprobante = serie.GeneraComprobante,
            }),
            antesDeGuardar: null,
            ct);
    }

    /// <summary>
    /// Da de baja una serie. No se borra: tiene números emitidos que hay que poder leer siempre.
    /// </summary>
    public Task DesactivarSerieAsync(
        Guid id,
        Guid admin,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct) =>
        DesactivarAsync(
            contexto.Serie,
            id,
            "serie",
            "serie.desactivada",
            s => s.Activa = false,
            admin,
            ip,
            mutacion,
            ct);

    // ---------------------------------------------------- numeradores de comprobante

    /// <summary>
    /// Numeradores de comprobante fiscal (F3-03, docs/ESPECIFICACION.md §3.7). El orden por defecto
    /// es por números que quedan, de menos a más: la pantalla se abre con lo que se está agotando
    /// arriba, que es lo que hay que mirar primero.
    /// </summary>
    public Task<Pagina<NumeradorFiscalRespuesta>> ListarNumeradoresAsync(ConsultaLista paginacion, CancellationToken ct) =>
        ConsultaPaginada.PaginarAsync(
            contexto.NumeradorFiscal.AsNoTracking().Include(n => n.Serie),
            OrdenesNumerador,
            "restantes",
            paginacion,
            n => ANumerador(n),
            ct);

    public async Task<NumeradorFiscalRespuesta> LeerNumeradorAsync(Guid serieId, CancellationToken ct) =>
        ANumerador(await CargarNumeradorAsync(serieId, ct));

    /// <summary>
    /// Guarda el numerador de una serie fiscal. Es de alta o de cambio según ya exista, y por eso la
    /// auditoría lleva el valor anterior y el nuevo: quién cambió el rango de una serie y de qué rango
    /// a cuál es la pregunta que se hace cuando falta un comprobante.
    /// <para>
    /// Los valores del rango y del umbral no se inventan aquí: los escribe el administrador. El
    /// servicio solo comprueba que sean coherentes entre sí.
    /// </para>
    /// </summary>
    public async Task<NumeradorFiscalRespuesta> GuardarNumeradorAsync(
        Guid serieId,
        NumeradorFiscalPeticion peticion,
        Guid admin,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var serie = await contexto.Serie.AsNoTracking().SingleOrDefaultAsync(s => s.Id == serieId, ct)
            ?? throw new KeyNotFoundException($"La serie '{serieId:D}' no existe.");

        ExigirNumeradorCoherente(peticion);

        var existente = await contexto.NumeradorFiscal
            .SingleOrDefaultAsync(n => n.SerieId == serieId, ct);
        var esAlta = existente is null;

        var anterior = esAlta ? null : ANumerador(existente!);

        var numerador = existente ?? new NumeradorFiscal { SerieId = serieId };
        numerador.TipoPrefijo = peticion.TipoPrefijo.Trim();
        numerador.RangoDesde = peticion.RangoDesde;
        numerador.RangoHasta = peticion.RangoHasta;
        numerador.FechaVencimiento = peticion.FechaVencimiento;
        numerador.SiguienteNumero = peticion.SiguienteNumero;
        numerador.UmbralAlerta = peticion.UmbralAlerta;

        var guardado = await GuardarAsync(
            contexto.NumeradorFiscal,
            numerador,
            n => ANumerador(n, serie.Numero),
            esAlta,
            esAlta ? "numerador_fiscal.creado" : "numerador_fiscal.actualizado",
            "numerador_fiscal",
            admin,
            ip,
            mutacion,
            datos => JsonSerializer.Serialize(new { Anterior = anterior, Nuevo = ANumerador(numerador, serie.Numero) }),
            antesDeGuardar: null,
            ct);

        return guardado;
    }

    /// <summary>
    /// Coherencia del rango. Vive en el servicio y no en la base porque una regla depende del valor
    /// que acaba de escribir quien guarda, y la restricción de la base no puede mirar una fila que
    /// todavía no existe.
    /// </summary>
    private static void ExigirNumeradorCoherente(NumeradorFiscalPeticion peticion)
    {
        if (peticion.RangoDesde > peticion.RangoHasta)
        {
            throw new InvalidOperationException(
                $"El rango no puede empezar ({peticion.RangoDesde}) más arriba de donde acaba "
                    + $"({peticion.RangoHasta}).");
        }

        // RangoHasta + 1 es lo que declara agotado un rango terminado, y es lo que puede dejar el
        // administrador al guardar después de haberlo gastado entero. Más allá ya no se puede volver.
        if (peticion.SiguienteNumero < peticion.RangoDesde || peticion.SiguienteNumero > peticion.RangoHasta + 1)
        {
            throw new InvalidOperationException(
                $"El siguiente número ({peticion.SiguienteNumero}) tiene que estar dentro del rango "
                    + $"({peticion.RangoDesde}..{peticion.RangoHasta}) o una posición más allá para "
                    + "declararlo agotado.");
        }

        if (peticion.UmbralAlerta <= 0)
        {
            throw new InvalidOperationException(
                "El umbral de aviso tiene que ser un número de números mayor que cero.");
        }
    }

    private async Task<NumeradorFiscal> CargarNumeradorAsync(Guid serieId, CancellationToken ct) =>
        await contexto.NumeradorFiscal.AsNoTracking().SingleOrDefaultAsync(n => n.SerieId == serieId, ct)
        ?? throw new KeyNotFoundException(
            $"La serie '{serieId:D}' no tiene numerador de comprobante fiscal configurado.");

    private static NumeradorFiscalRespuesta ANumerador(NumeradorFiscal n, int? serieNumero = null) => new(
        n.Id,
        n.SerieId,
        serieNumero ?? n.Serie?.Numero ?? 0,
        n.TipoPrefijo,
        n.RangoDesde,
        n.RangoHasta,
        n.FechaVencimiento,
        n.SiguienteNumero,
        n.UmbralAlerta)
    {
        Restantes = n.RangoHasta - n.SiguienteNumero + 1,
    };

    // ------------------------------------------------------- ITBIS y retenciones

    public Task<Pagina<TipoItbisRespuesta>> ListarTiposItbisAsync(ConsultaLista paginacion, CancellationToken ct) =>
        ConsultaPaginada.PaginarAsync(
            contexto.TipoItbis.AsNoTracking(), OrdenesItbis, "nombre", paginacion, AItbis, ct);

    public async Task<TipoItbisRespuesta> CrearTipoItbisAsync(
        TipoItbisPeticion peticion,
        Guid admin,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var tipo = new TipoItbis
        {
            Nombre = peticion.Nombre.Trim(),
            Porcentaje = Redondeo.Porcentaje(peticion.Porcentaje),
            Activo = peticion.Activo,
        };
        return await GuardarAsync(
            contexto.TipoItbis,
            tipo,
            AItbis,
            esAlta: true,
            "tipo_itbis.creado",
            "tipo_itbis",
            admin,
            ip,
            mutacion,
            datos => JsonSerializer.Serialize(AItbis(tipo)),
            antesDeGuardar: null,
            ct);
    }

    public async Task<TipoItbisRespuesta> ActualizarTipoItbisAsync(
        Guid id,
        TipoItbisPeticion peticion,
        Guid admin,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var tipo = await CargarAsync(contexto.TipoItbis, id, "tipo_itbis", ct);

        tipo.Nombre = peticion.Nombre.Trim();
        tipo.Porcentaje = Redondeo.Porcentaje(peticion.Porcentaje);
        tipo.Activo = peticion.Activo;
        return await GuardarAsync(
            contexto.TipoItbis,
            tipo,
            AItbis,
            esAlta: false,
            "tipo_itbis.actualizado",
            "tipo_itbis",
            admin,
            ip,
            mutacion,
            datos => JsonSerializer.Serialize(AItbis(tipo)),
            antesDeGuardar: null,
            ct);
    }

    public Task DesactivarTipoItbisAsync(
        Guid id,
        Guid admin,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct) =>
        DesactivarAsync(
            contexto.TipoItbis,
            id,
            "tipo_itbis",
            "tipo_itbis.desactivado",
            t => t.Activo = false,
            admin,
            ip,
            mutacion,
            ct);

    public Task<Pagina<TipoRetencionRespuesta>> ListarTiposRetencionAsync(
        ConsultaLista paginacion,
        CancellationToken ct) =>
        ConsultaPaginada.PaginarAsync(
            contexto.TipoRetencion.AsNoTracking(), OrdenesRetencion, "nombre", paginacion, ARetencion, ct);

    public async Task<TipoRetencionRespuesta> CrearTipoRetencionAsync(
        TipoRetencionPeticion peticion,
        Guid admin,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var tipo = new TipoRetencion
        {
            Nombre = peticion.Nombre.Trim(),
            Porcentaje = Redondeo.Porcentaje(peticion.Porcentaje),
            Base = peticion.Base,
            EsDefecto = peticion.EsDefecto,
            Activo = peticion.Activo,
        };
        return await GuardarAsync(
            contexto.TipoRetencion,
            tipo,
            ARetencion,
            esAlta: true,
            "tipo_retencion.creado",
            "tipo_retencion",
            admin,
            ip,
            mutacion,
            datos => JsonSerializer.Serialize(ARetencion(tipo)),
            peticion.EsDefecto ? () => MarcarDefectoAnteriorAsync(tipo.Id, admin, ct) : null,
            ct);
    }

    public async Task<TipoRetencionRespuesta> ActualizarTipoRetencionAsync(
        Guid id,
        TipoRetencionPeticion peticion,
        Guid admin,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var tipo = await CargarAsync(contexto.TipoRetencion, id, "tipo_retencion", ct);

        tipo.Nombre = peticion.Nombre.Trim();
        tipo.Porcentaje = Redondeo.Porcentaje(peticion.Porcentaje);
        tipo.Base = peticion.Base;
        tipo.Activo = peticion.Activo;
        tipo.EsDefecto = peticion.EsDefecto;
        return await GuardarAsync(
            contexto.TipoRetencion,
            tipo,
            ARetencion,
            esAlta: false,
            "tipo_retencion.actualizado",
            "tipo_retencion",
            admin,
            ip,
            mutacion,
            datos => JsonSerializer.Serialize(ARetencion(tipo)),
            peticion.EsDefecto ? () => MarcarDefectoAnteriorAsync(tipo.Id, admin, ct) : null,
            ct);
    }

    public Task DesactivarTipoRetencionAsync(
        Guid id,
        Guid admin,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct) =>
        DesactivarAsync(
            contexto.TipoRetencion,
            id,
            "tipo_retencion",
            "tipo_retencion.desactivado",
            t => t.Activo = false,
            admin,
            ip,
            mutacion,
            ct);

    // ----------------------------------------------- formas de pago y cuentas

    public Task<Pagina<FormaPagoRespuesta>> ListarFormasPagoAsync(ConsultaLista paginacion, CancellationToken ct) =>
        ConsultaPaginada.PaginarAsync(
            contexto.FormaPago.AsNoTracking(), OrdenesFormaPago, "nombre", paginacion, AFormaPago, ct);

    public async Task<FormaPagoRespuesta> CrearFormaPagoAsync(
        FormaPagoPeticion peticion,
        Guid admin,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var forma = new FormaPago { Nombre = peticion.Nombre.Trim(), Activa = peticion.Activa };
        return await GuardarAsync(
            contexto.FormaPago,
            forma,
            AFormaPago,
            esAlta: true,
            "forma_pago.creada",
            "forma_pago",
            admin,
            ip,
            mutacion,
            datos => JsonSerializer.Serialize(AFormaPago(forma)),
            antesDeGuardar: null,
            ct);
    }

    public async Task<FormaPagoRespuesta> ActualizarFormaPagoAsync(
        Guid id,
        FormaPagoPeticion peticion,
        Guid admin,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var forma = await CargarAsync(contexto.FormaPago, id, "forma_pago", ct);

        forma.Nombre = peticion.Nombre.Trim();
        forma.Activa = peticion.Activa;
        return await GuardarAsync(
            contexto.FormaPago,
            forma,
            AFormaPago,
            esAlta: false,
            "forma_pago.actualizada",
            "forma_pago",
            admin,
            ip,
            mutacion,
            datos => JsonSerializer.Serialize(AFormaPago(forma)),
            antesDeGuardar: null,
            ct);
    }

    public Task DesactivarFormaPagoAsync(
        Guid id,
        Guid admin,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct) =>
        DesactivarAsync(
            contexto.FormaPago,
            id,
            "forma_pago",
            "forma_pago.desactivada",
            f => f.Activa = false,
            admin,
            ip,
            mutacion,
            ct);

    public Task<Pagina<CuentaReceptoraRespuesta>> ListarCuentasAsync(
        ConsultaLista paginacion,
        CancellationToken ct) =>
        ConsultaPaginada.PaginarAsync(
            contexto.CuentaReceptora.AsNoTracking(), OrdenesCuenta, "nombre", paginacion, ACuenta, ct);

    public async Task<CuentaReceptoraRespuesta> CrearCuentaAsync(
        CuentaReceptoraPeticion peticion,
        Guid admin,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var cuenta = new CuentaReceptora
        {
            Nombre = peticion.Nombre.Trim(),
            Banco = peticion.Banco?.Trim(),
            Moneda = peticion.Moneda,
            Activa = peticion.Activa,
        };
        return await GuardarAsync(
            contexto.CuentaReceptora,
            cuenta,
            ACuenta,
            esAlta: true,
            "cuenta_receptora.creada",
            "cuenta_receptora",
            admin,
            ip,
            mutacion,
            datos => JsonSerializer.Serialize(ACuenta(cuenta)),
            antesDeGuardar: null,
            ct);
    }

    public async Task<CuentaReceptoraRespuesta> ActualizarCuentaAsync(
        Guid id,
        CuentaReceptoraPeticion peticion,
        Guid admin,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var cuenta = await CargarAsync(contexto.CuentaReceptora, id, "cuenta_receptora", ct);

        cuenta.Nombre = peticion.Nombre.Trim();
        cuenta.Banco = peticion.Banco?.Trim();
        cuenta.Moneda = peticion.Moneda;
        cuenta.Activa = peticion.Activa;
        return await GuardarAsync(
            contexto.CuentaReceptora,
            cuenta,
            ACuenta,
            esAlta: false,
            "cuenta_receptora.actualizada",
            "cuenta_receptora",
            admin,
            ip,
            mutacion,
            datos => JsonSerializer.Serialize(ACuenta(cuenta)),
            antesDeGuardar: null,
            ct);
    }

    public Task DesactivarCuentaAsync(
        Guid id,
        Guid admin,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct) =>
        DesactivarAsync(
            contexto.CuentaReceptora,
            id,
            "cuenta_receptora",
            "cuenta_receptora.desactivada",
            c => c.Activa = false,
            admin,
            ip,
            mutacion,
            ct);

    // ---------------------------------------------------------- subempresas

    public Task<Pagina<SubempresaRespuesta>> ListarSubempresasAsync(
        ConsultaLista paginacion,
        CancellationToken ct) =>
        ConsultaPaginada.PaginarAsync(
            contexto.Subempresa.AsNoTracking(), OrdenesSubempresa, "nombre", paginacion, ASubempresa, ct);

    public async Task<SubempresaRespuesta> CrearSubempresaAsync(
        SubempresaPeticion peticion,
        Guid admin,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var subempresa = new Subempresa
        {
            Nombre = peticion.Nombre.Trim(),
            Propietaria = peticion.Propietaria,
            Facturadora = peticion.Facturadora,
            Activa = peticion.Activa,
        };
        return await GuardarAsync(
            contexto.Subempresa,
            subempresa,
            ASubempresa,
            esAlta: true,
            "subempresa.creada",
            "subempresa",
            admin,
            ip,
            mutacion,
            datos => JsonSerializer.Serialize(ASubempresa(subempresa)),
            antesDeGuardar: null,
            ct);
    }

    public async Task<SubempresaRespuesta> ActualizarSubempresaAsync(
        Guid id,
        SubempresaPeticion peticion,
        Guid admin,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var subempresa = await CargarAsync(contexto.Subempresa, id, "subempresa", ct);

        subempresa.Nombre = peticion.Nombre.Trim();
        subempresa.Propietaria = peticion.Propietaria;
        subempresa.Facturadora = peticion.Facturadora;
        subempresa.Activa = peticion.Activa;
        return await GuardarAsync(
            contexto.Subempresa,
            subempresa,
            ASubempresa,
            esAlta: false,
            "subempresa.actualizada",
            "subempresa",
            admin,
            ip,
            mutacion,
            datos => JsonSerializer.Serialize(ASubempresa(subempresa)),
            antesDeGuardar: null,
            ct);
    }

    public Task DesactivarSubempresaAsync(
        Guid id,
        Guid admin,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct) =>
        DesactivarAsync(
            contexto.Subempresa,
            id,
            "subempresa",
            "subempresa.desactivada",
            s => s.Activa = false,
            admin,
            ip,
            mutacion,
            ct);

    // ------------------------------------------------ tolerancias y recordatorios

    public async Task<AjusteGeneralRespuesta> LeerAjustesAsync(CancellationToken ct) =>
        AAjustes(await CargarAjustesAsync(ct));

    public async Task<AjusteGeneralRespuesta> ActualizarAjustesAsync(
        AjusteGeneralPeticion peticion,
        Guid admin,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var ajustes = await CargarAjustesAsync(ct);

        ajustes.ToleranciaCambioUsd = peticion.ToleranciaCambioUsd is { } usd ? Redondeo.Importe(usd) : null;
        ajustes.ToleranciaCambioDop = peticion.ToleranciaCambioDop is { } dop ? Redondeo.Importe(dop) : null;
        ajustes.ModoRecordatorios = peticion.ModoRecordatorios;

        return await GuardarAsync(
            contexto.AjusteGeneral,
            ajustes,
            AAjustes,
            esAlta: false,
            "ajuste_general.actualizado",
            "ajuste_general",
            admin,
            ip,
            mutacion,
            datos => JsonSerializer.Serialize(AAjustes(ajustes)),
            antesDeGuardar: null,
            ct);
    }

    // ---------------------------------------------------------------- cálculo

    /// <summary>
    /// Calcula ITBIS y retenciones de un subtotal con los porcentajes que hay ahora mismo en la
    /// configuración. Es el camino que usarán las facturas, y la prueba de que cambiar la
    /// configuración cambia el resultado (D14).
    /// </summary>
    public async Task<ResultadoImpuestos> CalcularAsync(
        decimal subtotal,
        Guid? tipoItbisId,
        IReadOnlyList<Guid> tipoRetencionIds,
        CancellationToken ct)
    {
        var porcentajeItbis = tipoItbisId is { } idItbis
            ? await contexto.TipoItbis.AsNoTracking()
                .Where(t => t.Id == idItbis && t.Activo)
                .Select(t => t.Porcentaje)
                .SingleOrDefaultAsync(ct)
            : 0m;

        var retenciones = await contexto.TipoRetencion.AsNoTracking()
            .Where(t => t.Activo && tipoRetencionIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Porcentaje, t.Base })
            .ToListAsync(ct);

        // En el orden en que los pidió el documento, no en el que los devuelva la consulta.
        var porId = retenciones.ToDictionary(r => r.Id, r => (r.Porcentaje, r.Base));
        var aplicadas = new List<(decimal, BaseRetencion)>(tipoRetencionIds.Count);
        foreach (var id in tipoRetencionIds)
        {
            if (porId.TryGetValue(id, out var tipo))
            {
                aplicadas.Add(tipo);
            }
        }

        return CalculoImpuestos.Calcular(subtotal, porcentajeItbis, aplicadas);
    }

    // ---------------------------------------------------------------- internos

    private static SerieRespuesta ASerie(Serie s) => new(
        s.Id, s.Numero, s.Prefijo, s.Moneda, s.Tipo, s.PlantillaPdf,
        s.SumaAFacturado, s.SumaASaldos, s.GeneraComprobante, s.Siguiente, s.Activa);

    private static TipoItbisRespuesta AItbis(TipoItbis t) =>
        new(t.Id, t.Nombre, t.Porcentaje, t.Activo);

    private static TipoRetencionRespuesta ARetencion(TipoRetencion t) =>
        new(t.Id, t.Nombre, t.Porcentaje, t.Base, t.EsDefecto, t.Activo);

    private static FormaPagoRespuesta AFormaPago(FormaPago f) => new(f.Id, f.Nombre, f.Activa);

    private static CuentaReceptoraRespuesta ACuenta(CuentaReceptora c) =>
        new(c.Id, c.Nombre, c.Banco, c.Moneda, c.Activa);

    private static SubempresaRespuesta ASubempresa(Subempresa s) =>
        new(s.Id, s.Nombre, s.Propietaria, s.Facturadora, s.Activa);

    private static AjusteGeneralRespuesta AAjustes(AjusteGeneral a) =>
        new(a.Id, a.ToleranciaCambioUsd, a.ToleranciaCambioDop, a.ModoRecordatorios);

    private static void Aplicar(Serie serie, SeriePeticion peticion)
    {
        serie.Numero = peticion.Numero;
        serie.Prefijo = peticion.Prefijo.Trim();
        serie.Moneda = peticion.Moneda;
        serie.Tipo = peticion.Tipo;
        serie.PlantillaPdf = peticion.PlantillaPdf?.Trim();
        serie.SumaAFacturado = peticion.SumaAFacturado;
        serie.SumaASaldos = peticion.SumaASaldos;
        serie.GeneraComprobante = peticion.GeneraComprobante;
        serie.Activa = peticion.Activa;
    }

    private async Task ExigirNumeroYPrefijoLibresAsync(
        int numero,
        string prefijo,
        Guid? serieId,
        CancellationToken ct)
    {
        var prefijoNormalizado = prefijo.Trim();

        var existente = await contexto.Serie.AsNoTracking()
            .Where(s => (s.Numero == numero || s.Prefijo == prefijoNormalizado) && s.Id != serieId)
            .Select(s => new { s.Numero, s.Prefijo })
            .FirstOrDefaultAsync(ct);

        if (existente is not null)
        {
            throw new InvalidOperationException(
                $"Ya existe la serie {existente.Numero} ('{existente.Prefijo}') con ese número o ese prefijo.");
        }
    }

    private async Task<T> CargarAsync<T>(DbSet<T> conjunto, Guid id, string entidad, CancellationToken ct)
        where T : class, IEntidadConfiguracion =>
        await conjunto.FirstOrDefaultAsync(e => e.Id == id, ct)
        ?? throw new KeyNotFoundException($"No existe {entidad} '{id:D}'.");

    private async Task<AjusteGeneral> CargarAjustesAsync(CancellationToken ct) =>
        await contexto.AjusteGeneral.OrderBy(a => a.CreatedAt).FirstOrDefaultAsync(ct)
        ?? throw new KeyNotFoundException("No está la fila de ajustes generales.");

    private async Task DesactivarAsync<T>(
        DbSet<T> conjunto,
        Guid id,
        string entidad,
        string accion,
        Action<T> desactivar,
        Guid admin,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
        where T : class, IEntidadConfiguracion
    {
        var fila = await CargarAsync(conjunto, id, entidad, ct);

        desactivar(fila);

        await GuardarAsync(
            conjunto,
            fila,
            e => new { Id = id, Entidad = entidad },
            esAlta: false,
            accion,
            entidad,
            admin,
            ip,
            mutacion,
            datos => JsonSerializer.Serialize(new { Id = id.ToString("D"), Entidad = entidad }),
            antesDeGuardar: null,
            ct);
    }

    /// <summary>
    /// Guarda cualquier cambio de configuración: una transacción, auditoría con IP y la respuesta
    /// guardada para el reintento (AGENTS.md §6, docs/DOC_TECNICA.md §6).
    /// </summary>
    /// <param name="fila">
    /// Fila ya modificada por el llamante y seguida por el contexto, o una nueva en el alta.
    /// </param>
    /// <param name="proyectar">
    /// Cuerpo de la respuesta. Se construye después de guardar, porque en un alta el id todavía no
    /// existe y una respuesta con id vacío no sirve ni para el reintento.
    /// </param>
    /// <param name="esAlta">Si es alta se le da id y marca de creación; si no, marca de modificación.</param>
    /// <param name="antesDeGuardar">Ajustes que tienen que caer antes del <c>SaveChanges</c>.</param>
    private async Task<TRespuesta> GuardarAsync<T, TRespuesta>(
        DbSet<T> conjunto,
        T fila,
        Func<T, TRespuesta> proyectar,
        bool esAlta,
        string accion,
        string entidad,
        Guid admin,
        string ip,
        MutacionIdempotente? mutacion,
        Func<T, string> datosAuditoria,
        Func<Task>? antesDeGuardar,
        CancellationToken ct)
        where T : class, IEntidadConfiguracion
        where TRespuesta : notnull
    {
        var ahora = reloj.GetUtcNow();

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        if (esAlta)
        {
            fila.Id = Guid.NewGuid();
            fila.CreatedAt = ahora;
            fila.CreatedBy = admin;
            conjunto.Add(fila);
        }
        else
        {
            fila.UpdatedAt = ahora;
            fila.UpdatedBy = admin;
        }

        if (antesDeGuardar is not null)
        {
            await antesDeGuardar();
        }

        await contexto.SaveChangesAsync(ct);

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = accion,
            Ip = ip,
            UsuarioId = admin,
            Entidad = entidad,
            EntidadId = fila.Id.ToString("D"),
            DatosJson = datosAuditoria(fila),
            Resultado = "permitido",
            Origen = "api",
        }, ct);

        var respuesta = proyectar(fila);

        await idempotencia.RegistrarAsync(admin, mutacion, respuesta, ct);

        await transaccion.CommitAsync(ct);

        return respuesta;
    }

    /// <summary>
    /// Le quita el distintivo de por defecto a los otros tipos de retención: solo uno puede
    /// tenerlo, y el índice único parcial de la tabla es la red por si acaso.
    /// </summary>
    private async Task MarcarDefectoAnteriorAsync(Guid tipoRetencionId, Guid admin, CancellationToken ct)
    {
        var otros = await contexto.TipoRetencion
            .Where(t => t.EsDefecto && t.Id != tipoRetencionId)
            .ToListAsync(ct);

        var ahora = reloj.GetUtcNow();
        foreach (var otro in otros)
        {
            otro.EsDefecto = false;
            otro.UpdatedAt = ahora;
            otro.UpdatedBy = admin;
        }
    }
}