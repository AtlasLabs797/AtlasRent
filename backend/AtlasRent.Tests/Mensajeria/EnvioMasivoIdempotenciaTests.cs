using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Mensajeria;

/// <summary>
/// F4-03: confirmar dos veces con la misma <c>Idempotency-Key</c> no crea dos lotes.
/// <para>
/// Importa más de lo que parece en esta tarea: sin esto, un doble clic en «confirmar» encolaría dos
/// veces cada correo del lote. El lote no tiene fila propia —es el identificador que comparten las
/// filas de <c>envio</c>—, así que son esas filas las que no pueden duplicarse.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class EnvioMasivoIdempotenciaTests(ContenedorBdFixture bd, ApiTestFactoryMasivo api)
    : IClassFixture<ApiTestFactoryMasivo>
{
    /// <summary>La misma clave devuelve el mismo lote y no encola una segunda vez.</summary>
    [Fact]
    public async Task LaMismaClaveNoCreaDosLotes()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaEnviosMasivo.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        api.Cola.Vaciar();

        var clave = $"masivo-{Guid.NewGuid():N}";
        var peticion = AyudaEnviosMasivo.Peticion(escenario.ClienteId);

        var primero = await AyudaEnviosMasivo.ConfirmarAsync(admin, peticion, ct, clave);
        var segundo = await AyudaEnviosMasivo.ConfirmarAsync(admin, peticion, ct, clave);

        segundo.Should().Be(primero, "la misma clave devuelve el lote que ya se creó");
        api.Cola.Encolados.Should().HaveCount(1, "el reintento no encola otra vez los mismos correos");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var filas = await contexto.Envio.AsNoTracking()
            .Where(e => e.DocumentoId != Guid.Empty && e.LoteId != null)
            .Where(e => e.Id != Guid.Empty)
            .ToListAsync(ct);

        filas.Count(e => e.LoteId == primero).Should().Be(1, "el lote tiene un envío y solo uno");
        filas.Count(e => e.LoteId == segundo).Should().Be(1);

        (await AyudaEnviosMasivo.FilasDelDocumentoAsync(bd, filas.First(e => e.LoteId == primero).DocumentoId, ct))
            .Should().HaveCount(1, "y el documento no tiene dos filas de envío");
    }

    /// <summary>
    /// Preparar también acepta la clave: la respuesta es larga y repetirla no tiene por qué costar
    /// otro recorrido entero de la vista de cuentas. Y repetirla no manda nada.
    /// </summary>
    [Fact]
    public async Task PrepararConLaMismaClaveDevuelveElMismoResumen()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, _) = await AyudaEnviosMasivo.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        api.Cola.Vaciar();
        var correosAntes = api.Doble.Enviados.Count;

        var clave = $"preparar-{Guid.NewGuid():N}";
        var peticion = AyudaEnviosMasivo.Peticion(escenario.ClienteId);

        var primero = await AyudaEnviosMasivo.PrepararAsync(admin, peticion, ct, clave);
        var segundo = await AyudaEnviosMasivo.PrepararAsync(admin, peticion, ct, clave);

        primero.GetProperty("total").GetInt32().Should().Be(segundo.GetProperty("total").GetInt32());
        api.Cola.Encolados.Should().BeEmpty();
        api.Doble.Enviados.Should().HaveCount(correosAntes);
    }
}