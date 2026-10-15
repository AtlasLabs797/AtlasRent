using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Contratos;

/// <summary>
/// F2-05: el contrato toma la unidad al activarse y la suelta al terminar.
/// <list type="bullet">
/// <item>
/// Con el contrato activo, una reserva corta en esas fechas no entra: el candado es del contrato.
/// </item>
/// <item>
/// Y al terminar vuelve a entrar, porque §3.5 dice que el fin libera la unidad.
/// </item>
/// <item>
/// Un fallo a media alta no deja la unidad bloqueada sin contrato: el candado va en la misma
/// transacción que el cambio de estado, así que o salen los dos o no sale ninguno.
/// </item>
/// <item>
/// Y una reserva activa que se solapa queda en conflicto, que es lo que dice §3.4.
/// </item>
/// </list>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ContratoOcupacionTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// Activar bloquea la unidad y finalizar la libera. Entre medias, la reserva corta que con el
    /// contrato vigente no cabía entra otra vez.
    /// </summary>
    [Fact]
    public async Task ActivarBloqueaLaUnidadYFinalizarLaLibera()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct), Contrasena, ct);
        using var gerente = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct), Contrasena, ct);

        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);
        var inicio = AyudaContrato.Dia(300);

        var contratoId = await AyudaContrato.CrearAsync(
            empleado, AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, inicio), ct);
        await AyudaContrato.AceptarAsync(empleado, contratoId, "solicitud", new { }, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "aprobacion", null, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "activacion", null, ct);

        // Con el contrato activo, una reserva corta dentro de sus fechas no entra.
        var duranteElContrato = await AyudaReserva.CrearRawAsync(
            empleado, AyudaReserva.Corta(clienteId, unidadId, inicio.AddDays(3)), ct);
        duranteElContrato.StatusCode.Should().Be(
            HttpStatusCode.Conflict,
            "la unidad la tiene el contrato y una reserva corta no cabe encima");

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var candado = await contexto.OcupacionRango.AsNoTracking()
                .SingleAsync(r => r.Origen == OrigenOcupacion.Contrato && r.OrigenId == contratoId, ct);
            candado.Bloquea.Should().BeTrue();
            candado.FechaInicio.Should().Be(inicio);
            candado.FechaFin.Should().BeNull("el rango es abierto: lo alarga la renovación automática (F2-06)");
        }

        // Baja programada y fin: la unidad queda libre.
        await AyudaContrato.AceptarAsync(
            gerente, contratoId, "baja",
            new { fechaFin = inicio.AddMonths(12).ToString("yyyy-MM-dd") }, ct);
        (await AyudaContrato.EstadoAsync(bd, contratoId, ct)).ToString().Should().Be("BajaProgramada");

        await AyudaContrato.AceptarAsync(gerente, contratoId, "finalizacion", null, ct);
        (await AyudaContrato.EstadoAsync(bd, contratoId, ct)).ToString().Should().Be("Finalizado");

        await using var verificacion = bd.CrearContexto(bd.CadenaApp);
        var liberado = await verificacion.OcupacionRango.AsNoTracking()
            .SingleAsync(r => r.Origen == OrigenOcupacion.Contrato && r.OrigenId == contratoId, ct);
        liberado.Bloquea.Should().BeFalse("el fin libera la unidad (§3.5)");

        // Y ahora la reserva sí entra.
        var despues = await AyudaReserva.CrearRawAsync(
            empleado, AyudaReserva.Corta(clienteId, unidadId, inicio.AddDays(3)), ct);
        despues.StatusCode.Should().Be(HttpStatusCode.Created, await despues.Cuerpo());

        await AyudaContrato.AceptarAsync(gerente, contratoId, "liquidacion", null, ct);
        (await AyudaContrato.EstadoAsync(bd, contratoId, ct)).ToString().Should().Be("Liquidado");
    }

    /// <summary>
    /// El fallo a media alta no deja la unidad bloqueada sin contrato. Dos contratos aprobados sobre
    /// la misma unidad y las mismas fechas no pueden tomar la unidad los dos: el segundo falla al
    /// escribir su candado y la transacción se deshace entera, así que ni el contrato ni la
    /// ocupación quedan tocados.
    /// </summary>
    [Fact]
    public async Task UnFalloAlActivarNoDejaLaUnidadBloqueada()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct), Contrasena, ct);
        using var gerente = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct), Contrasena, ct);

        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);
        var inicio = AyudaContrato.Dia(330);

        var primero = await AprobadoAsync(empleado, gerente, clienteId, unidadId, facturadoraId, inicio, ct);
        var segundo = await AprobadoAsync(empleado, gerente, clienteId, unidadId, facturadoraId, inicio, ct);

        await AyudaContrato.AceptarAsync(gerente, primero, "activacion", null, ct);

        var activacion = await AyudaContrato.Boton(gerente, segundo, "activacion", ct: ct);
        activacion.StatusCode.Should().Be(
            HttpStatusCode.Conflict,
            $"la unidad ya la tiene el otro contrato: {await activacion.Cuerpo()}");

        // El segundo se quedó en aprobado y no existe candado suyo: lo que falló se deshizo entero.
        (await AyudaContrato.EstadoAsync(bd, segundo, ct)).ToString().Should().Be("Aprobado");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var candados = await contexto.OcupacionRango.AsNoTracking()
            .Where(r => r.Origen == OrigenOcupacion.Contrato && r.UnidadId == unidadId)
            .Select(r => new { r.OrigenId, r.Bloquea })
            .ToListAsync(ct);

        candados.Should().ContainSingle("una unidad, un candado de contrato");
        candados[0].OrigenId.Should().Be(primero);
        candados[0].Bloquea.Should().BeTrue("el candado que sí era legítimo sigue intacto");
    }

    /// <summary>Contrato creado, enviado y aprobado. Se queda en "aprobado", que es donde empieza esto.</summary>
    private static async Task<Guid> AprobadoAsync(
        SesionCliente empleado,
        SesionCliente gerente,
        Guid clienteId,
        Guid unidadId,
        Guid facturadoraId,
        DateOnly inicio,
        CancellationToken ct)
    {
        var id = await AyudaContrato.CrearAsync(
            empleado, AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, inicio), ct);
        await AyudaContrato.AceptarAsync(empleado, id, "solicitud", new { }, ct);
        await AyudaContrato.AceptarAsync(gerente, id, "aprobacion", null, ct);

        return id;
    }

    /// <summary>
    /// Una reserva activa sobre la unidad del contrato queda en conflicto (§3.4) y el cliente ve que
    /// tiene que buscar otra unidad. Va de los dos tipos de reserva porque los dos bloquean la
    /// unidad, pero de manera distinta: la mensual con un rango y la corta con días sueltos.
    /// </summary>
    [Theory]
    [InlineData("Mensual")]
    [InlineData("Corta")]
    public async Task ElContratoEnConflictoPoneLaReservaEnConflicto(string tipo)
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct), Contrasena, ct);
        using var gerente = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct), Contrasena, ct);

        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);
        var inicio = AyudaContrato.Dia(tipo == "Mensual" ? 360 : 362);
        var fechaReserva = inicio.AddDays(5);

        var reservaId = await AyudaReserva.CrearAsync(
            empleado,
            tipo == "Mensual"
                ? AyudaReserva.Mensual(clienteId, unidadId, fechaReserva)
                : AyudaReserva.Corta(clienteId, unidadId, fechaReserva),
            ct);

        var contratoId = await AyudaContrato.CrearAsync(
            empleado, AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, inicio), ct);
        await AyudaContrato.AceptarAsync(empleado, contratoId, "solicitud", new { }, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "aprobacion", null, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "activacion", null, ct);

        (await AyudaContrato.EstadoAsync(bd, contratoId, ct)).ToString()
            .Should().Be("Activo", "el contrato toma la unidad");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var reserva = await contexto.Reserva.AsNoTracking().SingleAsync(r => r.Id == reservaId, ct);
        reserva.Estado.ToString().Should().Be("EnConflicto", "§3.4: el cliente tiene que buscar otra unidad");

        // Y el candado que tenía la reserva ya no bloquea: con el contrato vigente, el candado es del
        // contrato. Sin esto, la reserva seguiría dejando la unidad ocupada y el contrato en vigor no
        // podría entregarse.
        (await contexto.OcupacionDia.AsNoTracking()
            .AnyAsync(d => d.OrigenId == reservaId && d.Bloquea, ct)).Should()
            .BeFalse("con el contrato activo, quien bloquea la unidad es el contrato");
        (await contexto.OcupacionRango.AsNoTracking()
            .AnyAsync(r => r.OrigenId == reservaId && r.Bloquea, ct)).Should()
            .BeFalse("con el contrato activo, quien bloquea la unidad es el contrato");

        (await contexto.OcupacionRango.AsNoTracking()
            .AnyAsync(r => r.Origen == OrigenOcupacion.Contrato && r.OrigenId == contratoId && r.Bloquea, ct))
            .Should().BeTrue("el contrato ocupa la unidad");
    }
}