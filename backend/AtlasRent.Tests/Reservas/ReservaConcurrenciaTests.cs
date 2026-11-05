using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Reservas;

/// <summary>
/// F2-03: el criterio de aceptación del plan de reservas, comprobado de punta a punta por la API.
/// <para>
/// La capa de base de datos de F2-01 ya comprueba la exclusividad de los bloqueos y la capacidad del
/// día. Estos tests no la vuelven a probar por dentro: la prueban por el camino que de verdad usa
/// una persona —dos peticiones HTTP a la vez contra la misma unidad—, que es donde además entran el
/// servicio de reservas, su transacción y el <c>Idempotency-Key</c>.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ReservaConcurrenciaTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// Dos reservas solapadas por la misma unidad y las mismas fechas: exactamente una entra y la
    /// otra falla.
    /// <para>
    /// Son dos reservas de <b>día completo</b>, no de medio día: el día completo es de uno solo, así
    /// que dos peticiones que lo piden a la vez son dos reservas que se solapan de verdad. Con dos
    /// medios días no habría solapamiento — caben los dos, y ese es justamente el otro criterio.
    /// </para>
    /// <para>
    /// Las dos peticiones salen <b>de verdad a la vez</b>: cada una con su cliente HTTP y su
    /// conexión, y las dos esperando a la misma barrera antes de salir. No son dos llamadas seguidas,
    /// que es como la base de datos no vería nunca una carrera. Y van <b>sin</b>
    /// <c>Idempotency-Key</c>: con la misma clave, la segunda sería un reintento guardado y no
    /// llegaría nunca a la base, que es justo lo que este test quiere que ocurra.
    /// </para>
    /// <para>
    /// Que gane una u otra da igual: lo que se comprueba es que no ganen las dos y que la perdedora
    /// reciba un 409 de disponibilidad, no un error de servidor.
    /// </para>
    /// </summary>
    [Fact]
    public async Task DosReservasSolapadasEnParaleloSoloUnaEntra()
    {
        var ct = TestContext.Current.CancellationToken;
        using var una = await SesionAsync(ct);
        using var otra = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(una, ct);
        var unidadId = await AyudaReserva.UnidadAsync(una, ct);
        var fecha = AyudaReserva.Dia(90);
        var cuerpo = AyudaReserva.Corta(
            clienteId,
            unidadId,
            fecha,
            dias: new[] { AyudaReserva.Franja(fecha, "08:00:00", "16:00:00") },
            modalidad: "DiaCompleto");

        var barrera = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var primera = EnviarAlSalir(barrera, una, cuerpo, ct);
        var segunda = EnviarAlSalir(barrera, otra, cuerpo, ct);

        barrera.SetResult();
        var estados = await Task.WhenAll(primera, segunda);
        var bloqueos = await AyudaReserva.BloqueosAsync(bd, unidadId, fecha, ct);

        estados.Count(e => e == HttpStatusCode.Created).Should().Be(1,
            "la unidad no puede estar reservada por dos personas a la vez");
        estados.Count(e => e == HttpStatusCode.Conflict).Should().Be(1,
            "la que pierde recibe un 409 de disponibilidad, no un 500");

        // Y la base de datos dice lo mismo que la API: un solo bloqueo vivo en ese día.
        bloqueos.Should().ContainSingle(b => b.Bloquea,
            "solo la reserva que entró dejó el candado puesto");
    }

    /// <summary>
    /// El otro criterio del plan: 2 medios días entran, el tercero falla. Es la capacidad del día de
    /// F2-01 vista por la API de reservas.
    /// </summary>
    [Fact]
    public async Task DosMediosDiasEntranYElTerceroFalla()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var fecha = AyudaReserva.Dia(91);

        var estados = new List<HttpStatusCode>();
        for (var persona = 0; persona < 3; persona++)
        {
            var respuesta = await AyudaReserva.CrearRawAsync(
                sesion, AyudaReserva.Corta(clienteId, unidadId, fecha), ct);
            estados.Add(respuesta.StatusCode);
        }

        estados.Count(e => e == HttpStatusCode.Created).Should().Be(2,
            "una unidad y un día caben en dos medios días");
        estados.Count(e => e == HttpStatusCode.Conflict).Should().Be(1,
            "la tercera persona se queda fuera");

        var bloqueos = await AyudaReserva.BloqueosAsync(bd, unidadId, fecha, ct);
        bloqueos.Count(b => b.Bloquea).Should().Be(2, "los dos medios días que entraron");
        bloqueos.Should().OnlyContain(b => b.Tipo == TipoOcupacionDia.MedioDia);
    }

    /// <summary>La barrera de salida: las dos peticiones esperan aquí y las dos salen a la vez.</summary>
    private static async Task<HttpStatusCode> EnviarAlSalir(
        TaskCompletionSource barrera,
        SesionCliente sesion,
        object cuerpo,
        CancellationToken ct)
    {
        await barrera.Task;

        return (await sesion.PostAsync("/api/v1/reservas", cuerpo, ct)).StatusCode;
    }

    private async Task<SesionCliente> SesionAsync(CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }
}