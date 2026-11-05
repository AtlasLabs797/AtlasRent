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

    /// <summary>
    /// La reserva en conflicto se puede cancelar: esa es la decisión del gerente, y es la única que
    /// se ha implementado.
    /// <para>
    /// Antes esto no existía: <c>CancelarAsync</c> exigía <c>Activa</c>, así que una reserva que el
    /// servicio de contratos había dejado en <c>EnConflicto</c> no se podía cancelar, se quedaba
    /// con <c>Activo = true</c> para siempre y su aviso de conflicto no lo cerraba nadie. docs/
    /// ESPECIFICACION.md §3.4 dice que en conflicto "sigue bloqueando hasta que el gerente decida"
    /// sin decir cuál es esa decisión; la que se toma aquí, y que decide el orquestador, es cancelar.
    /// </para>
    /// <para>
    /// Cancelarla tiene que soltar el candado (si no, la unidad sigue reservada para una reserva que
    /// ya no existe), cerrar su aviso de conflicto (si no, la bandeja de F2-10 queda con un aviso
    /// irresoluble) y seguir exigiendo <c>Acciones.ClienteEditar</c> y auditándose como cualquier
    /// otra baja.
    /// </para>
    /// </summary>
    [Fact]
    public async Task LaReservaEnConflictoSeCancelaYLiberaElCandadoYCierraSuAviso()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        using var gerente = await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
        var clienteId = await AyudaReserva.ClienteAsync(gerente, ct);
        var unidadId = await AyudaReserva.UnidadAsync(gerente, ct);
        var entrada = AyudaReserva.Dia(150);

        var id = await AyudaReserva.CrearAsync(
            gerente, AyudaReserva.Mensual(clienteId, unidadId, entrada), ct);

        await using (var contexto = bd.CrearContexto(bd.CadenaApp, new ContextoSesion { Rol = Roles.Gerente }))
        {
            await ServicioDe(contexto).MarcarEnConflictoAsync(id, usuario.Id, "198.51.100.7", ct);
        }

        // El aviso de conflicto existe: lo levanta el trabajo de F2-10 sobre las reservas en
        // conflicto. Sin él, la mitad de lo que se comprueba aquí (que la baja lo cierre) no tendría
        // nada que cerrar.
        var reloj = new RelojPrueba();
        await using (var contexto = bd.CrearContexto(bd.CadenaApp, new ContextoSesion { Rol = Roles.Gerente }))
        {
            var avisos = new ServicioAvisosOperacion(contexto, new ServicioAlertas(contexto, reloj), reloj);
            (await avisos.AvisarAsync(ct)).Conflictos.Should().BeGreaterThan(0, "la reserva en conflicto avisa");
        }

        var claveAviso = ServicioAvisosOperacion.ClaveReservaConflicto(id);

        await using (var contexto = bd.CrearContexto(bd.CadenaSuperusuario))
        {
            (await contexto.Alerta.AsNoTracking().SingleAsync(a => a.Clave == claveAviso, ct))
                .ResueltaAt.Should().BeNull("el aviso está vivo mientras la reserva siga en conflicto");
        }

        // La baja: mismo endpoint, mismos permisos y misma auditoría que una reserva activa.
        var antesAuditas = await AudienciasAsync(id, ct);
        (await gerente.DeleteAsync($"/api/v1/reservas/{id:D}", ct)).StatusCode
            .Should().Be(System.Net.HttpStatusCode.OK, "una reserva en conflicto también se puede cancelar");

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var reserva = await contexto.Reserva.AsNoTracking().SingleAsync(r => r.Id == id, ct);
            reserva.Estado.Should().Be(EstadoReserva.Cancelada, "la decisión del gerente es darla de baja");
            reserva.Activo.Should().BeFalse("una reserva cancelada no bloquea nada");
        }

        // El candado se soltó de verdad: otra persona se puede quedar con la unidad esas fechas.
        await AyudaReserva.CrearAsync(
            gerente, AyudaReserva.Corta(clienteId, unidadId, AyudaReserva.Dia(152)), ct);

        // El aviso se cerró en la misma transacción que la baja.
        await using (var contexto = bd.CrearContexto(bd.CadenaSuperusuario))
        {
            var aviso = await contexto.Alerta.AsNoTracking().SingleAsync(a => a.Clave == claveAviso, ct);
            aviso.ResueltaAt.Should().NotBeNull("cancelar el conflicto cierra su aviso: si no, nadie podría");
        }

        (await AudienciasAsync(id, ct))
            .Should().Be(antesAuditas + 1, "la baja se audita como cualquier otra");

        // Y ya no se puede volver a cancelar: está cancelada, y eso no se deshace desde aquí.
        (await gerente.DeleteAsync($"/api/v1/reservas/{id:D}", ct)).StatusCode
            .Should().Be(System.Net.HttpStatusCode.Conflict, "una reserva ya cancelada no se cancela otra vez");

        // El empleado sigue pudiendo cancelar: la salida del conflicto no estrecha el permiso.
        var empleado = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);
        using var sesionEmpleado = await AyudaPermisos.SesionAsync(api, empleado, Contrasena, ct);
        var otraUnidad = await AyudaReserva.UnidadAsync(gerente, ct);
        var otraId = await AyudaReserva.CrearAsync(
            sesionEmpleado, AyudaReserva.Mensual(clienteId, otraUnidad, AyudaReserva.Dia(160)), ct);

        await using (var contexto = bd.CrearContexto(bd.CadenaApp, new ContextoSesion { Rol = Roles.Gerente }))
        {
            await ServicioDe(contexto).MarcarEnConflictoAsync(otraId, usuario.Id, "198.51.100.7", ct);
        }

        (await sesionEmpleado.DeleteAsync($"/api/v1/reservas/{otraId:D}", ct)).StatusCode
            .Should().Be(System.Net.HttpStatusCode.OK, "cancelar sigue siendo ClienteEditar, y el empleado la tiene");
    }

    // ---------------------------------------------------------------- ayudantes

    private async Task<int> AudienciasAsync(Guid reservaId, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaSuperusuario);

        return await contexto.Auditoria.AsNoTracking()
            .CountAsync(a => a.Entidad == "reserva" && a.EntidadId == reservaId.ToString("D"), ct);
    }

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
            // F2-03: cancelar una reserva en conflicto cierra su aviso de conflicto, así que el
            // registro de alertas tiene que ser el de verdad.
            new ServicioAlertas(contexto, reloj),
            reloj);
    }

    private async Task<SesionCliente> SesionAsync(CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }
}