using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Fila que se proyecta en las listas y en la búsqueda por documento.
/// <para>
/// <strong>No trae el documento.</strong> La columna cifrada no está en la proyección, así que la
/// búsqueda por documento no la lee ni la descifra: solo compara <c>documento_hmac</c>
/// (docs/DOC_TECNICA.md §4.3).
/// </para>
/// <para>
/// Es una clase con constructor vacío y no un <c>record</c> posicional a propósito: EF Core solo
/// sabe filtrar y ordenar por los miembros de una proyección si esta se construye con inicializador
/// de propiedades. Con un <c>record</c> la ordenación deja de traducirse.
/// </para>
/// </summary>
public sealed class ClienteFila
{
    public Guid Id { get; init; }

    public int Numero { get; init; }

    public string Nombre { get; init; } = string.Empty;

    public TipoCliente Tipo { get; init; }

    public string? Email { get; init; }

    public string? Telefono { get; init; }

    public bool AgenteRetencion { get; init; }

    public bool EnGestionCobro { get; init; }

    public bool RecordatoriosPausados { get; init; }

    public bool Activo { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public string? DocumentoHmac { get; init; }
}

/// <summary>
/// Alta, cambio, baja lógica, lectura y búsqueda de clientes (docs/ESPECIFICACION.md §3.2).
/// <list type="bullet">
/// <item>El documento de identidad se cifra en reposo y se ve completo en la ficha (decisión D16).</item>
/// <item>
/// Buscar y detectar duplicados va por el índice ciego <c>documento_hmac</c>, con el HMAC del
/// documento normalizado: la tabla no se descifra para nada.
/// </item>
/// <item>El número sale de la secuencia <c>cliente_numero_seq</c> y nunca se reutiliza.</item>
/// <item>Nada se borra: la baja es lógica, porque un cliente con facturas tiene que poder leerse.</item>
/// </list>
/// <para>
/// TODO(A#): la ficha de la especificación §3.2 también lleva nacionalidad, idioma y canal
/// preferido. No están aquí porque esta tarea se limita al alta, la ficha cifrada y el índice
/// ciego; añadirlos es una columna por campo y el mismo patrón.
/// </para>
/// <para>
/// TODO(A#): la rotación de clave. <see cref="ServicioCifrado"/> solo conoce la versión que trae
/// la configuración, así que subir <c>Seguridad__CifradoVersion</c> sin reescribir lo cifrado con
/// la versión anterior deja los documentos viejos ilegibles. Cuando se rote habrá que mantener un
/// descifrado por versión y recifrar en segundo plano.
/// </para>
/// </summary>
public sealed class ServicioClientes(
    AtlasRentDbContext contexto,
    AuditoriaService auditoria,
    ServicioIdempotencia idempotencia,
    ServicioCifrado cifrado,
    IndiceCiegoDocumento indice,
    TimeProvider reloj)
{
    private static readonly Dictionary<string, Func<IQueryable<ClienteFila>, bool, IOrderedQueryable<ClienteFila>>>
        Ordenes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["numero"] = (q, desc) => desc ? q.OrderByDescending(c => c.Numero) : q.OrderBy(c => c.Numero),
            ["nombre"] = (q, desc) => desc ? q.OrderByDescending(c => c.Nombre) : q.OrderBy(c => c.Nombre),
            ["tipo"] = (q, desc) => desc ? q.OrderByDescending(c => c.Tipo) : q.OrderBy(c => c.Tipo),
            ["createdAt"] = (q, desc) => desc ? q.OrderByDescending(c => c.CreatedAt) : q.OrderBy(c => c.CreatedAt),
        };

