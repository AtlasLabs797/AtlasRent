using AtlasRent.Api.Authorization;
using AtlasRent.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AtlasRent.Api.Data;

/// <summary>
/// Contexto de Atlas Rent. La nomenclatura <c>snake_case</c> la pone
/// <c>UseSnakeCaseNamingConvention()</c> (docs/DOC_TECNICA.md §4.2).
/// <para>
/// F1-03 deja solo <see cref="Auditoria"/>; las entidades de usuarios, contratos y facturas
/// llegan en F1-04 y siguientes.
/// </para>
/// </summary>
public sealed class AtlasRentDbContext(DbContextOptions<AtlasRentDbContext> opciones) : DbContext(opciones)
{
    public DbSet<Auditoria> Auditoria => Set<Auditoria>();

    public DbSet<Usuario> Usuario => Set<Usuario>();

    public DbSet<Sesion> Sesion => Set<Sesion>();

    public DbSet<IntentoLogin> IntentoLogin => Set<IntentoLogin>();

    public DbSet<BloqueoIp> BloqueoIp => Set<BloqueoIp>();

    public DbSet<Alerta> Alerta => Set<Alerta>();

    public DbSet<MfaDesafio> MfaDesafio => Set<MfaDesafio>();

    public DbSet<CodigoRecuperacion> CodigoRecuperacion => Set<CodigoRecuperacion>();

    public DbSet<AccesoEmergencia> AccesoEmergencia => Set<AccesoEmergencia>();

    public DbSet<Rol> Rol => Set<Rol>();

    public DbSet<PermisoRol> PermisoRol => Set<PermisoRol>();

    public DbSet<UsuarioRol> UsuarioRol => Set<UsuarioRol>();

    public DbSet<UsuarioPermiso> UsuarioPermiso => Set<UsuarioPermiso>();

    // F6-01: credenciales de los agentes externos. La lista de acciones va en la fila; el techo, en
    // el usuario propietario.
    public DbSet<AgenteToken> AgenteToken => Set<AgenteToken>();

    // F1-08: configuración. Los porcentajes de ITBIS y retención son filas de estas tablas, no
    // constantes del código (D14).
    public DbSet<Serie> Serie => Set<Serie>();

    public DbSet<NumeroSerie> NumeroSerie => Set<NumeroSerie>();

    public DbSet<NumeradorFiscal> NumeradorFiscal => Set<NumeradorFiscal>();

    public DbSet<ComprobanteFiscal> ComprobanteFiscal => Set<ComprobanteFiscal>();

    public DbSet<ComprobanteRetencion> ComprobanteRetencion => Set<ComprobanteRetencion>();

    public DbSet<TipoItbis> TipoItbis => Set<TipoItbis>();

    public DbSet<TipoRetencion> TipoRetencion => Set<TipoRetencion>();

    public DbSet<FormaPago> FormaPago => Set<FormaPago>();

    public DbSet<CuentaReceptora> CuentaReceptora => Set<CuentaReceptora>();

    public DbSet<Subempresa> Subempresa => Set<Subempresa>();

    public DbSet<AjusteGeneral> AjusteGeneral => Set<AjusteGeneral>();

    public DbSet<Idempotencia> Idempotencia => Set<Idempotencia>();

    public DbSet<Cliente> Cliente => Set<Cliente>();

    public DbSet<Prospecto> Prospecto => Set<Prospecto>();

    public DbSet<ContactoCliente> ContactoCliente => Set<ContactoCliente>();

    // F1-10: inmuebles y unidades con sus modalidades y tarifas, y los documentos PDF de
    // propiedad. Los archivos de documento no están aquí: viven fuera del webroot y se sirven por
    // la API (docs/SEGURIDAD.md SEC-004).
    public DbSet<Inmueble> Inmueble => Set<Inmueble>();

    public DbSet<Unidad> Unidad => Set<Unidad>();

    public DbSet<ModalidadUnidad> ModalidadUnidad => Set<ModalidadUnidad>();

    public DbSet<TarifaUnidad> TarifaUnidad => Set<TarifaUnidad>();

    public DbSet<DocumentoInmueble> DocumentoInmueble => Set<DocumentoInmueble>();

    // F2-01: disponibilidad. La ocupación no es un campo de la unidad sino estas dos tablas, que la
    // base de datos mantiene coherentes por sí sola (docs/ESPECIFICACION.md §3.4 y §5).
    public DbSet<OcupacionRango> OcupacionRango => Set<OcupacionRango>();

    public DbSet<OcupacionDia> OcupacionDia => Set<OcupacionDia>();

    // F2-02: cotizaciones y sus líneas (docs/ESPECIFICACION.md §3.3). El destinatario es un cliente
    // o un prospecto, nunca los dos ni ninguno, y la unidad es opcional.
    public DbSet<Cotizacion> Cotizacion => Set<Cotizacion>();

    public DbSet<CotizacionLinea> CotizacionLinea => Set<CotizacionLinea>();

    // F2-03: reservas y sus días (docs/ESPECIFICACION.md §3.4). El bloque de la unidad lo escriben
    // las tablas de ocupación de F2-01, no estas: reserva es el negocio y ocupacion el candado.
    public DbSet<Reserva> Reserva => Set<Reserva>();

    public DbSet<ReservaDia> ReservaDia => Set<ReservaDia>();

    // F2-05: contratos, su historial de versiones y las solicitudes de aprobación
    // (docs/ESPECIFICACION.md §3.5). Los tres son de solo añadir: nada de esto se borra.
    public DbSet<Contrato> Contrato => Set<Contrato>();

    public DbSet<ContratoVersion> ContratoVersion => Set<ContratoVersion>();

    public DbSet<SolicitudAprobacion> SolicitudAprobacion => Set<SolicitudAprobacion>();

    // F2-06: bloques de renovación e incrementos de renta (docs/ESPECIFICACION.md §3.5). También
    // de solo añadir: el bloque es historial y el incremento es valor económico.
    public DbSet<ContratoBloque> ContratoBloque => Set<ContratoBloque>();

    public DbSet<ContratoIncremento> ContratoIncremento => Set<ContratoIncremento>();

    // F2-07: catálogo de plantillas de contrato y de anexos, y la copia congelada de cada contrato
    // aprobado (docs/ESPECIFICACION.md §3.5). Los tres son de solo añadir y de desactivar, nunca de
    // borrar: la copia congelada es lo que dice el contrato firmado.
    public DbSet<PlantillaContrato> PlantillaContrato => Set<PlantillaContrato>();

    public DbSet<PlantillaAnexo> PlantillaAnexo => Set<PlantillaAnexo>();

    public DbSet<ContratoPlantillaCongelada> ContratoPlantillaCongelada => Set<ContratoPlantillaCongelada>();

    // F2-08: inventario. Son tres tablas y no una porque son tres momentos distintos
    // (docs/ESPECIFICACION.md §3.1, §3.5 y §3.6): el catálogo vivo de la unidad, la copia congelada
    // que el contrato firma al activar y la revisión que se firma cuando el cliente se va. Ninguna se
    // borra: el base se desactiva, la copia no se reescribe y la revisión firmada no se vuelve a
    // firmar.
    public DbSet<InventarioBaseItem> InventarioBaseItem => Set<InventarioBaseItem>();

    public DbSet<ContratoInventarioItem> ContratoInventarioItem => Set<ContratoInventarioItem>();

    public DbSet<RevisionInventario> RevisionInventario => Set<RevisionInventario>();

    public DbSet<RevisionInventarioItem> RevisionInventarioItem => Set<RevisionInventarioItem>();

    public DbSet<ContratoLiquidacion> ContratoLiquidacion => Set<ContratoLiquidacion>();

    // F3-01: el tipo de cambio diario (docs/ESPECIFICACION.md §3.12). Tabla nueva de F3 y no una
    // columna de configuración: el valor se congela al emitir y al cobrar, así que hay que poder
    // leer el de un día pasado, que es justo lo que una fila de configuración no da.
    public DbSet<TipoCambio> TipoCambio => Set<TipoCambio>();

    // F3-02: facturas (y proformas, que son facturas de serie proforma) con sus líneas
    // (docs/ESPECIFICACION.md §3.8). Una sola tabla para las dos: lo que las distingue es el tipo
    // de la serie, no el sitio donde viven.
    public DbSet<Factura> Factura => Set<Factura>();

    public DbSet<FacturaLinea> FacturaLinea => Set<FacturaLinea>();

    // F3-06: cobros y sus aplicaciones a facturas (docs/ESPECIFICACION.md §3.10.1). El saldo a favor
    // es una tabla y no una columna de contrato porque tiene moneda propia: es crédito en la moneda
    // en que se cobró de más, y una factura nunca se convierte (D7).
    public DbSet<Cobro> Cobro => Set<Cobro>();

    public DbSet<CobroAsignacion> CobroAsignacion => Set<CobroAsignacion>();

    public DbSet<SaldoAFavor> SaldoAFavor => Set<SaldoAFavor>();

    // F3-07: el ajuste por diferencia de cambio (§3.10.2, D8). Tabla aparte de las asignaciones
    // porque no es dinero recibido: es el movimiento que cierra el céntimo que dejó el cambio, y
    // por eso nunca se suma dentro de «cobrado».
    public DbSet<AjusteDiferenciaCambio> AjusteDiferenciaCambio => Set<AjusteDiferenciaCambio>();

    // F3-09: fianzas (docs/ESPECIFICACION.md §3.11). El cobro de una fianza no es tabla propia: es una
    // asignación de `cobro_asignacion` que apunta aquí en vez de a una factura, para que el dinero que
    // entra tenga un solo camino. `fianza` guarda lo que el contrato exige y la decisión que se tomó
    // sobre ella; lo cobrado y lo que queda se calculan desde `cobro_asignacion` y `fianza_devolucion`
    // para que ningún importe de la tabla pueda desincronizarse del movimiento que lo produjo.
    public DbSet<Fianza> Fianza => Set<Fianza>();

    public DbSet<FianzaDevolucion> FianzaDevolucion => Set<FianzaDevolucion>();

    // F4-02: los correos con documento adjunto y la copia exacta del PDF que salió
    // (docs/ESPECIFICACION.md §3.14). `envio` no tiene clave foránea a ningún documento y eso es a
    // propósito: apunta a facturas, recibos, estados de cuenta, cotizaciones y contratos, que están en
    // cuatro tablas distintas. Ver `Data/Envio.cs`.
    public DbSet<Envio> Envio => Set<Envio>();

    // F4-02: el texto de los correos por tipo de mensaje e idioma. Se edita con `plantilla.editar`
    // (solo admin, §2) y se desactiva en vez de borrarse, para que un envío antiguo siga teniendo
    // explicación.
    public DbSet<PlantillaMensaje> PlantillaMensaje => Set<PlantillaMensaje>();

    // F4-05: el aviso de pago por cliente y unidad (§3.14, D12). No es un documento: no tiene
    // número ni archivo, y su clave es (cliente, unidad, fecha), que es la que hace que correr el
    // trabajo dos veces el mismo día no mande dos avisos. Ver `Data/Recordatorio.cs`.
    public DbSet<Recordatorio> Recordatorio => Set<Recordatorio>();

