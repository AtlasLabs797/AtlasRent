using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Contratos;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Inventario;
using AtlasRent.Tests.Reservas;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Tests.Liquidacion;

/// <summary>
/// Atajos de las pruebas de F2-09: correr el trabajo de fin con un reloj propio y leer de la base lo
/// que dejó (fecha efectiva, candado, liquidación, alerta y auditoría).
/// <para>
/// El trabajo se monta a mano, como el de renovación, para poder mover el reloj y ver qué pasa días
/// después sin esperar días.
/// </para>
/// </summary>
internal static class AyudaLiquidacion
{
    /// <summary>Sesión de un rol, como las de inventario y contratos.</summary>
    public static Task<SesionCliente> SesionAsync(
        ApiTestFactoryBloqueos api,
        ContenedorBdFixture bd,
        string rol,
        CancellationToken ct) =>
        AyudaInventario.SesionAsync(api, bd, rol, ct);

    /// <summary>
    /// Usuario y sesión del mismo rol juntos. Hace falta cuando la prueba mira quién hizo algo —la
    /// liquidación guarda quién liquida—: con la sesión suelta no se sabe qué identificador tiene la
    /// persona que la abrió.
    /// </summary>
    public static async Task<(Usuario Usuario, SesionCliente Sesion)> SesionDeAsync(
        ApiTestFactoryBloqueos api,
        ContenedorBdFixture bd,
        string rol,
        CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, AyudaInventario.Contrasena, rol: rol, ct: ct);
        var sesion = await AyudaPermisos.SesionAsync(api, usuario, AyudaInventario.Contrasena, ct);

