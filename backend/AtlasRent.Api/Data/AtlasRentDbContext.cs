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

    // F1-08: configuración. Los porcentajes de ITBIS y retención son filas de estas tablas, no
    // constantes del código (D14).
    public DbSet<Serie> Serie => Set<Serie>();

    public DbSet<NumeroSerie> NumeroSerie => Set<NumeroSerie>();

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

        // F1-12: registro de lo que falló y que hay que mirar. La clave es única porque el criterio
        // de aceptación de F1-12 es "fallo → alerta" sin duplicar: si el backup falla cada hora, sin
        // esa restricción la tabla crece y la señal desaparece.
        modelBuilder.Entity<Alerta>(entidad =>
        {
            entidad.ToTable("alerta");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Clave).IsRequired();
            entidad.Property(e => e.Tipo).IsRequired();
            entidad.Property(e => e.Mensaje).IsRequired();
            entidad.Property(e => e.OcurridoAt).IsRequired();
            entidad.HasIndex(e => e.Clave).IsUnique();
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

        // F1-08: series y numeración. El contador de la serie lo mueve el UPDATE ... RETURNING de
        // ServicioSeries, dentro de la transacción de emisión; numero_serie guarda qué números se
        // han emitido y cuáles están anulados, sin liberar nunca ninguno (docs/DOC_TECNICA.md §4.3).
        modelBuilder.Entity<Serie>(entidad =>
        {
            entidad.ToTable("serie");
            entidad.HasKey(e => e.Id);
            entidad.Property(e => e.Prefijo).HasMaxLength(16);
            entidad.Property(e => e.PlantillaPdf).HasMaxLength(120);
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