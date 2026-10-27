using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Reservas;

/// <summary>
/// Auditoría 26/10/2026: BUG-027 (la hora de la reserva corta es un texto orientativo, D11),
/// BUG-041 (cambiar de tipo no deja un candado huérfano) y BUG-049 (no se reserva una unidad
/// inactiva, en mantenimiento ni con una modalidad que no admite).
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ReservaAuditoriaReglasTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    // ---------------------------------------------------------------- BUG-027

    /// <summary>D11: sin franjas fijas. Una reserva corta sin horas es válida.</summary>
    [Fact]
    public async Task CortaSinHorasEsValida()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var fecha = AyudaReserva.Dia(410);

        var respuesta = await AyudaReserva.CrearRawAsync(
            sesion,
            AyudaReserva.Corta(clienteId, unidadId, fecha, new object[] { new { fecha = fecha.ToString("yyyy-MM-dd") } }),
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Created, await respuesta.Cuerpo());
        (await AyudaReserva.BloqueosAsync(bd, unidadId, fecha, ct)).Should().ContainSingle(
            b => b.Tipo == TipoOcupacionDia.MedioDia && b.Bloquea);
    }

    /// <summary>La hora es un texto libre orientativo: se guarda tal cual y se devuelve.</summary>
    [Fact]
    public async Task CortaConHoraOrientativaEnTextoLibreSeGuardaTalCual()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var fecha = AyudaReserva.Dia(411);

        var respuesta = await AyudaReserva.CrearRawAsync(
            sesion,
            AyudaReserva.Corta(
                clienteId,
                unidadId,
                fecha,
                new object[] { new { fecha = fecha.ToString("yyyy-MM-dd"), horaOrientativa = "de 9 a 19 aprox." } },
                modalidad: "DiaCompleto"),
            ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Created, await respuesta.Cuerpo());

        var lectura = await sesion.GetAsync($"/api/v1/reservas/{await AyudaReserva.Id(respuesta, ct):D}", ct);
        var dia = (await AyudaReserva.Json(lectura, ct)).GetProperty("dias")[0];
        dia.GetProperty("horaOrientativa").GetString().Should().Be("de 9 a 19 aprox.");
    }

    [Fact]
    public async Task LaHoraOrientativaNoAdmiteSaltosDeLineaNiTextosLargos()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var fecha = AyudaReserva.Dia(412);

        foreach (var texto in new[] { "9\nmas", new string('x', 61) })
        {
            var respuesta = await AyudaReserva.CrearRawAsync(
                sesion,
                AyudaReserva.Corta(
                    clienteId,
                    unidadId,
                    fecha,
                    new object[] { new { fecha = fecha.ToString("yyyy-MM-dd"), horaOrientativa = texto } }),
                ct);

            respuesta.IsSuccessStatusCode.Should().BeFalse("el texto orientativo es de una línea y corto");
        }
    }

    // ---------------------------------------------------------------- BUG-041

    [Fact]
    public async Task DeMensualACortaSueltaElCandadoDelRango()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var fecha = AyudaReserva.Dia(420);

        var id = await AyudaReserva.CrearAsync(sesion, AyudaReserva.Mensual(clienteId, unidadId, fecha), ct);
        var edicion = await sesion.PutAsync(
            $"/api/v1/reservas/{id:D}", AyudaReserva.Corta(clienteId, unidadId, fecha), ct);
        edicion.StatusCode.Should().Be(HttpStatusCode.OK, await edicion.Cuerpo());

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.OcupacionRango.AsNoTracking().AnyAsync(r => r.OrigenId == id && r.Bloquea, ct))
            .Should().BeFalse("la reserva ya no es mensual y su rango abierto no puede seguir bloqueando");
        (await AyudaReserva.BloqueosVivosAsync(bd, unidadId, ct)).Should().Be(1, "solo queda el día de la corta");

        // Y otra persona puede reservar un mes más adelante.
        var otro = await AyudaReserva.ClienteAsync(sesion, ct);
        (await AyudaReserva.CrearRawAsync(
            sesion, AyudaReserva.Mensual(otro, unidadId, fecha.AddDays(30)), ct))
            .StatusCode.Should().Be(HttpStatusCode.Created, "no queda un candado huérfano");
    }

    [Fact]
    public async Task DeCortaAMensualSueltaLosDiasDelBloqueo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var fecha = AyudaReserva.Dia(421);

        var id = await AyudaReserva.CrearAsync(sesion, AyudaReserva.Corta(clienteId, unidadId, fecha), ct);
        var edicion = await sesion.PutAsync(
            $"/api/v1/reservas/{id:D}", AyudaReserva.Mensual(clienteId, unidadId, fecha), ct);
        edicion.StatusCode.Should().Be(HttpStatusCode.OK, await edicion.Cuerpo());

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.OcupacionDia.AsNoTracking().AnyAsync(d => d.OrigenId == id && d.Bloquea, ct))
            .Should().BeFalse("la reserva ya no es corta y sus días no pueden seguir bloqueando");
        (await AyudaReserva.BloqueosVivosAsync(bd, unidadId, ct)).Should().Be(1, "solo queda el rango mensual");
    }

    // ---------------------------------------------------------------- BUG-049

    [Theory]
    [InlineData("Mantenimiento")]
    [InlineData("FueraDeServicio")]
    public async Task NoSeReservaUnaUnidadEnMantenimientoNiFueraDeServicio(string estado)
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var nuevo = Enum.Parse<EstadoUnidad>(estado);
        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            await contexto.Unidad.Where(u => u.Id == unidadId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.EstadoManual, nuevo), ct);
        }

        (await AyudaReserva.CrearRawAsync(
            sesion, AyudaReserva.Mensual(clienteId, unidadId, AyudaReserva.Dia(430)), ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await AyudaReserva.CrearRawAsync(
            sesion, AyudaReserva.Corta(clienteId, unidadId, AyudaReserva.Dia(431)), ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task NoSeReservaUnaUnidadInactiva()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            await contexto.Unidad.Where(u => u.Id == unidadId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Activo, false), ct);
        }

        (await AyudaReserva.CrearRawAsync(
            sesion, AyudaReserva.Mensual(clienteId, unidadId, AyudaReserva.Dia(432)), ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task NoSeReservaConUnaModalidadQueLaUnidadNoAdmite()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var inmueble = await AyudaReserva.Id(await sesion.PostAsync(
            "/api/v1/inmuebles", new { nombre = $"Edificio {Guid.NewGuid():N}"[..40], tipo = "Edificio" }, ct), ct);
        var soloDiaCompleto = await AyudaReserva.Id(await sesion.PostAsync(
            $"/api/v1/inmuebles/{inmueble:D}/unidades",
            new { nombre = $"Unidad {Guid.NewGuid():N}"[..20], tipo = "Oficina", modalidades = new[] { "DiaCompleto" } },
            ct), ct);

        (await AyudaReserva.CrearRawAsync(
            sesion, AyudaReserva.Corta(clienteId, soloDiaCompleto, AyudaReserva.Dia(433)), ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "la unidad no ofrece medio día");
        (await AyudaReserva.CrearRawAsync(
            sesion, AyudaReserva.Mensual(clienteId, soloDiaCompleto, AyudaReserva.Dia(434)), ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "la unidad no ofrece alquiler mensual");
        (await AyudaReserva.CrearRawAsync(
            sesion,
            AyudaReserva.Corta(clienteId, soloDiaCompleto, AyudaReserva.Dia(435), modalidad: "DiaCompleto"),
            ct)).StatusCode.Should().Be(HttpStatusCode.Created, "el día completo sí lo ofrece");
    }

    private async Task<SesionCliente> SesionAsync(CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }
}
