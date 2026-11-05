using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Contratos;

/// <summary>
/// BUG-023: al editar un borrador, el fin del bloque vigente solo crecía. Un borrador no ha tenido
/// renovación ni baja, así que su fin vigente es siempre su fin inicial.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ContratoBorradorFinTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    [Fact]
    public async Task EditarUnBorradorADuracionMenorRecortaElFinVigente()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct), Contrasena, ct);

        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);
        var inicio = AyudaContrato.Dia(400);

        var contratoId = await AyudaContrato.CrearAsync(
            empleado, AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, inicio, duracionMeses: 12), ct);
        (await FinesAsync(contratoId, ct)).Should().Be((inicio.AddMonths(12), inicio.AddMonths(12)));

        var editado = await empleado.PutAsync(
            $"/api/v1/contratos/{contratoId:D}",
            AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, inicio, duracionMeses: 6),
            ct);
        editado.StatusCode.Should().Be(HttpStatusCode.OK, await editado.Cuerpo());

        (await FinesAsync(contratoId, ct)).Should().Be(
            (inicio.AddMonths(6), inicio.AddMonths(6)),
            "un borrador no ha tenido renovación ni baja: su fin vigente es el inicial");
    }

    [Fact]
    public async Task UnaSolicitudEnviadaConDoceMesesCaducaSiSeEditaASeis()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct), Contrasena, ct);
        using var gerente = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct), Contrasena, ct);

        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);
        var inicio = AyudaContrato.Dia(410);

        var contratoId = await AyudaContrato.CrearAsync(
            empleado, AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, inicio, duracionMeses: 12), ct);
        await AyudaContrato.AceptarAsync(empleado, contratoId, "solicitud", new { }, ct);

        var editado = await empleado.PutAsync(
            $"/api/v1/contratos/{contratoId:D}",
            AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, inicio, duracionMeses: 6),
            ct);
        editado.StatusCode.Should().Be(HttpStatusCode.OK, await editado.Cuerpo());

        (await AyudaContrato.Boton(gerente, contratoId, "aprobacion", ct: ct)).StatusCode
            .Should().Be(HttpStatusCode.Conflict, "se aprobaría un contrato con otra fecha de fin que la enviada");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.SolicitudAprobacion.AsNoTracking().SingleAsync(s => s.ContratoId == contratoId, ct))
            .Resultado.ToString().Should().Be("Caducada");
        (await FinesAsync(contratoId, ct)).Vigente.Should().Be(inicio.AddMonths(6));
    }

    private async Task<(DateOnly Inicial, DateOnly Vigente)> FinesAsync(Guid contratoId, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var c = await contexto.Contrato.AsNoTracking().SingleAsync(x => x.Id == contratoId, ct);

        return (c.FechaFinInicial, c.FechaFinVigente);
    }
}
