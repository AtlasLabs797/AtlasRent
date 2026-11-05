using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Jobs;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AtlasRent.Tests.Mensajeria;

/// <summary>
/// BUG-037 y BUG-038: el envío masivo no deja filas huérfanas (barrido) y un mismo envío no sale dos
/// veces aunque dos trabajos compitan o el guardado final falle (reclamo Pendiente a Enviando).
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class EnvioRobustezTests(ContenedorBdFixture bd, ApiTestFactoryMasivo api)
    : IClassFixture<ApiTestFactoryMasivo>
{
    private async Task<(Guid EnvioId, Guid Lote, Guid Usuario)> LoteDeUnoAsync(CancellationToken ct)
    {
        var (admin, usuario) = await AyudaEnviosMasivo.SesionAsync(bd, api, Roles.Admin, ct);
        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        api.Cola.Vaciar();
        var lote = await AyudaEnviosMasivo.ConfirmarAsync(admin, AyudaEnviosMasivo.Peticion(escenario.ClienteId), ct);

        return (api.Cola.Encolados[0], lote, usuario);
    }

    private async Task BarrerAsync(CancellationToken ct)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TrabajoBarridoEnvios>().EjecutarAsync(ct);
    }

    /// <summary>BUG-037: el encolado se perdió (la cola no hizo nada); el barrido lo recupera y sale una sola vez.</summary>
    [Fact]
    public async Task ElBarridoReencolaLoPendienteSinTrabajoYSaleUnaSolaVez()
    {
        var ct = TestContext.Current.CancellationToken;
        var (envioId, lote, usuario) = await LoteDeUnoAsync(ct);

        // Recién confirmado: todavía no es huérfano, el barrido no lo toca.
        api.Cola.Vaciar();
        await BarrerAsync(ct);
        api.Cola.Encolados.Should().NotContain(envioId);

        // Pasa una hora sin que nadie lo mande: el trabajo se perdió entre el commit y el encolado.
        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            await contexto.Envio.Where(e => e.LoteId == lote)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.CreatedAt, DateTimeOffset.UtcNow.AddHours(-1)), ct);
        }

        await BarrerAsync(ct);
        api.Cola.Encolados.Should().Contain(envioId, "el barrido reencola lo que quedó pendiente sin trabajo");

        // Si el barrido vuelve a pasar enseguida no lo duplica en cada pasada.
        var tras = api.Cola.Total;
        await BarrerAsync(ct);
        api.Cola.Total.Should().Be(tras);

        var antes = api.Doble.Enviados.Count;
        await AyudaEnviosMasivo.EjecutarTrabajoAsync(api, envioId, usuario, ct);
        await AyudaEnviosMasivo.EjecutarTrabajoAsync(api, envioId, usuario, ct);

        api.Doble.Enviados.Should().HaveCount(antes + 1, "reencolar es idempotente: un envío, un correo");
        (await AyudaEnviosMasivo.FilasDelLoteAsync(bd, lote, ct)).Single().Estado.Should().Be(EstadoEnvio.Enviado);
    }

    /// <summary>BUG-038: dos trabajos simultáneos para la misma fila mandan un solo correo.</summary>
    [Fact]
    public async Task DosEjecucionesConcurrentesMandanUnSoloCorreo()
    {
        var ct = TestContext.Current.CancellationToken;
        var (envioId, lote, usuario) = await LoteDeUnoAsync(ct);
        var antes = api.Doble.Enviados.Count;

        // Ambos llegan al servidor de correo a la vez si el reclamo no existe.
        var barrera = new Barrier(2);
        api.Doble.AntesDeEnviar = () => barrera.SignalAndWait(TimeSpan.FromSeconds(3));

        try
        {
            await Task.WhenAll(
                Task.Run(() => AyudaEnviosMasivo.EjecutarTrabajoAsync(api, envioId, usuario, ct), ct),
                Task.Run(() => AyudaEnviosMasivo.EjecutarTrabajoAsync(api, envioId, usuario, ct), ct));
        }
        finally
        {
            api.Doble.AntesDeEnviar = null;
        }

        api.Doble.Enviados.Should().HaveCount(antes + 1);
        var fila = (await AyudaEnviosMasivo.FilasDelLoteAsync(bd, lote, ct)).Single();
        fila.Estado.Should().Be(EstadoEnvio.Enviado);
        fila.Intentos.Should().Be(1);
    }

    /// <summary>BUG-038: si el guardado tras enviar falla, el reintento no reenvía: queda Enviando para revisión.</summary>
    [Fact]
    public async Task SiFallaElGuardadoTrasEnviarElReintentoNoReenvia()
    {
        var ct = TestContext.Current.CancellationToken;
        var (envioId, lote, usuario) = await LoteDeUnoAsync(ct);
        var antes = api.Doble.Enviados.Count;

        using var fuente = new CancellationTokenSource();
        api.Doble.DespuesDeEnviar = () => fuente.Cancel();

        try
        {
            var primero = async () => await AyudaEnviosMasivo.EjecutarTrabajoAsync(api, envioId, usuario, fuente.Token);
            await primero.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            api.Doble.DespuesDeEnviar = null;
        }

        (await AyudaEnviosMasivo.FilasDelLoteAsync(bd, lote, ct)).Single().Estado
            .Should().Be(EstadoEnvio.Enviando, "no se pudo apuntar el resultado");

        // El reintento de Hangfire.
        await AyudaEnviosMasivo.EjecutarTrabajoAsync(api, envioId, usuario, ct);

        api.Doble.Enviados.Should().HaveCount(antes + 1, "el reintento no vuelve a mandar un correo que pudo salir");
        (await AyudaEnviosMasivo.FilasDelLoteAsync(bd, lote, ct)).Single().Estado.Should().Be(EstadoEnvio.Enviando);
    }

    /// <summary>BUG-038: un usuario sin permiso no provoca reintentos: la fila queda Fallido y no se manda nada.</summary>
    [Fact]
    public async Task SinPermisoNoSeReintentaYQuedaFallido()
    {
        var ct = TestContext.Current.CancellationToken;
        var (envioId, lote, _) = await LoteDeUnoAsync(ct);
        var (_, empleado) = await AyudaEnviosMasivo.SesionAsync(bd, api, Roles.Empleado, ct);
        var antes = api.Doble.Enviados.Count;

        await AyudaEnviosMasivo.EjecutarTrabajoAsync(api, envioId, empleado, ct);

        api.Doble.Enviados.Should().HaveCount(antes);
        (await AyudaEnviosMasivo.FilasDelLoteAsync(bd, lote, ct)).Single().Estado.Should().Be(EstadoEnvio.Fallido);
    }
}
