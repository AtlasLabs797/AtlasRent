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
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Recordatorios;

/// <summary>
/// F4-05: el trabajo es idempotente por dÃ­a, que es la misma regla que se puso el repo en F3-05 y
/// F2-06 (docs/DOC_TECNICA.md Â§8.0).
/// <para>
/// AquÃ­ importa mÃ¡s que en los otros trabajos porque este manda correo: correrlo dos veces el mismo
/// dÃ­a no serÃ­a una fila repetida, serÃ­an dos correos pidiendo el mismo dinero al mismo cliente.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class RecordatorioIdempotenciaTests(ContenedorBdFixture bd, ApiTestFactoryCorreo api)
    : IClassFixture<ApiTestFactoryCorreo>
{
    /// <summary>
    /// Dos ejecuciones del mismo dÃ­a dejan un aviso y un correo. El Ãºnico de
    /// <c>(cliente, unidad, fecha)</c> es lo que lo impide, no una comprobaciÃ³n previa del servicio.
    /// </summary>
    [Fact]
    public async Task EjecutarElTrabajoDosvecesElMismoDiaNoMandaDosAvisos()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaRecordatorios.SesionAsync(bd, api, Roles.Admin, ct);
        await AyudaRecordatorios.PonerModoAsync(admin, ModoRecordatorios.Automatico, ct: ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, hoy.AddDays(-5), 300m, ct);

        await AyudaRecordatorios.EjecutarTrabajoAsync(api, ct);
        await AyudaRecordatorios.EjecutarTrabajoAsync(api, ct);

        var avisos = await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct);

        avisos.Should().ContainSingle("el Ãºnico de (cliente, unidad, fecha) es la idempotencia del trabajo");
        (await AyudaRecordatorios.CorreosDe(bd, api, escenario.ClienteId, ct)).Should().ContainSingle("y por eso sale un correo, no dos");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var envios = await contexto.Envio.AsNoTracking()
            .Where(e => e.ClienteId == escenario.ClienteId)
            .ToListAsync(ct);

        envios.Should().ContainSingle("la fila de envÃ­o es la del aviso, no hay una segunda");
    }

    /// <summary>La fila del aviso es la que se lee en la pantalla de revisiÃ³n, con su fecha de negocio.</summary>
    [Fact]
    public async Task ElAvisoQuedaEnPendienteConSuEnvio()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaRecordatorios.SesionAsync(bd, api, Roles.Admin, ct);
        await AyudaRecordatorios.PonerModoAsync(admin, ModoRecordatorios.Manual, ct: ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, hoy.AddDays(-5), 300m, ct);

        await AyudaRecordatorios.EjecutarTrabajoAsync(api, ct);

        var aviso = (await AyudaRecordatorios.AvisosAsync(bd, escenario.ClienteId, ct)).Single();

        aviso.Estado.Should().Be(EstadoRecordatorio.Pendiente);
        aviso.UnidadId.Should().Be(escenario.UnidadId, "Â§3.14 avisa por cliente y unidad");
        aviso.Fecha.Should().Be(hoy, "la fecha es la de negocio del aviso, no la del reloj del servidor");
        aviso.MotivoOmision.Should().BeNull("no se omitiÃ³ nada");

        var envio = await AyudaRecordatorios.EnvioDelAvisoAsync(bd, aviso, ct);

        envio.Estado.Should().Be(EstadoEnvio.Pendiente);
        envio.TipoDocumento.Should().Be(TipoDocumentoEnvio.EstadoCuenta);
    }
}