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
    /// siquiera existen las tablas todavía (el cobro es de F3-01).
    /// <para>
    /// La comprobación se hace sobre <c>information_schema</c> y no sobre el contexto, porque
    /// consultarlas a través de EF no se puede escribir todavía. Cuando F3-01 cree
    /// <c>cobro</c> y <c>factura</c>, este mismo test empezará a contar filas de verdad, y es
    /// entonces cuando empezaría a ser capaz de fallar.
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

        var id = await AyudaReserva.CrearAsync(
            sesion,
            AyudaReserva.Corta(
                clienteId,
                unidadId,
                fecha,
                anticipo: 2500m,
                anticipoNota: "Se acordado en efectivo al reservar; si se cancela antes del día anterior se devuelve"),
            ct);

        // El anticipo sí está guardado, con su importe y su nota.
        var reserva = await AyudaReserva.Json(
            await sesion.GetAsync($"/api/v1/reservas/{id:D}", ct), ct);
        reserva.GetProperty("anticipoImporte").GetDecimal().Should().Be(2500m);
        reserva.GetProperty("anticipoNota").GetString().Should().Contain("devuelve");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var tablasDeDinero = await contexto.Database
            .SqlQueryRaw<string>(
                """
                SELECT table_name AS "Nombre"
                FROM information_schema.tables
                WHERE table_schema = 'public'
                  AND table_name IN ('cobro', 'factura', 'factura_borrador', 'asiento')
                """)
            .ToListAsync(ct);

        foreach (var tabla in tablasDeDinero)
        {
            // El SQL sale de una lista literal: el nombre de la tabla no se interpola en la
            // consulta (EF1002 lo marca como error, y con razón).
            var consulta = tabla switch
            {
                "cobro" => """SELECT COUNT(*) AS "Total" FROM cobro""",
                "factura" => """SELECT COUNT(*) AS "Total" FROM factura""",
                "factura_borrador" => """SELECT COUNT(*) AS "Total" FROM factura_borrador""",
                "asiento" => """SELECT COUNT(*) AS "Total" FROM asiento""",
                _ => throw new InvalidOperationException($"Tabla de dinero no prevista: {tabla}"),
            };

            (await contexto.Database.SqlQueryRaw<long>(consulta).SingleAsync(ct)).Should().Be(0,
                $"un anticipo no escribe nada en {tabla}: el cobro se registra al pagar la factura");
        }

        tablasDeDinero.Should().BeEmpty(
            "a día de hoy no existe ninguna de esas tablas: no hay nada donde un anticipo pudiera "
            + "haberse escrito por accidente");
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