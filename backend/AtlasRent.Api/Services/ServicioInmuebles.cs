using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Inmuebles y unidades (docs/ESPECIFICACION.md §3.1): alta, cambio, baja lógica, lectura y
/// filtro por estado manual.
/// <para>
/// Dos reglas que este servicio no deja pasar:
/// </para>
/// <list type="bullet">
/// <item>
/// Una unidad solo ofrece las modalidades que tiene permitidas, y toda tarifa tiene que ser de una
/// modalidad permitida.
/// </item>
/// <item>
/// El estado manual no es ocupación. Ocupada y reservada se calculan por fechas en una fase
/// posterior (AGENTS.md §6), así que aquí no hay ningún campo que las represente.
/// </item>
/// </list>
/// </summary>
public sealed class ServicioInmuebles(
    AtlasRentDbContext contexto,
    AuditoriaService auditoria,
    ServicioIdempotencia idempotencia,
    TimeProvider reloj)
{
    private static readonly Dictionary<string, Func<IQueryable<InmuebleFila>, bool, IOrderedQueryable<InmuebleFila>>>
        Ordenes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["nombre"] = (q, desc) => desc ? q.OrderByDescending(i => i.Nombre) : q.OrderBy(i => i.Nombre),
            ["tipo"] = (q, desc) => desc ? q.OrderByDescending(i => i.Tipo) : q.OrderBy(i => i.Tipo),
            ["createdAt"] = (q, desc) => desc ? q.OrderByDescending(i => i.CreatedAt) : q.OrderBy(i => i.CreatedAt),
        };

    private static readonly Dictionary<string, Func<IQueryable<Unidad>, bool, IOrderedQueryable<Unidad>>>
        OrdenesUnidad = new(StringComparer.OrdinalIgnoreCase)
        {
            ["nombre"] = (q, desc) => desc ? q.OrderByDescending(u => u.Nombre) : q.OrderBy(u => u.Nombre),
            ["tipo"] = (q, desc) => desc ? q.OrderByDescending(u => u.Tipo) : q.OrderBy(u => u.Tipo),
            ["estadoManual"] = (q, desc) => desc ? q.OrderByDescending(u => u.EstadoManual) : q.OrderBy(u => u.EstadoManual),
            ["createdAt"] = (q, desc) => desc ? q.OrderByDescending(u => u.CreatedAt) : q.OrderBy(u => u.CreatedAt),
        };

    // ---------------------------------------------------------------- inmuebles

    public Task<Pagina<InmuebleResumen>> ListarAsync(
        ConsultaLista paginacion,
        string? busqueda,
        TipoInmueble? tipo,
        bool incluirInactivos,
        CancellationToken ct)
    {
        var consulta = contexto.Inmueble.AsNoTracking();

        if (!incluirInactivos)
        {
            consulta = consulta.Where(i => i.Activo);
        }

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            var termino = busqueda.Trim().ToLowerInvariant();
            consulta = consulta.Where(i => i.Nombre.ToLower().Contains(termino));
        }

        if (tipo is not null)
        {
            consulta = consulta.Where(i => i.Tipo == tipo);
        }

        return ConsultaPaginada.PaginarAsync(Proyectar(consulta), Ordenes, "nombre", paginacion, AResumen, ct);
    }

    /// <summary>Ficha del inmueble con sus unidades, modalidades y tarifas.</summary>
    public async Task<InmuebleRespuesta> LeerAsync(Guid id, CancellationToken ct)
    {
        var inmueble = await contexto.Inmueble
            .Include(i => i.Unidades)
            .ThenInclude(u => u.Modalidades)
            .Include(i => i.Unidades)
            .ThenInclude(u => u.Tarifas)
            .AsNoTracking()
            .SingleOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new KeyNotFoundException($"No existe el inmueble '{id:D}'.");

        return new InmuebleRespuesta(
            inmueble.Id,
            inmueble.Nombre,
            inmueble.Direccion,
            inmueble.Tipo,
            inmueble.PropietariaId,
            inmueble.Notas,
            inmueble.PlantillaContratoPorDefecto,
            inmueble.RenovacionDefectoMeses,
            inmueble.Activo,
            inmueble.CreatedAt,
            [.. inmueble.Unidades.Select(AUnidad).OrderBy(u => u.Nombre, StringComparer.Ordinal)]);
    }

    public Task<InmuebleResumen> CrearAsync(
        InmueblePeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var inmueble = new Inmueble { Activo = true };
        Aplicar(inmueble, peticion);

        return GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Inmueble, inmueble,
            esAlta: true,
            accion: "inmueble.creado",
            entidad: "inmueble",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: DatosAuditoria,
            ct: ct);
    }

    public async Task<InmuebleResumen> ActualizarAsync(
        Guid id,
        InmueblePeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var inmueble = await CargarInmuebleAsync(id, ct);
        Aplicar(inmueble, peticion);

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Inmueble, inmueble,
            esAlta: false,
            accion: "inmueble.actualizado",
            entidad: "inmueble",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: DatosAuditoria,
            ct: ct);
    }

    /// <summary>
    /// Baja lógica. Un inmueble con contratos o facturas detrás no se borra nunca
    /// (docs/ESPECIFICACION.md §6: lo que tiene efecto económico se anula, no se elimina).
    /// </summary>
    public async Task DesactivarAsync(
        Guid id,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var inmueble = await CargarInmuebleAsync(id, ct);
        inmueble.Activo = false;

        await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Inmueble, inmueble,
            esAlta: false,
            accion: "inmueble.desactivado",
            entidad: "inmueble",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: DatosAuditoria,
            ct: ct);
    }

    // ---------------------------------------------------------------- unidades

    /// <summary>
    /// Unidades de un inmueble. El filtro por estado manual es el que usa la pantalla de
    /// disponibilidad: corta las que están en mantenimiento o fuera de servicio.
    /// </summary>
    public Task<Pagina<UnidadRespuesta>> ListarUnidadesAsync(
        Guid inmuebleId,
        ConsultaLista paginacion,
        EstadoUnidad? estado,
        CancellationToken ct)
    {
        var consulta = contexto.Unidad
            .AsNoTracking()
            .Include(u => u.Modalidades)
            .Include(u => u.Tarifas)
            .Where(u => u.InmuebleId == inmuebleId && u.Activo);

        if (estado is not null)
        {
            consulta = consulta.Where(u => u.EstadoManual == estado);
        }

        return ConsultaPaginada.PaginarAsync(
            consulta, OrdenesUnidad, "nombre", paginacion, AUnidad, ct);
    }

    public async Task<UnidadRespuesta> CrearUnidadAsync(
        Guid inmuebleId,
        UnidadPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var inmueble = await CargarInmuebleAsync(inmuebleId, ct);

        var unidad = new Unidad
        {
            InmuebleId = inmueble.Id,
            Activo = true,
            // La propietaria del inmueble es la de la unidad mientras no se diga otra cosa.
            PropietariaId = peticion.PropietariaId ?? inmueble.PropietariaId,
        };

        AplicarUnidad(unidad, peticion, esAlta: true);

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Unidad, unidad,
            esAlta: true,
            accion: "unidad.creada",
            entidad: "unidad",
            usuario,
            ip,
            mutacion,
            proyectar: AUnidad,
            datosAuditoria: DatosAuditoriaUnidad,
            ct: ct);
    }

    public async Task<UnidadRespuesta> ActualizarUnidadAsync(
        Guid id,
        UnidadPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var unidad = await contexto.Unidad
            .Include(u => u.Modalidades)
            .Include(u => u.Tarifas)
            .SingleOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new KeyNotFoundException($"No existe la unidad '{id:D}'.");

        AplicarUnidad(unidad, peticion, esAlta: false);

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Unidad, unidad,
            esAlta: false,
            accion: "unidad.actualizada",
            entidad: "unidad",
            usuario,
            ip,
            mutacion,
            proyectar: AUnidad,
            datosAuditoria: DatosAuditoriaUnidad,
            ct: ct);
    }

    // ---------------------------------------------------------------- internos

    /// <summary>
    /// Fila proyectada de la lista de inmuebles. Es una clase con constructor vacío, y no un
    /// <c>record</c> posicional, porque EF Core solo traduce el orden si la proyección se construye
    /// con inicializador de propiedades (igual que en <see cref="ServicioClientes"/>).
    /// </summary>
    private sealed class InmuebleFila
    {
        public Guid Id { get; init; }

        public string Nombre { get; init; } = string.Empty;

        public string? Direccion { get; init; }

        public TipoInmueble Tipo { get; init; }

        public Guid? PropietariaId { get; init; }

        public string? PlantillaContratoPorDefecto { get; init; }

        public int? RenovacionDefectoMeses { get; init; }

        public bool Activo { get; init; }

        public DateTimeOffset CreatedAt { get; init; }
    }

    private static IQueryable<InmuebleFila> Proyectar(IQueryable<Inmueble> consulta) =>
        consulta.Select(i => new InmuebleFila
        {
            Id = i.Id,
            Nombre = i.Nombre,
            Direccion = i.Direccion,
            Tipo = i.Tipo,
            PropietariaId = i.PropietariaId,
            PlantillaContratoPorDefecto = i.PlantillaContratoPorDefecto,
            RenovacionDefectoMeses = i.RenovacionDefectoMeses,
            Activo = i.Activo,
            CreatedAt = i.CreatedAt,
        });

    private static InmuebleResumen AResumen(InmuebleFila fila) =>
        new(
            fila.Id,
            fila.Nombre,
            fila.Direccion,
            fila.Tipo,
            fila.PropietariaId,
            fila.PlantillaContratoPorDefecto,
            fila.RenovacionDefectoMeses,
            fila.Activo,
            fila.CreatedAt);

    private static InmuebleResumen AResumen(Inmueble inmueble) =>
        new(
            inmueble.Id,
            inmueble.Nombre,
            inmueble.Direccion,
            inmueble.Tipo,
            inmueble.PropietariaId,
            inmueble.PlantillaContratoPorDefecto,
            inmueble.RenovacionDefectoMeses,
            inmueble.Activo,
            inmueble.CreatedAt);

    private static UnidadRespuesta AUnidad(Unidad unidad) =>
        new(
            unidad.Id,
            unidad.InmuebleId,
            unidad.Nombre,
            unidad.Tipo,
            unidad.PropietariaId,
            unidad.FacturadoraPorDefectoId,
            unidad.EstadoManual,
            [.. unidad.Modalidades.Select(m => m.Modalidad).OrderBy(m => m)],
            [.. unidad.Tarifas
                .OrderBy(t => t.Modalidad)
                .ThenBy(t => t.Moneda)
                .Select(t => new TarifaRespuesta(t.Modalidad, t.Moneda, t.Importe))],
            unidad.MetrosCuadrados,
            unidad.Capacidad,
            unidad.Habitaciones,
            unidad.Banos,
            unidad.Medidor,
            unidad.Notas,
            unidad.Activo,
            unidad.CreatedAt);

    private static object DatosAuditoria(Inmueble inmueble) => new
    {
        inmueble.Nombre,
        Tipo = inmueble.Tipo.ToString(),
        inmueble.PropietariaId,
        inmueble.Activo,
    };

    private static object DatosAuditoriaUnidad(Unidad unidad) => new
    {
        unidad.Nombre,
        unidad.InmuebleId,
        Tipo = unidad.Tipo.ToString(),
        EstadoManual = unidad.EstadoManual.ToString(),
        Modalidades = unidad.Modalidades.Select(m => m.Modalidad.ToString()).Order().ToArray(),
        unidad.Activo,
    };

    private async Task<Inmueble> CargarInmuebleAsync(Guid id, CancellationToken ct) =>
        await contexto.Inmueble.FirstOrDefaultAsync(i => i.Id == id, ct)
        ?? throw new KeyNotFoundException($"No existe el inmueble '{id:D}'.");

    private static void Aplicar(Inmueble inmueble, InmueblePeticion peticion)
    {
        inmueble.Nombre = peticion.Nombre.Trim();
        inmueble.Direccion = Limpiar(peticion.Direccion);
        inmueble.Tipo = peticion.Tipo;
        inmueble.PropietariaId = peticion.PropietariaId;
        inmueble.Notas = Limpiar(peticion.Notas);
        inmueble.PlantillaContratoPorDefecto = Limpiar(peticion.PlantillaContratoPorDefecto);
        inmueble.RenovacionDefectoMeses = peticion.RenovacionDefectoMeses;
    }

    /// <summary>
    /// Aplica lo pedido a la unidad y deja las modalidades y las tarifas como están pedidas. Es un
    /// conjunto completo, no un parche: quien edita la unidad dice qué modalidades ofrece y qué
    /// tarifa tiene cada una.
    /// </summary>
    private static void AplicarUnidad(Unidad unidad, UnidadPeticion peticion, bool esAlta)
    {
        unidad.Nombre = peticion.Nombre.Trim();
        unidad.Tipo = peticion.Tipo;
        unidad.PropietariaId = peticion.PropietariaId;
        unidad.FacturadoraPorDefectoId = peticion.FacturadoraPorDefectoId;
        unidad.EstadoManual = peticion.EstadoManual;
        unidad.MetrosCuadrados = peticion.MetrosCuadrados;
        unidad.Capacidad = peticion.Capacidad;
        unidad.Habitaciones = peticion.Habitaciones;
        unidad.Banos = peticion.Banos;
        unidad.Medidor = Limpiar(peticion.Medidor);
        unidad.Notas = Limpiar(peticion.Notas);

        var modalidades = peticion.Modalidades.Distinct().ToArray();
        ExigirTarifasPermitidas(modalidades, peticion.Tarifas);

        if (!esAlta)
        {
            // Reemplazo del conjunto: se borra lo anterior y se inserta lo pedido. Las filas viejas
            // no se reusan, porque una modalidad que se quita deja de estar permitida.
            unidad.Modalidades.Clear();
            unidad.Tarifas.Clear();
        }

        foreach (var modalidad in modalidades.Order())
        {
            unidad.Modalidades.Add(new ModalidadUnidad { Modalidad = modalidad });
        }

        foreach (var tarifa in peticion.Tarifas)
        {
            unidad.Tarifas.Add(new TarifaUnidad
            {
                Modalidad = tarifa.Modalidad,
                Moneda = tarifa.Moneda,
                Importe = tarifa.Importe,
            });
        }
    }

    /// <summary>
    /// La regla que no se puede violar (docs/ESPECIFICACION.md §3.1): toda tarifa tiene que ser de
    /// una modalidad que la unidad ofrece, y no puede haber dos tarifas de lo mismo.
    /// </summary>
    private static void ExigirTarifasPermitidas(
        IReadOnlyList<ModalidadAlquiler> modalidades,
        IReadOnlyList<TarifaPeticion> tarifas)
    {
        foreach (var tarifa in tarifas)
        {
            if (!modalidades.Contains(tarifa.Modalidad))
            {
                throw new ArgumentException(
                    $"La unidad no ofrece la modalidad {tarifa.Modalidad}: no puede tener esa tarifa.");
            }

            if (tarifa.Importe < 0)
            {
                throw new ArgumentException("Una tarifa no puede ser negativa.");
            }
        }

        var repetida = tarifas
            .GroupBy(t => new { t.Modalidad, t.Moneda })
            .FirstOrDefault(grupo => grupo.Count() > 1);
        if (repetida is not null)
        {
            throw new ArgumentException(
                $"Hay dos tarifas para {repetida.Key.Modalidad} en {repetida.Key.Moneda}: una sola.");
        }
    }

    private static string? Limpiar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}