    /// <summary>
    /// Consulta filtrada por el índice ciego. Es pública para que una prueba pueda comprobar que
    /// el SQL no toca la columna cifrada: esa es la prueba de que buscar no descifra.
    /// </summary>
    public IQueryable<ClienteFila> ConsultaPorDocumento(string documento)
    {
        var consulta = contexto.Cliente.AsNoTracking();
        var huella = indice.Huella(documento);

        // El filtro va sobre la entidad y la proyección al final: filtrar por una propiedad de un
        // tipo proyectado no lo sabe traducir EF.
        return huella is null
            ? Proyectar(consulta).Take(0)
            : Proyectar(consulta.Where(c => c.DocumentoHmac == huella));
    }

    public Task<Pagina<ClienteResumen>> ListarAsync(
        ConsultaLista paginacion,
        string? busqueda,
        string? documento,
        TipoCliente? tipo,
        bool incluirInactivos,
        CancellationToken ct)
    {
        var consulta = contexto.Cliente.AsNoTracking();

        if (!incluirInactivos)
        {
            consulta = consulta.Where(c => c.Activo);
        }

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            var termino = busqueda.Trim().ToLowerInvariant();
            consulta = consulta.Where(c => c.Nombre.ToLower().Contains(termino));
        }

        if (documento is not null)
        {
            // Índice ciego: se busca por la huella, no por el documento cifrado.
            var huella = indice.Huella(documento);
            if (huella is null)
            {
                return Task.FromResult(PaginaVacia(paginacion));
            }

            consulta = consulta.Where(c => c.DocumentoHmac == huella);
        }

        if (tipo is not null)
        {
            consulta = consulta.Where(c => c.Tipo == tipo);
        }

