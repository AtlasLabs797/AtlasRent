using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Mensajeria;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Recordatorios;

/// <summary>
/// F4-05: la cadencia del aviso, que es lo que separa un recordatorio de un correo diario
/// (docs/ESPECIFICACION.md §3.14).
/// <para>
/// «Primero a los 5 días del vencimiento, luego cada 15» viene en la especificación como
/// <em>supuesto configurable</em> y los valores viven en la base. Lo que se comprueba aquí es la
/// cuenta, no el 5 y el 15 en sí: si la cuenta estuviera mal, cambiar los plazos no la arreglaría.
/// </para>
/// <para>
/// <b>El atraso se hace con la fecha de vencimiento, no avanzando el reloj.</b> Cada día que se quiere
/// comprobar tiene su propio cliente y el trabajo corre una sola vez: así se ve la cadencia entera de
/// golpe. Adelantar el reloj, además, impide volver a iniciar sesión con segundo factor, que es un
/// límite de la infraestructura de pruebas y no del sistema que se está probando.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class RecordatorioCadenciaTests(ContenedorBdFixture bd, ApiTestFactoryCorreo api)
    : IClassFixture<ApiTestFactoryCorreo>
{
    /// <summary>Un cliente por día de atraso: avisan los que están en plazo y no los que no.</summary>
    [Fact]
    public async Task SoloAvisaEnLosDiasDeLaCadencia()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaRecordatorios.SesionAsync(bd, api, Roles.Admin, ct);
        await AyudaRecordatorios.PonerModoAsync(admin, ModoRecordatorios.Manual, ct: ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);

        // Antes del primer plazo y a un día de cualquier plazo no se avisa; en el 5, el 20 y el 35 sí.
        int[] vencidos = [4, 5, 6, 20, 21, 35];
        var clientes = new Dictionary<int, Guid>();

        foreach (var dias in vencidos)
        {
            var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
            await AyudaCuentas.EmitidaAsync(admin, escenario, hoy.AddDays(-dias), 300m, ct);

            clientes[dias] = escenario.ClienteId;
        }

        await AyudaRecordatorios.EjecutarTrabajoAsync(api, ct);

        foreach (var dias in vencidos)
        {
            var avisos = await AyudaRecordatorios.AvisosAsync(bd, clientes[dias], ct);
            var enPlazo = dias is 5 or 20 or 35;

            if (enPlazo)
            {
                avisos.Should().ContainSingle($"a los {dias} días del vencimiento toca avisar");
            }
            else
            {
                avisos.Should().BeEmpty($"a los {dias} días no toca: los plazos son 5 y luego cada 15");
            }
        }
    }

    /// <summary>
    /// Un cliente con dos facturas, una en plazo hoy y otra también, recibe un solo aviso: §3.14 dice
    /// «un mensaje por cliente y unidad», no uno por factura.
    /// </summary>
    [Fact]
    public async Task LaCadenciaEsPorClienteYUnidadYNoPorFactura()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaRecordatorios.SesionAsync(bd, api, Roles.Admin, ct);
        await AyudaRecordatorios.PonerModoAsync(admin, ModoRecordatorios.Manual, ct: ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);

        // Las dos vencidas hace cinco días: las dos están en plazo el mismo día.
        await AyudaCuentas.EmitidaAsync(admin, escenario, hoy.AddDays(-5), 300m, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, hoy.AddDays(-5), 90m, ct);

        await AyudaRecordatorios.EjecutarTrabajoAsync(api, ct);

        var avisos = await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct);

        avisos.Should().ContainSingle("las dos facturas del mismo cliente y unidad son un solo aviso");

        var envio = await AyudaRecordatorios.EnvioDelAvisoAsync(bd, avisos.Single(), ct);

        envio.Cuerpo.Should().Contain("390,00 USD", "y el aviso menciona el pendiente de las dos");
    }

    /// <summary>La lista de la pantalla se lee por API y trae el cliente, la unidad y el estado.</summary>
    [Fact]
    public async Task LaListaSeFiltraPorClienteYPorEstado()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaRecordatorios.SesionAsync(bd, api, Roles.Admin, ct);
        await AyudaRecordatorios.PonerModoAsync(admin, ModoRecordatorios.Manual, ct: ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, hoy.AddDays(-5), 300m, ct);

        await AyudaRecordatorios.EjecutarTrabajoAsync(api, ct);

        var todos = await admin.GetAsync($"/api/v1/recordatorios?clienteId={escenario.ClienteId:D}", ct);
        todos.StatusCode.Should().Be(HttpStatusCode.OK, await todos.Cuerpo());

        var pendientes = await admin.GetAsync(
            $"/api/v1/recordatorios?clienteId={escenario.ClienteId:D}&estado=Pendiente",
            ct);
        pendientes.StatusCode.Should().Be(HttpStatusCode.OK, await pendientes.Cuerpo());

        var cuerpo = await pendientes.Content.ReadAsStringAsync(ct);
        cuerpo.Should().Contain(nameof(EstadoRecordatorio.Pendiente));
        cuerpo.Should().Contain(escenario.ClienteId.ToString("D"));
    }
}