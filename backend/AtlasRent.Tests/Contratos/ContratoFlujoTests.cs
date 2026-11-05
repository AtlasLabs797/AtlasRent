using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Contratos;

/// <summary>
/// F2-05: el flujo del contrato (docs/ESPECIFICACION.md §3.5), el rechazo con comentario, la
/// plantilla congelada al aprobar y los campos obligatorios.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ContratoFlujoTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// El recorrido entero <c>Borrador → Pendiente → Aprobado → Activo</c> y, en cada paso, la
    /// transición que no toca: no se activa lo que está pendiente, no se aprueba lo aprobado y no
    /// se edita lo que ya está vigente.
    /// </summary>
    [Fact]
    public async Task ElFlujoCompletoHastaActivo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct), Contrasena, ct);
        using var gerente = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct), Contrasena, ct);

        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);
        var inicio = AyudaContrato.Dia(210);
        var cuerpo = AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, inicio);

        var id = await AyudaContrato.CrearAsync(empleado, cuerpo, ct);
        (await AyudaContrato.EstadoAsync(bd, id, ct)).ToString().Should().Be("Borrador");

        // Desde borrador no se activa ni se aprueba: son transiciones que no están en la tabla.
        (await AyudaContrato.Boton(gerente, id, "aprobacion", ct: ct)).StatusCode
            .Should().Be(HttpStatusCode.Conflict);
        (await AyudaContrato.Boton(gerente, id, "activacion", ct: ct)).StatusCode
            .Should().Be(HttpStatusCode.Conflict);

        await AyudaContrato.AceptarAsync(empleado, id, "solicitud", new { }, ct);
        (await AyudaContrato.EstadoAsync(bd, id, ct)).ToString().Should().Be("PendienteAprobacion");

        // Pendiente no se activa todavía.
        (await AyudaContrato.Boton(gerente, id, "activacion", ct: ct)).StatusCode
            .Should().Be(HttpStatusCode.Conflict, "un contrato pendiente no bloquea la unidad");

        await AyudaContrato.AceptarAsync(gerente, id, "aprobacion", null, ct);
        (await AyudaContrato.EstadoAsync(bd, id, ct)).ToString().Should().Be("Aprobado");

        // Aprobado no se aprueba otra vez ni se vuelve a enviar.
        (await AyudaContrato.Boton(gerente, id, "aprobacion", ct: ct)).StatusCode
            .Should().Be(HttpStatusCode.Conflict);

        await AyudaContrato.AceptarAsync(gerente, id, "activacion", null, ct);
        (await AyudaContrato.EstadoAsync(bd, id, ct)).ToString().Should().Be("Activo");

        // Un contrato vigente ya no se edita por el camino del borrador: se modifica por su cambio
        // en activo, que genera versión.
        var edicionTardia = await empleado.PutAsync($"/api/v1/contratos/{id:D}", cuerpo, ct);
        edicionTardia.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// El rechazo vuelve a borrador y <b>exige comentario</b>: sin comentario se rechaza, y con
    /// comentario el contrato vuelve a estar editable para corregirlo.
    /// </summary>
    [Fact]
    public async Task ElRechazoVuelveABorradorYExigeComentario()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct), Contrasena, ct);
        using var gerente = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct), Contrasena, ct);

        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);
        var inicio = AyudaContrato.Dia(240);

        var id = await AyudaContrato.CrearAsync(
            empleado, AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, inicio), ct);
        await AyudaContrato.AceptarAsync(empleado, id, "solicitud", new { }, ct);

        // Sin comentario no hay rechazo: el empleado no sabría qué corregir.
        var sinComentario = await AyudaContrato.Boton(gerente, id, "rechazo", new { comentario = "   " }, ct);
        sinComentario.StatusCode.Should().Be(HttpStatusCode.BadRequest, "el comentario es obligatorio");

        var conComentario = await AyudaContrato.Boton(
            gerente, id, "rechazo", new { comentario = "La fianza no es la pactada." }, ct);
        conComentario.StatusCode.Should().Be(HttpStatusCode.OK, await conComentario.Cuerpo());

        (await AyudaContrato.EstadoAsync(bd, id, ct)).ToString().Should().Be("Borrador");

        // Y de vuelta a borrador se puede corregir y volver a enviar.
        var corregido = await empleado.PutAsync(
            $"/api/v1/contratos/{id:D}",
            AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, inicio, fianza: 30_000m),
            ct);
        corregido.StatusCode.Should().Be(HttpStatusCode.OK, await corregido.Cuerpo());

        await AyudaContrato.AceptarAsync(empleado, id, "solicitud", new { }, ct);
        await AyudaContrato.AceptarAsync(gerente, id, "aprobacion", null, ct);
        (await AyudaContrato.EstadoAsync(bd, id, ct)).ToString().Should().Be("Aprobado");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var solicitudes = await contexto.SolicitudAprobacion.AsNoTracking()
            .Where(s => s.ContratoId == id)
            .OrderBy(s => s.SolicitadoAt)
            .Select(s => new { s.Resultado, s.Comentario })
            .ToListAsync(ct);

        solicitudes.Should().HaveCount(2, "el rechazo queda escrito y el reenvío abre otra solicitud");
        solicitudes[0].Resultado.ToString().Should().Be("Rechazada");
        solicitudes[0].Comentario.Should().Be("La fianza no es la pactada.");
        solicitudes[1].Resultado.ToString().Should().Be("Aprobada");
    }

    /// <summary>
    /// La plantilla se congela al aprobar (§3.5): después de aprobar, cambiar la plantilla del
    /// contrato ya no cambia la que usa ese contrato.
    /// <para>
    /// Aquí la plantilla es un <b>identificador</b>, no una clave ajena: la tabla de plantillas es de
    /// F2-07. Lo que se congela es ese identificador, que es lo que el contrato firmado usará.
    /// </para>
    /// </summary>
    [Fact]
    public async Task LaPlantillaQuedaCongeladaAlAprobar()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct), Contrasena, ct);
        using var gerente = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct), Contrasena, ct);

        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);
        var inicio = AyudaContrato.Dia(270);

        var id = await AyudaContrato.CrearAsync(
            empleado, AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, inicio, plantilla: "larga-2026"), ct);

        await AyudaContrato.AceptarAsync(empleado, id, "solicitud", new { }, ct);
        await AyudaContrato.AceptarAsync(gerente, id, "aprobacion", null, ct);

        var congelada = await AyudaContrato.FichaAsync(gerente, id, ct);
        congelada.GetProperty("plantillaContratoCongeladaId").GetString().Should()
            .Be("larga-2026", "al aprobar se congela la plantilla que tenía el contrato");
        congelada.GetProperty("plantillaCongeladaAt").ValueKind.Should().NotBe(
            System.Text.Json.JsonValueKind.Null, "la congelación lleva su fecha");

        await AyudaContrato.AceptarAsync(gerente, id, "activacion", null, ct);

        // Con el contrato vigente, cambiar la plantilla del contrato no toca la congelada: el
        // contrato firmado sigue usando la que tenía cuando se aprobó.
        var cambio = await AyudaContrato.Boton(
            gerente, id, "cambios", AyudaContrato.Cambio(60_000m, "Cambio de plantilla"), ct);
        cambio.StatusCode.Should().Be(HttpStatusCode.OK, await cambio.Cuerpo());

        await using var verificacion = bd.CrearContexto(bd.CadenaApp);
        var contrato = await verificacion.Contrato.AsNoTracking().SingleAsync(c => c.Id == id, ct);
        contrato.PlantillaContratoCongeladaId.Should()
            .Be("larga-2026", "editar la plantilla del contrato no cambia la congelada (§3.5)");
    }

    /// <summary>
    /// Los obligatorios de §3.5. Todos devuelven 409 con el motivo: no es un 400 de formato, es un
    /// contrato que no se puede guardar.
    /// </summary>
    [Theory]
    [InlineData("cliente")]
    [InlineData("unidad")]
    [InlineData("renta")]
    [InlineData("moneda")]
    [InlineData("diaPago")]
    [InlineData("fianza")]
    [InlineData("facturadora")]
    [InlineData("inicio")]
    [InlineData("duracion")]
    [InlineData("regla")]
    [InlineData("plantilla")]
    public async Task SinLosObligatoriosNoHayContrato(string falta)
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct), Contrasena, ct);
        using var gerente = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct), Contrasena, ct);

        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);

        object cuerpo = falta switch
        {
            "cliente" => AyudaContrato.Peticion(null, unidadId, facturadoraId),
            "unidad" => AyudaContrato.Peticion(clienteId, null, facturadoraId),
            "facturadora" => AyudaContrato.Peticion(clienteId, unidadId, Guid.Empty),
            "inicio" => AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, omitirInicio: true),
            "duracion" => AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, duracionMeses: null),
            "renta" => AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, renta: null),
            // El enum llega del JSON: un null se queda en el valor por defecto (0), que no es ninguna moneda,
            // y eso tiene que rechazarse como regla de negocio, no como error de formato.
            "moneda" => AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, moneda: null),
            "diaPago" => AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, diaPago: null),
            "fianza" => AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, fianza: null),
            "regla" => AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, renovacionMeses: null),
            _ => AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, plantilla: "  "),
        };

        var respuesta = await empleado.PostAsync("/api/v1/contratos", cuerpo, ct);

        respuesta.StatusCode.Should().Be(
            HttpStatusCode.Conflict,
            $"falta {falta} y el contrato no se puede guardar sin él: {await respuesta.Cuerpo()}");
    }
}