        return ConsultaPaginada.PaginarAsync(
            Proyectar(consulta), Ordenes, "numero", paginacion, AResumen, ct);
    }

    /// <summary>Ficha del cliente con el documento descifrado (decisión D16: se ve completo).</summary>
    public async Task<ClienteRespuesta> LeerAsync(Guid id, CancellationToken ct)
    {
        var cliente = await CargarAsync(id, ct);

        return new ClienteRespuesta(
            cliente.Id,
            cliente.Numero,
            cliente.Nombre,
            cliente.Tipo,
            Descifrar(cliente.Documento),
            cliente.Email,
            cliente.Telefono,
            cliente.Direccion,
            cliente.Notas,
            cliente.AgenteRetencion,
            cliente.EnGestionCobro,
            cliente.RecordatoriosPausados,
            cliente.Activo,
            cliente.CreatedAt);
    }

    public async Task<ClienteResumen> CrearAsync(
        ClientePeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var huella = indice.Huella(peticion.Documento);
        await ExigirDocumentoLibreAsync(huella, null, ct);

        var cliente = new Cliente();
        Aplicar(cliente, peticion, huella);
        cliente.Activo = true;

        // Solo la importación de Factusol trae número. Los clientes con efecto económico pueden
        // tener ese número, así que la secuencia tiene que quedar por encima de él o el siguiente
        // alta repetiría uno ya usado.
        var numeroImportado = peticion.Numero is { } numero && numero > 0;
        if (numeroImportado)
        {
            cliente.Numero = peticion.Numero!.Value;
        }
        else
        {
            cliente.Numero = (int)await SiguienteNumeroAsync(ct);
        }

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Cliente, cliente,
            esAlta: true,
            accion: "cliente.creado",
            entidad: "cliente",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: DatosAuditoria,
            despuesDeGuardar: numeroImportado ? () => AlinearSecuenciaAsync(ct) : null,
            ct: ct);
    }

    public async Task<ClienteResumen> ActualizarAsync(
        Guid id,
        ClientePeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var cliente = await CargarAsync(id, ct);

        var huella = indice.Huella(peticion.Documento);
        await ExigirDocumentoLibreAsync(huella, id, ct);

        Aplicar(cliente, peticion, huella);

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Cliente, cliente,
            esAlta: false,
            accion: "cliente.actualizado",
            entidad: "cliente",
            usuario,
            ip,
            mutacion,
            proyectar: AResumen,
            datosAuditoria: DatosAuditoria,
            ct: ct);
    }

    /// <summary>
    /// Baja lógica. Un cliente con facturas o contratos detrás no se borra nunca
    /// (docs/ESPECIFICACION.md §3.2).
    /// </summary>
    public async Task DesactivarAsync(
        Guid id,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var cliente = await CargarAsync(id, ct);
        cliente.Activo = false;

        await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.Cliente, cliente,
            esAlta: false,
            accion: "cliente.desactivado",
            entidad: "cliente",
            usuario,
            ip,
            mutacion,
            proyectar: c => AResumen(AFila(c)),
            datosAuditoria: DatosAuditoria,
            ct: ct);
    }

    /// <summary>
    /// Pausa o levanta los avisos de pago de este cliente (F4-05, §3.14 y D12).
    /// <para>
    /// Existe su propio endpoint y no solo el <c>PUT</c> general de la ficha porque la pausa se cambia
    /// sola y en el momento en que se la ve en un correo: mandar la ficha entera para mover una
    /// bandera es pedir que alguien la sobrescriba con lo que tenía la pantalla hace diez segundos.
    /// </para>
    /// </summary>
    public async Task<ClienteResumen> PausarRecordatoriosAsync(
        Guid id,
        bool pausados,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var cliente = await CargarAsync(id, ct);

        if (cliente.RecordatoriosPausados != pausados)
        {
            cliente.RecordatoriosPausados = pausados;

            return await GuardadoAuditado.GuardarAsync(
                contexto, auditoria, idempotencia, reloj,
                contexto.Cliente, cliente,
                esAlta: false,
                accion: pausados ? "cliente.recordatorios_pausados" : "cliente.recordatorios_reanudados",
                entidad: "cliente",
                usuario,
                ip,
                mutacion,
                proyectar: c => AResumen(AFila(c)),
                datosAuditoria: DatosAuditoria,
                ct: ct);
        }

        // Poner dos veces lo mismo no es un cambio: no se escribe una fila de auditoría ni se gasta la
        // clave de idempotencia en algo que no pasó.
        return AResumen(AFila(cliente));
    }

    // ---------------------------------------------------------------- internos

    /// <summary>
    /// Proyección de las listas. Deja fuera <c>Documento</c> a propósito: la consulta no llega a
    /// leer la columna cifrada.
    /// </summary>
    private static IQueryable<ClienteFila> Proyectar(IQueryable<Cliente> consulta) =>
        consulta.Select(c => new ClienteFila
        {
            Id = c.Id,
            Numero = c.Numero,
            Nombre = c.Nombre,
            Tipo = c.Tipo,
            Email = c.Email,
            Telefono = c.Telefono,
            AgenteRetencion = c.AgenteRetencion,
            EnGestionCobro = c.EnGestionCobro,
            RecordatoriosPausados = c.RecordatoriosPausados,
            Activo = c.Activo,
            CreatedAt = c.CreatedAt,
            DocumentoHmac = c.DocumentoHmac,
        });

    private static ClienteResumen AResumen(Cliente cliente) => AResumen(AFila(cliente));

    private static ClienteResumen AResumen(ClienteFila fila) =>
        new(
            fila.Id,
            fila.Numero,
            fila.Nombre,
            fila.Tipo,
            fila.Email,
            fila.Telefono,
            fila.AgenteRetencion,
            fila.EnGestionCobro,
            fila.RecordatoriosPausados,
            fila.Activo,
            fila.CreatedAt);

    private static ClienteFila AFila(Cliente cliente) =>
        new()
        {
            Id = cliente.Id,
            Numero = cliente.Numero,
            Nombre = cliente.Nombre,
            Tipo = cliente.Tipo,
            Email = cliente.Email,
            Telefono = cliente.Telefono,
            AgenteRetencion = cliente.AgenteRetencion,
            EnGestionCobro = cliente.EnGestionCobro,
            RecordatoriosPausados = cliente.RecordatoriosPausados,
            Activo = cliente.Activo,
            CreatedAt = cliente.CreatedAt,
            DocumentoHmac = cliente.DocumentoHmac,
        };

    /// <summary>Lo que sí puede ir a la auditoría: el documento nunca, ni cifrado ni en claro.</summary>
    private static object DatosAuditoria(Cliente cliente) => new
    {
        cliente.Numero,
        cliente.Nombre,
        Tipo = cliente.Tipo.ToString(),
        cliente.Activo,
    };

    private static Pagina<ClienteResumen> PaginaVacia(ConsultaLista paginacion) =>
        new([], 0, paginacion.Page, paginacion.PageSize, 0);

    private async Task<Cliente> CargarAsync(Guid id, CancellationToken ct) =>
        await contexto.Cliente.FirstOrDefaultAsync(c => c.Id == id, ct)
        ?? throw new KeyNotFoundException($"No existe el cliente '{id:D}'.");

    private void Aplicar(Cliente cliente, ClientePeticion peticion, string? huella)
    {
        cliente.Nombre = peticion.Nombre.Trim();
        cliente.Tipo = peticion.Tipo;
        cliente.Documento = string.IsNullOrWhiteSpace(peticion.Documento)
            ? null
            : cifrado.Cifrar(peticion.Documento.Trim());
        cliente.DocumentoHmac = huella;
        cliente.Email = Limpiar(peticion.Email);
        cliente.Telefono = Limpiar(peticion.Telefono);
        cliente.Direccion = Limpiar(peticion.Direccion);
        cliente.Notas = Limpiar(peticion.Notas);
        cliente.AgenteRetencion = peticion.AgenteRetencion;
        cliente.EnGestionCobro = peticion.EnGestionCobro;
        cliente.RecordatoriosPausados = peticion.RecordatoriosPausados;
    }

    private static string? Limpiar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private string? Descifrar(string? documentoCifrado) =>
        documentoCifrado is null ? null : cifrado.Descifrar(documentoCifrado);

    /// <summary>
    /// Como el HMAC es del documento <em>normalizado</em>, "001-1234567-8" y "00112345678"
    /// chocan aquí y no solo en el índice único. El índice único es la red por si acaso; esto da
    /// el mensaje que entiende quien está delante.
    /// </summary>
    private async Task ExigirDocumentoLibreAsync(string? huella, Guid? idActual, CancellationToken ct)
    {
        if (huella is null)
        {
            return;
        }

        var consulta = contexto.Cliente.Where(c => c.DocumentoHmac == huella);
        if (idActual is { } id)
        {
            consulta = consulta.Where(c => c.Id != id);
        }

        if (await consulta.AnyAsync(ct))
        {
            throw new InvalidOperationException(
                "Ya hay un cliente con ese documento de identidad.");
        }
    }

    /// <summary>
    /// El siguiente número de la secuencia. Es lo que garantiza que dos altas simultáneas no
    /// coger el mismo: PostgreSQL serializa el <c>nextval</c>.
    /// </summary>
    private async Task<long> SiguienteNumeroAsync(CancellationToken ct) =>
        await contexto.Database
            .SqlQuery<long>($"""
                SELECT nextval('cliente_numero_seq') AS "Value"
                """)
            .SingleAsync(ct);

    /// <summary>
    /// Deja la secuencia por encima del número más alto de la tabla, sin bajarla nunca: un
    /// <c>setval</c> hacia atrás haría que el siguiente alta repitiera un número ya usado.
    /// </summary>
    private async Task AlinearSecuenciaAsync(CancellationToken ct)
    {
        // Dos importaciones a la vez no se pisan: el candado dura lo que la transacción del alta.
        await contexto.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtext('cliente_numero_seq'))", ct);

        await contexto.Database
            .SqlQuery<long>(
                $"""
                SELECT setval(
                    'cliente_numero_seq',
                    GREATEST(
                        COALESCE((SELECT MAX(numero) FROM cliente), 0),
                        (SELECT last_value FROM cliente_numero_seq))) AS "Value"
                """)
            .SingleAsync(ct);
    }
}