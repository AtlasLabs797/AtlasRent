using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Contratos;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Inventario;

/// <summary>
/// F2-08: la revisión de salida (docs/ESPECIFICACION.md §3.6). La revisión es un documento histórico
/// firmado una vez, y por eso las pruebas miran sobre todo lo que <b>no</b> deja hacer.
/// <list type="bullet">
/// <item>Se firma una vez: el segundo intento es 409 con el motivo, como el incremento ya aplicado de
/// F2-06.</item>
/// <item>Registra los tres estados (presente, ausente y dañado) y el importe del faltante para F2-09.</item>
/// <item>Se firma con el contrato vigente, con la baja ya programada o con el ya finalizado —que es
/// cuando el cliente se va—; en borrador no hay salida que revisar. Antes aquí ponía "o en
/// finalizado no hay salida que revisar" y era un callejón sin salida que impedía liquidar: lo
/// explica el test de F2-09 <c>LaRevisionSePuedeFirmarEnFinalizado</c>.</item>
/// <item>Un cuerpo que no describe un mueble es 400, no 409: un nombre vacío o una cantidad negativa
/// no son un conflicto con el estado de nadie.</item>
/// </list>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class RevisionSalidaTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    private static readonly (string Nombre, decimal Cantidad, string? Notas)[] TresElementos =
    [
        ("Colchón", 2, null),
        ("Nevera", 1, null),
        ("Silla de oficina", 4, null),
    ];

    /// <summary>
    /// Firmar es un hecho histórico, no un campo editable: la segunda firma es 409 con el motivo, y la
    /// primera revisión sigue siendo la que está escrita.
    /// </summary>
    [Fact]
    public async Task LaRevisionDeSalidaSeFirmaUnaVez()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaInventario.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaInventario.SesionAsync(api, bd, Roles.Gerente, ct);

        var (_, contratoId) = await AyudaInventario.ContratoActivoConInventarioAsync(
            empleado, gerente, bd, TresElementos, desplazamientoInicio: 310, ct: ct);

        var congelado = await AyudaInventario.InventarioContratoAsync(empleado, contratoId, ct);
        var primera = await AyudaInventario.FirmarAsync(
            gerente,
            contratoId,
            congelado,
            [("Presente", null), ("Presente", null), ("Presente", null)],
            importePropuesto: 0m,
            ct: ct);

        var segunda = await AyudaInventario.FirmarRawAsync(
            gerente,
            contratoId,
            AyudaInventario.CuerpoDeFirma(
                congelado,
                [("Ausente", "Se lo llevó"), ("Presente", null), ("Ausente", "Rotos")],
                importePropuesto: 99_000m),
            ct);

        segunda.StatusCode.Should().Be(
            HttpStatusCode.Conflict,
            "firmar es un hecho histórico: la segunda vez se devuelve 409 con el motivo");

        // Y lo que hay escrito sigue siendo lo primero que se firmó, no el segundo intento.
        var leida = await gerente.GetAsync($"/api/v1/contratos/{contratoId:D}/revision-inventario", ct);
        leida.StatusCode.Should().Be(HttpStatusCode.OK, await leida.Cuerpo());
        var cuerpo = await leida.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("id").GetGuid().Should().Be(primera.GetProperty("id").GetGuid());
        AyudaInventario.Estados(cuerpo).Should().AllBe("Presente");
        cuerpo.GetProperty("importePropuesto").GetDecimal().Should().Be(0m);
    }

    /// <summary>
    /// Los tres estados se guardan y se leen, y el importe del faltante queda escrito para que F2-09
    /// liquide contra él. "Ausente" y "dañado" son cosas distintas y llegan distintas a la firma.
    /// </summary>
    [Fact]
    public async Task LaRevisionRegistraAusenteYDanado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaInventario.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaInventario.SesionAsync(api, bd, Roles.Gerente, ct);

        var (_, contratoId) = await AyudaInventario.ContratoActivoConInventarioAsync(
            empleado, gerente, bd, TresElementos, desplazamientoInicio: 311, ct: ct);

        var congelado = await AyudaInventario.InventarioContratoAsync(empleado, contratoId, ct);
        var firmada = await AyudaInventario.FirmarAsync(
            gerente,
            contratoId,
            congelado,
            [
                ("Presente", null),
                ("Ausente", "No está la nevera"),
                ("Danado", "Dos sillas con el asiento roto"),
            ],
            importePropuesto: 12_500m,
            ct: ct);

        firmada.GetProperty("contratoId").GetGuid().Should().Be(contratoId);
        firmada.GetProperty("importePropuesto").GetDecimal().Should().Be(
            12_500m, "el importe del faltante queda en la revisión, que es contra lo que liquida F2-09");

        var lineas = firmada.GetProperty("lineas").EnumerateArray().ToList();
        lineas.Should().HaveCount(3, "la revisión dice el estado de cada elemento (§3.6)");
        AyudaInventario.Estados(firmada).Should().Equal("Presente", "Ausente", "Danado");

        lineas.Select(l => l.GetProperty("nombre").GetString()).Should()
            .Equal(TresElementos.Select(e => e.Nombre),
                "cada línea dice qué elemento del inventario congelado está comentando");
        lineas[1].GetProperty("nota").GetString().Should().Be("No está la nevera");
        lineas[2].GetProperty("nota").GetString().Should().Be("Dos sillas con el asiento roto");
        lineas[0].GetProperty("nota").ValueKind.Should().Be(
            JsonValueKind.Null,
            "en 'presente' la nota es opcional: no hay nada que anotar y no se inventa un texto");

        // Y se lee igual por la API, sin que la firma la haya dejado en un estado raro.
        var leida = await gerente.GetAsync($"/api/v1/contratos/{contratoId:D}/revision-inventario", ct);
        var cuerpo = await leida.Content.ReadFromJsonAsync<JsonElement>(ct);
        AyudaInventario.Estados(cuerpo).Should().Equal("Presente", "Ausente", "Danado");
    }

    /// <summary>
    /// La salida se revisa con el contrato vigente. En borrador el cliente no ha entrado; en
    /// finalizado y liquidado ya se fue, y revisar después sería escribir el documento histórico a
    /// posteriori. Con la baja ya programada sí se puede: el contrato sigue en pie.
    /// </summary>
    [Fact]
    public async Task LaRevisionNoSePuedeHacerEnUnContratoQueNoEstaActivo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaInventario.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaInventario.SesionAsync(api, bd, Roles.Gerente, ct);

        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);
        var inicio = AyudaContrato.Dia(312);

        await AyudaInventario.BaseAsync(
            empleado, unidadId, TresElementos, ct);

        var contratoId = await AyudaContrato.CrearAsync(
            empleado, AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, inicio), ct);

        // En borrador no hay nada que revisar: el inventario ni siquiera está congelado todavía. Con un
        // cuerpo bien formado, la regla que salta es la del estado, no la del cuerpo.
        var enBorrador = await AyudaInventario.FirmarRawAsync(
            gerente,
            contratoId,
            new
            {
                lineas = new object[]
                {
                    Linea(Guid.NewGuid(), "Presente"),
                    Linea(Guid.NewGuid(), "Presente"),
                    Linea(Guid.NewGuid(), "Presente"),
                },
                importePropuesto = (decimal?)null,
                observacion = "Intento en borrador.",
            },
            ct);
        enBorrador.StatusCode.Should().Be(
            HttpStatusCode.Conflict,
            "un contrato en borrador no tiene salida que revisar: el cliente todavía no ha entrado");
        await enBorrador.Cuerpo();

        await AyudaContrato.AceptarAsync(empleado, contratoId, "solicitud", new { }, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "aprobacion", null, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "activacion", null, ct);
        var congelado = await AyudaInventario.InventarioContratoAsync(empleado, contratoId, ct);

        // Con la baja programada el contrato sigue en pie: ahí sí se revisa la salida.
        await AyudaContrato.AceptarAsync(
            gerente, contratoId, "baja", new { fechaFin = inicio.AddMonths(12).ToString("yyyy-MM-dd") }, ct);
        var conBaja = await AyudaInventario.FirmarAsync(
            gerente,
            contratoId,
            congelado,
            [("Presente", null), ("Presente", null), ("Presente", null)],
            ct: ct);
        conBaja.GetProperty("contratoId").GetGuid().Should().Be(contratoId);
    }

    /// <summary>
    /// Un contrato <b>finalizado</b> sí tiene salida que revisar, y este test antes afirmaba lo
    /// contrario. Es la corrección de una instrucción previa (F2-09), y la razón importa:
    /// <para>
    /// El fin por fecha efectiva de F2-09 pasa el contrato a <c>Finalizado</c> solo, el día que la
    /// fecha efectiva llega, y la revisión se firma ese mismo día. Si aquí se exigiera el contrato "en
    /// pie", cualquiera que finalizara sin firmar se quedaría en un contrato que ya no se puede
    /// finalizar nunca, no se puede firmar nunca y por tanto no se puede liquidar nunca: la liquidación
    /// exige la revisión firmada. Eso no era una regla prudente, era un callejón sin salida.
    /// </para>
    /// <para>
    /// Lo que sigue sin permitirse es firmar antes de que el cliente entre (<c>Borrador</c>,
    /// <c>PendienteAprobacion</c>, <c>Aprobado</c>) y después de liquidar.
    /// </para>
    /// </summary>
    [Fact]
    public async Task LaRevisionSePuedeFirmarEnUnContratoFinalizado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaInventario.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaInventario.SesionAsync(api, bd, Roles.Gerente, ct);

        var (_, contratoId) = await AyudaInventario.ContratoActivoConInventarioAsync(
            empleado, gerente, bd, TresElementos, desplazamientoInicio: 314, ct: ct);

        var congelado = await AyudaInventario.InventarioContratoAsync(empleado, contratoId, ct);
        congelado.GetArrayLength().Should().Be(3, "el inventario sí está congelado: lo que falta es la revisión");

        await AyudaContrato.AceptarAsync(
            gerente, contratoId, "baja", new { fechaFin = AyudaContrato.Dia(314).AddMonths(12).ToString("yyyy-MM-dd") }, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "finalizacion", null, ct);
        (await AyudaContrato.EstadoAsync(bd, contratoId, ct)).Should().Be(EstadoContrato.Finalizado);

        var firmada = await AyudaInventario.FirmarAsync(
            gerente,
            contratoId,
            congelado,
            [("Presente", null), ("Presente", null), ("Presente", null)],
            ct: ct);
        firmada.GetProperty("contratoId").GetGuid().Should().Be(contratoId);
        firmada.GetProperty("importePropuesto").ValueKind.Should().Be(
            System.Text.Json.JsonValueKind.Null,
            "todo está presente: no hay faltante que liquidar");
    }

    private static object Linea(Guid contratoInventarioItemId, string estado) =>
        new { contratoInventarioItemId, estado, nota = (string?)null };

    /// <summary>
    /// Un cuerpo que no describe un mueble es 400, no 409. Y una revisión que no cubre todos los
    /// elementos del inventario congelado es 409, porque dejar un mueble sin comprobar y llamarlo
    /// "revisado" es justo el fallo que el criterio de aceptación quiere evitar.
    /// </summary>
    [Fact]
    public async Task LasValidacionesDelInventarioYDeLaRevision()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaInventario.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaInventario.SesionAsync(api, bd, Roles.Gerente, ct);

        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);

        // Un elemento sin nombre no es un mueble.
        var sinNombre = await empleado.PostAsync(
            $"/api/v1/unidades/{unidadId:D}/inventario-base", AyudaInventario.Elemento(nombre: null), ct);
        sinNombre.StatusCode.Should().Be(
            HttpStatusCode.BadRequest,
            "un elemento sin nombre no describe nada y sale como cuerpo inválido, no como conflicto");
        await sinNombre.Cuerpo();

        // Una cantidad negativa tampoco.
        var negativa = await empleado.PostAsync(
            $"/api/v1/unidades/{unidadId:D}/inventario-base",
            AyudaInventario.Elemento(cantidad: -3),
            ct);
        negativa.StatusCode.Should().Be(HttpStatusCode.BadRequest, "no hay -3 sillas: es un tecleo");
        await negativa.Cuerpo();

        await AyudaInventario.BaseAsync(empleado, unidadId, TresElementos, ct);

        var contratoId = await AyudaContrato.CrearAsync(
            empleado, AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, AyudaContrato.Dia(313)), ct);
        await AyudaContrato.AceptarAsync(empleado, contratoId, "solicitud", new { }, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "aprobacion", null, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "activacion", null, ct);

        var congelado = await AyudaInventario.InventarioContratoAsync(empleado, contratoId, ct);

        // Una revisión que deja un elemento sin mirar es 409, no 201.
        // Una revisión que deja un elemento sin mirar es 409, no 201. El cuerpo va a mano porque la
        // ayuda los empareja por cantidad y justo lo que se prueba es que falte uno.
        var incompleta = await AyudaInventario.FirmarRawAsync(
            gerente,
            contratoId,
            new
            {
                lineas = AyudaInventario.LineasDe(congelado, [("Presente", null), ("Presente", null)]),
                importePropuesto = (decimal?)null,
                observacion = "Se olvidó de una silla.",
            },
            ct);
        incompleta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await incompleta.Cuerpo();

        // Y un importe de faltante negativo tampoco entra: el faltante es lo que se pide, nunca un saldo
        // a favor que nadie ha pedido.
        var negativo = await AyudaInventario.FirmarRawAsync(
            gerente,
            contratoId,
            AyudaInventario.CuerpoDeFirma(
                congelado,
                [("Presente", null), ("Presente", null), ("Presente", null)],
                importePropuesto: -100m),
            ct);
        negativo.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await negativo.Cuerpo();

        // Un ausente sin nota es 400: la nota es lo que explica el importe que se va a proponer.
        var sinNota = await AyudaInventario.FirmarRawAsync(
            gerente,
            contratoId,
            AyudaInventario.CuerpoDeFirma(
                congelado,
                [("Presente", null), ("Ausente", null), ("Presente", null)]),
            ct);
        sinNota.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await sinNota.Cuerpo();
    }
}