using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Contratos;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Bandeja;

/// <summary>
/// F2-10: el criterio de aceptación del plan, tal cual, "<b>alertas sin duplicar</b>".
/// <para>
/// La forma en que se rompe es siempre la misma y nunca es visible en los datos: correr el trabajo
/// dos veces deja dos filas iguales, o una fila que se refresca dos veces, y el contador de
/// repeticiones dice "lo vio dos veces" el primer día. Con la clave única de <c>alerta.clave</c> y
/// el <c>ON CONFLICT ... WHERE resuelta_at IS NULL</c> de <see cref="ServicioAlertas"/> eso no puede
/// pasar: lo que hay es una fila, y cada pasada la actualiza.
/// </para>
/// <para>
/// Y como la condición de fecha es "exactamente dentro de X días", la segunda pasada del mismo día
/// encuentra lo mismo. Las dos garantías juntas son las que hacen que relanzar el trabajo a mano, o
/// que el servidor se reinicie a media ejecución, no dejen un panel con treinta filas repetidas.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class AlertasSinDuplicarTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    /// <summary>
    /// Los cuatro avisos nuevos, cada uno con su clave, y todos con una sola fila después de correr
    /// el trabajo dos veces.
    /// </summary>
    [Fact]
    public async Task LasAlertasNoSeDuplican()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaBandeja.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaBandeja.SesionAsync(api, bd, Roles.Gerente, ct);

        // Tres entidades y cuatro avisos distintos. Se arman todas en el mismo test porque lo que se
        // comprueba es que el trabajo entero, con sus cuatro barridos, no se pisa a sí mismo.
        var conBaja = await ContratoActivoAsync(empleado, gerente, bd, 40, ct);
        var conLiberacion = await ContratoActivoAsync(empleado, gerente, bd, 41, ct);
        var conConflicto = await ContratoActivoAsync(empleado, gerente, bd, 42, ct);

        // 1. Baja programada: el aviso es a 7 días de la fecha efectiva (§3.5 da 30 de preaviso, así
        // que a 7 días todavía hay margen de reacción).
        await DarDeBajaEnLaVentanaAsync(bd, gerente, conBaja, ct);

        // 2. Unidad que se libera en 30 días (§3.16 lo dice literal). Es un contrato <i>vigente</i>:
        // uno en baja ya tiene su propio aviso y no puede tener también este, que sería el mismo
        // hecho con dos claves.
        await AyudaBandeja.PonerFinVigenteAsync(
            bd,
            conLiberacion,
            AyudaContrato.Dia(ServicioAvisosOperacion.DiasDeAvisoLiberacion),
            ct);

        // 3. Reserva en conflicto con un contrato vigente.
        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadReservada = await AyudaReserva.UnidadAsync(empleado, ct);
        var reservaId = await AyudaReserva.CrearAsync(
            empleado,
            AyudaReserva.Mensual(clienteId, unidadReservada, AyudaReserva.Dia(60)),
            ct);
        await AyudaBandeja.PonerEnConflictoAsync(bd, reservaId, ct);

        // 4. IP bloqueada por el escalado de login.
        var bloqueoId = await BloquearIpAsync(ct);

        // Primera pasada. Los cuatro barridos encuentran lo suyo; los números agregados no se comprueban
        // contra un valor exacto porque la base es compartida con las demás pruebas de la colección y
        // pueden dejar contratos y reservas en las mismas condiciones.
        var primera = await AyudaBandeja.EjecutarAvisosAsync(bd, ct);
        primera.BajasProgramadas.Should().BeGreaterThanOrEqualTo(1);
        primera.Liberaciones.Should().BeGreaterThanOrEqualTo(1);
        primera.Conflictos.Should().BeGreaterThanOrEqualTo(1);

        // Segunda pasada, la del relanzamiento a mano o la del reinicio a media ejecución. Encuentra
        // exactamente lo mismo —las condiciones son por fecha y el día no ha cambiado— y no cierra
        // nada, porque lo que sigue aplicando no es una alerta caduca.
        var segunda = await AyudaBandeja.EjecutarAvisosAsync(bd, ct);
        segunda.BajasProgramadas.Should().Be(primera.BajasProgramadas);
        segunda.Liberaciones.Should().Be(primera.Liberaciones);
        segunda.Conflictos.Should().Be(primera.Conflictos);
        segunda.AlertasCerradas.Should().Be(0, "lo que sigue aplicando no se cierra");

        // Y una fila por entidad, con la clave que toca.
        await UnaFilaConLaClaveAsync(ServicioAvisosOperacion.ClaveBajaProgramada(conBaja), ct);
        await UnaFilaConLaClaveAsync(ServicioAvisosOperacion.ClaveLiberacion30(conLiberacion), ct);
        await UnaFilaConLaClaveAsync(ServicioAvisosOperacion.ClaveReservaConflicto(reservaId), ct);
        await UnaFilaConLaClaveAsync(ServicioAvisosOperacion.ClaveIpBloqueada(bloqueoId), ct);
    }

    /// <summary>
    /// La fila se refresca en vez de duplicarse: una sola, viva, con el contador a 2 porque el trabajo
    /// la vio dos veces. El mensaje dice por qué se avisó.
    /// </summary>
    [Fact]
    public async Task LaAlertaQueSeRepiteEsLaMismaFilaRefrescada()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaBandeja.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaBandeja.SesionAsync(api, bd, Roles.Gerente, ct);

        var contratoId = await ContratoActivoAsync(empleado, gerente, bd, 60, ct);
        await DarDeBajaEnLaVentanaAsync(bd, gerente, contratoId, ct);

        await AyudaBandeja.EjecutarAvisosAsync(bd, ct);
        var clave = ServicioAvisosOperacion.ClaveBajaProgramada(contratoId);
        var primera = await AyudaBandeja.AlertasDeAsync(bd, clave, ct);
        primera.Should().ContainSingle();

        await AyudaBandeja.EjecutarAvisosAsync(bd, ct);
        var segunda = await AyudaBandeja.AlertasDeAsync(bd, clave, ct);
        segunda.Should().ContainSingle();

        segunda[0].Id.Should().Be(primera[0].Id, "es la misma fila de siempre, actualizada");
        segunda[0].ResueltaAt.Should().BeNull("el problema sigue abierto: la baja no ha llegado a su fecha");
        segunda[0].Repeticiones.Should().Be(2, "la misma fila acumula las veces que se ha visto");
        segunda[0].Mensaje.Should().Contain("baja programada");
    }

    /// <summary>
    /// La otra mitad: el aviso se <b>cierra</b> cuando deja de aplicar. Anular la baja deja de anunciar
    /// una fecha que ya no existe, y sin esto el panel acabaría lleno de avisos de bajas que alguien
    /// se equivocó y anuló hace tres meses.
    /// <para>
    /// Cerrar es poner <c>resuelta_at</c>, nunca borrar (AGENTS.md §6): la fila sigue ahí diciendo qué
    /// se avisó y cuándo dejó de aplicar.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ElAvisoSeCierraCuandoLaBajaSeAnula()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaBandeja.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaBandeja.SesionAsync(api, bd, Roles.Gerente, ct);

        var contratoId = await ContratoActivoAsync(empleado, gerente, bd, 70, ct);
        await DarDeBajaEnLaVentanaAsync(bd, gerente, contratoId, ct);

        await AyudaBandeja.EjecutarAvisosAsync(bd, ct);
        var clave = ServicioAvisosOperacion.ClaveBajaProgramada(contratoId);

        var abierta = await AyudaBandeja.AlertasDeAsync(bd, clave, ct);
        abierta.Should().ContainSingle();
        abierta[0].ResueltaAt.Should().BeNull("la baja está programada y todavía no se ha cerrado");

        await AyudaContrato.AceptarAsync(gerente, contratoId, "anular-baja", null, ct);

        var resumen = await AyudaBandeja.EjecutarAvisosAsync(bd, ct);
        resumen.AlertasCerradas.Should().Be(1, "el aviso de una baja anulada es ruido, no información");

        var cerrada = await AyudaBandeja.AlertasDeAsync(bd, clave, ct);
        cerrada.Should().ContainSingle("cerrar no es borrar: la fila sigue ahí");
        cerrada[0].ResueltaAt.Should().NotBeNull();
    }

    /// <summary>
    /// Un bloqueo de IP que expira deja de avisar, por el mismo motivo que la baja anulada. Sin esto,
    /// cada bloqueo de la historia seguiría encendido en el panel.
    /// </summary>
    [Fact]
    public async Task ElAvisoDeIpBloqueadaSeCierraCuandoElBloqueoExpira()
    {
        var ct = TestContext.Current.CancellationToken;
        var bloqueoId = await BloquearIpAsync(ct);

        await AyudaBandeja.EjecutarAvisosAsync(bd, ct);
        var clave = ServicioAvisosOperacion.ClaveIpBloqueada(bloqueoId);

        var viva = await AyudaBandeja.AlertasDeAsync(bd, clave, ct);
        viva.Should().ContainSingle();
        viva[0].ResueltaAt.Should().BeNull("la IP está bloqueada y todavía no venció");

        // Mañana: el bloqueo de nivel 1 (una hora) ya venció.
        await AyudaBandeja.EjecutarAvisosAsync(bd, ct, TimeSpan.FromDays(1));

        var cerrada = await AyudaBandeja.AlertasDeAsync(bd, clave, ct);
        cerrada.Should().ContainSingle();
        cerrada[0].ResueltaAt.Should().NotBeNull("un bloqueo vencido no bloquea a nadie");
    }

    // ---------------------------------------------------------------- atajos

    private async Task UnaFilaConLaClaveAsync(string clave, CancellationToken ct)
    {
        var filas = await AyudaBandeja.AlertasDeAsync(bd, clave, ct);

        filas.Should().ContainSingle(
            $"«{clave}» identifica una sola entidad y la clave es única: correr el trabajo dos veces no "
            + "puede dejar dos filas");
        filas[0].ResueltaAt.Should().BeNull("la entidad sigue en la condición que motivó el aviso");
    }

    /// <summary>Contrato activo con su unidad, listo para darle de baja o para moverle la fecha de fin.</summary>
    private static async Task<Guid> ContratoActivoAsync(
        SesionCliente empleado,
        SesionCliente gerente,
        ContenedorBdFixture bd,
        int desplazamientoInicio,
        CancellationToken ct)
    {
        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);

        var contratoId = await AyudaContrato.CrearAsync(
            empleado,
            AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, AyudaContrato.Dia(-desplazamientoInicio)),
            ct);

        await AyudaContrato.AceptarAsync(empleado, contratoId, "solicitud", new { }, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "aprobacion", null, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "activacion", null, ct);

        return contratoId;
    }

    /// <summary>
    /// Bloquea una IP por la base de datos con las columnas que dice la tabla <c>bloqueo_ip</c>: nivel
    /// del escalado, fallos y fin del bloqueo. Se escribe a mano porque no hay endpoint para bloquear
    /// una IP —eso lo hace el servicio de bloqueos cuando alguien falla un login— y lo que se prueba
    /// es el barrido, no el escalado.
    /// </summary>
    /// <summary>
    /// Da de baja el contrato con su fecha efectiva dentro de la ventana de aviso.
    /// <para>
    /// Antes mueve el fin del bloque en curso a hoy. La regla de negocio es real y no se salta: una
    /// baja no puede caer antes de que termine el bloque que está pagando el cliente. Un contrato de
    /// doce meses no admits una baja a siete días, así que se acorta el bloque en la base en vez de
    /// calcular un mes que vary con los días del mes —este test va de la clave de deduplicación, no
    /// de la aritmética de la duración, que tiene su propia prueba en F2-05.
    /// </para>
    /// </summary>
    private static async Task DarDeBajaEnLaVentanaAsync(
        ContenedorBdFixture bd,
        SesionCliente gerente,
        Guid contratoId,
        CancellationToken ct)
    {
        await AyudaBandeja.PonerFinVigenteAsync(bd, contratoId, AyudaContrato.Dia(), ct);

        await AyudaContrato.AceptarAsync(
            gerente,
            contratoId,
            "baja",
            new
            {
                fechaFin = AyudaContrato
                    .Dia(ServicioAvisosOperacion.DiasDeAvisoBaja)
                    .ToString("yyyy-MM-dd"),
            },
            ct);
    }

    private async Task<Guid> BloquearIpAsync(CancellationToken ct)
    {
        var bloqueo = new BloqueoIp
        {
            Id = Guid.NewGuid(),
            Ip = $"198.51.100.{Random.Shared.Next(1, 250)}",
            Nivel = 1,
            Fallos = 5,
            BloqueadaHasta = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        contexto.BloqueoIp.Add(bloqueo);
        await contexto.SaveChangesAsync(ct);

        return bloqueo.Id;
    }
}