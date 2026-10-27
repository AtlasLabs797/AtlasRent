using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AtlasRent.Tests.Contratos;

/// <summary>
/// BUG-026 (§3.4): el contrato nace de una reserva, la convierte al activarse sin conflicto, la
/// corta se completa y la mensual con el contrato sin activar pasa a conflicto al llegar su entrada.
/// BUG-049: no se activa contrato en una unidad no alquilable.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ContratoDesdeReservaTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    [Fact]
    public async Task ActivarElContratoDeLaReservaLaConvierteSinConflicto()
    {
        var ct = TestContext.Current.CancellationToken;
        var (empleado, gerente) = await SesionesAsync(ct);
        using var _1 = empleado;
        using var _2 = gerente;
        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);
        var inicio = AyudaContrato.Dia(500);

        var reservaId = await AyudaReserva.CrearAsync(empleado, AyudaReserva.Mensual(clienteId, unidadId, inicio), ct);
        var contratoId = await CrearConReservaAsync(empleado, clienteId, unidadId, facturadoraId, inicio, reservaId, ct);
        await AyudaContrato.AceptarAsync(empleado, contratoId, "solicitud", new { }, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "aprobacion", null, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "activacion", null, ct);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Reserva.AsNoTracking().SingleAsync(r => r.Id == reservaId, ct)).Estado
            .Should().Be(EstadoReserva.Convertida);
        (await contexto.OcupacionRango.AsNoTracking().AnyAsync(r => r.OrigenId == reservaId && r.Bloquea, ct))
            .Should().BeFalse("el candado de la reserva lo sustituye el del contrato");
        (await contexto.OcupacionRango.AsNoTracking()
            .AnyAsync(r => r.Origen == OrigenOcupacion.Contrato && r.OrigenId == contratoId && r.Bloquea, ct))
            .Should().BeTrue();
    }

    [Fact]
    public async Task ElContratoSoloPuedeNacerDeUnaReservaDeSuMismaUnidadYCliente()
    {
        var ct = TestContext.Current.CancellationToken;
        var (empleado, gerente) = await SesionesAsync(ct);
        using var _1 = empleado;
        using var _2 = gerente;
        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var otroCliente = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var otraUnidad = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);
        var inicio = AyudaContrato.Dia(510);
        var reservaId = await AyudaReserva.CrearAsync(empleado, AyudaReserva.Mensual(clienteId, unidadId, inicio), ct);

        (await empleado.PostAsync("/api/v1/contratos", Cuerpo(otroCliente, unidadId, facturadoraId, inicio, reservaId), ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await empleado.PostAsync("/api/v1/contratos", Cuerpo(clienteId, otraUnidad, facturadoraId, inicio, reservaId), ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task LaCortaPasadaSeCompletaYLaFuturaNo()
    {
        var ct = TestContext.Current.CancellationToken;
        var (empleado, gerente) = await SesionesAsync(ct);
        using var _1 = empleado;
        using var _2 = gerente;
        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var pasada = await AyudaReserva.CrearAsync(
            empleado, AyudaReserva.Corta(clienteId, unidadId, AyudaReserva.Dia(-3)), ct);
        var futura = await AyudaReserva.CrearAsync(
            empleado, AyudaReserva.Corta(clienteId, unidadId, AyudaReserva.Dia(3)), ct);

        await using (var alcance = api.Services.CreateAsyncScope())
        {
            await alcance.ServiceProvider.GetRequiredService<ServicioReservas>().ProcesarFechasAsync(ct);
        }

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Reserva.AsNoTracking().SingleAsync(r => r.Id == pasada, ct)).Estado
            .Should().Be(EstadoReserva.Completada);
        (await contexto.Reserva.AsNoTracking().SingleAsync(r => r.Id == futura, ct)).Estado
            .Should().Be(EstadoReserva.Activa);
    }

    [Fact]
    public async Task LlegaLaEntradaConElContratoEnBorradorLaReservaPasaAConflictoYSigueBloqueando()
    {
        var ct = TestContext.Current.CancellationToken;
        var (empleado, gerente) = await SesionesAsync(ct);
        using var _1 = empleado;
        using var _2 = gerente;
        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);
        var entrada = AyudaContrato.Dia(0);
        var reservaId = await AyudaReserva.CrearAsync(empleado, AyudaReserva.Mensual(clienteId, unidadId, entrada), ct);
        await CrearConReservaAsync(empleado, clienteId, unidadId, facturadoraId, entrada, reservaId, ct);

        await using (var alcance = api.Services.CreateAsyncScope())
        {
            await alcance.ServiceProvider.GetRequiredService<ServicioReservas>().ProcesarFechasAsync(ct);
        }

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.Reserva.AsNoTracking().SingleAsync(r => r.Id == reservaId, ct)).Estado
            .Should().Be(EstadoReserva.EnConflicto);
        (await contexto.OcupacionRango.AsNoTracking().AnyAsync(r => r.OrigenId == reservaId && r.Bloquea, ct))
            .Should().BeTrue("en conflicto sigue bloqueando hasta que el gerente decida (§3.4)");
    }

    [Theory]
    [InlineData("Mantenimiento")]
    [InlineData("FueraDeServicio")]
    [InlineData("Inactiva")]
    public async Task NoSeActivaUnContratoEnUnaUnidadNoAlquilable(string caso)
    {
        var ct = TestContext.Current.CancellationToken;
        var (empleado, gerente) = await SesionesAsync(ct);
        using var _1 = empleado;
        using var _2 = gerente;
        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);
        var contratoId = await CrearConReservaAsync(
            empleado, clienteId, unidadId, facturadoraId, AyudaContrato.Dia(520), null, ct);
        await AyudaContrato.AceptarAsync(empleado, contratoId, "solicitud", new { }, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "aprobacion", null, ct);

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var consulta = contexto.Unidad.Where(u => u.Id == unidadId);
            if (caso == "Inactiva")
            {
                await consulta.ExecuteUpdateAsync(s => s.SetProperty(u => u.Activo, false), ct);
            }
            else
            {
                var estado = Enum.Parse<EstadoUnidad>(caso);
                await consulta.ExecuteUpdateAsync(s => s.SetProperty(u => u.EstadoManual, estado), ct);
            }
        }

        (await AyudaContrato.Boton(gerente, contratoId, "activacion", ct: ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await AyudaContrato.EstadoAsync(bd, contratoId, ct)).ToString().Should().Be("Aprobado");
    }

    private static object Cuerpo(Guid clienteId, Guid unidadId, Guid facturadoraId, DateOnly inicio, Guid? reservaId) =>
        new
        {
            clienteId,
            unidadId,
            reservaId,
            facturadoraId,
            fechaInicio = inicio.ToString("yyyy-MM-dd"),
            duracionInicialMeses = 12,
            renta = 50_000m,
            moneda = "DOP",
            diaPago = 5,
            fianza = 20_000m,
            reglaRenovacion = "Anual",
            renovacionMeses = 12,
            plantillaContratoId = "estandar",
        };

    private static async Task<Guid> CrearConReservaAsync(
        SesionCliente sesion,
        Guid clienteId,
        Guid unidadId,
        Guid facturadoraId,
        DateOnly inicio,
        Guid? reservaId,
        CancellationToken ct) =>
        await AyudaContrato.CrearAsync(sesion, Cuerpo(clienteId, unidadId, facturadoraId, inicio, reservaId), ct);

    private async Task<(SesionCliente Empleado, SesionCliente Gerente)> SesionesAsync(CancellationToken ct) =>
        (await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct), Contrasena, ct),
         await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct), Contrasena, ct));
}
