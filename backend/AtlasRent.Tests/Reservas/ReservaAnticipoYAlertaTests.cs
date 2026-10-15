using System.Data;
using System.Globalization;
using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Jobs;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Reservas;

/// <summary>
/// F2-03: el anticipo de la reserva y el aviso de los 7 días.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ReservaAnticipoYAlertaTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// D10: el anticipo es una nota con número. No hay pasarela de pago, así que una reserva con
    /// anticipo no puede haber generado un cobro ni una factura: ni los escribe la aplicación ni
    /// siquiera existen esas tablas todavía (el cobro es de F3-01).
    /// <para>
    /// Lo que se comprueba es doble. Primero, que el anticipo sí está guardado en la reserva, con su
    /// importe y su nota. Segundo, que <b>ninguna tabla de efecto económico que exista</b> haya
    /// cambiado al crearla, y se busca por nombre con un <c>LIKE</c> para que la lista crezca sola
    /// cuando F3 cree las suyas, sin que este test haya que reescribirse.
    /// </para>
    /// <para>
    /// Los recuentos son antes y después, no absolutos, porque la base de pruebas la comparten todas
    /// las clases de la colección y otras han dejado filas en ellas. Lo que importa no es que la
    /// tabla esté a cero, sino que crear la reserva no la haya movido.
    /// </para>
    /// <para>
    /// <b>Lo que este test todavía no puede comprobar, y es importante decirlo:</b> el criterio de
    /// aceptación completo de F2-03 es "el anticipo no aparece en ningún saldo ni informe económico",
    /// y un saldo es algo que hoy no existe. <c>cobro</c> y <c>factura</c> son de F3-01, así que
    /// hasta que F3 no las cree, aquí solo se puede afirmar que no se ha escrito nada donde hoy se
    /// puede escribir. Cuando existan, este mismo test empezará a mirarlas de verdad sin cambiar una
    /// línea.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ElAnticipoNoEsUnCobro()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var fecha = AyudaReserva.Dia(140);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        var economicas = await TablasDeDineroAsync(contexto, ct);
        var antes = await ContarAsync(contexto, economicas, ct);

        var id = await AyudaReserva.CrearAsync(
            sesion,
            AyudaReserva.Corta(
                clienteId,
                unidadId,
                fecha,
                anticipo: 2500m,
                anticipoNota: "Se acordado en efectivo al reservar; si se cancela antes del día anterior se devuelve"),
            ct);

        // El anticipo sí está guardado, con su importe y su nota: es una nota, no un cobro, y por eso
        // vive en la reserva y no en otro sitio.
        var reserva = await AyudaReserva.Json(
            await sesion.GetAsync($"/api/v1/reservas/{id:D}", ct), ct);
        reserva.GetProperty("anticipoImporte").GetDecimal().Should().Be(2500m);
        reserva.GetProperty("anticipoNota").GetString().Should().Contain("devuelve");

        var despues = await ContarAsync(contexto, economicas, ct);
        foreach (var tabla in economicas)
        {
            despues[tabla].Should().Be(
                antes[tabla],
                $"un anticipo no escribe nada en {tabla}: el cobro se registra al pagar la factura, "
                + "que es de F3. Ojo con el alcance de esto: mientras F3 no cree `cobro` ni "
                + "`factura`, esta comprobación solo demuestra que no se ha escrito en lo que existe "
                + "hoy, que no es el criterio completo de F2-03");
        }
    }

    /// <summary>
    /// Las tablas donde un movimiento de dinero dejaría rastro, buscadas por prefijo sobre
    /// <c>information_schema</c>. Hoy devuelve las de F2 que ya guardan importes
    /// (<c>contrato_liquidacion</c>, <c>cotizacion</c>, <c>cotizacion_linea</c>) y nada más; cuando
    /// F3 cree <c>cobro</c>, <c>factura</c>, <c>asiento</c> o lo que haga falta, entrarán solas.
    /// <para>
    /// Es una búsqueda por nombre y, por lo tanto, heurística: si F3 le pone a una tabla un nombre
    /// que no case con ninguno de estos patrones, este test no la mirará. Si eso llega a pasar, lo
    /// que hay que hacer es añadir el patrón, no escribir un test nuevo.
    /// </para>
    /// </summary>
    private static async Task<List<string>> TablasDeDineroAsync(
        AtlasRentDbContext contexto,
        CancellationToken ct) =>
        await contexto.Database
            .SqlQueryRaw<string>(
                """
                SELECT table_name AS "Nombre"
                FROM information_schema.tables
                WHERE table_schema = 'public'
                  AND table_type = 'BASE TABLE'
                  AND (
                      table_name LIKE 'cobro%'
                      OR table_name LIKE 'factura%'
                      OR table_name LIKE 'asiento%'
                      OR table_name LIKE 'pago%'
                      OR table_name LIKE 'cotizacion%'
                      OR table_name LIKE '%liquidacion'
                  )
                ORDER BY table_name
                """)
            .ToListAsync(ct);

    private static async Task<Dictionary<string, long>> ContarAsync(
        AtlasRentDbContext contexto,
        List<string> tablas,
        CancellationToken ct)
    {
        var cuentas = new Dictionary<string, long>();

        // Por ADO.NET y no por EF: un identificador de tabla no se puede pasar como parámetro, y EF
        // (EF1002) no deja ni `SqlQueryRaw` con interpolación ni `FromSqlRaw` sobre un escalar.
        // La conexión se deja como estaba.
        var conexion = contexto.Database.GetDbConnection();
        var yaEstabaAbierta = conexion.State == ConnectionState.Open;
        if (!yaEstabaAbierta)
        {
            await conexion.OpenAsync(ct);
        }

        try
        {
            foreach (var tabla in tablas)
            {
                // El nombre va metido en el texto porque no hay otra forma, y sale de
                // information_schema de este propio esquema: antes de usarlo se comprueba que sea un
                // identificador normal, así que no hay por dónde colarse nada en la consulta.
                tabla.Should().MatchRegex("^[a-z_][a-z0-9_]*$", $"'{tabla}' es un nombre de tabla");

                await using var comando = conexion.CreateCommand();
                comando.CommandText = $"""SELECT count(*) FROM "{tabla}" """;
                var total = await comando.ExecuteScalarAsync(ct);

                cuentas[tabla] = Convert.ToInt64(total, CultureInfo.InvariantCulture);
            }
        }
        finally
        {
            if (!yaEstabaAbierta)
            {
                await conexion.CloseAsync();
            }
        }

        return cuentas;
    }

    /// <summary>
    /// El trabajo diario avisa de las reservas activas que entran dentro de 7 días, ni un día antes
    /// ni un día después, y correrlo dos veces no deja dos alertas: la clave de deduplicación lleva
    /// la reserva.
    /// <para>
    /// Se ejecuta el trabajo directamente en vez de esperar al planificador, que es apagado en las
    /// pruebas; lo que se prueba es el trabajo, no el cron. El cron se comprueba en
    /// <c>ProgramacionJobsTests</c>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task LaAlertaDeSieteDiasNoSeDuplica()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var hoy = TiempoNegocio.Hoy();

        var enSiete = await AyudaReserva.CrearAsync(
            sesion, AyudaReserva.Mensual(clienteId, await AyudaReserva.UnidadAsync(sesion, ct), hoy.AddDays(7)), ct);
        var enOcho = await AyudaReserva.CrearAsync(
            sesion, AyudaReserva.Mensual(clienteId, await AyudaReserva.UnidadAsync(sesion, ct), hoy.AddDays(8)), ct);
        var enSeis = await AyudaReserva.CrearAsync(
            sesion, AyudaReserva.Mensual(clienteId, await AyudaReserva.UnidadAsync(sesion, ct), hoy.AddDays(6)), ct);

        var reloj = new RelojPrueba();
        await using var contexto = bd.CrearContexto(bd.CadenaApp, new ContextoSesion());
        var trabajo = new TrabajoAlertaReserva(contexto, new ServicioAlertas(contexto, reloj), reloj);

        await trabajo.EjecutarAsync(ct);
        await trabajo.EjecutarAsync(ct);

        var claves = new[]
        {
            TrabajoAlertaReserva.Clave(enSiete),
            TrabajoAlertaReserva.Clave(enOcho),
            TrabajoAlertaReserva.Clave(enSeis),
        };
        var alertas = await contexto.Alerta.AsNoTracking()
            .Where(a => claves.Contains(a.Clave))
            .ToListAsync(ct);

        alertas.Should().ContainSingle("solo avisa de la que entra dentro de 7 días");
        alertas[0].Clave.Should().Be(TrabajoAlertaReserva.Clave(enSiete));
        alertas[0].Tipo.Should().Be("reserva");
        alertas[0].Repeticiones.Should().Be(2,
            "dos ejecuciones del trabajo son una alerta con dos repeticiones, no dos alertas");
    }

    /// <summary>
    /// Una reserva que no está activa no entra en el aviso: una cancelada cuya entrada prevista sea
    /// dentro de 7 días ya no tiene nada que avisar.
    /// </summary>
    [Fact]
    public async Task UnaReservaCanceladaNoSeAvisa()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var entrada = TiempoNegocio.Hoy().AddDays(7);

        var id = await AyudaReserva.CrearAsync(
            sesion, AyudaReserva.Mensual(clienteId, unidadId, entrada), ct);
        (await sesion.DeleteAsync($"/api/v1/reservas/{id:D}", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var reloj = new RelojPrueba();
        await using var contexto = bd.CrearContexto(bd.CadenaApp, new ContextoSesion());
        await new TrabajoAlertaReserva(contexto, new ServicioAlertas(contexto, reloj), reloj)
            .EjecutarAsync(ct);

        (await contexto.Alerta.CountAsync(a => a.Clave == TrabajoAlertaReserva.Clave(id), ct))
            .Should().Be(0, "una reserva cancelada no genera avisos");
    }

    private async Task<SesionCliente> SesionAsync(CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }
}