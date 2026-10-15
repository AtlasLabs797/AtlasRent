using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Reservas;

/// <summary>
/// F2-03: las reglas de la reserva corta de docs/ESPECIFICACION.md §3.4 y D11. Todas se comprueban
/// por la API, que es donde el usuario las se.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ReservaCortaReglasTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// Media jornada = 4 horas exactas, y el día completo = la jornada entera. Ni una hora más ni
    /// una hora menos: no hay alquiler por horas (D11).
    /// </summary>
    [Fact]
    public async Task MediaJornadaDeCuatroHorasYLaMediaJornadaDeMasSeRechaza()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);

        var tresHoras = await AyudaReserva.CrearRawAsync(
            sesion,
            AyudaReserva.Corta(clienteId, unidadId, AyudaReserva.Dia(92), new[]
            {
                AyudaReserva.Franja(AyudaReserva.Dia(92), "08:00:00", "11:00:00"),
            }),
            ct);
        tresHoras.StatusCode.Should().Be(HttpStatusCode.Conflict, "3 horas no son ni medio día ni jornada");

        var cincoHoras = await AyudaReserva.CrearRawAsync(
            sesion,
            AyudaReserva.Corta(clienteId, unidadId, AyudaReserva.Dia(93), new[]
            {
                AyudaReserva.Franja(AyudaReserva.Dia(93), "08:00:00", "13:00:00"),
            }),
            ct);
        cincoHoras.StatusCode.Should().Be(HttpStatusCode.Conflict, "5 horas tampoco");

        var cuatroHoras = await AyudaReserva.CrearRawAsync(
            sesion,
            AyudaReserva.Corta(clienteId, unidadId, AyudaReserva.Dia(94), new[]
            {
                AyudaReserva.Franja(AyudaReserva.Dia(94), "08:00:00", "12:00:00"),
            }),
            ct);
        cuatroHoras.StatusCode.Should().Be(HttpStatusCode.Created, "4 horas exactas son un medio día");

        var jornada = await AyudaReserva.CrearRawAsync(
            sesion,
            AyudaReserva.Corta(
                clienteId,
                unidadId,
                AyudaReserva.Dia(95),
                new[] { AyudaReserva.Franja(AyudaReserva.Dia(95), "08:00:00", "16:00:00") },
                modalidad: "DiaCompleto"),
            ct);
        jornada.StatusCode.Should().Be(HttpStatusCode.Created, "el día completo entra si la unidad está libre");
    }

    /// <summary>
    /// El día completo es de uno solo: con los dos medios días cogidos, el día completo ya no cabe.
    /// </summary>
    [Fact]
    public async Task ElDiaCompletoNoEntraSiElDiaYaEstaOcupado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var ocupada = await AyudaReserva.UnidadAsync(sesion, ct);
        var fecha = AyudaReserva.Dia(96);

        await AyudaReserva.CrearAsync(sesion, AyudaReserva.Corta(clienteId, ocupada, fecha), ct);
        await AyudaReserva.CrearAsync(
            sesion,
            AyudaReserva.Corta(
                clienteId,
                ocupada,
                fecha,
                new[] { AyudaReserva.Franja(fecha, "13:00:00", "17:00:00") }),
            ct);

        (await AyudaReserva.CrearRawAsync(
            sesion,
            AyudaReserva.Corta(
                clienteId,
                ocupada,
                fecha,
                new[] { AyudaReserva.Franja(fecha, "08:00:00", "16:00:00") },
                modalidad: "DiaCompleto"),
            ct)).StatusCode.Should().Be(HttpStatusCode.Conflict,
            "el día ya tiene sus dos medios días y el completo no cabe");
    }

    /// <summary>
    /// Entrada y salida el mismo día, siempre (docs/ESPECIFICACION.md §3.4). Una reserva que pide
    /// dormir fuera no es una corta: es una mensual, y por eso se rechaza con un 409 que lo dice.
    /// </summary>
    [Fact]
    public async Task EntradaYSalidaEnElMismoDia()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var entrada = AyudaReserva.Dia(97);

        var respuesta = await AyudaReserva.CrearRawAsync(
            sesion,
            AyudaReserva.Corta(clienteId, unidadId, entrada, salida: entrada.AddDays(1)),
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await AyudaReserva.Json(respuesta, ct)).GetProperty("detail").GetString()
            .Should().Contain("mismo día", "el motivo es de negocio, no un error de formato");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.OcupacionDia.CountAsync(d => d.UnidadId == unidadId, ct)).Should().Be(0,
            "una reserva rechazada no bloquea nada");
    }

    /// <summary>
    /// Dos personas el mismo día son dos reservas. Meter las dos en una sola choca con el bloqueo,
    /// que es de unidad, día y fila de origen: el trigger contaría uno y dejaría pasar a un
    /// tercero. El servicio lo rechaza antes, con un error que se puede entender.
    /// </summary>
    [Fact]
    public async Task UnaReservaNoRepiteElMismoDia()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var fecha = AyudaReserva.Dia(98);

        var respuesta = await AyudaReserva.CrearRawAsync(
            sesion,
            AyudaReserva.Corta(clienteId, unidadId, fecha, new[]
            {
                AyudaReserva.Franja(fecha, "08:00:00", "12:00:00"),
                AyudaReserva.Franja(fecha, "13:00:00", "17:00:00"),
            }),
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await AyudaReserva.Json(respuesta, ct)).GetProperty("detail").GetString()
            .Should().Contain("dos reservas");
    }

    /// <summary>
    /// Una reserva corta necesita cliente o prospecto, no los dos ni ninguno, y el cuerpo se
    /// valida entero antes de tocar la base.
    /// </summary>
    [Fact]
    public async Task ElDestinatarioEsUnoSoloYLasHorasSePiden()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var prospectoId = await AyudaReserva.ProspectoAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var fecha = AyudaReserva.Dia(99);

        (await AyudaReserva.CrearRawAsync(
            sesion, AyudaReserva.Corta(null, unidadId, fecha), ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict,
                "sin cliente ni prospecto no hay a quién reservar");

        var cuerpoConLosDos = new
        {
            unidadId,
            clienteId,
            prospectoId,
            tipo = "Corta",
            entradaPrevista = fecha.ToString("yyyy-MM-dd"),
            modalidad = "MedioDia",
            moneda = "DOP",
            importe = 1000m,
            dias = new[] { AyudaReserva.Franja(fecha) },
        };
        (await AyudaReserva.CrearRawAsync(sesion, cuerpoConLosDos, ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await AyudaReserva.CrearRawAsync(
            sesion, AyudaReserva.Corta(null, unidadId, fecha, prospectoId: prospectoId), ct))
            .StatusCode.Should().Be(HttpStatusCode.Created, "un prospecto también puede reservar");
    }

    private async Task<SesionCliente> SesionAsync(CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }
}