    protected override void ConfigureConventions(ModelConfigurationBuilder convenciones)
    {
        // AGENTS.md §6: fechas de negocio en date, marcas de tiempo en timestamptz, dinero en numeric.
        convenciones.Properties<DateTime>().HaveColumnType("timestamp with time zone");
        convenciones.Properties<DateTimeOffset>().HaveColumnType("timestamp with time zone");
        convenciones.Properties<DateOnly>().HaveColumnType("date");
        convenciones.Properties<Guid>().HaveColumnType("uuid");
        // El tipo de cambio (numeric(18,6)) se configura en su entidad: lo explícito gana a la convención.
        convenciones.Properties<decimal>().HaveColumnType("numeric(18,2)");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Auditoria>(entidad =>
        {
            entidad.ToTable("auditoria");
            entidad.HasKey(e => e.Id);
            // Columnas de texto sin límite fijo (docs/DOC_TECNICA.md §4.3): los límites
            // de longitud los fija cada dominio, no esta base.
            entidad.Property(e => e.Accion).IsRequired();
            entidad.Property(e => e.Datos).HasColumnType("jsonb");
            entidad.Property(e => e.Ip).IsRequired();
            entidad.Property(e => e.Firma).IsRequired();
            entidad.Property(e => e.FirmaAnterior).IsRequired();
            entidad.Property(e => e.Secuencia).HasColumnName("secuencia");
            // La app fija created_at para que coincida con lo firmado; el default de la tabla
            // solo cubre los inserts hechos a mano (script, job, consola).
            entidad.Property(e => e.CreatedAt).HasColumnName("created_at");
            entidad.HasIndex(e => e.Secuencia).IsUnique();
            entidad.HasIndex(e => e.CreatedAt);
        });

        // F1-04: identidad y sesiones.
        modelBuilder.Entity<Usuario>(entidad =>
        {
            entidad.ToTable("usuario");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.NombreAcceso).IsRequired();
            entidad.Property(e => e.PasswordHash).IsRequired();
            entidad.Property(e => e.CreatedAt).IsRequired();
            // Único también entre usuarios borrados lógicamente: reutilizar el nombre de acceso de
            // alguien dado de baja haría más fácil la suplantación.
            entidad.HasIndex(e => e.NombreAcceso).IsUnique();
        });

