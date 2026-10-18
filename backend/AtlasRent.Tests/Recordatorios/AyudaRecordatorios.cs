using AtlasRent.Api.Authorization;
using System.Net;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Jobs;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Mensajeria;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtlasRent.Tests.Recordatorios;

/// <summary>
/// Apoyo común de las pruebas de recordatorios (F4-05, docs/ESPECIFICACION.md §3.14, D12).
/// <para>
/// <b>Los datos salen de la API real</b>, como en el resto de las pruebas: un cliente y un contrato de
/// verdad, facturas emitidas de verdad y cobros de verdad. Un recordatorio montado a mano sobre la
/// tabla probaría un aviso que el sistema no tiene.
/// </para>
/// <para>
/// El trabajo se pide al contenedor y no se llama por HTTP, que es lo mismo que hace
/// <see cref="AyudaEnviosMasivo.EjecutarTrabajoAsync"/> con el trabajo de la cola: así el barrido usa
/// el mismo <c>IServicioCorreo</c> —el doble— que el resto de la prueba y no sale nada a internet.
/// </para>
/// </summary>
internal static class AyudaRecordatorios
{
    /// <summary>Sesión de un rol con su identificador de usuario, que es quien firma los avisos.</summary>
    /// <para>
    /// <b>Hay que volver a crearla después de avanzar el reloj.</b> La sesión tiene un máximo
    /// absoluto de doce horas (AUT-003) y un salto de días la deja caducada. Por eso el trabajo se
    /// pide al contenedor y no por HTTP: correrlo no necesita sesión, y así la prueba puede viajar
    /// quince días sin loguearse quince veces.
    /// </para>
    public static Task<(SesionCliente Sesion, Guid Usuario)> SesionAsync(
        ContenedorBdFixture bd,
        ApiTestFactory api,
        string rol,
        CancellationToken ct) =>
        AyudaEnviosMasivo.SesionAsync(bd, api, rol, ct);

    /// <summary>
    /// Los ajustes de la casa: el modo y los plazos. Van por la API porque es la pantalla por la que
    /// se cambian, y porque es esa llamada la que deja <c>updated_by</c> —el usuario que firma los
    /// avisos automáticos— y sin él el trabajo no mandaría nada.
    /// </summary>
    public static async Task PonerModoAsync(
        SesionCliente admin,
        ModoRecordatorios modo,
        int? primero = 5,
        int? entre = 15,
        CancellationToken ct = default)
    {
        var respuesta = await admin.PutAsync(
            "/api/v1/config/ajustes",
            new
            {
                toleranciaCambioUsd = (decimal?)null,
                toleranciaCambioDop = (decimal?)null,
                modoRecordatorios = modo.ToString(),
                diasPrimerRecordatorio = primero,
                diasEntreRecordatorios = entre,
            },
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());
    }

    /// <summary>Corre el trabajo diario, como haría el planificador a las 09:00.</summary>
    public static async Task EjecutarTrabajoAsync(ApiTestFactory api, CancellationToken ct)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var trabajo = scope.ServiceProvider.GetRequiredService<TrabajoRecordatorios>();

        await trabajo.EjecutarAsync(ct);
    }

    /// <summary>Avanza el reloj de la aplicación los días pedidos.</summary>
    public static void Avanzar(ApiTestFactory api, int dias) =>
        api.Reloj.Avanzar(TimeSpan.FromDays(dias));

    /// <summary>Los avisos de un cliente, leídos de la base y no de la respuesta.</summary>
    public static async Task<List<Recordatorio>> AvisosAsync(
        ContenedorBdFixture bd,
        Guid clienteId,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        return await contexto.Recordatorio.AsNoTracking()
            .Where(r => r.ClienteId == clienteId)
            .OrderBy(r => r.Fecha)
            .ThenBy(r => r.Id)
            .ToListAsync(ct);
    }

    /// <summary>El envío al que dio lugar el aviso, leído de la base.</summary>
    public static async Task<Envio> EnvioDelAvisoAsync(
        ContenedorBdFixture bd,
        Recordatorio recordatorio,
        CancellationToken ct)
    {
        recordatorio.EnvioId.Should().NotBeNull("el aviso preparado tiene siempre un envío detrás");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        return await contexto.Envio.AsNoTracking()
            .FirstAsync(e => e.Id == recordatorio.EnvioId!.Value, ct);
    }

    /// <summary>Las alertas vivas de un tipo, leídas de la base.</summary>
    public static async Task<List<Alerta>> AlertasAsync(
        ContenedorBdFixture bd,
        string tipo,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        return await contexto.Alerta.AsNoTracking()
            .Where(a => a.Tipo == tipo && a.ResueltaAt == null)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Los correos que este doble ha recibido <b>para este cliente</b>.
    /// <para>
    /// El barrido es global: cuando corre, ve también las facturas vencidas que dejaron las otras
    /// pruebas de la clase, que es lo que tiene que hacer en producción. Contar los correos sin
    /// filtrar sería medir las demás pruebas, así que se filtra por el nombre del cliente, que cada
    /// prueba tiene distinto y va en la primera línea del cuerpo.
    /// </para>
    /// </summary>
    public static async Task<List<MensajeCorreo>> CorreosDe(
        ContenedorBdFixture bd,
        ApiTestFactoryCorreo api,
        Guid clienteId,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        var nombre = await contexto.Cliente.AsNoTracking()
            .Where(c => c.Id == clienteId)
            .Select(c => c.Nombre)
            .SingleAsync(ct);

        return
        [
            .. api.Doble.Enviados.Where(c => c.Cuerpo.StartsWith($"Estimado {nombre}:", StringComparison.Ordinal)),
        ];
    }

    /// <summary>Activa o levanta la pausa de recordatorios de un cliente.</summary>
    public static async Task PausarAsync(SesionCliente sesion, Guid clienteId, bool pausados, CancellationToken ct)
    {
        var respuesta = await sesion.PutAsync(
            $"/api/v1/clientes/{clienteId:D}/recordatorios-pausados",
            new { pausados },
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());
    }

    /// <summary>Envía un aviso a mano, como el botón de la pantalla de revisión.</summary>
    public static async Task<string> EnviarAsync(SesionCliente sesion, Guid recordatorioId, CancellationToken ct)
    {
        var respuesta = await sesion.PostAsync($"/api/v1/recordatorios/{recordatorioId:D}/enviar", new { }, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());

        return await respuesta.Content.ReadAsStringAsync(ct);
    }
}