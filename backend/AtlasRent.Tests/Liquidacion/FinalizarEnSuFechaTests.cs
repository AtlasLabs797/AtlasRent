using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Calendario;
using AtlasRent.Tests.Contratos;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Liquidacion;

/// <summary>
/// BUG-025: finalizar a mano solo es válido cuando la fecha de baja ya llegó, y al liberar la unidad
/// el candado se recorta a <c>[inicio, fin)</c>: el calendario conserva el pasado del contrato y la
/// unidad se puede reservar desde el día del fin.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class FinalizarEnSuFechaTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    [Fact]
    public async Task FinalizarConBajaFuturaEsConflictoYNoLiberaLaUnidad()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaLiquidacion.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaLiquidacion.SesionAsync(api, bd, Roles.Gerente, ct);

        var (_, contratoId) = await AyudaLiquidacion.ContratoActivoAsync(
            empleado, gerente, bd, AyudaContrato.Dia(-20), ct);
        await AyudaContrato.AceptarAsync(
            gerente, contratoId, "baja", new { fechaFin = AyudaContrato.Dia(5).ToString("yyyy-MM-dd") }, ct);

        var respuesta = await AyudaContrato.Boton(gerente, contratoId, "finalizacion", null, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict, await respuesta.Cuerpo());

        (await AyudaContrato.EstadoAsync(bd, contratoId, ct)).ToString().Should().Be("BajaProgramada");
        var candado = await AyudaLiquidacion.CandadoAsync(bd, contratoId, ct);
        candado.Bloquea.Should().BeTrue("la unidad sigue siendo del contrato hasta su fecha efectiva");
        candado.FechaFin.Should().BeNull();
    }

    [Fact]
    public async Task FinalizarConBajaPasadaConservaElPasadoYLiberaDesdeLaFechaDeFin()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaLiquidacion.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaLiquidacion.SesionAsync(api, bd, Roles.Gerente, ct);

        var inicio = AyudaContrato.Dia(-20);
        var fin = AyudaContrato.Dia();
        var (unidadId, contratoId) = await AyudaLiquidacion.ContratoActivoAsync(empleado, gerente, bd, inicio, ct);
        await AyudaLiquidacion.DarBajaYFinalizarAsync(gerente, contratoId, fin, ct);

        var candado = await AyudaLiquidacion.CandadoAsync(bd, contratoId, ct);
        candado.FechaFin.Should().Be(fin, "el candado se recorta, no se apaga");

        var unidad = AyudaCalendario.PrimeraUnidad(
            await AyudaCalendario.DeUnidadAsync(empleado, unidadId, inicio, fin.AddDays(2), ct));
        for (var dia = inicio; dia < fin; dia = dia.AddDays(1))
        {
            AyudaCalendario.Tipo(AyudaCalendario.Dia(unidad, dia)).Should().Be("Contrato", $"{dia:yyyy-MM-dd}");
        }

        AyudaCalendario.Tipo(AyudaCalendario.Dia(unidad, fin)).Should().Be("Libre", "libre desde la fecha de fin");

        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);

        var solapa = await AyudaReserva.CrearRawAsync(
            empleado, AyudaReserva.Corta(clienteId, unidadId, fin.AddDays(-3)), ct);
        solapa.StatusCode.Should().Be(HttpStatusCode.Conflict, "esos días fueron del contrato");

        var desdeElFin = await AyudaReserva.CrearRawAsync(
            empleado, AyudaReserva.Corta(clienteId, unidadId, fin), ct);
        desdeElFin.StatusCode.Should().Be(HttpStatusCode.Created, await desdeElFin.Cuerpo());
    }
}