        modelBuilder.Entity<Sesion>(entidad =>
        {
            entidad.ToTable("sesion");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.TokenHash).IsRequired();
            entidad.Property(e => e.Ip).IsRequired();
            entidad.Property(e => e.EmitidaAt).IsRequired();
            entidad.Property(e => e.UltimaActividad).IsRequired();
            entidad.Property(e => e.CaducaAbsoluta).IsRequired();
            // La búsqueda del token de refresco es por hash y tiene que ser única.
            entidad.HasIndex(e => e.TokenHash).IsUnique();
            // Revocar una familia entera y listar las sesiones de un usuario.
            entidad.HasIndex(e => e.FamiliaId);
            entidad.HasIndex(e => e.UsuarioId);
            entidad.HasOne(e => e.Usuario)
                .WithMany()
                .HasForeignKey(e => e.UsuarioId);
        });

        // F1-05: intentos de login y bloqueos de IP. El contador de fallos vive en intento_login,
        // no en una columna de contador de usuario: una sola fuente de verdad.
        modelBuilder.Entity<IntentoLogin>(entidad =>
        {
            entidad.ToTable("intento_login");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Ip).IsRequired();
            entidad.Property(e => e.OcurridoAt).IsRequired();
            // Se cuenta por IP dentro de una ventana y por usuario desde el último acierto.
            entidad.HasIndex(e => new { e.Ip, e.OcurridoAt });
            entidad.HasIndex(e => new { e.UsuarioId, e.Exito });
            entidad.HasOne(e => e.Usuario)
                .WithMany()
                .HasForeignKey(e => e.UsuarioId);
        });

        // F1-12: registro de lo que falló y que hay que mirar. La unicidad está porque el criterio
        // de aceptación de F1-12 es "fallo → alerta" sin duplicar: si el backup falla cada hora, sin
        // esa restricción la tabla crece y la señal desaparece.
        //
        // BUG-008: la unicidad es **parcial y sobre (tipo, clave)**, no sobre `clave` a secas. Con un
        // único único por `clave`, una alerta cerrada bloqueaba su propia clave para siempre y el
        // `ON CONFLICT ... WHERE resuelta_at IS NULL` no insertaba ni resucitaba nada: un problema que
        // aparecía, se cerraba y volvía a aparecer no volvía a avisar, al revés de "alerta
        // persistente" (§3.16) y de AGENTS.md §6. Filtrando a `resuelta_at IS NULL` la restricción
        // solo rige las alertas vivas —que es lo único que no puede duplicarse— y deja que la misma
        // clave vuelva a levantarse una vez resuelta. Va con `tipo` delante porque la identidad de un
        // fallo es «de qué tipo y con qué clave»: dos tipos distintos que coinciden en la clave son
        // dos hechos, no uno (prueba `FallosDeTiposDistintosSonAlertasDistintas`).
        modelBuilder.Entity<Alerta>(entidad =>
        {
            entidad.ToTable("alerta");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Clave).IsRequired();
            entidad.Property(e => e.Tipo).IsRequired();
            entidad.Property(e => e.Mensaje).IsRequired();
            entidad.Property(e => e.OcurridoAt).IsRequired();
            entidad.HasIndex(e => new { e.Tipo, e.Clave })
                .IsUnique()
                .HasFilter("resuelta_at IS NULL");
            entidad.HasIndex(e => new { e.Tipo, e.OcurridoAt });
        });

        modelBuilder.Entity<BloqueoIp>(entidad =>
        {
            entidad.ToTable("bloqueo_ip");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Ip).IsRequired();
            entidad.Property(e => e.CreatedAt).IsRequired();
            entidad.Property(e => e.UpdatedAt).IsRequired();
            // Una fila por IP: es la búsqueda que hace el middleware en cada petición.
            entidad.HasIndex(e => e.Ip).IsUnique();
        });

        // F1-06: segundo factor. El desafío es el estado del token provisional del login en dos
        // pasos: un solo uso y con caducidad corta, así que no es una sesión más.
        modelBuilder.Entity<MfaDesafio>(entidad =>
        {
            entidad.ToTable("mfa_desafio");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.TokenHash).IsRequired();
            entidad.Property(e => e.Ip).IsRequired();
            entidad.Property(e => e.EmitidoAt).IsRequired();
            entidad.Property(e => e.CaducaAt).IsRequired();
            // Único: es la búsqueda del token, igual que con el token de refresco.
            entidad.HasIndex(e => e.TokenHash).IsUnique();
            entidad.HasIndex(e => e.UsuarioId);
            entidad.HasOne(e => e.Usuario)
                .WithMany()
                .HasForeignKey(e => e.UsuarioId);
        });

        // El código en claro solo existe en la respuesta que lo muestra una vez; aquí, su hash.
        modelBuilder.Entity<CodigoRecuperacion>(entidad =>
        {
            entidad.ToTable("codigo_recuperacion");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.CodigoHash).IsRequired();
            entidad.Property(e => e.CreadoAt).IsRequired();
            // La búsqueda es por usuario y hash, y un mismo código no puede repetirse.
            entidad.HasIndex(e => new { e.UsuarioId, e.CodigoHash }).IsUnique();
            entidad.HasOne(e => e.Usuario)
                .WithMany()
                .HasForeignKey(e => e.UsuarioId);
        });

        modelBuilder.Entity<AccesoEmergencia>(entidad =>
        {
            entidad.ToTable("acceso_emergencia");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Motivo).IsRequired();
            entidad.Property(e => e.Ip).IsRequired();
            entidad.Property(e => e.CreadoAt).IsRequired();
            entidad.HasIndex(e => e.UsuarioId);
            entidad.HasIndex(e => e.SolicitanteId);
            // Tres referencias a usuario (a quién se le da, quién lo pidió y quién lo aprobó): sin
            // navegación para que EF no las confunda entre sí.
            entidad.HasOne<Usuario>().WithMany().HasForeignKey(e => e.UsuarioId);
            entidad.HasOne<Usuario>().WithMany().HasForeignKey(e => e.SolicitanteId);
            entidad.HasOne<Usuario>().WithMany().HasForeignKey(e => e.AprobadoPorId);
        });

        // F1-07: roles y permisos. La matriz de docs/ESPECIFICACION.md §2 va como datos sembrados,
        // no como un switch en el código: cambiarla es una migración con registro.
        modelBuilder.Entity<Rol>(entidad =>
        {
            entidad.ToTable("rol");
            entidad.HasKey(e => e.Clave);
            entidad.Property(e => e.Clave).HasMaxLength(32);
            entidad.Property(e => e.Nombre).IsRequired();
            entidad.HasData(
                MatrizPermisos.PorRol.Keys.Select(clave => new Rol
                {
                    Clave = clave,
                    Nombre = MatrizPermisos.Nombres[clave],
                }));
        });

        modelBuilder.Entity<PermisoRol>(entidad =>
        {
            entidad.ToTable("permiso_rol");
            entidad.HasKey(e => new { e.RolClave, e.Accion });
            entidad.Property(e => e.Accion).HasMaxLength(64);
            entidad.HasOne<Rol>()
                .WithMany()
                .HasForeignKey(e => e.RolClave);
            entidad.HasData(MatrizPermisos.PorRol
                .SelectMany(techo => techo.Value.Select(accion => new PermisoRol
                {
                    RolClave = techo.Key,
                    Accion = accion,
                })));
        });

        modelBuilder.Entity<UsuarioRol>(entidad =>
        {
            entidad.ToTable("usuario_rol");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.CreatedAt).IsRequired();
            // Mismo tipo que rol.clave: es su clave ajena.
            entidad.Property(e => e.RolClave).HasMaxLength(32);
            // Un usuario tiene un solo rol: es lo que lee el pipeline y lo que pasa a app.rol.
            entidad.HasIndex(e => e.UsuarioId).IsUnique();
            entidad.HasOne(e => e.Usuario)
                .WithOne(e => e.UsuarioRol)
                .HasForeignKey<UsuarioRol>(e => e.UsuarioId);
        });

        modelBuilder.Entity<UsuarioPermiso>(entidad =>
        {
            entidad.ToTable("usuario_permiso");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Accion).HasMaxLength(64);
            entidad.Property(e => e.CreatedAt).IsRequired();
            // Solo se guarda lo que difiere del techo, y una sola vez por acción.
            entidad.HasIndex(e => new { e.UsuarioId, e.Accion }).IsUnique();
            entidad.HasOne<Usuario>()
                .WithMany()
                .HasForeignKey(e => e.UsuarioId);
        });

        // F6-01: tokens de agente. Corre junto a los permisos porque el token no es una credencial
        // suelta: vale lo que valga su propietario, y su lista de acciones es un recorte de ese
        // techo. Nunca se borra: revocar es poner la fecha (AGENTS.md §6).
        modelBuilder.Entity<AgenteToken>(entidad =>
        {
            entidad.ToTable("agente_token");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Nombre).IsRequired();
            // SHA-256 en hexadecimal, como el hash del token de refresco.
            entidad.Property(e => e.HashToken).IsRequired().HasMaxLength(64);
            entidad.Property(e => e.Acciones).HasColumnType("text[]").IsRequired();
            entidad.Property(e => e.Creado).IsRequired();
            // Único: es la búsqueda del token en cada petición.
            entidad.HasIndex(e => e.HashToken).IsUnique();
            entidad.HasIndex(e => e.UsuarioId);
            entidad.HasOne(e => e.Usuario)
                .WithMany()
                .HasForeignKey(e => e.UsuarioId);
        });

        // F1-08: series y numeración. El contador de la serie lo mueve el UPDATE ... RETURNING de
        // ServicioSeries, dentro de la transacción de emisión; numero_serie guarda qué números se
        // han emitido y cuáles están anulados, sin liberar nunca ninguno (docs/DOC_TECNICA.md §4.3).
        modelBuilder.Entity<Serie>(entidad =>
        {
            entidad.ToTable("serie");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Prefijo).HasMaxLength(16);
            entidad.Property(e => e.PlantillaPdf).HasMaxLength(120);

            // F4-01: las condiciones de pago del pie son un párrafo, no una etiqueta. Anulable porque
            // una serie puede no tenerlas escritas y el pie sale sin ese párrafo.
            entidad.Property(e => e.CondicionesPago).HasMaxLength(500);
            entidad.Property(e => e.CreatedAt).IsRequired();
            // El número de serie y el prefijo identifican la serie para el usuario: los dos únicos.
            entidad.HasIndex(e => e.Numero).IsUnique();
            entidad.HasIndex(e => e.Prefijo).IsUnique();
        });

        modelBuilder.Entity<NumeroSerie>(entidad =>
        {
            entidad.ToTable("numero_serie");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.MotivoAnulacion).HasMaxLength(500);
            entidad.Property(e => e.EmitidoAt).IsRequired();
            // La unicidad (serie, numero) es la red de seguridad del contador: si dos emisiones
            // simultáneas salieran con el mismo número, la segunda no llegaría a commitear.
            entidad.HasIndex(e => new { e.SerieId, e.Numero }).IsUnique();
            entidad.HasIndex(e => e.SerieId);
        });

        // F3-03: el numerador del comprobante fiscal (docs/ESPECIFICACION.md §3.7). Es 1:1 con la
        // serie, y su contador es otro contador: lo mueve ServicioComprobantes con su propio
        // UPDATE ... RETURNING, dentro de la misma transacción de emisión, y nunca MAX() + 1.
        modelBuilder.Entity<NumeradorFiscal>(entidad =>
        {
            entidad.ToTable("numerador_fiscal", tabla =>
            {
                // Un rango autorizado es un intervalo, y el umbral es un número de números: si
                // admitieran valores que no lo son, la comprobación de "agotado" y "pocos" dejaría de
                // tener sentido. La coherencia del rango (que SiguienteNumero caiga dentro) la
                // comprueba el servicio al guardar, porque depende del valor anterior.
                tabla.HasCheckConstraint("ck_numerador_fiscal_rango", "rango_desde <= rango_hasta");
                tabla.HasCheckConstraint(
                    "ck_numerador_fiscal_siguiente_en_rango",
                    "siguiente_numero >= rango_desde AND siguiente_numero <= rango_hasta + 1");
                tabla.HasCheckConstraint("ck_numerador_fiscal_umbral", "umbral_alerta > 0");
            });
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.TipoPrefijo).HasMaxLength(16).IsRequired();
            // Un numerador por serie: dos numeradores para la misma serie harían que el número de
            // comprobante saliera de dos contadores distintos y el índice único de comprobante_fiscal
            // lo convertiría en un fallo en vez de en una decisión.
            entidad.HasIndex(e => e.SerieId).IsUnique();
            entidad.HasOne(e => e.Serie)
                .WithOne()
                .HasForeignKey<NumeradorFiscal>(e => e.SerieId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // F3-03: el comprobante que sale con cada factura de serie fiscal. No se borra nunca (D28),
        // así que la tabla no lleva DELETE en los permisos.
        modelBuilder.Entity<ComprobanteFiscal>(entidad =>
        {
            entidad.ToTable("comprobante_fiscal", tabla =>
            {
                // Anulado o emitido, y siempre con motivo y con quién: una anulación sin explicación
                // no dice nada. Las columnas las rellena F3-08.
                tabla.HasCheckConstraint(
                    "ck_comprobante_fiscal_anulacion_completa",
                    "estado = 1 OR (anulado_at IS NOT NULL AND anulado_por IS NOT NULL AND motivo_anulacion IS NOT NULL)");
            });
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Tipo).HasMaxLength(16).IsRequired();
            entidad.Property(e => e.MotivoAnulacion).HasMaxLength(500);
            entidad.Property(e => e.RegistradoAt).IsRequired();
            // Una factura, un comprobante.
            entidad.HasIndex(e => e.FacturaId).IsUnique();
            // La red de seguridad del contador, igual que (serie_id, numero) en numero_serie: si dos
            // emisiones simultáneas sacaran el mismo número de comprobante, la segunda no llega a
            // commitear.
            entidad.HasIndex(e => new { e.SerieId, e.Numero }).IsUnique();
            entidad.HasOne(e => e.Factura)
                .WithOne()
                .HasForeignKey<ComprobanteFiscal>(e => e.FacturaId)
                .OnDelete(DeleteBehavior.Restrict);
            entidad.HasOne(e => e.Serie)
                .WithMany()
                .HasForeignKey(e => e.SerieId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // F1-08: D14. Los porcentajes son datos y van con cuatro decimales, que es lo que
        // redondea Redondeo.Porcentaje. No hay ninguna cifra fiscal en el código.
        modelBuilder.Entity<TipoItbis>(entidad =>
        {
            entidad.ToTable("tipo_itbis");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Nombre).HasMaxLength(120).IsRequired();
            entidad.Property(e => e.Porcentaje).HasColumnType("numeric(18,4)");
            entidad.Property(e => e.CreatedAt).IsRequired();
            entidad.HasIndex(e => e.Nombre);
        });

        modelBuilder.Entity<TipoRetencion>(entidad =>
        {
            entidad.ToTable("tipo_retencion");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Nombre).HasMaxLength(120).IsRequired();
            entidad.Property(e => e.Porcentaje).HasColumnType("numeric(18,4)");
            entidad.Property(e => e.CreatedAt).IsRequired();
            entidad.HasIndex(e => e.Nombre);
            // Solo un tipo de retención puede ser el de por defecto. El servicio además le quita
            // el distintivo al anterior, así que este índice casi nunca se nota: está para que dos
            // administradores a la vez no dejen la tabla con dos por defecto.
            entidad.HasIndex(e => e.EsDefecto)
                .IsUnique()
                .HasFilter("(es_defecto = true)");
        });

        // F3-04 (D14): el comprobante que el cliente de agente de retención entrega tras la emisión.
        // Uno por factura y no se borra nunca, porque es la prueba de que se retuvo lo que dijo
        // retener, así que la tabla tampoco lleva DELETE en los permisos.
        modelBuilder.Entity<ComprobanteRetencion>(entidad =>
        {
            entidad.ToTable("comprobante_retencion", tabla =>
            {
                // Recibido o pendiente, y sin medios estados: o hay número, fecha y quién lo recibió,
                // o no hay nada. Sin esta regla, "pendiente" con un número puesto significa que
                // alguien empezó a rellenarlo y se quedó, que es indistinguible de que llegó.
                tabla.HasCheckConstraint(
                    "ck_comprobante_retencion_recibido_completo",
                    "(estado = 1) = (numero IS NULL AND fecha_recepcion IS NULL AND recibido_at IS NULL AND recibido_por IS NULL)");
                // El importe retenido es positivo y el porcentaje no es negativo: los dos son
                // magnitudes, el signo lo lleva la línea de factura.
                tabla.HasCheckConstraint("ck_comprobante_retencion_importe", "importe > 0");
                tabla.HasCheckConstraint("ck_comprobante_retencion_porcentaje", "porcentaje >= 0");
            });
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Tipo).HasMaxLength(160).IsRequired();
            // El porcentaje, como en tipo_itbis, es un porcentaje y no dinero.
            entidad.Property(e => e.Porcentaje).HasColumnType("numeric(18,4)");
            entidad.Property(e => e.CreatedAt).IsRequired();
            // El número lo teclea quien recibe el comprobante y no se valida contra nada (D6, D14),
            // pero sigue siendo texto libre de una persona: se limita para que no quepa un documento
            // entero dentro de una fila.
            entidad.Property(e => e.Numero).HasMaxLength(120);
            // Una factura, un comprobante. El índice único es también la red de seguridad contra
            // una doble emisión que soltara dos comprobantes para la misma factura.
            entidad.HasIndex(e => e.FacturaId).IsUnique();
            // El trabajo diario busca por estado, que es toda su consulta.
            entidad.HasIndex(e => e.Estado);
            entidad.HasOne(e => e.Factura)
                .WithOne()
                .HasForeignKey<ComprobanteRetencion>(e => e.FacturaId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FormaPago>(entidad =>
        {
            entidad.ToTable("forma_pago");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Nombre).HasMaxLength(120).IsRequired();
            entidad.Property(e => e.CreatedAt).IsRequired();
            entidad.HasIndex(e => e.Nombre);
        });

        modelBuilder.Entity<CuentaReceptora>(entidad =>
        {
            entidad.ToTable("cuenta_receptora");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Nombre).HasMaxLength(120).IsRequired();
            entidad.Property(e => e.Banco).HasMaxLength(120);
            entidad.Property(e => e.CreatedAt).IsRequired();
            entidad.HasIndex(e => e.Nombre);
        });

        modelBuilder.Entity<Subempresa>(entidad =>
        {
            entidad.ToTable("subempresa");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Nombre).HasMaxLength(120).IsRequired();

            // F4-01: datos de cabecera de la empresa facturadora (docs/DESIGN.md §7). Los tamaños
            // son los de `cliente`, que es donde ya están decididos. Los cuatro son anulables: una
            // subempresa dada de alta por importación puede no tener contacto todavía, y el PDF
            // imprime los que hay.
            // El RNC va cifrado (AGENTS.md §6) y por eso no lleva `HasMaxLength`: su longitud es la
            // del texto cifrado, no la del RNC. Es lo mismo que hace `cliente.documento`, que sale
            // como `text`.
            entidad.Property(e => e.Direccion).HasMaxLength(300);
            entidad.Property(e => e.Telefono).HasMaxLength(40);
            entidad.Property(e => e.Email).HasMaxLength(200);
            entidad.Property(e => e.CreatedAt).IsRequired();
            entidad.HasIndex(e => e.Nombre);
        });

        modelBuilder.Entity<AjusteGeneral>(entidad =>
        {
            entidad.ToTable("ajuste_general");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.CreatedAt).IsRequired();
        });

        modelBuilder.Entity<Idempotencia>(entidad =>
        {
            entidad.ToTable("idempotencia");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Clave).HasMaxLength(128).IsRequired();
            entidad.Property(e => e.Ruta).HasMaxLength(256).IsRequired();
            entidad.Property(e => e.Huella).HasMaxLength(64).IsRequired();
            entidad.Property(e => e.Respuesta).HasColumnType("jsonb").IsRequired();
            entidad.Property(e => e.CreatedAt).IsRequired();
            // La clave es del usuario: dos personas distintas pueden usar la misma cadena.
            entidad.HasIndex(e => new { e.UsuarioId, e.Clave }).IsUnique();
        });

        modelBuilder.Entity<Cliente>(entidad =>
        {
            entidad.ToTable("cliente");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Nombre).HasMaxLength(200).IsRequired();
            entidad.Property(e => e.Email).HasMaxLength(200);
            entidad.Property(e => e.Telefono).HasMaxLength(40);
            entidad.Property(e => e.Direccion).HasMaxLength(300);
            entidad.Property(e => e.Notas).HasMaxLength(1000);
            entidad.Property(e => e.DocumentoHmac).HasMaxLength(IndiceCiegoDocumento.LongitudHuella);
            entidad.Property(e => e.CreatedAt).IsRequired();
            // El número lo asigna la secuencia cliente_numero_seq. El único cubre la importación
            // de números que ya venían de Factusol: nunca puede haber dos clientes con el mismo.
            entidad.HasIndex(e => e.Numero).IsUnique();
            // Índice ciego: único y sin descifrar nada, dos clientes con el mismo documento
            // escrito de forma distinta ("001-1234567-8" y "00112345678") chocan aquí.
            entidad.HasIndex(e => e.DocumentoHmac).IsUnique();
            entidad.HasIndex(e => e.Nombre);
        });

        modelBuilder.Entity<Prospecto>(entidad =>
        {
            entidad.ToTable("prospecto");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Nombre).HasMaxLength(200).IsRequired();
            entidad.Property(e => e.Email).HasMaxLength(200);
            entidad.Property(e => e.Telefono).HasMaxLength(40);
            entidad.Property(e => e.Notas).HasMaxLength(1000);
            entidad.Property(e => e.DocumentoHmac).HasMaxLength(IndiceCiegoDocumento.LongitudHuella);
            entidad.Property(e => e.CreatedAt).IsRequired();
            entidad.HasIndex(e => e.DocumentoHmac).IsUnique();
            entidad.HasIndex(e => e.Nombre);
        });

        modelBuilder.Entity<ContactoCliente>(entidad =>
        {
            entidad.ToTable("contacto_cliente");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Nombre).HasMaxLength(200).IsRequired();
            entidad.Property(e => e.Relacion).HasMaxLength(120);
            entidad.Property(e => e.Email).HasMaxLength(200);
            entidad.Property(e => e.Telefono).HasMaxLength(40);
            entidad.Property(e => e.CreatedAt).IsRequired();
            // Un solo contacto principal por cliente. El índice es parcial porque solo afecta a
            // los marcados como principales, y PostgreSQL no filtra índices únicos por código.
            entidad.HasIndex(e => e.ClienteId)
                .IsUnique()
                .HasFilter("(es_principal = true)")
                .HasDatabaseName("ix_contacto_cliente_principal");
            entidad.HasOne(e => e.Cliente)
                .WithMany(c => c.Contactos)
                .HasForeignKey(e => e.ClienteId);
        });

        // F1-10: inmuebles, unidades y documentos de propiedad (docs/ESPECIFICACION.md §3.1).
        modelBuilder.Entity<Inmueble>(entidad =>
        {
            entidad.ToTable("inmueble");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Nombre).HasMaxLength(200).IsRequired();
            entidad.Property(e => e.Direccion).HasMaxLength(300);
            entidad.Property(e => e.Notas).HasMaxLength(1000);
            entidad.Property(e => e.PlantillaContratoPorDefecto).HasMaxLength(120);
            entidad.Property(e => e.CreatedAt).IsRequired();
            entidad.HasIndex(e => e.Nombre);
            entidad.HasIndex(e => e.PropietariaId);
            // Subempresa propietaria: etiqueta, no multiempresa (docs/ESPECIFICACION.md §1). Sin
            // navegación para que EF no la confunda con la facturadora de la unidad.
            entidad.HasOne<Subempresa>().WithMany().HasForeignKey(e => e.PropietariaId);
        });

        modelBuilder.Entity<Unidad>(entidad =>
        {
            entidad.ToTable("unidad");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Nombre).HasMaxLength(120).IsRequired();
            entidad.Property(e => e.Medidor).HasMaxLength(120);
            entidad.Property(e => e.Notas).HasMaxLength(1000);
            // Baños con medios (1,5) y metros con decimales; los dos son medidas, no cuentas.
            entidad.Property(e => e.MetrosCuadrados).HasColumnType("numeric(18,2)");
            entidad.Property(e => e.Banos).HasColumnType("numeric(18,2)");
            entidad.Property(e => e.CreatedAt).IsRequired();
            entidad.HasIndex(e => e.Nombre);
            // Índice compuesto por el filtro de estado manual de la lista de unidades.
            entidad.HasIndex(e => new { e.InmuebleId, e.EstadoManual });
            entidad.HasOne<Inmueble>()
                .WithMany(i => i.Unidades)
                .HasForeignKey(e => e.InmuebleId);
            entidad.HasOne<Subempresa>().WithMany().HasForeignKey(e => e.PropietariaId);
            entidad.HasOne<Subempresa>().WithMany().HasForeignKey(e => e.FacturadoraPorDefectoId);
        });

        // Modalidades permitidas: la regla que no se puede violar. La clave compuesta hace
        // imposible repetir una modalidad en la misma unidad.
        modelBuilder.Entity<ModalidadUnidad>(entidad =>
        {
            entidad.ToTable("unidad_modalidad");
            entidad.HasKey(e => new { e.UnidadId, e.Modalidad });
            entidad.HasOne(e => e.Unidad)
                .WithMany(u => u.Modalidades)
                .HasForeignKey(e => e.UnidadId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TarifaUnidad>(entidad =>
        {
            entidad.ToTable("unidad_tarifa");
            entidad.HasKey(e => e.Id);
            // La tarifa se elige por modalidad y por moneda (docs/ESPECIFICACION.md §3.1): una unidad
            // no tiene dos tarifas de lo mismo, y el importe es numeric(18,2) por la convención.
            entidad.HasIndex(e => new { e.UnidadId, e.Modalidad, e.Moneda }).IsUnique();
            entidad.HasOne(e => e.Unidad)
                .WithMany(u => u.Tarifas)
                .HasForeignKey(e => e.UnidadId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // F2-01: disponibilidad calculada (AGENTS.md §6, docs/ESPECIFICACION.md §5). Estas tablas
        // no declaran nada que no exista en la base: la restricción de exclusión de
        // ocupacion_rango y el trigger de capacidad de ocupacion_dia los crea la migración, porque
        // son reglas que solo PostgreSQL puede cumplir sin carreras.
        //
        // Origen y tipo son enums mapeados a entero, como el resto del dominio. Los valores
        // posibles están en OrigenOcupacion y TipoOcupacionDia, y los comparan también los triggers
        // de la migración, que es SQL escrito a mano.
        modelBuilder.Entity<OcupacionRango>(entidad =>
        {
            entidad.ToTable("ocupacion_rango");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.CreadoAt).IsRequired();
            // Uno por fila de origen: reintentar la misma ocupación no duplica el bloqueo.
            entidad.HasIndex(e => new { e.Origen, e.OrigenId }).IsUnique();
            entidad.HasIndex(e => e.UnidadId);
            entidad.HasOne<Unidad>()
                .WithMany()
                .HasForeignKey(e => e.UnidadId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OcupacionDia>(entidad =>
        {
            entidad.ToTable("ocupacion_dia");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.CreadoAt).IsRequired();
            // Un día por fila de origen: reintentar la misma reserva no duplica sus días.
            entidad.HasIndex(e => new { e.Origen, e.OrigenId, e.Fecha }).IsUnique();
            // La consulta de disponibilidad y la del trigger de capacidad son por unidad y día.
            entidad.HasIndex(e => new { e.UnidadId, e.Fecha });
            entidad.HasOne<Unidad>()
                .WithMany()
                .HasForeignKey(e => e.UnidadId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // F2-02: cotizaciones (docs/ESPECIFICACION.md §3.3). El destinatario es un cliente o un
        // prospecto: la restricción la pone la base de datos con un CHECK, porque es la única forma
        // de que "los dos" y "ninguno" queden excluidos también si algo escribe por fuera de la API.
        // Estado y moneda son enums mapeados a entero, como el resto del dominio.
        modelBuilder.Entity<Cotizacion>(entidad =>
        {
            entidad.ToTable("cotizacion", tabla => tabla.HasCheckConstraint(
                "ck_cotizacion_destinatario",
                "(cliente_id IS NOT NULL) <> (prospecto_id IS NOT NULL)"));
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.CreatedAt).IsRequired();
            // La lista filtra por estado y ordena por fecha: el índice acompaña a las dos cosas.
            entidad.HasIndex(e => new { e.Activo, e.Estado });
            entidad.HasIndex(e => e.CreatedAt);
            entidad.HasIndex(e => e.ClienteId);
            entidad.HasIndex(e => e.ProspectoId);
            entidad.HasIndex(e => e.UnidadId);
            entidad.HasOne(e => e.Cliente)
                .WithMany()
                .HasForeignKey(e => e.ClienteId)
                .OnDelete(DeleteBehavior.Restrict);
            entidad.HasOne(e => e.Prospecto)
                .WithMany()
                .HasForeignKey(e => e.ProspectoId)
                .OnDelete(DeleteBehavior.Restrict);
            entidad.HasOne(e => e.Unidad)
                .WithMany()
                .HasForeignKey(e => e.UnidadId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Las líneas se reemplazan enteras mientras la cotización está en borrador, así que el
        // borrado en cascada es el mismo patrón que las modalidades y las tarifas de una unidad: se
        // borra lo que ya no está, nunca el documento. Una vez enviada, la cotización no se edita y
        // sus líneas quedan congeladas.
        modelBuilder.Entity<CotizacionLinea>(entidad =>
        {
            entidad.ToTable("cotizacion_linea");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Descripcion).HasMaxLength(300).IsRequired();
            entidad.Property(e => e.CreatedAt).IsRequired();
            // El orden del documento sale de aquí, no del orden en que PostgreSQL devuelva las filas.
            entidad.HasIndex(e => new { e.CotizacionId, e.Posicion }).IsUnique();
            entidad.HasOne(e => e.Cotizacion)
                .WithMany(c => c.Lineas)
                .HasForeignKey(e => e.CotizacionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // F2-03: reservas (docs/ESPECIFICACION.md §3.4). Como en la cotización, el destinatario es
        // un cliente o un prospecto y lo garantiza el CHECK de la base de datos, no solo el servicio.
        modelBuilder.Entity<Reserva>(entidad =>
        {
            entidad.ToTable("reserva", tabla => tabla.HasCheckConstraint(
                "ck_reserva_destinatario",
                "(cliente_id IS NOT NULL) <> (prospecto_id IS NOT NULL)"));
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.AnticipoNota).HasMaxLength(300);
            entidad.Property(e => e.Observaciones).HasMaxLength(1000);
            entidad.Property(e => e.CreatedAt).IsRequired();
            // El importe y el anticipo son dinero: numeric(18,2) y half-up (AGENTS.md §6).
            entidad.Property(e => e.Importe).HasColumnType("numeric(18,2)");
            entidad.Property(e => e.AnticipoImporte).HasColumnType("numeric(18,2)");
            // La lista filtra por estado y las vivas por las activas; este es el mismo índice que en
            // la cotización.
            entidad.HasIndex(e => new { e.Activo, e.Estado });
            entidad.HasIndex(e => e.CreatedAt);
            entidad.HasIndex(e => e.ClienteId);
            entidad.HasIndex(e => e.ProspectoId);
            entidad.HasIndex(e => e.UnidadId);
            // El trabajo diario de las alertas de 7 días busca exactamente las activas cuya entrada
            // prevista es hoy+7: este índice es para ese recorrido.
            entidad.HasIndex(e => new { e.Estado, e.EntradaPrevista });
            entidad.HasOne(e => e.Cliente)
                .WithMany()
                .HasForeignKey(e => e.ClienteId)
                .OnDelete(DeleteBehavior.Restrict);
            entidad.HasOne(e => e.Prospecto)
                .WithMany()
                .HasForeignKey(e => e.ProspectoId)
                .OnDelete(DeleteBehavior.Restrict);
            entidad.HasOne(e => e.Unidad)
                .WithMany()
                .HasForeignKey(e => e.UnidadId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Los días de la reserva corta son negocio, no bloqueo: se borran y se reescriben al editar
        // la reserva (como las líneas de una cotización en borrador) y sobreviven a la cancelación.
        // (reserva_id, fecha) es ÚNICO a propósito: una reserva es de una persona, así que dos personas
        // el mismo día son dos reservas con un medio día cada una (docs/ESPECIFICACION.md §3.4). Meter
        // los dos medios días en una sola reserva dejaría medio día sin bloquear —ocupacion_dia es
        // único por (origen, origen_id, fecha) y la segunda fila del mismo día no tendría dónde
        // bloquearse— y el trigger de F2-01 contaría una fila donde hay dos, dejando pasar a un
        // tercero. El servicio ya lo rechaza con un error de negocio; el índice lo cierra en la base
        // para que ningún camino futuro (importación, trabajo, inserción directa) pueda saltárselo.
        modelBuilder.Entity<ReservaDia>(entidad =>
        {
            entidad.ToTable("reserva_dia");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.CreatedAt).IsRequired();
            entidad.HasIndex(e => new { e.ReservaId, e.Fecha }).IsUnique();
            entidad.HasOne(e => e.Reserva)
                .WithMany(r => r.Dias)
                .HasForeignKey(e => e.ReservaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // F2-05: contratos (docs/ESPECIFICACION.md §3.5). Todos los obligatorios de la
        // especificación son NOT NULL aquí, y no por decoración: el servicio los rechaza con 409
        // antes de escribir, y la base los exige igual para que ningún camino futuro (importación,
        // trabajo, inserción directa) pueda guardar un contrato a medio escribir.
        modelBuilder.Entity<Contrato>(entidad =>
        {
            entidad.ToTable("contrato");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Observaciones).HasMaxLength(1000);
            entidad.Property(e => e.EstadoActualComentario).HasMaxLength(1000);
            // F2-07: el código de la plantilla, no una clave ajena al catálogo: es lo que guarda
            // el front y lo que trae por defecto el inmueble (F1-10). La plantilla se resuelve al
            // aprobar, que es cuando se copia a contrato_plantilla_congelada.
            entidad.Property(e => e.PlantillaContratoId).HasMaxLength(120).IsRequired();
            entidad.Property(e => e.PlantillaContratoCongeladaId).HasMaxLength(120);
            entidad.Property(e => e.CreatedAt).IsRequired();
            // La renta y la fianza son dinero: numeric(18,2) y half-up (AGENTS.md §6).
            entidad.Property(e => e.Renta).HasColumnType("numeric(18,2)");
            entidad.Property(e => e.Fianza).HasColumnType("numeric(18,2)");
            // La lista filtra por estado y ordena por fecha de creación; la pantalla de la unidad
            // busca por unidad y estado. Los índices de cliente y unidad también los usa el trabajo
            // de la alerta persistente (§3.5) y la ocupación, que busca el contrato vivo de la
            // unidad.
            entidad.HasIndex(e => new { e.Estado, e.CreatedAt });
            entidad.HasIndex(e => e.ClienteId);
            entidad.HasIndex(e => new { e.UnidadId, e.Estado });
            entidad.HasOne(e => e.Cliente)
                .WithMany()
                .HasForeignKey(e => e.ClienteId)
                .OnDelete(DeleteBehavior.Restrict);
            entidad.HasOne(e => e.Unidad)
                .WithMany()
                .HasForeignKey(e => e.UnidadId)
                .OnDelete(DeleteBehavior.Restrict);
            entidad.HasOne(e => e.Facturadora)
                .WithMany()
                .HasForeignKey(e => e.FacturadoraId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // El historial de versiones es de solo añadir (docs/ESPECIFICACION.md §3.5). (contrato_id,
        // numero) es único a propósito: el número se calcula leyendo el máximo dentro de la
        // transacción del cambio, y el índice es la red para que dos cambios simultáneos no se
        // numeren igual.
        modelBuilder.Entity<ContratoVersion>(entidad =>
        {
            entidad.ToTable("contrato_version");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Cambio).HasMaxLength(500).IsRequired();
            entidad.Property(e => e.Alcance).HasMaxLength(200).IsRequired();
            entidad.Property(e => e.CreatedAt).IsRequired();
            entidad.Property(e => e.RentaAnterior).HasColumnType("numeric(18,2)");
            entidad.Property(e => e.RentaNueva).HasColumnType("numeric(18,2)");
            entidad.HasIndex(e => new { e.ContratoId, e.Numero }).IsUnique();
            entidad.HasOne(e => e.Contrato)
                .WithMany(c => c.Versiones)
                .HasForeignKey(e => e.ContratoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Una sola solicitud viva por contrato: el índice único parcial sobre resultado = pendiente
        // (1) es lo que hace imposible que dos envíos simultáneos dejen dos contratos esperando
        // aprobación. Cuando la primera caduca, la segunda cabe; mientras siga pendiente, no.
        modelBuilder.Entity<SolicitudAprobacion>(entidad =>
        {
            entidad.ToTable("solicitud_aprobacion");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.HuellaContenido).HasMaxLength(64).IsRequired();
            entidad.Property(e => e.Comentario).HasMaxLength(1000);
            entidad.Property(e => e.MotivoCaducidad).HasMaxLength(200);
            entidad.Property(e => e.CreatedAt).IsRequired();
            // Único y parcial: una sola solicitud viva por contrato. El índice es lo que hace
            // imposible que dos envíos simultáneos dejen dos contratos esperando aprobación; cuando
            // la primera caduca, la siguiente cabe porque ya no cumple el predicado.
            entidad.HasIndex(e => e.ContratoId).IsUnique().HasFilter("(resultado = 1)");
            // La bandeja de aprobación de F2-10 lista por resultado y ordena por fecha de envío.
            entidad.HasIndex(e => new { e.Resultado, e.SolicitadoAt });
            entidad.HasOne(e => e.Contrato)
                .WithMany(c => c.Solicitudes)
                .HasForeignKey(e => e.ContratoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // F2-06: historial de bloques de renovación. (contrato_id, numero) único por el mismo motivo
        // que en contrato_version y solicitud_aprobacion: el trabajo diario calcula el número leyendo
        // el máximo y el índice es la red contra dos ejecuciones simultáneas del mismo trabajo.
        modelBuilder.Entity<ContratoBloque>(entidad =>
        {
            entidad.ToTable("contrato_bloque");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.CreatedAt).IsRequired();
            entidad.Property(e => e.Renta).HasColumnType("numeric(18,2)");
            entidad.HasIndex(e => new { e.ContratoId, e.Numero }).IsUnique();
            entidad.HasOne(e => e.Contrato)
                .WithMany(c => c.Bloques)
                .HasForeignKey(e => e.ContratoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // F2-06: incrementos de renta. El porcentaje y el importe van en columnas distintas y
        // numéricas, no en una sola "valor": un importe fijo es dinero en la moneda del contrato
        // (numeric(18,2)) y un porcentaje es un tipo (numeric(18,4)); mezclarlos en una columna
        // obliga a convertir cada vez que se lee y es como un importe fijo acaba tratado como si
        // fuera un porcentaje. El CHECK de la migración es lo que impide tener las dos columnas
        // llenas o las dos vacías.
        modelBuilder.Entity<ContratoIncremento>(entidad =>
        {
            entidad.ToTable("contrato_incremento", tabla => tabla.HasCheckConstraint(
                "ck_contrato_incremento_tipo_valor",
                """
                (tipo = 1 AND porcentaje IS NOT NULL AND importe IS NULL)
                OR (tipo = 2 AND importe IS NOT NULL AND porcentaje IS NULL)
                """));
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.CreatedAt).IsRequired();
            entidad.Property(e => e.Porcentaje).HasColumnType("numeric(18,4)");
            entidad.Property(e => e.Importe).HasColumnType("numeric(18,2)");
            entidad.Property(e => e.RentaAntes).HasColumnType("numeric(18,2)");
            entidad.Property(e => e.RentaDespues).HasColumnType("numeric(18,2)");
            // El trabajo diario busca los de un contrato que aún no se aplicaron y cuya fecha ya
            // toca, ordenados por fecha: ese índice es su consulta.
            entidad.HasIndex(e => new { e.ContratoId, e.Aplicado, e.FechaVigencia });
            entidad.HasOne(e => e.Contrato)
                .WithMany(c => c.Incrementos)
                .HasForeignKey(e => e.ContratoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // F2-07: catálogo de plantillas de contrato (docs/ESPECIFICACION.md §3.5). El código es
        // único y no se reutiliza: un contrato firmado cita el código de su plantilla, así que
        // reutilizarlo haría que dos plantillas distintas se escribieran con el mismo nombre. El
        // catálogo no tiene categorías porque la especificación no las define para plantillas (sí
        // para los documentos del inmueble, §3.1): lo que ordena el catálogo es el código, y con
        // él ya no pueden existir dos plantillas activas con el mismo nombre, que es lo que pedía
        // la regla de "uno activo por categoría" si algún día esa categoría existiera.
        modelBuilder.Entity<PlantillaContrato>(entidad =>
        {
            entidad.ToTable("plantilla_contrato");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Codigo).HasMaxLength(60).IsRequired();
            entidad.Property(e => e.Nombre).HasMaxLength(120).IsRequired();
            entidad.Property(e => e.CreatedAt).IsRequired();
            entidad.Property(e => e.Secciones)
                .HasConversion(TextoDePlantilla.Secciones).HasColumnType("jsonb").IsRequired();
            entidad.Property(e => e.AnexosTipos)
                .HasConversion(TextoDePlantilla.Tipos).HasColumnType("jsonb").IsRequired();
            entidad.HasIndex(e => e.Codigo).IsUnique();
            // El selector de plantillas del front lista las activas por nombre.
            entidad.HasIndex(e => new { e.Activa, e.Nombre });
        });

        // F2-07: catálogo de anexos, uno activo por tipo (§3.5). El índice único parcial sobre
        // (tipo) con el predicado "activa" es la regla en la base, no solo en el servicio: dos
        // anexos activos del mismo tipo no pueden existir ni por un camino futuro (importación,
        // trabajo, inserción directa). Cuando el primero se desactiva, el segundo cabe.
        modelBuilder.Entity<PlantillaAnexo>(entidad =>
        {
            entidad.ToTable("plantilla_anexo");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Nombre).HasMaxLength(120).IsRequired();
            entidad.Property(e => e.CreatedAt).IsRequired();
            entidad.Property(e => e.Secciones)
                .HasConversion(TextoDePlantilla.Secciones).HasColumnType("jsonb").IsRequired();
            entidad.HasIndex(e => e.Tipo).IsUnique().HasFilter("(activa)");
            entidad.HasIndex(e => new { e.Activa, e.Nombre });
        });

        // F2-07: la copia congelada de la plantilla y sus anexos. La clave es contrato_id, así que
        // un contrato no puede tener dos copias; y no hay updated_at porque no se edita nunca. El
        // texto va en jsonb porque es texto de usuario que el PDF imprime entero, no algo que la
        // aplicación consulte por partes.
        modelBuilder.Entity<ContratoPlantillaCongelada>(entidad =>
        {
            entidad.ToTable("contrato_plantilla_congelada");
            entidad.HasKey(e => e.ContratoId);
            entidad.Property(e => e.Codigo).HasMaxLength(60).IsRequired();
            entidad.Property(e => e.Nombre).HasMaxLength(120).IsRequired();
            entidad.Property(e => e.CongeladaAt).IsRequired();
            entidad.Property(e => e.Secciones)
                .HasConversion(TextoDePlantilla.Secciones).HasColumnType("jsonb").IsRequired();
            entidad.Property(e => e.Anexos)
                .HasConversion(TextoDePlantilla.Anexos).HasColumnType("jsonb").IsRequired();
            entidad.HasIndex(e => e.PlantillaId);
            entidad.HasOne(e => e.Contrato)
                .WithOne(c => c.PlantillaCongelada)
                .HasForeignKey<ContratoPlantillaCongelada>(e => e.ContratoId)
                .OnDelete(DeleteBehavior.Restrict);
            entidad.HasOne(e => e.Plantilla)
                .WithMany()
                .HasForeignKey(e => e.PlantillaId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // F2-08 (1/3): inventario base de la unidad. Catálogo vivo y opcional (§3.1): sin fotos y sin
        // columna de imagen, porque §3.1 lo dice y nadie pidió lo contrario. El índice por
        // (unidad, activo, orden) es el de las lecturas de la pantalla de la unidad.
        modelBuilder.Entity<InventarioBaseItem>(entidad =>
        {
            entidad.ToTable("inventario_base_item");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Nombre).HasMaxLength(120).IsRequired();
            entidad.Property(e => e.Notas).HasMaxLength(1000);
            entidad.Property(e => e.CreatedAt).IsRequired();
            entidad.HasIndex(e => new { e.UnidadId, e.Activo, e.Orden });
            entidad.HasOne(e => e.Unidad)
            .WithMany(u => u.InventarioBase)
            .HasForeignKey(e => e.UnidadId)
            .OnDelete(DeleteBehavior.Restrict);
        });

        // F2-08 (2/3): la copia congelada. Mismo patrón que la plantilla congelada: no implementa
        // IEntidadTrazada y no tiene updated_at, porque no existe ningún camino que la escriba después
        // de la activación. La única referencia al catálogo es InventarioBaseId y va en Restrict: es
        // para rastrear de dónde salió la línea, no para colgar de ella.
        modelBuilder.Entity<ContratoInventarioItem>(entidad =>
        {
            entidad.ToTable("contrato_inventario_item");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Nombre).HasMaxLength(120).IsRequired();
            entidad.Property(e => e.Notas).HasMaxLength(1000);
            entidad.HasIndex(e => new { e.ContratoId, e.Orden });
            entidad.HasIndex(e => e.InventarioBaseId);
            entidad.HasOne(e => e.Contrato)
            .WithMany(c => c.Inventario)
            .HasForeignKey(e => e.ContratoId)
            .OnDelete(DeleteBehavior.Restrict);
            entidad.HasOne(e => e.InventarioBaseItem)
            .WithMany()
            .HasForeignKey(e => e.InventarioBaseId)
            .OnDelete(DeleteBehavior.Restrict);
        });

        // F2-08 (3/3): la revisión de salida. Nace firmada y no se vuelve a escribir, así que el
        // índice único sobre contrato_id es la regla de "se firma una sola vez" puesta en la base y no
        // solo en el servicio: es la misma red que evita dos liquidaciones para el mismo contrato.
        // El importe propuesto va en la moneda del contrato y por eso no lleva columna de moneda: una
        // moneda por documento (§6).
        modelBuilder.Entity<RevisionInventario>(entidad =>
        {
            entidad.ToTable("revision_inventario");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Observacion).HasMaxLength(1000);
            entidad.Property(e => e.FirmadaAt).IsRequired();
            entidad.Property(e => e.CreatedAt).IsRequired();
            entidad.HasIndex(e => e.ContratoId).IsUnique();
            entidad.HasOne(e => e.Contrato)
            .WithOne(c => c.RevisionInventario)
            .HasForeignKey<RevisionInventario>(e => e.ContratoId)
            .OnDelete(DeleteBehavior.Restrict);
        });

        // F2-09: la liquidación. Nace cerrada y no se vuelve a escribir, así que el índice único sobre
        // contrato_id es la regla de "se liquida una sola vez" en la base y no solo en el servicio: es
        // la misma red que ya evita dos revisiones de salida para un contrato. Los importes van en la
        // moneda del contrato y por eso no llevan columna de moneda (una moneda por documento, §6), y
        // no hay cobro_id ni fianza_devolucion_id porque esas tablas son de F3 y no existen todavía.
        modelBuilder.Entity<ContratoLiquidacion>(entidad =>
        {
            entidad.ToTable("contrato_liquidacion");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Observacion).HasMaxLength(1000);
            entidad.Property(e => e.FechaLiquidacion).IsRequired();
            entidad.Property(e => e.CreatedAt).IsRequired();
            entidad.HasIndex(e => e.ContratoId).IsUnique();
            entidad.HasOne(e => e.Contrato)
            .WithOne(c => c.Liquidacion)
            .HasForeignKey<ContratoLiquidacion>(e => e.ContratoId)
            .OnDelete(DeleteBehavior.Restrict);
            entidad.HasOne(e => e.RevisionInventario)
            .WithMany()
            .HasForeignKey(e => e.RevisionInventarioId)
            .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RevisionInventarioItem>(entidad =>
        {
            entidad.ToTable("revision_inventario_item");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Nota).HasMaxLength(1000);
            entidad.Property(e => e.CreatedAt).IsRequired();
            // Una línea por elemento del inventario congelado: el índice único es lo que impide que
            // dos líneas opinen sobre el mismo mueble.
            entidad.HasIndex(e => new { e.RevisionId, e.ContratoInventarioItemId }).IsUnique();
            entidad.HasOne(e => e.Revision)
            .WithMany(r => r.Items)
            .HasForeignKey(e => e.RevisionId)
            .OnDelete(DeleteBehavior.Restrict);
            entidad.HasOne(e => e.ContratoInventarioItem)
            .WithMany()
            .HasForeignKey(e => e.ContratoInventarioItemId)
            .OnDelete(DeleteBehavior.Restrict);
        });

        // F3-01: tipo de cambio diario (docs/ESPECIFICACION.md §3.12, D9 y D22).
        modelBuilder.Entity<TipoCambio>(entidad =>
        {
            entidad.ToTable("tipo_cambio", tabla =>
            {
                // El dólar es la base de informes y vale 1 por definición: la fila se resuelve sin
                // consultar la base, así que una fila de USD solo podría ser una contradicción
                // (un tipo de cambio del dólar que no sea 1) y nada más.
                tabla.HasCheckConstraint("ck_tipo_cambio_no_usd", "moneda <> 1");
                // Un tipo de cambio cero o negativo rompe toda conversión y además dividiría por
                // cero al consolidar informes. El servicio lo rechaza con un 409 y la base lo
                // rechaza igual, para que ningún escritor por fuera de la API pueda colarlo.
                tabla.HasCheckConstraint("ck_tipo_cambio_valor_positivo", "valor > 0");
            });
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Fuente).HasMaxLength(50).IsRequired();
            entidad.Property(e => e.CreatedAt).IsRequired();
            // El tipo de cambio es el único decimal que no es dinero ni porcentaje: la convención
            // global pone numeric(18,2) y aquí lo que manda es docs/DOC_TECNICA.md §4.2.
            entidad.Property(e => e.Valor).HasColumnType("numeric(18,6)");
            // Un valor por día y por moneda: es la regla del dominio puesta en la base, no en el
            // servicio. También es lo que hace que el trabajo se pueda correr dos veces el mismo día.
            entidad.HasIndex(e => new { e.Fecha, e.Moneda }).IsUnique();
        });

        // F3-02: facturas y sus líneas (docs/ESPECIFICACION.md §3.8).
        modelBuilder.Entity<Factura>(entidad =>
        {
            entidad.ToTable("factura", tabla =>
            {
                // Una factura emitida tiene número y una anulada también: el número no vuelve a
                // liberarse nunca (D28). Lo que la base impide es que una factura en borrador
                // tenga número ya, que es lo que haría que consumiera uno sin pasar por el
                // contador transaccional.
                tabla.HasCheckConstraint(
                    "ck_factura_numero_estado",
                    "(estado = 1) = (numero IS NULL)");
                // La fecha de emisión la pone la emisión, no quien la escribe: un borrador no
                // tiene día de salida. El tipo de cambio congelado va con ella por la misma razón.
                tabla.HasCheckConstraint(
                    "ck_factura_cambio_con_emision",
                    "tipo_cambio_fecha IS NULL OR fecha_emision IS NOT NULL");
                // F3-04: la retención va en positivo, porque es una cantidad que se resta de lo
                // facturado y no un descuento más, que ya va en negativo dentro de las líneas.
                tabla.HasCheckConstraint(
                    "ck_factura_retencion_no_negativa",
                    "retencion >= 0");
                // El neto a cobrar es una identidad, no un dato que alguien pueda escribir de otra
                // manera: si la fórmula no cuadra, la fila está corrupta y la base lo dice.
                tabla.HasCheckConstraint(
                    "ck_factura_neto_a_cobrar",
                    "neto_a_cobrar = total - retencion");
            });
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Concepto).HasMaxLength(300).IsRequired();
            entidad.Property(e => e.CreatedAt).IsRequired();
            // La referencia de e-factura es texto libre que alguien teclea (D5): se limita la
            // longitud para que no quepa un documento entero dentro de una factura.
            entidad.Property(e => e.ReferenciaEfactura).HasMaxLength(120);
            // F3-08: el motivo de la anulación es texto que alguien teclea, igual que la referencia
            // de arriba, y comparte su límite para que no quepa un documento dentro del otro.
            entidad.Property(e => e.MotivoAnulacion).HasMaxLength(500);
            // El tipo de cambio congelado es el único decimal de la tabla que no es dinero:
            // numeric(18,6), como en la fila de F3-01 de la que salió.
            entidad.Property(e => e.TipoCambioValor).HasColumnType("numeric(18,6)");
            // La lista filtra por estado y ordena por fecha de emisión, y las de un cliente o una
            // unidad son la consulta de la vista de cuentas (§3.13) y del expediente.
            entidad.HasIndex(e => new { e.Estado, e.FechaEmision });
            entidad.HasIndex(e => e.ClienteId);
            entidad.HasIndex(e => e.UnidadId);
            entidad.HasIndex(e => e.ContratoId);
            // Un número por serie, y único: es la red de seguridad del contador de §3.7. Sin ella,
            // dos emisiones simultáneas que salieran con el mismo número commitearían dos facturas
            // con el mismo número, que es justo lo que no puede pasar.
            entidad.HasIndex(e => new { e.SerieId, e.Numero })
                .IsUnique()
                .HasFilter("numero IS NOT NULL");
            // F3-05: una factura periódica por contrato, periodo y concepto (§3.8 «impide duplicados:
            // contrato, periodo, concepto»). El filtro saca las facturas que no son periódicas
            // (periodo_inicio nulo) y las anuladas: una factura anulada libera su clave, que es lo que
            // permite emitir la que la sustituye (F3-08), y su número no vuelve atrás (D28).
            //
            // En la base el índice es NULLS NOT DISTINCT (ver la migración), cosa que el modelo no
            // puede expresar: sin ella, una factura de reserva corta —que no tiene contrato— no
            // estaría protegida y su doble ejecución sí duplicaría.
            entidad.HasIndex(e => new { e.ContratoId, e.PeriodoInicio, e.Concepto })
                .IsUnique()
                .HasDatabaseName("ix_factura_periodo_unico")
                .HasFilter("(periodo_inicio IS NOT NULL AND estado <> 3)");
            entidad.HasOne(e => e.Cliente)
                .WithMany()
                .HasForeignKey(e => e.ClienteId)
                .OnDelete(DeleteBehavior.Restrict);
            entidad.HasOne(e => e.Unidad)
                .WithMany()
                .HasForeignKey(e => e.UnidadId)
                .OnDelete(DeleteBehavior.Restrict);
            entidad.HasOne(e => e.Contrato)
                .WithMany()
                .HasForeignKey(e => e.ContratoId)
                .OnDelete(DeleteBehavior.Restrict);
            entidad.HasOne(e => e.Serie)
                .WithMany()
                .HasForeignKey(e => e.SerieId)
                .OnDelete(DeleteBehavior.Restrict);
            entidad.HasOne(e => e.TipoCambio)
                .WithMany()
                .HasForeignKey(e => e.TipoCambioId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Las líneas se reescriben enteras mientras la factura está en borrador, así que el borrado
        // en cascada es el mismo patrón que las modalidades de una unidad: se borra lo que ya no
        // está, nunca el documento. Emitida, la factura ya no se toca y sus líneas quedan
        // congeladas; lo que se hace con una factura emitida es anularla (F3-08).
        modelBuilder.Entity<FacturaLinea>(entidad =>
        {
            entidad.ToTable("factura_linea");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Concepto).HasMaxLength(300).IsRequired();
            entidad.Property(e => e.Cantidad).HasColumnType("numeric(18,4)");
            // El porcentaje de ITBIS es un porcentaje, no dinero: cuatro decimales, que es lo que
            // redondea Redondeo.Porcentaje.
            entidad.Property(e => e.PorcentajeItbis).HasColumnType("numeric(18,4)");
            // F3-04: el porcentaje de retención, igual que el de ITBIS, es un porcentaje y no dinero.
            entidad.Property(e => e.PorcentajeRetencion).HasColumnType("numeric(18,4)");
            entidad.Property(e => e.CreatedAt).IsRequired();
            // El orden del documento sale de aquí, no del orden en que PostgreSQL devuelva las
            // filas: el PDF y la vista de cuentas leen este número.
            entidad.HasIndex(e => new { e.FacturaId, e.Posicion }).IsUnique();
            entidad.HasOne(e => e.Factura)
                .WithMany(f => f.Lineas)
                .HasForeignKey(e => e.FacturaId)
                .OnDelete(DeleteBehavior.Cascade);
            entidad.HasOne(e => e.TipoItbis)
                .WithMany()
                .HasForeignKey(e => e.TipoItbisId)
                .OnDelete(DeleteBehavior.Restrict);
            // F3-04: la línea de retención apunta al tipo del que salió. Restrict y no cascada porque
            // un tipo de retención es configuración compartida y no puede desaparecer debajo de una
            // factura ya emitida.
            entidad.HasOne(e => e.TipoRetencion)
                .WithMany()
                .HasForeignKey(e => e.TipoRetencionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // F3-06: cobros (docs/ESPECIFICACION.md §3.10.1). Un cobro es un movimiento de dinero: no
        // tiene borradores ni borrados, solo registro y anulación con motivo.
        modelBuilder.Entity<Cobro>(entidad =>
        {
            entidad.ToTable("cobro", tabla =>
            {
                // Un cobro de cero o de menos no es un cobro: es un error de captura, y dejarlo
                // pasar metería filas que cuadran en la suma y no en el banco.
                tabla.HasCheckConstraint("ck_cobro_importe_positivo", "importe_recibido > 0");
                // Ni el crédito aplicado ni el excedente pueden ser negativos: un importe negativo
                // es una devolución, y eso se registra con su propio motivo y su propia fecha.
                tabla.HasCheckConstraint("ck_cobro_no_negativos", "saldo_favor_usado >= 0 AND excedente >= 0");
                // Anulado significa anulado con motivo y con fecha, y no al revés. Sin esta regla,
                // la única forma de saber si una anulación está completa es mirando tres columnas
                // a la vez, y la respuesta puede ser que sí y que no.
                tabla.HasCheckConstraint(
                    "ck_cobro_anulado_completo",
                    "(estado = 2) = (anulado_at IS NOT NULL AND motivo_anulacion IS NOT NULL)");
            });
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Referencia).HasMaxLength(120);
            entidad.Property(e => e.MotivoAnulacion).HasMaxLength(500);
            entidad.Property(e => e.CreatedAt).IsRequired();
            // El tipo de cambio congelado es el único decimal de la tabla que no es dinero:
            // numeric(18,6), como en la fila de tipo_cambio de F3-01 de la que salió.
            entidad.Property(e => e.TipoCambioValor).HasColumnType("numeric(18,6)");
            // La vista de cobros filtra por cliente y ordena por fecha efectiva; los listados por
            // contrato, moneda y estado son las mismas consultas con un filtro más.
            entidad.HasIndex(e => new { e.ClienteId, e.FechaEfectiva });
            entidad.HasIndex(e => e.ContratoId);
            entidad.HasIndex(e => new { e.Estado, e.FechaEfectiva });
            entidad.HasIndex(e => new { e.MonedaRecibida, e.FechaEfectiva });
            entidad.HasOne(e => e.Cliente)
                .WithMany()
                .HasForeignKey(e => e.ClienteId)
                .OnDelete(DeleteBehavior.Restrict);
            entidad.HasOne(e => e.Contrato)
                .WithMany()
                .HasForeignKey(e => e.ContratoId)
                .OnDelete(DeleteBehavior.Restrict);
            entidad.HasOne(e => e.FormaPago)
                .WithMany()
                .HasForeignKey(e => e.FormaPagoId)
                .OnDelete(DeleteBehavior.Restrict);
            entidad.HasOne(e => e.CuentaReceptora)
                .WithMany()
                .HasForeignKey(e => e.CuentaReceptoraId)
                .OnDelete(DeleteBehavior.Restrict);
            entidad.HasOne(e => e.TipoCambio)
                .WithMany()
                .HasForeignKey(e => e.TipoCambioId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Las asignaciones no se borran cuando se anula el cobro ni cuando se anula la factura que
        // pagaban (D6): se quedan sin vigor. Por eso el borrado en cascada es el mismo patrón que el
        // de las líneas de factura: no hay documento que se borre nunca, y lo que se borra es lo que
        // ya no está. Nunca hay DELETE desde la API, así que la cascada solo existe para las
        // migraciones y los scripts.
        modelBuilder.Entity<CobroAsignacion>(entidad =>
        {
            entidad.ToTable("cobro_asignacion", tabla =>
            {
                // Una asignación de cero no asigna nada y haría creer que esa factura se pagó.
                tabla.HasCheckConstraint("ck_cobro_asignacion_importe_positivo", "importe > 0");
                // F3-07 (§3.10.2): lo recibido y el cambio van juntos o no van. Una línea con
                // importe recibido y sin cambio no se puede convertir a la moneda de la factura, y
                // una con cambio y sin importe recibido guardaría un cambio que no produjo nada.
                tabla.HasCheckConstraint(
                    "ck_cobro_asignacion_conversion_completa",
                    "(importe_recibido IS NULL) = (tipo_cambio_valor IS NULL)");
                tabla.HasCheckConstraint(
                    "ck_cobro_asignacion_conversion_positiva",
                    "(importe_recibido IS NULL OR importe_recibido > 0) AND (tipo_cambio_valor IS NULL OR tipo_cambio_valor > 0)");
                // F3-09 (§3.11): el cobro se aplica a una factura o a una fianza, y exactamente a una.
                // Sin esta restricción, una asignación sin destino no aplicaría nada y alguien la
                // leería como dinero cobrado sin saber a qué; y una con los dos destinos contaría el
                // mismo importe dos veces, una al cerrar la factura y otra al cerrar la fianza.
                tabla.HasCheckConstraint(
                    "ck_cobro_asignacion_destino_unico",
                    "(factura_id IS NULL) <> (fianza_id IS NULL)");
            });
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.CreatedAt).IsRequired();
            // El único decimal que no es dinero: el cambio realmente aplicado en esta línea
            // (numeric(18,6)), con el mismo tipo que el congelado en el cobro.
            entidad.Property(e => e.TipoCambioValor).HasColumnType("numeric(18,6)");
            // F3-08: el motivo de la anulación de la asignación es el de la factura que dejó de
            // pagarse, y por eso comparte su longitud con el de la cabecera del cobro.
            entidad.Property(e => e.MotivoAnulacion).HasMaxLength(500);
            // El índice por factura es el que resuelve la suma de lo cobrado de una página de
            // facturas en una sola consulta, sin N+1 (§3.13).
            entidad.HasIndex(e => e.FacturaId);
            // Desde F3-09 hay además unicidad, y es **por cobro y destino, no por destino**: dos cobros
            // distintos pueden pagar la misma factura —terminar una factura a medias con el segundo
            // cobro es el caso normal de §3.10.1— y de lo mismo una fianza se puede ir cobrando por
            // partes. Lo que no puede repetirse es la misma línea dos veces dentro del mismo cobro.
            //
            // Y el índice es **parcial sobre `vigente`**: anular un cobro y volver a cobrar lo mismo es
            // el camino de §3.10.2 para corregir un cambio equivocado, y la fila anulada no se borra
            // (AGENTS.md §6), así que la unicidad tiene que regir solo entre las líneas que cuentan.
            entidad.HasIndex(e => new { e.CobroId, e.FacturaId })
                .IsUnique()
                .HasFilter("factura_id IS NOT NULL AND vigente")
                .HasDatabaseName("ux_cobro_asignacion_cobro_factura");
            // El reflejo del anterior para la fianza. Son dos índices parciales y no uno compuesto por
            // las dos columnas de destino porque el destino conocido es uno solo: con la otra fila a
            // null, un único índice sobre las cuatro columnas no serviría para ninguna de las dos
            // consultas.
            entidad.HasIndex(e => new { e.CobroId, e.FianzaId })
                .IsUnique()
                .HasFilter("fianza_id IS NOT NULL AND vigente")
                .HasDatabaseName("ux_cobro_asignacion_cobro_fianza");
            entidad.HasIndex(e => e.CobroId);
            entidad.HasOne(e => e.Cobro)
                .WithMany(c => c.Asignaciones)
                .HasForeignKey(e => e.CobroId)
                .OnDelete(DeleteBehavior.Cascade);
            entidad.HasOne(e => e.Factura)
                .WithMany()
                .HasForeignKey(e => e.FacturaId)
                .OnDelete(DeleteBehavior.Restrict);
            entidad.HasOne(e => e.Fianza)
                .WithMany()
                .HasForeignKey(e => e.FianzaId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // F3-09: la fianza (§3.11) y sus devoluciones. Lo cobrado y el saldo no son columnas: salen de
        // `cobro_asignacion` y de `fianza_devolucion`, y por eso la tabla solo guarda lo exigido, la
        // moneda y la decisión que se tomó.
        modelBuilder.Entity<Fianza>(entidad =>
        {
            entidad.ToTable("fianza", tabla =>
            {
                tabla.HasCheckConstraint("ck_fianza_importe_positivo", "importe_exigido > 0");
                // El número de fianzas es un entero de negocio, no un decimal, y no puede ser cero: un
                // contrato sin fianzas no tiene fila aquí, se entiende con la ausencia de la fila.
                tabla.HasCheckConstraint("ck_fianza_numero_positivo", "numero_fianzas > 0");
            });
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.CreatedAt).IsRequired();
            // El motivo lo escribe quien decide devolver y lo lee quien audita el dinero que no se
            // devolvió, así que tiene la misma longitud que el del cobro y el de la factura. El estado
            // y la moneda no llevan nada aquí: los enums van en integer por defecto (AGENTS.md §6) y por
            // eso <c>EstadoFianza</c> fija sus valores en el enum y no los toma de la posición.
            // Una fianza por contrato y moneda. Es lo que hace legible el saldo: dos fianzas del mismo
            // contrato y la misma moneda serían dos números que hay que sumar a mano para responder lo
            // único que el cliente pregunta, cuánto le queda.
            entidad.HasIndex(e => new { e.ContratoId, e.Moneda })
                .IsUnique()
                .HasDatabaseName("ix_fianza_contrato_moneda");
            entidad.HasIndex(e => e.ContratoId);
            entidad.HasOne(e => e.Contrato)
                .WithMany()
                .HasForeignKey(e => e.ContratoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // F3-09: la devolución es un movimiento de salida con su parte devuelta y su parte retenida
        // (§3.6). Las dos sumas, y no una sola, porque el saldo de la fianza baja por las dos.
        modelBuilder.Entity<FianzaDevolucion>(entidad =>
        {
            entidad.ToTable("fianza_devolucion", tabla =>
            {
                // Una devolución negativa no es una devolución: es una devolución al revés, y lo que
                // hace falta para eso es cobrar, que es un cobro asignado a la fianza.
                tabla.HasCheckConstraint(
                    "ck_fianza_devolucion_importe_no_negativo",
                    "importe >= 0 AND importe_descontado >= 0");
                // O se devuelve algo, o se retiene algo, o las dos cosas. Una devolución de cero y de
                // cero no devuelve nada: sería una fila que cuadra y no dice nada.
                tabla.HasCheckConstraint(
                    "ck_fianza_devolucion_importe_al_menos_uno",
                    "importe > 0 OR importe_descontado > 0");
                // El motivo no vive en un CHECK porque depende de si la devolución deja la fianza a
                // cero, y eso no se sabe dentro de esta tabla: lo comprueba el servicio contra el saldo
                // real antes de insertar (§3.11 y el criterio de aceptación de F3-09).
                tabla.HasCheckConstraint(
                    "ck_fianza_devolucion_descontado_con_motivo",
                    "importe_descontado = 0 OR motivo IS NOT NULL");
            });
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.CreatedAt).IsRequired();
            entidad.Property(e => e.Motivo).HasMaxLength(500);
            // El índice que resuelve "cuánto se ha devuelto de esta fianza" en una consulta agrupada,
            // sin N+1, igual que el de factura de `cobro_asignacion` (§3.13).
            entidad.HasIndex(e => e.FianzaId);
            entidad.HasOne(e => e.Fianza)
                .WithMany(f => f.Devoluciones)
                .HasForeignKey(e => e.FianzaId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // El saldo a favor es un saldo del contrato, y el único es por contrato y moneda: es el
        // mismo crédito del mismo contrato el que se actualiza al cobrar y al consumir, y dos filas
        // para lo mismo harían que la deuda del cliente se leyera dos veces.
        modelBuilder.Entity<SaldoAFavor>(entidad =>
        {
            entidad.ToTable("saldo_a_favor", tabla =>
            {
                // Un saldo a favor negativo no es un saldo a favor: es un cobro sin asignar, y eso
                // es la columna excedente del cobro, que sí tiene su propia historia.
                tabla.HasCheckConstraint("ck_saldo_a_favor_no_negativo", "importe >= 0");
            });
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.CreatedAt).IsRequired();
            entidad.HasIndex(e => new { e.ContratoId, e.Moneda })
                .IsUnique();
            entidad.HasOne(e => e.Contrato)
                .WithMany()
                .HasForeignKey(e => e.ContratoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // F3-07: el ajuste por diferencia de cambio (docs/ESPECIFICACION.md §3.10.2, D8). No es
        // dinero recibido —eso vive en cobro_asignacion— sino el movimiento con el que se cierra un
        // pendiente que solo salió porque el cambio no dividía exacto. Por eso es una tabla propia y
        // no una asignación más: un informe que sume «cobrado» no debe estar metiendo aquí un euro
        // que nunca llegó a la caja.
        modelBuilder.Entity<AjusteDiferenciaCambio>(entidad =>
        {
            entidad.ToTable("ajuste_diferencia_cambio", tabla =>
            {
                // Cierra un pendiente: nunca es cero ni negativo, y nunca es una devolución.
                tabla.HasCheckConstraint("ck_ajuste_diferencia_cambio_positivo", "importe > 0");
                // Sin cambio no se puede reconstruir la diferencia, y un cambio de cero no convierte.
                tabla.HasCheckConstraint("ck_ajuste_diferencia_cambio_tipo_cambio", "tipo_cambio_valor > 0");
                // El motivo es lo que separa una diferencia de cambio real de un error de captura.
                tabla.HasCheckConstraint("ck_ajuste_diferencia_cambio_motivo", "length(trim(motivo)) > 0");
                // Igual que en cobro: anulado significa anulado con motivo, usuario y fecha, y no al
                // revés. Así de una fila se sabe si el ajuste cuenta sin mirar tres columnas.
                tabla.HasCheckConstraint(
                    "ck_ajuste_diferencia_cambio_anulacion_completa",
                    "(anulado_at IS NULL) = (anulado_por IS NULL AND motivo_anulacion IS NULL)");
            });
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Motivo).HasMaxLength(500).IsRequired();
            entidad.Property(e => e.MotivoAnulacion).HasMaxLength(500);
            entidad.Property(e => e.CreatedAt).IsRequired();
            entidad.Property(e => e.TipoCambioValor).HasColumnType("numeric(18,6)");
            // La suma de lo ajustado de una página de facturas sale de este índice, sin N+1.
            entidad.HasIndex(e => e.FacturaId);
            entidad.HasOne(e => e.Factura)
                .WithMany()
                .HasForeignKey(e => e.FacturaId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // RLS sobre documento_inmueble (docs/SEGURIDAD.md A.4 y DOC_TECNICA.md §4.3): solo gerente y
        // administrador leen y escriben la fila, con independencia de lo que diga la API.
        modelBuilder.Entity<DocumentoInmueble>(entidad =>
        {
            entidad.ToTable("documento_inmueble");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.NombreOriginal).HasMaxLength(200).IsRequired();
            entidad.Property(e => e.NombreArchivo).HasMaxLength(64).IsRequired();
            entidad.Property(e => e.TipoContenido).HasMaxLength(60).IsRequired();
            entidad.Property(e => e.CreatedAt).IsRequired();
            // El nombre en disco es único: es la ruta del archivo, y dos filas apuntando al mismo
            // archivo harían que una descarga sirviera el documento de otro inmueble.
            entidad.HasIndex(e => e.NombreArchivo).IsUnique();
            // Una versión por categoría: la versión siguiente es la que hay, y el índice único es
            // la red de seguridad si dos subidas a la vez cogieran el mismo número.
            entidad.HasIndex(e => new { e.InmuebleId, e.Categoria, e.Version }).IsUnique();
            entidad.HasIndex(e => e.InmuebleId);
            entidad.HasOne<Inmueble>()
                .WithMany()
                .HasForeignKey(e => e.InmuebleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // F4-02: `envio`, el correo que salió con su documento adjunto (docs/ESPECIFICACION.md §3.14).
        modelBuilder.Entity<Envio>(entidad =>
        {
            entidad.ToTable("envio");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.DestinatarioEmail).HasMaxLength(320).IsRequired();
            entidad.Property(e => e.DestinatarioNombre).HasMaxLength(200);
            entidad.Property(e => e.Asunto).HasMaxLength(300).IsRequired();
            entidad.Property(e => e.Cuerpo).IsRequired();
            entidad.Property(e => e.Idioma).HasMaxLength(10).IsRequired();
            entidad.Property(e => e.NombreArchivo).HasMaxLength(200).IsRequired();
            // El motivo de un fallo es corto porque no es un mensaje de excepción: o es el texto de
            // CorreoNoConfiguradoException o es «TipoDeExcepcion sin detalle.» (criterio de F3-01,
            // DOC_TECNICA §8.0.1).
            entidad.Property(e => e.UltimoError).HasMaxLength(500);
            entidad.Property(e => e.CreatedAt).IsRequired();
            entidad.Property(e => e.Intentos).IsRequired();

            // Los índices son los tres filtros que §3.13 cablea y la lista de envíos por documento.
            //
            // «(tipo, documento, año)» es la búsqueda del envío de un documento concreto: la usa la
            // columna estado_envio de la vista única, el detalle de un documento y el antidiplicado
            // de §3.14. Va con el año porque un estado de cuenta es (cliente, año).
            entidad.HasIndex(e => new { e.TipoDocumento, e.DocumentoId, e.DocumentoAnio })
                .HasDatabaseName("ix_envio_tipo_documento");

            // El filtro «envío fallido» de §3.13 mira solo el estado.
            entidad.HasIndex(e => e.Estado).HasDatabaseName("ix_envio_estado");

            // El antidiplicado de §3.14 compara documento, año y destinatario. No es único a
            // propósito: forzar un reenvío tiene que poder escribir una segunda fila, y lo que evita
            // el duplicado es la comprobación previa del servicio, no una restricción que solo daría
            // un error de restricción sin decir a quién se lo mandó ya.
            entidad.HasIndex(e => new { e.TipoDocumento, e.DocumentoId, e.DocumentoAnio, e.DestinatarioEmail })
                .HasDatabaseName("ix_envio_documento_destinatario");

            // F4-03: el lote es esta columna, no una tabla (§3.14). El índice es el que hace baratos el
            // progreso, el recuento y el reintento, que son las tres agregaciones del lote por `lote_id`.
            entidad.HasIndex(e => e.LoteId).HasDatabaseName("ix_envio_lote");
        });

        // F4-02: `plantilla_mensaje`, el texto de los correos por tipo de mensaje e idioma (§3.14).
        modelBuilder.Entity<PlantillaMensaje>(entidad =>
        {
            entidad.ToTable("plantilla_mensaje");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Idioma).HasMaxLength(10).IsRequired();
            entidad.Property(e => e.Asunto).HasMaxLength(300).IsRequired();
            entidad.Property(e => e.Cuerpo).IsRequired();
            entidad.Property(e => e.CreatedAt).IsRequired();

            // Una plantilla por tipo de mensaje e idioma: es lo que hace el índice único del
            // compuesto, y lo que hace que guardar sea un PUT que crea o edita en vez de dos
            // endpoints distintos con dos formas de dejar la misma fila duplicada.
            entidad.HasIndex(e => new { e.Tipo, e.Idioma }).IsUnique();
        });

        // F4-05: `recordatorio`, el aviso de pago por cliente y unidad (docs/ESPECIFICACION.md §3.14).
        modelBuilder.Entity<Recordatorio>(entidad =>
        {
            entidad.ToTable("recordatorio");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.MotivoOmision).HasMaxLength(300);
            entidad.HasOne(e => e.Cliente)
                .WithMany()
                .HasForeignKey(e => e.ClienteId)
                .OnDelete(DeleteBehavior.Restrict);
            entidad.HasOne(e => e.Unidad)
                .WithMany()
                .HasForeignKey(e => e.UnidadId)
                .OnDelete(DeleteBehavior.Restrict);
            entidad.HasOne(e => e.Envio)
                .WithMany()
                .HasForeignKey(e => e.EnvioId)
                .OnDelete(DeleteBehavior.Restrict);

            // El único de (cliente, unidad, fecha) es la idempotencia del trabajo (§8.0): correrlo
            // dos veces el mismo día choca aquí en vez de mandar dos avisos. En la migración se crea
            // a mano con NULLS NOT DISTINCT, porque `unidad_id` puede ser nula y en PostgreSQL los
            // nulos de un único no chocan entre sí: sin eso, un cliente sin contrato con unidad
            // recibiría un aviso por día por cada intento. Es el mismo trato que
            // ix_factura_periodo_unico y por el mismo motivo.
            entidad.HasIndex(e => new { e.ClienteId, e.UnidadId, e.Fecha })
                .IsUnique()
                .HasDatabaseName("ix_recordatorio_cliente_unidad_fecha");

            // Los dos filtros de la lista de §3.14: lo que está por revisar y lo que se mandó.
            entidad.HasIndex(e => e.Estado).HasDatabaseName("ix_recordatorio_estado");
            entidad.HasIndex(e => e.Fecha).HasDatabaseName("ix_recordatorio_fecha");
            entidad.HasIndex(e => e.ClienteId).HasDatabaseName("ix_recordatorio_cliente");
        });

        // docs/DOC_TECNICA.md §4.2: concurrencia optimista con la columna de sistema xmin y sin
        // columna extra. Npgsql mapea a xmin toda propiedad uint OnAddOrUpdate marcada como token
        // de concurrencia (lo mismo que IsRowVersion).
        // Auditoria se queda sin token: es append-only, no hay UPDATE que lo use, y EF Core
        // pediría el valor con RETURNING, que PostgreSQL filtra con la política de SELECT de la
        // tabla; con RETURNING, un empleado no podría registrar su propia auditoría.
        foreach (var tipoEntidad in modelBuilder.Model.GetEntityTypes()
            .Where(t => t.ClrType != typeof(Auditoria)))
        {
            var xmin = tipoEntidad.AddProperty("xmin", typeof(uint));
            xmin.ValueGenerated = ValueGenerated.OnAddOrUpdate;
            xmin.IsConcurrencyToken = true;
        }
    }
}