using System.Net;
using System.Text;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Contratos;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Renovaciones;

/// <summary>
/// F2-06: programar, corregir y cancelar un incremento de renta.
/// <para>
/// Lo que se comprueba aquí es que la regla de §3.5 sea utilizable de verdad: alguien tiene que poder
/// darle de alta a un incremento, corregirlo antes de que llegue el día y retirarlo si se dio de
/// alta por error, sin que nada de eso toque la renta. La renta solo la mueve el trabajo, cuando el
/// bloque con la fecha de vigencia llega.
/// </para>
/// <para>
/// Programar un incremento exige <c>contrato.estado</c>, el mismo permiso que cambiar la renta a
/// mano: subir la renta de alguien es una decisión de gerente, no una tarea de empleado.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class IncrementoContratoTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// El gerente programa los dos tipos de incremento y los ve en la lectura; el empleado no puede
    /// crearlos, y sin sesión tampoco.
    /// </summary>
    [Fact]
    public async Task ElGerenteCreaIncrementosYElEmpleadoNo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(Roles.Empleado, ct);
        using var gerente = await SesionAsync(Roles.Gerente, ct);
        var contratoId = await ContratoActivoAsync(empleado, gerente, Vencido(13), 50_000m, ct);

        var ruta = $"/api/v1/contratos/{contratoId:D}/incrementos";

        var creado = await gerente.PostAsync(ruta, Cuerpo("Porcentaje", porcentaje: 5m), ct);
        creado.StatusCode.Should().Be(HttpStatusCode.Created, await creado.Cuerpo());
        var porcentajeId = await AyudaReserva.Id(creado, ct);

        var importe = await gerente.PostAsync(
            ruta,
            new
            {
                tipo = "ImporteFijo",
                importe = 1_500m,
                fechaVigencia = TiempoNegocio.Hoy().ToString("yyyy-MM-dd"),
            },
            ct);
        importe.StatusCode.Should().Be(HttpStatusCode.Created, await importe.Cuerpo());

        // El empleado solo lee: crearlos es una decisión con efecto económico.
        (await empleado.PostAsync(ruta, Cuerpo("Porcentaje", porcentaje: 5m), ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Y sin sesión tampoco.
        using var anonimo = api.CrearCliente();
        var cuerpo = JsonSerializer.Serialize(
            new { tipo = "Porcentaje", porcentaje = 5m, fechaVigencia = TiempoNegocio.Hoy().ToString("yyyy-MM-dd") });
        (await anonimo.PostAsync(
                ruta,
                new StringContent(cuerpo, Encoding.UTF8, "application/json"),
                ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Lo que se creó se ve, y lo que no se creó no.
        var listado = await AyudaReserva.Json(await empleado.GetAsync(ruta, ct), ct);
        listado.GetArrayLength().Should().Be(2);
        listado[0].GetProperty("tipo").GetString().Should().Be("Porcentaje");
        listado[0].GetProperty("porcentaje").GetDecimal().Should().Be(5m);
        listado[0].GetProperty("aplicado").GetBoolean().Should().BeFalse();
        listado[0].GetProperty("cancelado").GetBoolean().Should().BeFalse();
        listado[1].GetProperty("importe").GetDecimal().Should().Be(1_500m);

        // Y el suelto, en su endpoint.
        var suelto = await AyudaReserva.Json(
            await empleado.GetAsync($"/api/v1/contratos/{contratoId:D}/incrementos/{porcentajeId:D}", ct), ct);
        suelto.GetProperty("id").GetGuid().Should().Be(porcentajeId);

        // La renta no se ha movido: eso es del trabajo, no del alta.
        (await RentaAsync(contratoId, ct)).Should().Be(50_000m);
    }

    /// <summary>
    /// El valor tiene que ser el del tipo que dice: un porcentaje con importe, un importe con
    /// porcentaje o los dos vacíos son incrementos que el trabajo aplicaría mal o no aplicaría.
    /// </summary>
    [Theory]
    [InlineData("Porcentaje", false, true)]
    [InlineData("ImporteFijo", true, true)]
    [InlineData("ImporteFijo", false, false)]
    public async Task UnIncrementoConLaColumnaEquivocadaSeRechaza(
        string tipo,
        bool mandanPorcentaje,
        bool mandanImporte)
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(Roles.Empleado, ct);
        using var gerente = await SesionAsync(Roles.Gerente, ct);
        var contratoId = await ContratoActivoAsync(empleado, gerente, Vencido(13), 50_000m, ct);

        var respuesta = await gerente.PostAsync(
            $"/api/v1/contratos/{contratoId:D}/incrementos",
            new
            {
                tipo,
                porcentaje = mandanPorcentaje ? 5m : (decimal?)null,
                importe = mandanImporte ? 1_000m : (decimal?)null,
                fechaVigencia = TiempoNegocio.Hoy().ToString("yyyy-MM-dd"),
            },
            ct);

        respuesta.StatusCode.Should().Be(
            HttpStatusCode.Conflict,
            $"un incremento de tipo {tipo} no puede llevar la otra columna: {await respuesta.Cuerpo()}");

        (await IncrementosAsync(contratoId, ct)).Should().Be(0, "no se guardó nada a medias");
    }

    /// <summary>
    /// Un porcentaje disparatado se rechaza en la puerta. Por encima de la mitad la renta deja de
    /// subir "un poco": eso es una renegociación, y se hace como un cambio de contrato.
    /// </summary>
    [Fact]
    public async Task UnPorcentajeFueraDelLimiteSeRechaza()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(Roles.Empleado, ct);
        using var gerente = await SesionAsync(Roles.Gerente, ct);
        var contratoId = await ContratoActivoAsync(empleado, gerente, Vencido(13), 50_000m, ct);

        (await gerente.PostAsync(
                $"/api/v1/contratos/{contratoId:D}/incrementos",
                Cuerpo("Porcentaje", porcentaje: 80m),
                ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Cero y negativo tampoco: bajar la renta es un cambio de contrato, no un incremento.
        (await gerente.PostAsync(
                $"/api/v1/contratos/{contratoId:D}/incrementos",
                Cuerpo("Porcentaje", porcentaje: 0m),
                ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await gerente.PostAsync(
                $"/api/v1/contratos/{contratoId:D}/incrementos",
                Cuerpo("Porcentaje", porcentaje: -3m),
                ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await IncrementosAsync(contratoId, ct)).Should().Be(0);

        // El del tope exacto entra: 50 es el límite, no un valor redondo.
        (await gerente.PostAsync(
                $"/api/v1/contratos/{contratoId:D}/incrementos",
                Cuerpo("Porcentaje", porcentaje: 50m),
                ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// Un incremento ya aplicado no se edita ni se cancela: su efecto ya está en la renta del
    /// contrato y en una versión del historial, y deshacer eso es un cambio de contrato.
    /// </summary>
    [Fact]
    public async Task UnIncrementoAplicadoNoSeEditaNiSeCancela()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(Roles.Empleado, ct);
        using var gerente = await SesionAsync(Roles.Gerente, ct);
        var contratoId = await ContratoActivoAsync(empleado, gerente, Vencido(13), 50_000m, ct);

        // La fecha del incremento es la del bloque que se va a abrir al renovar, que es cuando un
        // incremento le toca: con una fecha de hoy, que es posterior a ese inicio, el trabajo lo
        // dejaría para más adelante, que es lo correcto.
        var creado = await gerente.PostAsync(
            $"/api/v1/contratos/{contratoId:D}/incrementos",
            Cuerpo("Porcentaje", porcentaje: 10m, await FinVigenteAsync(contratoId, ct)),
            ct);
        var incrementoId = await AyudaReserva.Id(creado, ct);

        await EjecutarJobAsync(ct);
        (await RentaAsync(contratoId, ct)).Should().Be(55_000m);

        var edicion = await gerente.PutAsync(
            $"/api/v1/contratos/{contratoId:D}/incrementos/{incrementoId:D}",
            Cuerpo("Porcentaje", porcentaje: 90m),
            ct);
        edicion.StatusCode.Should().Be(
            HttpStatusCode.Conflict,
            $"el efecto ya está en la renta: {await edicion.Cuerpo()}");

        var cancelacion = await gerente.PostAsync(
            $"/api/v1/contratos/{contratoId:D}/incrementos/{incrementoId:D}/cancelacion",
            new { },
            ct);
        cancelacion.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Ni la renta ni el propio incremento se han movido.
        (await RentaAsync(contratoId, ct)).Should().Be(55_000m);
        var fila = await FilaAsync(incrementoId, ct);
        fila.Porcentaje.Should().Be(10m, "la edición rechazada no dejó nada a medias");
        fila.Aplicado.Should().BeTrue();
        fila.Cancelado.Should().BeFalse("la cancelación rechazada tampoco lo dejó marcado");
    }

    /// <summary>
    /// Cancelar un incremento pendiente lo saca de la mira del trabajo sin borrar la fila: el GET lo
    /// sigue enseñando, con su marca, y la renta no se mueve aunque le toque el bloque.
    /// </summary>
    [Fact]
    public async Task UnIncrementoCanceladoNoSubeLaRenta()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(Roles.Empleado, ct);
        using var gerente = await SesionAsync(Roles.Gerente, ct);
        var contratoId = await ContratoActivoAsync(empleado, gerente, Vencido(13), 50_000m, ct);

        var creado = await gerente.PostAsync(
            $"/api/v1/contratos/{contratoId:D}/incrementos",
            Cuerpo("Porcentaje", porcentaje: 10m, await FinVigenteAsync(contratoId, ct)),
            ct);
        var incrementoId = await AyudaReserva.Id(creado, ct);

        (await gerente.PostAsync(
                $"/api/v1/contratos/{contratoId:D}/incrementos/{incrementoId:D}/cancelacion",
                new { },
                ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await EjecutarJobAsync(ct);

        (await RentaAsync(contratoId, ct)).Should().Be(50_000m, "un incremento cancelado no sube la renta");

        var fila = await FilaAsync(incrementoId, ct);
        fila.Cancelado.Should().BeTrue("la fila se queda, con su marca, porque no se borra nunca");
        fila.CanceladoAt.Should().NotBeNull();
        fila.Aplicado.Should().BeFalse();

        // Y tampoco se puede editar después de cancelado.
        (await gerente.PutAsync(
                $"/api/v1/contratos/{contratoId:D}/incrementos/{incrementoId:D}",
                Cuerpo("Porcentaje", porcentaje: 10m, await FinVigenteAsync(contratoId, ct)),
                ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Editar un incremento pendiente vale: el trabajo todavía no lo ha mirado.</summary>
    [Fact]
    public async Task UnIncrementoPendienteSeEdita()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(Roles.Empleado, ct);
        using var gerente = await SesionAsync(Roles.Gerente, ct);
        var contratoId = await ContratoActivoAsync(empleado, gerente, Vencido(13), 50_000m, ct);

        var creado = await gerente.PostAsync(
            $"/api/v1/contratos/{contratoId:D}/incrementos",
            new
            {
                tipo = "ImporteFijo",
                importe = 1_000m,
                fechaVigencia = Vencido(20).ToString("yyyy-MM-dd"),
            },
            ct);
        var incrementoId = await AyudaReserva.Id(creado, ct);

        // Se corrige a porcentaje, con su fecha puesta donde toca.
        var fecha = TiempoNegocio.Hoy().AddDays(1).ToString("yyyy-MM-dd");
        var editado = await gerente.PutAsync(
            $"/api/v1/contratos/{contratoId:D}/incrementos/{incrementoId:D}",
            new
            {
                tipo = "Porcentaje",
                porcentaje = 8m,
                fechaVigencia = fecha,
                comentario = "Se pactó un 8 %, no un importe fijo.",
            },
            ct);
        editado.StatusCode.Should().Be(HttpStatusCode.OK, await editado.Cuerpo());

        var fila = await FilaAsync(incrementoId, ct);
        fila.Tipo.Should().Be(TipoIncremento.Porcentaje);
        fila.Porcentaje.Should().Be(8m);
        fila.Importe.Should().BeNull("al cambiar de tipo se vacía la columna que no corresponde");
        fila.FechaVigencia.Should().Be(DateOnly.Parse(fecha));
    }

    /// <summary>
    /// Un incremento olvidado el día que tocaba no se pierde: se da de alta después y el trabajo lo
    /// coge en la siguiente pasada, porque su fecha cae dentro de un bloque anterior.
    /// <para>
    /// Es el caso que justifica que el bucle de la renovación recupere los bloques perdidos: el
    /// incremento tiene fecha de un tramo que ya se facturó, y sin esto se quedaría sin aplicar
    /// para siempre.
    /// </para>
    /// </summary>
    [Fact]
    public async Task UnIncrementoRetroactivoLoCogeElTrabajoEnLaPasadaSiguiente()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(Roles.Empleado, ct);
        using var gerente = await SesionAsync(Roles.Gerente, ct);
        var contratoId = await ContratoActivoAsync(empleado, gerente, Vencido(13), 50_000m, ct);

        // El primer bloque se abre sin incrementos.
        await EjecutarJobAsync(ct);
        (await RentaAsync(contratoId, ct)).Should().Be(50_000m);

        // Ahora se programa uno con fecha en el bloque que se abrirá después del que está en curso: el
        // trabajo no lo coge todavía, porque su fecha es posterior al inicio del bloque actual.
        var dentroDelBloque = await FinVigenteAsync(contratoId, ct);
        var creado = await gerente.PostAsync(
            $"/api/v1/contratos/{contratoId:D}/incrementos",
            new
            {
                tipo = "Porcentaje",
                porcentaje = 10m,
                fechaVigencia = dentroDelBloque.ToString("yyyy-MM-dd"),
            },
            ct);
        creado.StatusCode.Should().Be(HttpStatusCode.Created, await creado.Cuerpo());
        var incrementoId = await AyudaReserva.Id(creado, ct);

        await EjecutarJobAsync(ct);
        (await RentaAsync(contratoId, ct)).Should().Be(50_000m, "todavía no le toca: su bloque no ha vencido");

        // Cuando ese bloque vence, el incremento entra de golpe.
        await EjecutarJobAsync(ct, avanzar: TimeSpan.FromDays(400));
        (await RentaAsync(contratoId, ct))
            .Should().Be(55_000m, "el atrasado se aplicó en el bloque que le tocaba");
        (await FilaAsync(incrementoId, ct)).Aplicado.Should().BeTrue();
    }

    /// <summary>
    /// En un contrato que no está activo no se programa nada: en borrador se cambia la renta en el
    /// propio contrato y en baja programada la renta ya está acabando.
    /// </summary>
    [Fact]
    public async Task EnUnContratoNoActivoNoSePrograma()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await SesionAsync(Roles.Empleado, ct);
        using var gerente = await SesionAsync(Roles.Gerente, ct);

        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);

        var borrador = await AyudaContrato.CrearAsync(
            empleado,
            AyudaContrato.Peticion(
                clienteId, unidadId, facturadoraId, Vencido(13),
                renta: 50_000m, renovacionMeses: 12),
            ct);

        (await gerente.PostAsync(
                $"/api/v1/contratos/{borrador:D}/incrementos",
                Cuerpo("Porcentaje", porcentaje: 5m),
                ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // -------------------------------------------------------------------------------------------
    // Atajos
    // -------------------------------------------------------------------------------------------

    private static object Cuerpo(string tipo, decimal? porcentaje, DateOnly? fechaVigencia = null) => new
    {
        tipo,
        porcentaje,
        importe = (decimal?)null,
        fechaVigencia = (fechaVigencia ?? TiempoNegocio.Hoy()).ToString("yyyy-MM-dd"),
    };

    private static DateOnly Vencido(int meses) => TiempoNegocio.Hoy().AddMonths(-meses);

    private async Task<SesionCliente> SesionAsync(string rol, CancellationToken ct) =>
        await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: rol, ct: ct), Contrasena, ct);

    private async Task<Guid> ContratoActivoAsync(
        SesionCliente empleado,
        SesionCliente gerente,
        DateOnly inicio,
        decimal renta,
        CancellationToken ct)
    {
        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);

        var id = await AyudaContrato.CrearAsync(
            empleado, AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, inicio, renta: renta), ct);

        await AyudaContrato.AceptarAsync(empleado, id, "solicitud", new { }, ct);
        await AyudaContrato.AceptarAsync(gerente, id, "aprobacion", null, ct);
        await AyudaContrato.AceptarAsync(gerente, id, "activacion", null, ct);

        return id;
    }

    /// <summary>Una pasada del trabajo diario, con el reloj donde haga falta.</summary>
    private async Task EjecutarJobAsync(CancellationToken ct, TimeSpan? avanzar = null)
    {
        var reloj = new RelojPrueba();
        if (avanzar is { } delta)
        {
            reloj.Avanzar(delta);
        }

        await using var contexto = bd.CrearContexto(bd.CadenaApp, new ContextoSesion());
        await new ServicioRenovaciones(
                contexto,
                new AuditoriaService(contexto),
                new ServicioAlertas(contexto, reloj),
                reloj)
            .RenovarVencidosAsync(ct);
    }

    private Task<decimal> RentaAsync(Guid contratoId, CancellationToken ct) =>
        EnContextoAsync(c => c.Contrato.Where(x => x.Id == contratoId).Select(x => x.Renta).SingleAsync(ct));

    private Task<DateOnly> FinVigenteAsync(Guid contratoId, CancellationToken ct) =>
        EnContextoAsync(c => c.Contrato.Where(x => x.Id == contratoId).Select(x => x.FechaFinVigente).SingleAsync(ct));

    private Task<int> IncrementosAsync(Guid contratoId, CancellationToken ct) =>
        EnContextoAsync(c => c.ContratoIncremento.AsNoTracking().CountAsync(i => i.ContratoId == contratoId, ct));

    /// <summary>La fila del incremento tal y como está en la base, sin pasar por la API.</summary>
    private Task<ContratoIncremento> FilaAsync(Guid incrementoId, CancellationToken ct) =>
        EnContextoAsync(c => c.ContratoIncremento.AsNoTracking().SingleAsync(i => i.Id == incrementoId, ct));

    private async Task<T> EnContextoAsync<T>(Func<AtlasRentDbContext, Task<T>> consulta)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        return await consulta(contexto);
    }
}