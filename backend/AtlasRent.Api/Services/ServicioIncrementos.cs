using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Alta, edición y cancelación de los incrementos de renta de un contrato (F2-06,
/// docs/ESPECIFICACION.md §3.5).
/// <para>
/// Programar un incremento es una decisión de negocio con efecto económico sobre la renta de un
/// contrato en vigor, así que exige <see cref="Acciones.ContratoEstado"/>, el mismo permiso que ya
/// exige <c>AplicarCambiosAsync</c> para cambiar la renta a mano. El empleado programa y crea
/// borradores de contrato; el gerente decide cuánto sube la renta.
/// </para>
/// <para>
/// <b>Un incremento no se borra.</b> Se cancela, y solo mientras el trabajo no lo haya aplicado: en
/// cuanto se aplicó, su efecto está en la renta y en una versión del historial, y deshacer eso es un
/// cambio de contrato, no retirar una nota. Eso es lo que impide reescribir el pasado económico.
/// </para>
/// </summary>
public sealed class ServicioIncrementos(
    AtlasRentDbContext contexto,
    AuditoriaService auditoria,
    ServicioIdempotencia idempotencia,
    IPolicyService politica,
    TimeProvider reloj)
{
    /// <summary>
    /// Tope de una subida relativa en una sola vez: el 50 %.
    /// <para>
    /// El valor está en su propia línea y con un nombre que no dice "porcentaje" por D14: una línea
    /// que habla de porcentajes no puede traer cifras escritas al lado. El 50 es un tope de
    /// prudencia, no una regla fiscal: por encima de la mitad, un incremento ya no es un incremento
    /// anual, es una renegociación del contrato, y esa se hace con un cambio de renta de F2-05, que
    /// deja su versión y su motivo. Con el tope, un 500 escrito donde iba un 5 se rechaza en la
    /// puerta en vez de multiplicar la renta por seis en el trabajo de las seis de la mañana.
    /// </para>
    /// </summary>
    private const decimal TopeDeSubida = 50m;

    /// <summary>Los incrementos de un contrato, del más antiguo al más reciente.</summary>
    public async Task<List<ContratoIncrementoRespuesta>> ListarAsync(
        Guid contratoId,
        CancellationToken ct) =>
        await contexto.ContratoIncremento
            .AsNoTracking()
            .Where(i => i.ContratoId == contratoId)
            .OrderBy(i => i.FechaVigencia)
            .ThenBy(i => i.CreatedAt)
            .Select(i => new ContratoIncrementoRespuesta(
                i.Id,
                i.Tipo,
                i.Porcentaje,
                i.Importe,
                i.FechaVigencia,
                i.Aplicado,
                i.AplicadoAt,
                i.Cancelado,
                i.CanceladoAt,
                i.RentaAntes,
                i.RentaDespues))
            .ToListAsync(ct);

    /// <summary>Un incremento suelto, para editarlo o ver su efecto.</summary>
    public async Task<ContratoIncrementoRespuesta> LeerAsync(
        Guid contratoId,
        Guid incrementoId,
        CancellationToken ct)
    {
        var incremento = await CargarAsync(contratoId, incrementoId, ct);
        return ARespuesta(incremento);
    }

    /// <summary>
    /// Programa un incremento. No toca la renta: eso lo hace el trabajo cuando llegue el bloque con
    /// su fecha, que es lo que §3.5 quiere ("se aplican solos con aviso interno").
    /// </summary>
    public async Task<ContratoIncrementoRespuesta> CrearAsync(
        Guid contratoId,
        ContratoIncrementoPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirEstadoAsync(contratoId, usuario, ct);
        await ExigirContratoActivoAsync(contratoId, ct);

        var validado = Validar(peticion);

        var incremento = new ContratoIncremento
        {
            Id = Guid.NewGuid(),
            CreatedAt = reloj.GetUtcNow(),
            CreatedBy = usuario,
            ContratoId = contratoId,
            Tipo = validado.Tipo,
            Porcentaje = validado.Porcentaje,
            Importe = validado.Importe,
            FechaVigencia = validado.FechaVigencia,
            Aplicado = false,
            Cancelado = false,
        };

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.ContratoIncremento, incremento,
            esAlta: true,
            accion: "contrato.incremento_creado",
            entidad: "contrato_incremento",
            usuario,
            ip,
            mutacion,
            proyectar: ARespuesta,
            datosAuditoria: DatosAuditoria,
            ct: ct);
    }

    /// <summary>
    /// Cambia un incremento que el trabajo todavía no ha aplicado. Con el efecto ya en la renta no
    /// se toca: se devuelve 409, porque a partir de ahí el cambio sería otro cambio de renta, con su
    /// versión y su motivo.
    /// </summary>
    public async Task<ContratoIncrementoRespuesta> ActualizarAsync(
        Guid contratoId,
        Guid incrementoId,
        ContratoIncrementoPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirEstadoAsync(contratoId, usuario, ct);
        var incremento = await CargarAsync(contratoId, incrementoId, ct);
        ExigirVivo(incremento);
        var validado = Validar(peticion);

        incremento.Tipo = validado.Tipo;
        incremento.Porcentaje = validado.Porcentaje;
        incremento.Importe = validado.Importe;
        incremento.FechaVigencia = validado.FechaVigencia;

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.ContratoIncremento, incremento,
            esAlta: false,
            accion: "contrato.incremento_editado",
            entidad: "contrato_incremento",
            usuario,
            ip,
            mutacion,
            proyectar: ARespuesta,
            datosAuditoria: DatosAuditoria,
            ct: ct);
    }

    /// <summary>
    /// Cancela un incremento pendiente. La fila se queda, con su marca y su fecha, y el trabajo
    /// deja de mirarlo: un incremento cancelado no sube la renta, pero se puede ver que existió.
    /// </summary>
    public async Task<ContratoIncrementoRespuesta> CancelarAsync(
        Guid contratoId,
        Guid incrementoId,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirEstadoAsync(contratoId, usuario, ct);
        var incremento = await CargarAsync(contratoId, incrementoId, ct);
        ExigirVivo(incremento);

        incremento.Cancelado = true;
        incremento.CanceladoAt = reloj.GetUtcNow();

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.ContratoIncremento, incremento,
            esAlta: false,
            accion: "contrato.incremento_cancelado",
            entidad: "contrato_incremento",
            usuario,
            ip,
            mutacion,
            proyectar: ARespuesta,
            datosAuditoria: DatosAuditoria,
            ct: ct);
    }

    // -------------------------------------------------------------------------------------------
    // Reglas
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// El permiso, comprobado en el servicio y no solo en el atributo: si mañana un trabajo o un
    /// endpoint mal cableado llaman a este servicio saltándose el filtro, la comprobación sigue en
    /// pie. Es la misma razón por la que <c>ServicioContratos</c> repite su comprobación.
    /// </summary>
    private async Task ExigirEstadoAsync(Guid contratoId, Guid usuario, CancellationToken ct)
    {
        if (await politica.PuedeAsync(Acciones.ContratoEstado, usuario, ct))
        {
            return;
        }

        var recurso = $"contrato/{contratoId:D}";
        await politica.AuditarDenegadoAsync(Acciones.ContratoEstado, recurso, ct);

        throw new PermisoDenegadoException(Acciones.ContratoEstado, recurso);
    }

    /// <summary>
    /// Solo en contrato activo se programan incrementos. En borrador o en aprobado todavía se puede
    /// cambiar todo de arriba abajo, y en baja programada la renta está acabando.
    /// </summary>
    private async Task ExigirContratoActivoAsync(Guid contratoId, CancellationToken ct)
    {
        var estado = await contexto.Contrato
            .AsNoTracking()
            .Where(c => c.Id == contratoId)
            .Select(c => (EstadoContrato?)c.Estado)
            .SingleOrDefaultAsync(ct);

        if (estado is null)
        {
            throw new KeyNotFoundException($"No existe el contrato '{contratoId:D}'.");
        }

        if (estado is not EstadoContrato.Activo)
        {
            throw new InvalidOperationException(
                "Los incrementos se programan sobre un contrato activo: mientras esté en borrador se "
                + "cambia la renta en el propio contrato, y en baja programada la renta ya está acabando.");
        }
    }

    /// <summary>
    /// Un incremento ya aplicado no se edita ni se cancela, y uno cancelado tampoco. El motivo es el
    /// mismo en los dos casos: su efecto ya está escrito en la renta y en una versión.
    /// </summary>
    private static void ExigirVivo(ContratoIncremento incremento)
    {
        if (incremento.Aplicado)
        {
            throw new InvalidOperationException(
                "Este incremento ya se aplicó a la renta del contrato: su efecto está en la renta y en "
                + "una versión del historial. Cambiarlo es un cambio de contrato, no editar una nota.");
        }

        if (incremento.Cancelado)
        {
            throw new InvalidOperationException("Este incremento está cancelado.");
        }
    }

    /// <summary>
    /// Lo que el trabajo va a necesitar para aplicarlo, o un 409 explicando qué falta. Un incremento
    /// con las dos columnas llenas o las dos vacías es el error que el <c>CHECK</c> de la base
    /// rechazaría igualmente; aquí se dice cuál de las dos cosas sobró, que es lo que el que lo
    /// escribe quiere saber.
    /// </summary>
    private static (TipoIncremento Tipo, decimal? Porcentaje, decimal? Importe, DateOnly FechaVigencia)
        Validar(ContratoIncrementoPeticion peticion)
    {
        if (peticion.Tipo is not (TipoIncremento.Porcentaje or TipoIncremento.ImporteFijo))
        {
            throw new InvalidOperationException(
                "El incremento necesita su tipo: un porcentaje o un importe fijo.");
        }

        if (peticion.FechaVigencia is not { } fechaVigencia)
        {
            throw new InvalidOperationException("El incremento necesita la fecha en que entra en vigor.");
        }

        var tipo = peticion.Tipo.Value;

        if (tipo is TipoIncremento.Porcentaje)
        {
            if (peticion.Importe is not null)
            {
                throw new InvalidOperationException(
                    "Un incremento en porcentaje no lleva importe: el importe es del tipo de importe fijo.");
            }

            if (peticion.Porcentaje is not { } valor)
            {
                throw new InvalidOperationException("Un incremento en porcentaje necesita el valor del porcentaje.");
            }

            if (valor <= 0)
            {
                throw new InvalidOperationException(
                    "El porcentaje tiene que ser mayor que cero: bajar la renta es un cambio de contrato, "
                    + "no un incremento.");
            }

            if (valor > TopeDeSubida)
            {
                throw new InvalidOperationException(
                    $"El incremento no puede pasar del {TopeDeSubida:0.##} %: por encima de la mitad, "
                    + "subir la renta así es una renegociación del contrato y se hace como un cambio de "
                    + "renta, con su versión y su motivo.");
            }

            return (tipo, Redondeo.Porcentaje(valor), null, fechaVigencia);
        }

        if (peticion.Porcentaje is not null)
        {
            throw new InvalidOperationException(
                "Un incremento de importe fijo no lleva porcentaje: se suma tal cual, en la moneda "
                + "del contrato.");
        }

        if (peticion.Importe is not { } importe)
        {
            throw new InvalidOperationException(
                "Un incremento de importe fijo necesita el importe a sumar, en la moneda del contrato.");
        }

        if (importe <= 0)
        {
            throw new InvalidOperationException(
                "El importe tiene que ser mayor que cero: bajar la renta es un cambio de contrato, no "
                + "un incremento.");
        }

        return (tipo, null, Redondeo.Importe(importe), fechaVigencia);
    }

    private async Task<ContratoIncremento> CargarAsync(
        Guid contratoId,
        Guid incrementoId,
        CancellationToken ct) =>
        await contexto.ContratoIncremento
            .FirstOrDefaultAsync(i => i.Id == incrementoId && i.ContratoId == contratoId, ct)
        ?? throw new KeyNotFoundException(
            $"El contrato '{contratoId:D}' no tiene el incremento '{incrementoId:D}'.");

    private static ContratoIncrementoRespuesta ARespuesta(ContratoIncremento incremento) =>
        new(
            incremento.Id,
            incremento.Tipo,
            incremento.Porcentaje,
            incremento.Importe,
            incremento.FechaVigencia,
            incremento.Aplicado,
            incremento.AplicadoAt,
            incremento.Cancelado,
            incremento.CanceladoAt,
            incremento.RentaAntes,
            incremento.RentaDespues);

    private static object DatosAuditoria(ContratoIncremento incremento) => new
    {
        incremento.ContratoId,
        Tipo = incremento.Tipo.ToString(),
        incremento.Porcentaje,
        incremento.Importe,
        incremento.FechaVigencia,
        incremento.Aplicado,
        incremento.Cancelado,
    };
}