using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Contratos;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Calendario;

/// <summary>
/// F2-04: el calendario de disponibilidad muestra contratos, reservas, medios días y
/// mantenimiento (criterio de aceptación de <c>docs/PLAN.md</c>).
/// <list type="bullet">
/// <item>Lee <c>ocupacion_rango</c> y <c>ocupacion_dia</c>, que son la fuente de verdad de la
/// disponibilidad desde F2-01, y nunca <c>contrato</c> ni <c>reserva</c>: el test que el calendario
/// es de solo lectura y el que comprueba que una ocupación liberada no aparece son los que
///_subjectan esa lectura.</item>
/// <item>El estado manual de la unidad sale sin ninguna ocupación con fecha, que es justo lo que
/// §3.1 dice que no es un campo "ocupado".</item>
/// <item>Un día con dos medios días trae los dos. Es el punto donde un calendario mal hecho se
/// come la mitad de la jornada.</item>
/// </list>
/// <para>
/// Cada prueba crea su unidad, así que no se pisan entre sí aunque compartan la base de la
/// colección.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class CalendarioDisponibilidadTests(
    ContenedorBdFixture bd,
    ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    // ---------------------------------------------------------------- base

    /// <summary>
    /// El caso base: una unidad sin nada encima sale libre en todos los días del rango, y trae un
    /// día por cada fecha pedida. Sin esto, un fallo de los demás tests se puede esconder detrás
    /// de un calendario que no pinta nada.
    /// </summary>
    [Fact]
    public async Task UnaUnidadLibreSaleLibreTodoElRango()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var desde = AyudaCalendario.Dia(10);
        var hasta = AyudaCalendario.Dia(16);

        var unidad = AyudaCalendario.PrimeraUnidad(
            await AyudaCalendario.DeUnidadAsync(sesion, unidadId, desde, hasta, ct));

        unidad.GetProperty("desde").GetString().Should().Be($"{desde:yyyy-MM-dd}");
        unidad.GetProperty("hasta").GetString().Should().Be($"{hasta:yyyy-MM-dd}",
            "los dos extremos del rango van incluidos: es como se pide un calendario");
        unidad.GetProperty("estadoManual").GetString().Should().Be("Disponible");

        var dias = unidad.GetProperty("dias").EnumerateArray().ToList();
        dias.Should().HaveCount(7, "un día por cada fecha del rango, ni más ni menos");
        dias.Select(d => d.GetProperty("fecha").GetString())
            .Should().Equal(Enumerable.Range(0, 7).Select(d => $"{desde.AddDays(d):yyyy-MM-dd}"));
        dias.Should().OnlyContain(d =>
            AyudaCalendario.Tipo(d) == "Libre"
            && d.GetProperty("disponible").GetBoolean()
            && d.GetProperty("ocupaciones").GetArrayLength() == 0);
    }

    // ---------------------------------------------------------------- contrato

    /// <summary>
    /// Un contrato activo ocupa su unidad en un rango y el calendario lo dice con su origen, que es
    /// el identificador del propio contrato: el frontend puede abrir la ficha sin tener que buscarlo.
    /// </summary>
    [Fact]
    public async Task ElCalendarioMuestraUnContrato()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(ct, Roles.Empleado);
        using var gerente = await SesionAsync(ct, Roles.Gerente);

        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);
        var inicio = AyudaCalendario.Dia(200);

        var contratoId = await AyudaContrato.CrearAsync(
            empleado, AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, inicio), ct);
        await AyudaContrato.AceptarAsync(empleado, contratoId, "solicitud", new { }, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "aprobacion", null, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "activacion", null, ct);

        var desde = AyudaCalendario.Dia(198);
        var hasta = AyudaCalendario.Dia(203);
        var unidad = AyudaCalendario.PrimeraUnidad(
            await AyudaCalendario.DeUnidadAsync(empleado, unidadId, desde, hasta, ct));

        var antes = AyudaCalendario.Dia(unidad, inicio.AddDays(-1));
        AyudaCalendario.Tipo(antes).Should().Be("Libre", "el contrato empieza el día de inicio, no antes");

        foreach (var dia in Enumerable.Range(0, 4))
        {
            var fila = AyudaCalendario.Dia(unidad, inicio.AddDays(dia));
            AyudaCalendario.Tipo(fila).Should().Be("Contrato");
            fila.GetProperty("disponible").GetBoolean().Should().BeFalse();
            fila.GetProperty("motivo").GetString().Should().Be("Contrato vigente");

            var ocupaciones = AyudaCalendario.Ocupaciones(fila);
            ocupaciones.Should().ContainSingle();
            ocupaciones[0].Origen.Should().Be("Contrato");
            ocupaciones[0].OrigenId.Should().Be(contratoId.ToString());
            ocupaciones[0].TipoDia.Should().BeNull("un contrato ocupa un rango, no un medio día");
        }
    }

    // ---------------------------------------------------------------- reservas

    /// <summary>
    /// La mensual ocupa un rango abierto desde su entrada prevista y la corta ocupa días sueltos. En
    /// el mismo rango se ven las dos cosas, cada una con su forma, porque la mensual empieza
    /// después de que la corta ya soltó su día.
    /// </summary>
    [Fact]
    public async Task ElCalendarioMuestraUnaReservaMensualYUnaCorta()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);

        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var diaCorta = AyudaCalendario.Dia(30);
        var entradaMensual = AyudaCalendario.Dia(35);

        var cortaId = await AyudaReserva.CrearAsync(
            sesion, AyudaReserva.Corta(clienteId, unidadId, diaCorta), ct);
        await AyudaReserva.CrearAsync(
            sesion, AyudaReserva.Mensual(clienteId, unidadId, entradaMensual), ct);

        var desde = AyudaCalendario.Dia(28);
        var hasta = AyudaCalendario.Dia(40);
        var unidad = AyudaCalendario.PrimeraUnidad(
            await AyudaCalendario.DeUnidadAsync(sesion, unidadId, desde, hasta, ct));

        var corta = AyudaCalendario.Dia(unidad, diaCorta);
        AyudaCalendario.Tipo(corta).Should().Be("ReservaCorta");
        var deLaCorta = AyudaCalendario.Ocupaciones(corta);
        deLaCorta.Should().ContainSingle();
        deLaCorta[0].Origen.Should().Be("ReservaCorta");
        deLaCorta[0].OrigenId.Should().Be(cortaId.ToString());
        deLaCorta[0].TipoDia.Should().Be("MedioDia", "la franja de 4 h es un medio día");

        // La mensual es un rango abierto: se lleva la entrada y todos los días siguientes del rango
        // pedido, hasta el final.
        foreach (var dia in Enumerable.Range(0, 6))
        {
            var fila = AyudaCalendario.Dia(unidad, entradaMensual.AddDays(dia));
            AyudaCalendario.Tipo(fila).Should().Be("ReservaMensual");
            var ocupacion = AyudaCalendario.Ocupaciones(fila).Should().ContainSingle().Which;
            ocupacion.Origen.Should().Be("ReservaMensual");
            ocupacion.TipoDia.Should().BeNull("la mensual bloquea en rango y no lleva días");
        }

        // Y entre la corta y la mensual, los días libres.
        AyudaCalendario.Tipo(AyudaCalendario.Dia(unidad, diaCorta.AddDays(1)))
            .Should().Be("Libre", "la corta es de un día y la mensual todavía no ha entrado");
    }

    /// <summary>
    /// Dos medios días del mismo día se ven los dos. Es el criterio de aceptación y el punto donde
    /// un calendario mal hecho se come la mitad de la jornada: si el día sale con un solo motivo, la
    /// pantalla pinta una unidad libre que no lo está.
    /// </summary>
    [Fact]
    public async Task ElCalendarioMuestraLosMediosDias()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);

        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var fecha = AyudaCalendario.Dia(60);

        var manana = await AyudaReserva.CrearAsync(
            sesion,
            AyudaReserva.Corta(clienteId, unidadId, fecha,
                dias: new[] { AyudaReserva.Franja(fecha, "08:00:00", "12:00:00") }),
            ct);
        var tarde = await AyudaReserva.CrearAsync(
            sesion,
            AyudaReserva.Corta(clienteId, unidadId, fecha,
                dias: new[] { AyudaReserva.Franja(fecha, "13:00:00", "17:00:00") }),
            ct);

        var dia = AyudaCalendario.Dia(
            AyudaCalendario.PrimeraUnidad(
                await AyudaCalendario.DeUnidadAsync(sesion, unidadId, fecha, fecha, ct)),
            fecha);

        var ocupaciones = AyudaCalendario.Ocupaciones(dia);
        ocupaciones.Should().HaveCount(2, "los dos medios días del día se ven los dos");
        ocupaciones.Select(o => o.OrigenId).Should().BeEquivalentTo([manana.ToString(), tarde.ToString()]);
        ocupaciones.Should().OnlyContain(o => o.Origen == "ReservaCorta" && o.TipoDia == "MedioDia");

        AyudaCalendario.Tipo(dia).Should().Be("ReservaCorta");
        dia.GetProperty("disponible").GetBoolean().Should().BeFalse();
    }

    // ---------------------------------------------------------------- mantenimiento

    /// <summary>
    /// Una unidad en mantenimiento no está disponible ni en un solo día, y el motivo no es ninguna
    /// reserva: es el estado manual que alguien le puso a la unidad. Por eso el día no lleva ningún
    /// bloqueo en <c>ocupaciones</c> y el origen del motivo es la unidad, no un documento.
    /// </summary>
    [Fact]
    public async Task ElCalendarioMuestraElMantenimientoDeLaUnidad()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);

        await AyudaCalendario.PonerEstadoAsync(sesion, unidadId, EstadoUnidad.Mantenimiento, ct);

        var desde = AyudaCalendario.Dia(80);
        var hasta = AyudaCalendario.Dia(83);
        var unidad = AyudaCalendario.PrimeraUnidad(
            await AyudaCalendario.DeUnidadAsync(sesion, unidadId, desde, hasta, ct));

        unidad.GetProperty("estadoManual").GetString().Should().Be("Mantenimiento");

        var dias = unidad.GetProperty("dias").EnumerateArray().ToList();
        dias.Should().HaveCount(4, "el estado manual se ve en todos los días del rango");
        foreach (var dia in dias)
        {
            AyudaCalendario.Tipo(dia).Should().Be("EstadoManual");
            dia.GetProperty("disponible").GetBoolean().Should().BeFalse();
            dia.GetProperty("motivo").GetString().Should().Contain("mantenimiento");
            dia.GetProperty("ocupaciones").GetArrayLength().Should().Be(0,
                "el mantenimiento no viene de una ocupación con fecha: sale del estado de la unidad");
        }
    }

    // ---------------------------------------------------------------- liberar

    /// <summary>
    /// Un contrato terminado conserva su pasado en el calendario (el candado se recorta a
    /// <c>[inicio, fin)</c>) y la unidad sale libre desde el día de su fecha de fin (BUG-025). Antes se
    /// apagaba todo el rango y el calendario borraba el historial de ocupación.
    /// </summary>
    [Fact]
    public async Task UnContratoFinalizadoConservaSuPasadoYLiberaDesdeLaFechaDeFin()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(ct, Roles.Empleado);
        using var gerente = await SesionAsync(ct, Roles.Gerente);

        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);
        var inicio = AyudaCalendario.Dia(-10);
        var fin = AyudaCalendario.Dia(0);

        var contratoId = await AyudaContrato.CrearAsync(
            empleado, AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, inicio), ct);
        await AyudaContrato.AceptarAsync(empleado, contratoId, "solicitud", new { }, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "aprobacion", null, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "activacion", null, ct);

        var durante = AyudaCalendario.PrimeraUnidad(
            await AyudaCalendario.DeUnidadAsync(empleado, unidadId, inicio, fin.AddDays(1), ct));
        AyudaCalendario.Tipo(AyudaCalendario.Dia(durante, fin)).Should().Be("Contrato");

        await AyudaContrato.AceptarAsync(
            gerente, contratoId, "baja", new { fechaFin = fin.ToString("yyyy-MM-dd") }, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "finalizacion", null, ct);

        var despues = AyudaCalendario.PrimeraUnidad(
            await AyudaCalendario.DeUnidadAsync(empleado, unidadId, inicio, fin.AddDays(1), ct));

        for (var dia = inicio; dia < fin; dia = dia.AddDays(1))
        {
            AyudaCalendario.Tipo(AyudaCalendario.Dia(despues, dia)).Should().Be("Contrato", $"{dia:yyyy-MM-dd} es pasado del contrato");
        }

        foreach (var dia in new[] { fin, fin.AddDays(1) })
        {
            var libre = AyudaCalendario.Dia(despues, dia);
            AyudaCalendario.Tipo(libre).Should().Be("Libre", "el contrato terminó ese día");
            libre.GetProperty("disponible").GetBoolean().Should().BeTrue();
            libre.GetProperty("ocupaciones").GetArrayLength().Should().Be(0);
        }
    }

    // ---------------------------------------------------------------- solo lectura

    /// <summary>
    /// El calendario es una consulta: no crea ni toca nada. Se cuentan las filas de las dos tablas de
    /// ocupación y de las dos de negocio antes y después de pedirlo, y tienen que ser las mismas.
    /// Si el calendario guardara "ocupado" en algún sitio, saldría aquí.
    /// </summary>
    [Fact]
    public async Task ElCalendarioEsSoloLectura()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);

        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var fecha = AyudaCalendario.Dia(90);
        await AyudaReserva.CrearAsync(sesion, AyudaReserva.Corta(clienteId, unidadId, fecha), ct);

        var antes = await ContarAsync(unidadId, ct);
        antes.Rangos.Should().Be(0, "todavía no hay ningún contrato que bloquee");
        antes.Dias.Should().Be(1, "la reserva corta dejó su bloqueo de día");

        for (var intento = 0; intento < 3; intento++)
        {
            (await AyudaCalendario.GetAsync(
                sesion, unidadId, fecha.AddDays(-2), fecha.AddDays(2), ct))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var despues = await ContarAsync(unidadId, ct);
        despues.Rangos.Should().Be(antes.Rangos);
        despues.Dias.Should().Be(antes.Dias);
        despues.Reservas.Should().Be(antes.Reservas);
        despues.Contratos.Should().Be(antes.Contratos,
            "tres lecturas del calendario no crean ni liberan ni un solo bloqueo");
    }

    private async Task<(int Rangos, int Dias, int Reservas, int Contratos)> ContarAsync(
        Guid unidadId,
        CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        return (
            await contexto.OcupacionRango.AsNoTracking().CountAsync(r => r.UnidadId == unidadId, ct),
            await contexto.OcupacionDia.AsNoTracking().CountAsync(d => d.UnidadId == unidadId, ct),
            await contexto.Reserva.AsNoTracking().CountAsync(r => r.UnidadId == unidadId, ct),
            await contexto.Contrato.AsNoTracking().CountAsync(c => c.UnidadId == unidadId, ct));
    }

    // ---------------------------------------------------------------- rango

    /// <summary>
    /// Un rango de más de un año es un 400 con la explicación, no una consulta que barra la cartera
    /// entera. El límite es <see cref="ServicioCalendario.DiasMaximoRango"/> y la explicación dice
    /// cómo se pide en su sitio.
    /// </summary>
    [Fact]
    public async Task RangoExcesivoSeRechaza()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);

        var desde = AyudaCalendario.Dia(0);
        var justo = await AyudaCalendario.GetAsync(sesion, unidadId, desde, desde.AddDays(365), ct);
        justo.StatusCode.Should().Be(HttpStatusCode.OK, "un año (366 días) es el máximo y cabe");

        var excessive = await AyudaCalendario.GetAsync(sesion, unidadId, desde, desde.AddDays(366), ct);
        excessive.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await excessive.Content.ReadAsStringAsync(ct)).Should()
            .Contain(ServicioCalendario.DiasMaximoRango.ToString(),
                "la respuesta dice cuál es el máximo y no solo que el rango no vale");

        var alReves = await AyudaCalendario.GetAsync(sesion, unidadId, desde.AddDays(5), desde, ct);
        alReves.StatusCode.Should().Be(HttpStatusCode.BadRequest, "un rango al revés no es un rango");
    }

    // ---------------------------------------------------------------- utilidades

    private async Task<SesionCliente> SesionAsync(CancellationToken ct, string rol = Roles.Gerente)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: rol, ct: ct);
        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }
}