using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Reservas;

/// <summary>
/// F2-03: el conflicto con un contrato y la transacción del bloqueo.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ReservaConflictoYTransaccionTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// El método que el servicio de contratos (F2-05) llamará al aprobar un contrato sobre una
    /// unidad que ya estaba reservada: la reserva pasa a <c>EnConflicto</c> y <b>sigue bloqueando</b>,
    /// que es lo que dice la especificación (hasta que el gerente decida, el sitio no es de nadie).
    /// <para>
    /// No hay endpoint para esto a propósito: no es una acción de una persona delante de una
    /// pantalla, es el efecto de aprobar el contrato.
    /// </para>
    /// </summary>
    [Fact]
    public async Task LaReservaEnConflictoMarcaYLaCanceladaNo()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var entrada = AyudaReserva.Dia(150);

        var id = await AyudaReserva.CrearAsync(
            sesion, AyudaReserva.Mensual(clienteId, unidadId, entrada), ct);

        await using (var contexto = bd.CrearContexto(bd.CadenaApp, new ContextoSesion { Rol = Roles.Gerente }))
        {
            await ServicioDe(contexto).MarcarEnConflictoAsync(id, usuario.Id, "198.51.100.7", ct);
        }

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var reserva = await contexto.Reserva.AsNoTracking().SingleAsync(r => r.Id == id, ct);
            reserva.Estado.Should().Be(EstadoReserva.EnConflicto);
            reserva.Activo.Should().BeTrue("en conflicto la reserva sigue viva");
        }

        (await AyudaReserva.BloqueosVivosAsync(bd, unidadId, ct)).Should().BeGreaterThan(0,
            "el bloqueo sigue puesto: quien tiene la unidad no puede ser el que busca sitio al cliente");

        // Solo se marca una vez: una reserva ya marcada no vuelve a aceptarlo.
        await using (var contexto = bd.CrearContexto(bd.CadenaApp, new ContextoSesion { Rol = Roles.Gerente }))
        {
            var repetida = () => ServicioDe(contexto).MarcarEnConflictoAsync(id, usuario.Id, "198.51.100.7", ct);
            (await repetida.Should().ThrowAsync<InvalidOperationException>())
                .WithMessage("*en conflicto*");
        }

        // Y una cancelada no pasa a conflicto: ya no bloquea y no hay nada que decidir.
        var otraUnidad = await AyudaReserva.UnidadAsync(sesion, ct);
        var otra = await AyudaReserva.CrearAsync(
            sesion, AyudaReserva.Mensual(clienteId, otraUnidad, AyudaReserva.Dia(151)), ct);
        (await sesion.DeleteAsync($"/api/v1/reservas/{otra:D}", ct)).StatusCode
            .Should().Be(System.Net.HttpStatusCode.OK);

        await using (var contexto = bd.CrearContexto(bd.CadenaApp, new ContextoSesion { Rol = Roles.Gerente }))
        {
            var cancelada = () => ServicioDe(contexto).MarcarEnConflictoAsync(otra, usuario.Id, "198.51.100.7", ct);
            (await cancelada.Should().ThrowAsync<InvalidOperationException>())
                .WithMessage("*cancelada*");
        }

        await using var verificacion = bd.CrearContexto(bd.CadenaApp);
        (await verificacion.Reserva.AsNoTracking().SingleAsync(r => r.Id == otra, ct)).Estado
            .Should().Be(EstadoReserva.Cancelada, "una reserva cancelada no se resucita en conflicto");
    }

    /// <summary>
    /// El fallo a media alta no deja bloqueo: si la edición de una reserva choca con otra, la unidad
    /// se queda exactamente como estaba.
    /// <para>
    /// El caso es real y es el que importa. Al mover la reserva del día 160 al 161, el servicio suelta
    /// el día viejo y ocupa el nuevo <b>dentro de la misma transacción</b>. Si cada paso fuera por su
    /// cuenta, el día 160 quedaría libre para cualquiera y el 161 ocupado sin dueño. Con una sola
    /// transacción, el día 160 sigue bloqueado por la reserva que no se pudo mover, y esa reserva sigue
    /// con su día y con su estado de antes.
    /// <para>
    /// El choque es contra un <b>día completo</b>: dos medios días sí caben el mismo día (es el otro
    /// criterio del plan), así que para que la edición tenga que fallar de verdad el destino tiene
    /// que ser un día que ya está entero.
    /// </para>
    /// </summary>
    [Fact]
    public async Task UnFalloAMediaAltaNoDejaBloqueo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var primero = AyudaReserva.Dia(160);
        var ocupado = AyudaReserva.Dia(161);

        var movible = await AyudaReserva.CrearAsync(
            sesion, AyudaReserva.Corta(clienteId, unidadId, primero), ct);
        var quieta = await AyudaReserva.CrearAsync(
            sesion,
            AyudaReserva.Corta(
                clienteId,
                unidadId,
                ocupado,
                new[] { AyudaReserva.Franja(ocupado, "08:00:00", "16:00:00") },
                modalidad: "DiaCompleto"),
            ct);

        (await sesion.PutAsync(
            $"/api/v1/reservas/{movible:D}",
            AyudaReserva.Corta(clienteId, unidadId, ocupado),
            ct)).StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict, "el día 161 ya está entero");

        // El día viejo sigue bloqueado por su reserva: no se ha soltado a medias.
        var bloqueosPrimero = await AyudaReserva.BloqueosAsync(bd, unidadId, primero, ct);
        bloqueosPrimero.Should().ContainSingle(b => b.Bloquea, "el día viejo sigue siendo de la reserva");
        bloqueosPrimero.Should().OnlyContain(b => b.Origen == OrigenOcupacion.ReservaCorta);

        // Y el día nuevo sigue siendo de la otra reserva, sin una fila nueva de la que se movió.
        (await AyudaReserva.BloqueosAsync(bd, unidadId, ocupado, ct)).Count(b => b.Bloquea)
            .Should().Be(1, "solo hay un bloqueo vivo en ese día");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.OcupacionDia.CountAsync(d => d.OrigenId == quieta, ct)).Should().Be(1,
            "la reserva quieta conserva su único día");
        (await contexto.OcupacionDia.CountAsync(d => d.OrigenId == movible, ct)).Should().Be(1,
            "la que falló ni perdió su día ni ocupó el del otro");

        var reserva = await contexto.Reserva.AsNoTracking()
            .Include(r => r.Dias)
            .SingleAsync(r => r.Id == movible, ct);
        reserva.Estado.Should().Be(EstadoReserva.Activa, "la edición fallida no cambió el estado");
        reserva.EntradaPrevista.Should().Be(primero);
        reserva.Dias.Should().ContainSingle(d => d.Fecha == primero, "ni se le quitó ni se le puso un día");
    }

    // ---------------------------------------------------------------- ayudantes

    /// <summary>
    /// El servicio montado a mano, como lo montaría el contenedor de inyección. Se llama
    /// directamente porque <c>MarcarEnConflictoAsync</c> no lo llama ningún endpoint: lo llama el
    /// servicio de contratos de F2-05.
    /// </summary>
    private static ServicioReservas ServicioDe(AtlasRentDbContext contexto)
    {
        var reloj = new RelojPrueba();

        return new ServicioReservas(
            contexto,
            new AuditoriaService(contexto),
            new ServicioIdempotencia(contexto, reloj),
            new ServicioDisponibilidad(contexto, reloj),
            reloj);
    }

    private async Task<SesionCliente> SesionAsync(CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }
}