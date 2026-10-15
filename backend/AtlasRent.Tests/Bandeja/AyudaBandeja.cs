using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Contratos;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Inventario;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Tests.Bandeja;

/// <summary>
/// Atajos de las pruebas de F2-10: correr el trabajo de avisos con su propio reloj, dejar una
/// reserva en el estado que solo produce la activación de un contrato y leer lo que quedó escrito.
/// <para>
/// El trabajo se monta a mano, como el de renovación (F2-06) y el de fin (F2-09), para poder correrlo
/// dos veces seguidas sin esperar a las 08:00 y comprobar que la segunda pasada no duplica nada.
/// </para>
/// </summary>
internal static class AyudaBandeja
{
    /// <summary>Sesión de un rol, como las de inventario y contratos.</summary>
    public static Task<SesionCliente> SesionAsync(
        ApiTestFactoryBloqueos api,
        ContenedorBdFixture bd,
        string rol,
        CancellationToken ct) =>
        AyudaInventario.SesionAsync(api, bd, rol, ct);

    // ---------------------------------------------------------------- el trabajo de avisos

    /// <summary>
    /// Una pasada del trabajo de las 08:00 con su propio contexto y su propio reloj. Sin sesión,
    /// igual que en el planificador: es lo que permite ver que el aviso no depende de que haya una
    /// persona conectada.
    /// </summary>
    public static async Task<ResumenAvisos> EjecutarAvisosAsync(
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
        return await ServicioDe(contexto, reloj).AvisarAsync(ct);
    }

    /// <summary>El servicio del trabajo sobre un contexto ya abierto, que es como corre el planificador.</summary>
    public static ServicioAvisosOperacion ServicioDe(AtlasRentDbContext contexto, TimeProvider reloj) =>
        new(contexto, new ServicioAlertas(contexto, reloj), reloj);

    // ---------------------------------------------------------------- estado que arma la prueba

    /// <summary>
    /// Mueve la fecha de fin vigente del contrato a la que se le pase, por la base de datos.
    /// <para>
    /// Se hace así a propósito: lo que estas pruebas comprueban es la clave de deduplicación del
    /// barrido y que el aviso sale, no la aritmética de meses que usa
    /// <c>ServicioContratos</c> para calcular <c>FechaFinVigente</c>. Encadenar las dos cosas haría
    /// que un cambio en el cálculo de la duración del contrato rompiera un test que no va de eso, y
    /// además esa aritmética tiene su propio día de prueba (el 29 de febrero).
    /// </para>
    /// </summary>
    public static async Task PonerFinVigenteAsync(
        ContenedorBdFixture bd,
        Guid contratoId,
        DateOnly finVigente,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var contrato = await contexto.Contrato.FirstAsync(c => c.Id == contratoId, ct);
        contrato.FechaFinVigente = finVigente;
        await contexto.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Deja la reserva en <see cref="EstadoReserva.EnConflicto"/> sin pasar por la activación de un
    /// contrato que se solape con ella.
    /// <para>
    /// Lo mismo que el ajuste de la fecha de fin y por el mismo motivo: el estado lo pone
    /// <c>ServicioReservas.MarcarEnConflictoAsync</c> cuando un contrato activo pisa la reserva, y esa
    /// transición ya tiene su propia prueba en F2-05. Aquí lo que importa es que la reserva está en
    /// conflicto cuando el trabajo corre, y el candado —que el servicio real <i>no</i> suelta— se
    /// deja como está porque tampoco se comprueba aquí.
    /// </para>
    /// </summary>
    public static async Task PonerEnConflictoAsync(
        ContenedorBdFixture bd,
        Guid reservaId,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var reserva = await contexto.Reserva.FirstAsync(r => r.Id == reservaId, ct);
        reserva.Estado = EstadoReserva.EnConflicto;
        await contexto.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Inserta una versión de contrato, como si el contrato hubiera pasado por el historial de
    /// cambios y lo que se pide aprobar sea distinto de lo que tiene pactado hoy.
    /// <para>
    /// No hay ningún camino de negocio que deje un contrato activo con versiones y a la vez
    /// pendiente de aprobación —un contrato vigente no vuelve atrás— y estas pruebas no inventan
    /// estados imposibles: la fila se escribe porque lo que se está probando es que la bandeja lee el
    /// "valor actual" de <c>contrato_version</c> y lo enseña junto al propuesto, y ese cable tiene
    /// que poder probarse aunque hoy el segundo caso no se dé. La fila es historial y no se borra
    /// nunca, así que tampoco falsea nada después.
    /// </para>
    /// </summary>
    public static async Task InsertarVersionAsync(
        ContenedorBdFixture bd,
        Guid contratoId,
        decimal rentaAnterior,
        decimal rentaNueva,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        // MaxAsync sobre una secuencia vacía de enteros no-nullable no devuelve 0: devuelve null y
        // revienta al desempaquetar. El número se calcula en memoria por eso, que además son cuatro
        // filas como mucho.
        var numeros = await contexto.ContratoVersion.AsNoTracking()
            .Where(v => v.ContratoId == contratoId)
            .Select(v => v.Numero)
            .ToListAsync(ct);

        contexto.ContratoVersion.Add(new ContratoVersion
        {
            Id = Guid.NewGuid(),
            ContratoId = contratoId,
            Numero = numeros.Count == 0 ? 1 : numeros.Max() + 1,
            Cambio = "Renta ajustada",
            RentaAnterior = rentaAnterior,
            RentaNueva = rentaNueva,
            Moneda = Moneda.DOP,
            Alcance = "Afecta solo a facturas futuras.",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await contexto.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- lecturas

    /// <summary>Las filas de alerta que hay con una clave concreta, vivas o cerradas.</summary>
    public static Task<List<Alerta>> AlertasDeAsync(
        ContenedorBdFixture bd,
        string clave,
        CancellationToken ct) =>
        EnContextoAsync(bd, c => c.Alerta.AsNoTracking()
            .Where(a => a.Clave == clave)
            .OrderBy(a => a.OcurridoAt)
            .ToListAsync(ct));

    /// <summary>Las solicitudes de un contrato, que no se borran nunca (AGENTS.md §6).</summary>
    public static Task<List<SolicitudAprobacion>> SolicitudesDeAsync(
        ContenedorBdFixture bd,
        Guid contratoId,
        CancellationToken ct) =>
        EnContextoAsync(bd, c => c.SolicitudAprobacion.AsNoTracking()
            .Where(s => s.ContratoId == contratoId)
            .OrderBy(s => s.SolicitadoAt)
            .ToListAsync(ct));

    public static async Task<T> EnContextoAsync<T>(
        ContenedorBdFixture bd,
        Func<AtlasRentDbContext, Task<T>> consulta)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        return await consulta(contexto);
    }
}