using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Reservas;

/// <summary>
/// F2-03: la reserva mensual bloquea desde la entrada prevista, y cancelar una reserva suelta el
/// bloqueo sin borrar la fila.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ReservaMensualTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// La mensual bloquea en rango abierto desde la entrada prevista: una corta en ese día no entra,
    /// tampoco en los días siguientes (la unidad está tomada hasta que haya contrato), y una corta
    /// en otra unidad sí.
    /// </summary>
    [Fact]
    public async Task LaReservaMensualBloqueaDesdeLaEntradaPrevista()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidad = await AyudaReserva.UnidadAsync(sesion, ct);
        var otra = await AyudaReserva.UnidadAsync(sesion, ct);
        var entrada = AyudaReserva.Dia(120);

        await AyudaReserva.CrearAsync(sesion, AyudaReserva.Mensual(clienteId, unidad, entrada), ct);

        (await AyudaReserva.CrearRawAsync(sesion, AyudaReserva.Corta(clienteId, unidad, entrada), ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "el día de entrada está tomado por la mensual");

        (await AyudaReserva.CrearRawAsync(sesion, AyudaReserva.Corta(clienteId, unidad, entrada.AddDays(30)), ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict,
                "la mensual no tiene fecha de fin: el rango abierto bloquea también después");

        (await AyudaReserva.CrearRawAsync(sesion, AyudaReserva.Corta(clienteId, otra, entrada), ct))
            .StatusCode.Should().Be(HttpStatusCode.Created, "el bloqueo es de la unidad, no del cliente");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var rango = await contexto.OcupacionRango.AsNoTracking()
            .SingleAsync(r => r.UnidadId == unidad, ct);
        rango.Origen.Should().Be(OrigenOcupacion.ReservaMensual);
        rango.OrigenId.Should().NotBeEmpty();
        rango.FechaInicio.Should().Be(entrada);
        rango.FechaFin.Should().BeNull("hasta que haya contrato, el rango no se cierra");
        rango.Bloquea.Should().BeTrue();
    }

    /// <summary>
    /// Cancelar es baja lógica: la unidad vuelve a estar libre y la fila de la reserva sigue ahí, con
    /// su estado en cancelada. Los días de la corta tampoco se borran, porque son los que pidió el
    /// cliente y eso es historial.
    /// </summary>
    [Fact]
    public async Task CancelarLiberaElBloqueo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var fecha = AyudaReserva.Dia(121);

        var id = await AyudaReserva.CrearAsync(
            sesion, AyudaReserva.Corta(clienteId, unidadId, fecha), ct);

        (await AyudaReserva.BloqueosAsync(bd, unidadId, fecha, ct))
            .Should().ContainSingle(b => b.Bloquea, "la reserva corta bloquea su día");

        (await sesion.DeleteAsync($"/api/v1/reservas/{id:D}", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var bloqueos = await AyudaReserva.BloqueosAsync(bd, unidadId, fecha, ct);
        bloqueos.Should().ContainSingle(b => !b.Bloquea,
            "el candado se suelta, pero la fila de ocupación no se borra");
        (await AyudaReserva.BloqueosVivosAsync(bd, unidadId, ct)).Should().Be(0);

        // Y otro cliente puede quedarse con ese mismo día.
        (await AyudaReserva.CrearRawAsync(sesion, AyudaReserva.Corta(clienteId, unidadId, fecha), ct))
            .StatusCode.Should().Be(HttpStatusCode.Created, "la unidad vuelve a estar libre");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var reserva = await contexto.Reserva.AsNoTracking().SingleAsync(r => r.Id == id, ct);
        reserva.Activo.Should().BeFalse("la baja es lógica");
        reserva.Estado.Should().Be(EstadoReserva.Cancelada);
        (await contexto.ReservaDia.CountAsync(d => d.ReservaId == id, ct)).Should().Be(1,
            "los días que pidió el cliente se conservan aunque la reserva se cancele");

        // La cancelada no sale en la lista de activas, pero sí si se piden las dadas de baja.
        (await sesion.GetAsync($"/api/v1/reservas/{id:D}", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ListarIdsAsync(sesion, clienteId, incluirInactivas: false, ct)).Should().NotContain(id);
        (await ListarIdsAsync(sesion, clienteId, incluirInactivas: true, ct)).Should().Contain(id);
    }

    /// <summary>
    /// Editar una reserva activa mueve su bloqueo; una cancelada no se edita.
    /// </summary>
    [Fact]
    public async Task EditarMueveElDiaYLaReservaCanceladaNoSeEdita()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var primero = AyudaReserva.Dia(122);
        var segundo = AyudaReserva.Dia(123);

        var id = await AyudaReserva.CrearAsync(
            sesion, AyudaReserva.Corta(clienteId, unidadId, primero), ct);

        (await sesion.PutAsync(
            $"/api/v1/reservas/{id:D}",
            AyudaReserva.Corta(clienteId, unidadId, segundo),
            ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await AyudaReserva.BloqueosAsync(bd, unidadId, primero, ct))
            .Should().OnlyContain(b => !b.Bloquea, "el día viejo se suelta");
        (await AyudaReserva.BloqueosAsync(bd, unidadId, segundo, ct))
            .Should().ContainSingle(b => b.Bloquea, "el nuevo está cogido");

        (await sesion.DeleteAsync($"/api/v1/reservas/{id:D}", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await sesion.PutAsync(
            $"/api/v1/reservas/{id:D}",
            AyudaReserva.Corta(clienteId, unidadId, segundo),
            ct)).StatusCode.Should().Be(HttpStatusCode.Conflict, "una reserva cancelada ya no se toca");
    }

    /// <summary>
    /// Lista **solo las reservas del cliente** de la prueba.
    /// <para>
    /// Fijar el cliente no es un detalle: el listado sale paginado y la base es compartida, así que
    /// sin filtrar esta aserción solo pasaba mientras el bloque entero hadn't creado más reservas
    /// que las que caben en una página. Con el filtro, la afirmación va de la cancelada y de las
    /// suyas, que es lo que dice comprobar (BUG-011).
    /// </para>
    /// </summary>
    private static async Task<List<Guid>> ListarIdsAsync(
        SesionCliente sesion,
        Guid clienteId,
        bool incluirInactivas,
        CancellationToken ct)
    {
        var respuesta = await sesion.GetAsync(
            $"/api/v1/reservas?clienteId={clienteId:D}&incluirInactivas={incluirInactivas}", ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);

        return [.. (await AyudaReserva.Json(respuesta, ct))
            .GetProperty("data").EnumerateArray()
            .Select(r => r.GetProperty("id").GetGuid())];
    }

    private async Task<SesionCliente> SesionAsync(CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }
}