        return (usuario, sesion);
    }

    /// <summary>
    /// Una pasada del trabajo de las 07:00 con su propio contexto y su propio reloj.
    /// <paramref name="avanzar"/> mueve el reloj: es lo que deja comprobar que el fin ocurre el día de
    /// la fecha efectiva sin esperar a ese día.
    /// </summary>
    public static async Task<ResumenFines> EjecutarTrabajoAsync(
        ContenedorBdFixture bd,
        CancellationToken ct,
        TimeSpan? avanzar = null)
    {
        var reloj = new RelojPrueba();
        if (avanzar is { } delta)
        {
            reloj.Avanzar(delta);
        }

        await using var contexto = bd.CrearContexto(bd.CadenaApp, new ContextoSesion());
        return await ServicioDe(contexto, reloj).FinalizarVencidosAsync(ct);
    }

    /// <summary>El servicio del trabajo sobre un contexto ya abierto, que es como corre el planificador.</summary>
    public static ServicioFines ServicioDe(AtlasRentDbContext contexto, TimeProvider reloj) =>
        new(
            contexto,
            new AuditoriaService(contexto),
            new ServicioAlertas(contexto, reloj),
            new ServicioDisponibilidad(contexto, reloj),
            reloj);

    /// <summary>Contrato activo con su unidad, listo para darle de baja.</summary>
    public static async Task<(Guid UnidadId, Guid ContratoId)> ContratoActivoAsync(
        SesionCliente empleado,
        SesionCliente gerente,
        ContenedorBdFixture bd,
        DateOnly inicio,
        CancellationToken ct)
    {
        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);

        var contratoId = await AyudaContrato.CrearAsync(
            empleado,
            AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, inicio),
            ct);

        await AyudaContrato.AceptarAsync(empleado, contratoId, "solicitud", new { }, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "aprobacion", null, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "activacion", null, ct);

        return (unidadId, contratoId);
    }

    /// <summary>
    /// Baja y fin por el botón de la API, que es el camino que también existe para una persona: el
    /// trabajo de las 07:00 no es la única forma de finalizar, solo la que no depende de que alguien se
    /// acuerde.
    /// </summary>
    public static async Task DarBajaYFinalizarAsync(
        SesionCliente gerente,
        Guid contratoId,
        DateOnly fechaEfectiva,
        CancellationToken ct)
    {
        await AyudaContrato.AceptarAsync(
            gerente, contratoId, "baja", new { fechaFin = fechaEfectiva.ToString("yyyy-MM-dd") }, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "finalizacion", null, ct);
    }

    // ---------------------------------------------------------------- lecturas

    public static Task<DateOnly?> FechaFinRealAsync(ContenedorBdFixture bd, Guid contratoId, CancellationToken ct) =>
        EnContextoAsync(bd, c => c.Contrato
            .Where(x => x.Id == contratoId)
            .Select(x => x.FechaFinReal)
            .SingleAsync(ct));

    /// <summary>El candado del contrato: la fila de rango, que no se borra, solo se apaga.</summary>
    public static Task<OcupacionRango> CandadoAsync(ContenedorBdFixture bd, Guid contratoId, CancellationToken ct) =>
        EnContextoAsync(bd, c => c.OcupacionRango.AsNoTracking()
            .SingleAsync(r => r.Origen == OrigenOcupacion.Contrato && r.OrigenId == contratoId, ct));

    /// <summary>La liquidación escrita, si la hay.</summary>
    public static Task<ContratoLiquidacion?> LiquidacionAsync(
        ContenedorBdFixture bd,
        Guid contratoId,
        CancellationToken ct) =>
        EnContextoAsync(bd, c => c.ContratoLiquidacion.AsNoTracking()
            .FirstOrDefaultAsync(l => l.ContratoId == contratoId, ct));

    /// <summary>La alerta persistente del contrato, viva o cerrada.</summary>
    public static Task<Alerta?> AlertaAsync(ContenedorBdFixture bd, Guid contratoId, CancellationToken ct) =>
        EnContextoAsync(bd, c => c.Alerta.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Clave == ServicioFines.ClaveFirmezaPendiente(contratoId), ct));

    /// <summary>Cuántas filas de alerta hay con esa clave. Tiene que ser 1: persistente no es duplicar.</summary>
    public static Task<int> CuantasAlertasAsync(ContenedorBdFixture bd, Guid contratoId, CancellationToken ct) =>
        EnContextoAsync(bd, c => c.Alerta.AsNoTracking()
            .CountAsync(a => a.Clave == ServicioFines.ClaveFirmezaPendiente(contratoId), ct));

    /// <summary>
    /// Las filas de auditoría de un contrato, con rol de gerente porque la política de lectura de
    /// `auditoria` solo deja pasar a admin y gerente. Una fila sin usuario —la del trabajo— se lee
    /// exactamente igual que una con usuario, y por eso el test puede mirar que el trabajo no se
    /// disfrazó de persona.
    /// <para>
    /// La lectura va dentro de una transacción explícita a propósito: <c>ContextoTransaccionInterceptor</c>
    /// fija <c>app.rol</c> al empezar una transacción, no al empezar una consulta. Sin abrirla, RLS no
    /// ve el rol y devuelve cero filas, que es un falso "el trabajo no auditó nada".
    /// </para>
    /// </summary>
    public static async Task<List<Auditoria>> AuditoriasAsync(
        ContenedorBdFixture bd,
        Guid contratoId,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(
            bd.CadenaApp, new ContextoSesion { Rol = Roles.Gerente });
        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        var filas = await contexto.Auditoria.AsNoTracking()
            .Where(a => a.Entidad == "contrato" && a.EntidadId == contratoId.ToString("D"))
            .OrderBy(a => a.Secuencia)
            .ToListAsync(ct);

        await transaccion.CommitAsync(ct);

        return filas;
    }

    /// <summary>Filas de auditoría de una acción concreta sobre un contrato.</summary>
    public static async Task<List<Auditoria>> AuditoriasDeAccionAsync(
        ContenedorBdFixture bd,
        Guid contratoId,
        string accion,
        CancellationToken ct) =>
        [.. (await AuditoriasAsync(bd, contratoId, ct)).Where(a => a.Accion == accion)];

    public static async Task<T> EnContextoAsync<T>(
        ContenedorBdFixture bd,
        Func<AtlasRentDbContext, Task<T>> consulta)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        return await consulta(contexto);
    }
}