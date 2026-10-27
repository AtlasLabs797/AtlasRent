using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Reservas;

/// <summary>
/// F2-03: las reglas de la reserva corta de docs/ESPECIFICACION.md §3.4 y D11. Todas se comprueban
/// por la API, que es donde el usuario las se.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ReservaCortaReglasTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// Sin franjas fijas (D11): lo que manda es la modalidad, no las horas. Medio día y día completo
    /// entran con cualquier hora orientativa.
    /// </summary>
    [Fact]
    public async Task LaModalidadDecideElTipoDelDiaYNoLasHoras()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);

        var medio = await AyudaReserva.CrearRawAsync(
            sesion,
            AyudaReserva.Corta(clienteId, unidadId, AyudaReserva.Dia(94), new[]
            {
                AyudaReserva.Franja(AyudaReserva.Dia(94), "08:00:00", "11:00:00"),
            }),
            ct);
        medio.StatusCode.Should().Be(HttpStatusCode.Created, "tres horas orientativas son un medio día");

        var jornada = await AyudaReserva.CrearRawAsync(
            sesion,
            AyudaReserva.Corta(
                clienteId,
                unidadId,
                AyudaReserva.Dia(95),
                new[] { AyudaReserva.Franja(AyudaReserva.Dia(95), "08:00:00", "16:00:00") },
                modalidad: "DiaCompleto"),
            ct);
        jornada.StatusCode.Should().Be(HttpStatusCode.Created, "el día completo entra si la unidad está libre");

        var bloqueos = await AyudaReserva.BloqueosAsync(bd, unidadId, AyudaReserva.Dia(95), ct);
        bloqueos.Should().ContainSingle(b => b.Tipo == TipoOcupacionDia.DiaCompleto);
    }

    /// <summary>
    /// El día completo es de uno solo: con los dos medios días cogidos, el día completo ya no cabe.
    /// </summary>
    [Fact]
    public async Task ElDiaCompletoNoEntraSiElDiaYaEstaOcupado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var ocupada = await AyudaReserva.UnidadAsync(sesion, ct);
        var fecha = AyudaReserva.Dia(96);

        await AyudaReserva.CrearAsync(sesion, AyudaReserva.Corta(clienteId, ocupada, fecha), ct);
        await AyudaReserva.CrearAsync(
            sesion,
            AyudaReserva.Corta(
                clienteId,
                ocupada,
                fecha,
                new[] { AyudaReserva.Franja(fecha, "13:00:00", "17:00:00") }),
            ct);

        (await AyudaReserva.CrearRawAsync(
            sesion,
            AyudaReserva.Corta(
                clienteId,
                ocupada,
                fecha,
                new[] { AyudaReserva.Franja(fecha, "08:00:00", "16:00:00") },
                modalidad: "DiaCompleto"),
            ct)).StatusCode.Should().Be(HttpStatusCode.Conflict,
            "el día ya tiene sus dos medios días y el completo no cabe");
    }

    /// <summary>
    /// Entrada y salida el mismo día, siempre (docs/ESPECIFICACION.md §3.4). Una reserva que pide
    /// dormir fuera no es una corta: es una mensual, y por eso se rechaza con un 409 que lo dice.
    /// </summary>
    [Fact]
    public async Task EntradaYSalidaEnElMismoDia()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var entrada = AyudaReserva.Dia(97);

        var respuesta = await AyudaReserva.CrearRawAsync(
            sesion,
            AyudaReserva.Corta(clienteId, unidadId, entrada, salida: entrada.AddDays(1)),
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await AyudaReserva.Json(respuesta, ct)).GetProperty("detail").GetString()
            .Should().Contain("mismo día", "el motivo es de negocio, no un error de formato");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.OcupacionDia.CountAsync(d => d.UnidadId == unidadId, ct)).Should().Be(0,
            "una reserva rechazada no bloquea nada");
    }

    /// <summary>
    /// Dos personas el mismo día son dos reservas. Meter las dos en una sola choca con el bloqueo,
    /// que es de unidad, día y fila de origen: el trigger contaría uno y dejaría pasar a un
    /// tercero. El servicio lo rechaza antes, con un error que se puede entender.
    /// </summary>
    [Fact]
    public async Task UnaReservaNoRepiteElMismoDia()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var fecha = AyudaReserva.Dia(98);

        var respuesta = await AyudaReserva.CrearRawAsync(
            sesion,
            AyudaReserva.Corta(clienteId, unidadId, fecha, new[]
            {
                AyudaReserva.Franja(fecha, "08:00:00", "12:00:00"),
                AyudaReserva.Franja(fecha, "13:00:00", "17:00:00"),
            }),
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await AyudaReserva.Json(respuesta, ct)).GetProperty("detail").GetString()
            .Should().Contain("dos reservas");
    }

    /// <summary>
    /// Una reserva corta necesita cliente o prospecto, no los dos ni ninguno, y el cuerpo se
    /// valida entero antes de tocar la base.
    /// </summary>
    [Fact]
    public async Task ElDestinatarioEsUnoSoloYLasHorasSePiden()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var prospectoId = await AyudaReserva.ProspectoAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var fecha = AyudaReserva.Dia(99);

        (await AyudaReserva.CrearRawAsync(
            sesion, AyudaReserva.Corta(null, unidadId, fecha), ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict,
                "sin cliente ni prospecto no hay a quién reservar");

        var cuerpoConLosDos = new
        {
            unidadId,
            clienteId,
            prospectoId,
            tipo = "Corta",
            entradaPrevista = fecha.ToString("yyyy-MM-dd"),
            modalidad = "MedioDia",
            moneda = "DOP",
            importe = 1000m,
            dias = new[] { AyudaReserva.Franja(fecha) },
        };
        (await AyudaReserva.CrearRawAsync(sesion, cuerpoConLosDos, ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await AyudaReserva.CrearRawAsync(
            sesion, AyudaReserva.Corta(null, unidadId, fecha, prospectoId: prospectoId), ct))
            .StatusCode.Should().Be(HttpStatusCode.Created, "un prospecto también puede reservar");
    }

    /// <summary>
    /// Editar una corta en la <b>misma fecha</b> de medio día a jornada, y de vuelta, tiene que
    /// entrar. Es la ruta del <c>UPDATE</c> del bloqueo, no la del <c>INSERT</c>.
    /// <para>
    /// <c>SincronizarAsync</c> (F2-03) no borra y reinserta la fila de <c>ocupacion_dia</c>: si la
    /// fecha sigue en la reserva, la fila se queda y lo que cambia es su <c>tipo</c>. El trigger de
    /// capacidad de F2-01 se dispara también en ese <c>UPDATE</c> y, contando las filas vivas de la
    /// unidad en esa fecha, contaba <b>la propia</b>: dentro de un <c>BEFORE ROW</c> el
    /// <c>SELECT</c> ve el estado anterior, así que la fila que se estaba tocando salía en el
    /// recuento y el resultado era un falso positivo —"el día ya está ocupado y un día completo no
    /// cabe" al pasar a jornada, y "ya está reservado el día completo" al volver a medio día—.
    /// Nadie podía corregir su propia reserva.
    /// </para>
    /// <para>
    /// Lo que además hay que comprobar es que el arreglo no haya abierto el día: con la jornada
    /// puesta, otra reserva sí tiene que chocar.
    /// </para>
    /// </summary>
    [Fact]
    public async Task EditarLaMismaFechaCambiaDeMedioDiaAJornadaYVuelve()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var fecha = AyudaReserva.Dia(100);

        var id = await AyudaReserva.CrearAsync(
            sesion, AyudaReserva.Corta(clienteId, unidadId, fecha), ct);

        var jornada = await sesion.PutAsync(
            $"/api/v1/reservas/{id:D}",
            AyudaReserva.Corta(
                clienteId,
                unidadId,
                fecha,
                new[] { AyudaReserva.Franja(fecha, "08:00:00", "16:00:00") },
                modalidad: "DiaCompleto"),
            ct);

        jornada.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "ampliar la propia reserva a la jornada no choca con el día que ocupa ella misma");

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            (await contexto.OcupacionDia.AsNoTracking().SingleAsync(d => d.OrigenId == id, ct)).Tipo
                .Should().Be(TipoOcupacionDia.DiaCompleto, "el bloqueo pasó a ser de día entero");
        }

        // Y el revés: de vuelta a medio día.
        var vuelta = await sesion.PutAsync(
            $"/api/v1/reservas/{id:D}", AyudaReserva.Corta(clienteId, unidadId, fecha), ct);

        vuelta.StatusCode.Should().Be(
            HttpStatusCode.OK, "volver a medio día tampoco choca con el día completo que ocupa ella misma");

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            (await contexto.OcupacionDia.AsNoTracking().SingleAsync(d => d.OrigenId == id, ct)).Tipo
                .Should().Be(TipoOcupacionDia.MedioDia);
        }
    }

    /// <summary>
    /// El mismo cambio de tipo, pero cuando el día ya tiene sus dos capacidades ocupadas: ahí el
    /// trigger tiene que seguir rechazando, porque lo que se comprueba es la ocupación de los demás.
    /// <para>
    /// Es el contrapeso del anterior. Si el arreglo hubiera sido "dejar de contar" en vez de "dejar
    /// de contar la fila propia", este test pasaría y el anterior también, y una unidad podría llevar
    /// las dos mitades y además dejar que otro la tomara entera.
    /// </para>
    /// </summary>
    [Fact]
    public async Task EditarAJornadaNoEntraSobreLaJornadaDeOtra()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var fecha = AyudaReserva.Dia(101);

        // El día ya está con sus dos medios días: la jornada no cabe en ninguno de los dos huecos.
        var ajena = await AyudaReserva.CrearAsync(
            sesion, AyudaReserva.Corta(clienteId, unidadId, fecha), ct);

        var propia = await AyudaReserva.CrearAsync(
            sesion,
            AyudaReserva.Corta(
                clienteId,
                unidadId,
                fecha,
                new[] { AyudaReserva.Franja(fecha, "13:00:00", "17:00:00") }),
            ct);

        (await sesion.PutAsync(
            $"/api/v1/reservas/{propia:D}",
            AyudaReserva.Corta(
                clienteId,
                unidadId,
                fecha,
                new[] { AyudaReserva.Franja(fecha, "08:00:00", "16:00:00") },
                modalidad: "DiaCompleto"),
            ct)).StatusCode.Should().Be(
            HttpStatusCode.Conflict,
            "el otro medio día ocupa el día para los demás, y aquí no hay que mirarse a uno mismo");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.OcupacionDia.AsNoTracking().SingleAsync(d => d.OrigenId == propia, ct)).Tipo
            .Should().Be(TipoOcupacionDia.MedioDia, "la edición rechazada no dejó el bloqueo a medias");
        (await contexto.OcupacionDia.AsNoTracking().SingleAsync(d => d.OrigenId == ajena, ct)).Tipo
            .Should().Be(TipoOcupacionDia.MedioDia, "la otra reserva sigue con su medio día");
    }

    private async Task<SesionCliente> SesionAsync(CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }
}