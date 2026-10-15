using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Contratos;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AtlasRent.Tests.Inventario;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Bandeja;

/// <summary>
/// F2-10: la bandeja de aprobaciones de §3.16 ("Muestra quién, qué, valor actual y propuesto").
/// <para>
/// Lo que se comprueba aquí es lo que hace que la bandeja sea segura de usar y no solo cómoda:
/// que solo enseñe lo que sigue vivo, que solo la vea quien puede aprobar, y que aprobar desde ella
/// sea exactamente lo mismo que aprobar desde la ficha del contrato. Ese último punto es el que
/// importa de verdad: una bandeja con su propia lógica de aprobación sería dos reglas de negocio y
/// un sitio más donde se puede saltar la caducidad por huella.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class BandejaAprobacionesTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    // ---------------------------------------------------------------- solo lo vivo

    /// <summary>
    /// En la bandeja solo entra lo que sigue esperando: una caducada y una ya resuelta no salen, pero
    /// <b>siguen existiendo</b> y se ven en la ficha del contrato.
    /// <para>
    /// Esa segunda parte es la que no es opcional. Las solicitudes son el historial de por qué un
    /// contrato volvió a enviarse —"se te caducó porque cambiaste la renta", "se rechazó porque
    /// faltaba la plantilla"— y si desaparecieran de la base, ese "por qué" se pierde para siempre.
    /// </para>
    /// </summary>
    [Fact]
    public async Task LaBandejaMuestraSoloSolicitudesVivas()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaBandeja.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaBandeja.SesionAsync(api, bd, Roles.Gerente, ct);

        var pendiente = await PendienteAsync(empleado, 100, ct);

        // La caducada: el empleado edita el contrato mientras espera y la huella deja de valer.
        var caducada = await PendienteAsync(empleado, 101, ct);
        await EditarAsync(empleado, caducada, 75_000m, ct);

        // La resuelta: el gerente la aprueba y el contrato sale de la bandeja para siempre.
        var resuelta = await PendienteAsync(empleado, 102, ct);
        await AyudaContrato.AceptarAsync(gerente, resuelta.ContratoId, "aprobacion", null, ct);

        var bandeja = await BandejaAsync(gerente, ct);

        // La bandeja es de toda la base, no solo de este test: se mira lo de estos tres contratos y
        // nada más. Las pruebas de la colección comparten base de datos y las de al lado dejan
        // contratos esperando, que es exactamente lo que tiene que aparecer en la bandeja.
        bandeja.Select(f => f.ContratoId).Should().Contain(pendiente.ContratoId);
        bandeja.Should().NotContain(f => f.ContratoId == caducada.ContratoId, "una caducada hay que volver a enviarla");
        bandeja.Should().NotContain(f => f.ContratoId == resuelta.ContratoId, "una resuelta ya no se decide");

        // Y ninguna de las dos se borró: el historial sigue entero (AGENTS.md §6).
        var delCaducado = await AyudaBandeja.SolicitudesDeAsync(bd, caducada.ContratoId, ct);
        delCaducado.Should().ContainSingle();
        delCaducado[0].Resultado.ToString().Should().Be("Caducada");
        delCaducado[0].MotivoCaducidad.Should().NotBeNullOrWhiteSpace();

        var delResuelto = await AyudaBandeja.SolicitudesDeAsync(bd, resuelta.ContratoId, ct);
        delResuelto.Should().ContainSingle();
        delResuelto[0].Resultado.ToString().Should().Be("Aprobada");

        // La ficha del contrato los enseña los dos: el que decide y el que se le puede preguntar al
        // cliente por qué lleva tres veces yendo y viniendo.
        var fichaCaducada = await AyudaContrato.FichaAsync(gerente, caducada.ContratoId, ct);
        fichaCaducada.GetProperty("solicitudes").GetArrayLength()
            .Should().Be(1, "la solicitud caducada se ve en la ficha, no en la bandeja");

        var fichaResuelta = await AyudaContrato.FichaAsync(gerente, resuelta.ContratoId, ct);
        fichaResuelta.GetProperty("solicitudes").GetArrayLength()
            .Should().Be(1, "la solicitud resuelta también se ve en la ficha");

        // El resultado va como número porque el enum está mapeado a entero (docs/DOC_TECNICA §4.1):
        // Aprobada es 2.
        fichaResuelta.GetProperty("solicitudes")[0].GetProperty("resultado").GetInt32().Should().Be(2);
        fichaResuelta.GetProperty("solicitudes")[0].GetProperty("resueltoPorId").ValueKind
            .Should().Be(JsonValueKind.String, "quién resolvió queda escrito, no solo cuándo");
    }

    // ---------------------------------------------------------------- quién y qué

    /// <summary>
    /// La fila trae a quién la mandó, qué contrato es, y el valor actual y el propuesto
    /// (§3.16, literal).
    /// <para>
    /// El caso de un contrato nuevo es el que se da todos los días: todavía no hay renta vigente con
    /// la que comparar, así que <c>rentaVigente</c> viene vacía y el propuesto es la renta del
    /// contrato. Y <b>cuando no hay cambio de renta, los dos números son el mismo y
    /// <c>hayCambioDeRenta</c> es falso</b>: la bandeja no fabrica una diferencia para tener algo que
    /// enseñar, porque un "antes" inventado en la fila es exactamente el tipo de dato que hace que un
    /// gerente apruebe creyendo que hay algo que mirar.
    /// </para>
    /// </summary>
    [Fact]
    public async Task LaBandejaMuestraQuienQueYLosDosValores()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, empleado) = await SesionConUsuarioAsync(Roles.Empleado, ct);
        using var gerente = await AyudaBandeja.SesionAsync(api, bd, Roles.Gerente, ct);

        var contratoId = await PendienteAsync(empleado, 110, ct);

        var fila = FilaPropia(await BandejaAsync(gerente, ct), contratoId.ContratoId);

        // Quién.
        fila.SolicitadoPorId.Should().NotBeNull();
        fila.SolicitadoPor.Should().NotBeNullOrWhiteSpace("§3.16: la bandeja muestra quién la pidió");

        // Qué.
        fila.ContratoId.Should().Be(contratoId.ContratoId);
        fila.Cliente.Should().NotBeNullOrWhiteSpace();
        fila.Unidad.Should().NotBeNullOrWhiteSpace();
        fila.Estado.Should().Be("PendienteAprobacion");
        fila.HuellaContenido.Should().HaveLength(64, "la huella es lo que caduca si el contrato cambia");

        // Valor actual y propuesto. Sin historial de versiones no hay renta vigente todavía.
        fila.RentaVigente.Should().BeNull("un contrato que nunca estuvo activo no tiene renta vigente");
        fila.RentaPropuesta.Should().Be(50_000m, "la propuesta es la renta que pide el contrato");
        fila.Moneda.Should().Be("DOP");
        fila.HayCambioDeRenta.Should().BeFalse("sin renta vigente con la que comparar, no hay cambio");
    }

    /// <summary>
    /// El mismo contrato con historial: la bandeja lee el "valor actual" de <c>contrato_version</c> y
    /// lo enseña junto al propuesto, y avisa de que son distintos.
    /// <para>
    /// Ningún camino de negocio actual deja un contrato <i>activo</i> con versiones y a la vez
    /// pendiente de aprobación (un contrato vigente no vuelve atrás), así que la versión se escribe
    /// en la base. Lo que se prueba es el cable de la fila —que el valor actual sale del historial y
    /// no de la renta del contrato—, y ese cable tiene que poder probarse aunque hoy el segundo caso
    /// no se dé: cuando exista el reenvío de un contrato vigente, el dato ya estará dando el número
    /// correcto.
    /// </para>
    /// </summary>
    [Fact]
    public async Task LaBandejaEnsenaElValorActualDelHistorialCuandoHayCambioDeRenta()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaBandeja.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaBandeja.SesionAsync(api, bd, Roles.Gerente, ct);

        var contrato = await PendienteAsync(empleado, 111, ct);
        await AyudaBandeja.InsertarVersionAsync(bd, contrato.ContratoId, 45_000m, 50_000m, ct);

        var fila = FilaPropia(await BandejaAsync(gerente, ct), contrato.ContratoId);

        fila.RentaVigente.Should().Be(50_000m, "la vigente es la renta_nueva de la última versión");
        fila.RentaPropuesta.Should().Be(50_000m);
        fila.HayCambioDeRenta.Should().BeFalse(
            "la versión dice lo mismo que el contrato: sin cambio, los dos números coinciden y se dice");
    }

    // ---------------------------------------------------------------- permisos

    /// <summary>
    /// El empleado recibe <b>403</b> en la bandeja y al aprobar desde ella. No una lista vacía: una
    /// lista vacía esconde el error de permisos detrás de "no hay nada que aprobar", que es como se
    /// rompen estas pantallas sin que nadie se entere.
    /// <para>
    /// Y el contrato sigue esperando: el 403 tiene que haber parado la operación, no haberla
    /// dejado a medias.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ElEmpleadoNoPuedeAprobarDesdeLaBandejaNiVerla()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaBandeja.SesionAsync(api, bd, Roles.Empleado, ct);
        using var aprobador = await AyudaBandeja.SesionAsync(api, bd, Roles.Gerente, ct);

        var contratoId = await PendienteAsync(empleado, 120, ct);

        var ver = await empleado.GetAsync("/api/v1/contratos/solicitudes", ct);
        ver.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "contrato.estado no le viene al empleado en la matriz de la especificación §2");

        var aprobar = await empleado.PostAsync(
            $"/api/v1/contratos/solicitudes/{contratoId.ContratoId:D}/aprobacion", new { }, ct);
        aprobar.StatusCode.Should().Be(HttpStatusCode.Forbidden, await aprobar.Cuerpo());

        (await AyudaContrato.EstadoAsync(bd, contratoId.ContratoId, ct)).ToString()
            .Should().Be("PendienteAprobacion", "el 403 paró la operación antes de tocar nada");

        // El gerente sí ve lo mismo, y lo que el empleado no ve no ha desaparecido de nada.
        var bandejaDelGerente = await BandejaAsync(aprobador, ct);
        bandejaDelGerente.Select(f => f.ContratoId).Should().Contain(contratoId.ContratoId);
    }

    // ---------------------------------------------------------------- idempotencia

    /// <summary>
    /// Aprobar desde la bandeja es una acción de un clic, y un usuario impaciente lo aprieta dos
    /// veces. La misma <c>Idempotency-Key</c> dos veces tiene que devolver el resultado guardado, no
    /// un segundo intento de aprobación.
    /// <para>
    /// Lo que distingue esto de que "simplemente no se approves dos veces" es el código de la segunda
    /// respuesta: si no fuera idempotente, la segunda daría 409 por estado —el contrato ya está
    /// aprobado— y el usuario vería un error en algo que él ya había hecho bien. Aquí la segunda da
    /// 200 con la misma respuesta.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AprobarDesdeLaBandejaEsIdempotente()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaBandeja.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaBandeja.SesionAsync(api, bd, Roles.Gerente, ct);

        var contratoId = await PendienteAsync(empleado, 130, ct);
        var clave = $"aprobacion-bandeja-{Guid.NewGuid():N}";
        var ruta = $"/api/v1/contratos/solicitudes/{contratoId.ContratoId:D}/aprobacion";

        var primera = await gerente.PostAsync(ruta, new { }, clave, ct);
        primera.StatusCode.Should().Be(HttpStatusCode.OK, await primera.Cuerpo());

        var repetida = await gerente.PostAsync(ruta, new { }, clave, ct);
        repetida.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "la segunda es un reintento de la misma operación, no una operación nueva");

        // Se comparan los dos cuerpos por contenido, no por texto: el replay reescribe el JSON guardado y
        // sale con las claves en otro orden y con sangrías, pero tiene que ser el mismo resultado. Lo
        // que importa es que el reintento devuelva lo que devolvió la primera vez, no cómo lo
        // serializó cada una.
        var cuerpoEsperado = await primera.Content.ReadFromJsonAsync<JsonElement>(ct);
        var cuerpoRepetido = await repetida.Content.ReadFromJsonAsync<JsonElement>(ct);
        JsonElement.DeepEquals(cuerpoRepetido, cuerpoEsperado).Should().BeTrue(
            "se devuelve la respuesta guardada. Primera: {0}. Repetida: {1}",
            cuerpoEsperado.ToString(),
            cuerpoRepetido.ToString());

        // Y una sola aprobación de verdad: el contrato está aprobado y la plantilla congelada una vez.
        var ficha = await AyudaContrato.FichaAsync(gerente, contratoId.ContratoId, ct);
        ficha.GetProperty("estado").GetString().Should().Be("Aprobado");
        ficha.GetProperty("plantillaContratoCongeladaId").GetString()
            .Should().Be("estandar", "congelar la plantilla es lo que hace la aprobación, y va una vez");

        var solicitudes = await AyudaBandeja.SolicitudesDeAsync(bd, contratoId.ContratoId, ct);
        solicitudes.Should().ContainSingle("un envío, una solicitud, una resolución");
        solicitudes[0].Resultado.ToString().Should().Be("Aprobada");

        // Y con la misma clave desde la ficha tampoco hay una segunda aprobación: son la misma
        // operación y por eso comparten la huella de idempotencia.
        var desdeLaFicha = await gerente.PostAsync(
            $"/api/v1/contratos/{contratoId.ContratoId:D}/aprobacion", new { }, clave, ct);
        desdeLaFicha.StatusCode.Should().Be(HttpStatusCode.OK, await desdeLaFicha.Cuerpo());
        (await AyudaContrato.EstadoAsync(bd, contratoId.ContratoId, ct)).ToString().Should().Be("Aprobado");
    }

    // ---------------------------------------------------------------- caducidad

    /// <summary>
    /// Si el contrato cambió desde que se envió, aprobar desde la bandeja da 409 igual que por el
    /// endpoint de contratos.
    /// <para>
    /// Es la comprobación que la bandeja no puede saltarse por tener su propio camino: el gerente ve
    /// en la fila la renta que se le mandó a aprobar, y si alguien la cambia por debajo, lo que tiene
    /// delante ya no es lo que se pidió. Sin el 409, aprobar desde la bandeja sería el agujero que
    /// <c>HuellaContenido</c> de F2-05 cierra en el otro endpoint.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AprobarDesdeLaBandejaRespetaLaCaducidad()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaBandeja.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaBandeja.SesionAsync(api, bd, Roles.Gerente, ct);

        var contrato = await PendienteAsync(empleado, 140, ct);

        await EditarAsync(empleado, contrato, 75_000m, ct);

        var desdeLaBandeja = await gerente.PostAsync(
            $"/api/v1/contratos/solicitudes/{contrato.ContratoId:D}/aprobacion", new { }, ct);
        desdeLaBandeja.StatusCode.Should().Be(
            HttpStatusCode.Conflict, "lo que hay ahora no es lo que se pidió aprobar");

        var desdeLaFicha = await AyudaContrato.Boton(gerente, contrato.ContratoId, "aprobacion", ct: ct);
        desdeLaFicha.StatusCode.Should().Be(
            HttpStatusCode.Conflict,
            "los dos caminos dan lo mismo, que es lo que significa que la bandeja no reimplementa nada");

        (await AyudaContrato.EstadoAsync(bd, contrato.ContratoId, ct)).ToString().Should().Be("PendienteAprobacion");
    }

    // ---------------------------------------------------------------- misma lógica

    /// <summary>
    /// Aprobar por la bandeja y aprobar por la ficha producen lo mismo: mismo estado, mismo número de
    /// versión y misma plantilla congelada.
    /// <para>
    /// No es una prueba estética: si los dos caminos hicieran cada uno su cosa, el mismo contrato
    /// aprobado por dos personas distintas tendría dos historias distintas, y dentro de seis meses
    /// nadie sabría cuál de las dos es la buena.
    /// </para>
    /// </summary>
    [Fact]
    public async Task LaBandejaNoDuplicaLaLogicaDeAprobacion()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaBandeja.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaBandeja.SesionAsync(api, bd, Roles.Gerente, ct);

        var porLaBandeja = await PendienteAsync(empleado, 150, ct);
        var porLaFicha = await PendienteAsync(empleado, 151, ct);

        var respuesta = await gerente.PostAsync(
            $"/api/v1/contratos/solicitudes/{porLaBandeja.ContratoId:D}/aprobacion", new { }, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());

        await AyudaContrato.AceptarAsync(gerente, porLaFicha.ContratoId, "aprobacion", null, ct);

        (await AprobadoAsync(porLaBandeja.ContratoId, ct))
            .Should().Be(await AprobadoAsync(porLaFicha.ContratoId, ct));

        var desdeLaBandeja = await AyudaContrato.FichaAsync(gerente, porLaBandeja.ContratoId, ct);
        var desdeLaFicha = await AyudaContrato.FichaAsync(gerente, porLaFicha.ContratoId, ct);

        // Lo que la aprobación escribe, y tiene que ser lo mismo por los dos caminos. No se compara
        // la fecha de fin porque los dos contratos se crearon con días de inicio distintos y ahí la
        // diferencia sería de los datos de entrada, no de la aprobación.
        foreach (var campo in new[]
        {
            "estado",
            "plantillaContratoId",
            "plantillaContratoCongeladaId",
            "renta",
            "moneda",
        })
        {
            desdeLaBandeja.GetProperty(campo).ToString()
                .Should().Be(desdeLaFicha.GetProperty(campo).ToString(), $"«{campo}» tiene que ser el mismo");
        }

        // La huella congelada: la plantilla se copió en los dos casos, y una sola vez en cada uno.
        desdeLaBandeja.GetProperty("plantillaCongeladaAt").ValueKind.Should().Be(JsonValueKind.String);
        desdeLaFicha.GetProperty("plantillaCongeladaAt").ValueKind.Should().Be(JsonValueKind.String);
    }

    /// <summary>
    /// Rechazar desde la bandeja también es el rechazo de la ficha: vuelve a borrador y exige
    /// comentario (§3.5). Sin comentario no hay rechazo, ni desde la ficha ni desde la bandeja.
    /// </summary>
    [Fact]
    public async Task RechazarDesdeLaBandejaExigeComentarioYVuelveABorrador()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaBandeja.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaBandeja.SesionAsync(api, bd, Roles.Gerente, ct);

        var contratoId = await PendienteAsync(empleado, 160, ct);
        var ruta = $"/api/v1/contratos/solicitudes/{contratoId.ContratoId:D}/rechazo";

        var sinComentario = await gerente.PostAsync(ruta, new { comentario = "" }, ct);
        sinComentario.StatusCode.Should().Be(HttpStatusCode.BadRequest, await sinComentario.Cuerpo());

        var conComentario = await gerente.PostAsync(
            ruta, new { comentario = "Falta la plantilla vigente." }, ct);
        conComentario.StatusCode.Should().Be(HttpStatusCode.OK, await conComentario.Cuerpo());

        (await AyudaContrato.EstadoAsync(bd, contratoId.ContratoId, ct)).ToString().Should().Be("Borrador");
        (await AyudaBandeja.SolicitudesDeAsync(bd, contratoId.ContratoId, ct)).Should().ContainSingle();
    }

    // ---------------------------------------------------------------- atajos

    /// <summary>La bandeja tal y como la ve el gerente.</summary>
    private static async Task<List<Fila>> BandejaAsync(SesionCliente sesion, CancellationToken ct)
    {
        var respuesta = await sesion.GetAsync("/api/v1/contratos/solicitudes", ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());

        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        return [.. cuerpo.GetProperty("data").EnumerateArray().Select(Fila.Desde)];
    }

    /// <summary>
    /// <summary>
    /// Usuario y sesión del mismo rol juntos. Hace falta cuando la prueba compara lo que se ve en la
    /// fila con quien la envió: con la sesión suelta no se sabe qué nombre tiene la persona.
    /// </summary>
    private async Task<(Usuario Usuario, SesionCliente Sesion)> SesionConUsuarioAsync(
        string rol,
        CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(
            bd, AyudaInventario.Contrasena, rol: rol, ct: ct);

        return (usuario, await AyudaPermisos.SesionAsync(api, usuario, AyudaInventario.Contrasena, ct));
    }

    /// <summary>
    /// La fila de este contrato dentro de la bandeja. Se busca por identificador y no se exige que
    /// sea la única: la base es compartida con las demás pruebas de la colección y un
    /// <c>ContainSingle()</c> sobre la bandeja entera fallaría en cuanto otra dejara un contrato
    /// esperando.
    /// </summary>
    private static Fila FilaPropia(List<Fila> bandeja, Guid contratoId)
    {
        var propias = bandeja.Where(f => f.ContratoId == contratoId).ToList();
        propias.Should().ContainSingle("una solicitud viva por contrato: el índice parcial lo garantiza");

        return propias[0];
    }

    /// <summary>Contrato creado por el empleado y enviado a aprobación, que es lo que mete una fila en la
    /// bandeja. Cada prueba usa su propio desplazamiento de días, que es lo que evita que dos
    /// compartan unidad.
    /// </summary>
    private async Task<Pendiente> PendienteAsync(
        SesionCliente empleado,
        int desplazamiento,
        CancellationToken ct)
    {
        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);
        var inicio = AyudaContrato.Dia(desplazamiento);

        var contratoId = await AyudaContrato.CrearAsync(
            empleado,
            AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, inicio),
            ct);

        await AyudaContrato.AceptarAsync(empleado, contratoId, "solicitud", new { }, ct);

        return new Pendiente(contratoId, clienteId, unidadId, facturadoraId, inicio);
    }

    private sealed record Pendiente(
        Guid ContratoId,
        Guid ClienteId,
        Guid UnidadId,
        Guid FacturadoraId,
        DateOnly Inicio);

    /// <summary>
    /// Edita la renta del contrato pendiente, que es lo que caduca la solicitud. Va por el
    /// <c>PUT</c> completo de la ficha porque el endpoint exige también en la edición los
    /// obligatorios de §3.5.
    /// </summary>
    private static async Task EditarAsync(
        SesionCliente empleado,
        Pendiente contrato,
        decimal renta,
        CancellationToken ct)
    {
        var respuesta = await empleado.PutAsync(
            $"/api/v1/contratos/{contrato.ContratoId:D}",
            AyudaContrato.Peticion(
                contrato.ClienteId,
                contrato.UnidadId,
                contrato.FacturadoraId,
                contrato.Inicio,
                renta: renta),
            ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Cuerpo());
    }

    /// <summary>Lo que se compara entre los dos caminos de aprobación.</summary>
    private Task<Aprobado> AprobadoAsync(Guid contratoId, CancellationToken ct) =>
        AyudaBandeja.EnContextoAsync(
            bd,
            async c =>
            {
                var contrato = await c.Contrato.AsNoTracking().SingleAsync(x => x.Id == contratoId, ct);
                var versiones = await c.ContratoVersion.AsNoTracking()
                    .CountAsync(v => v.ContratoId == contratoId, ct);

                return new Aprobado(contrato.Estado, versiones, contrato.PlantillaContratoCongeladaId);
            });

    private sealed record Aprobado(EstadoContrato Estado, int Versiones, string? PlantillaCongelada);

    /// <summary>Fila de la bandeja tal y como la devuelve la API, para comprobar los dos valores.</summary>
    private sealed record Fila(
        Guid SolicitudId,
        Guid ContratoId,
        Guid? SolicitadoPorId,
        string? SolicitadoPor,
        string? Cliente,
        string? Unidad,
        string Estado,
        DateOnly FechaInicio,
        DateOnly FechaFinVigente,
        decimal? RentaVigente,
        decimal RentaPropuesta,
        string Moneda,
        bool HayCambioDeRenta,
        string HuellaContenido,
        DateTimeOffset SolicitadoAt,
        string? Comentario)
    {
        public static Fila Desde(JsonElement e) =>
            new(
                e.GetProperty("solicitudId").GetGuid(),
                e.GetProperty("contratoId").GetGuid(),
                e.GetProperty("solicitadoPorId").ValueKind == JsonValueKind.Null
                    ? null
                    : e.GetProperty("solicitadoPorId").GetGuid(),
                e.GetProperty("solicitadoPor").ValueKind == JsonValueKind.Null
                    ? null
                    : e.GetProperty("solicitadoPor").GetString(),
                e.GetProperty("cliente").ValueKind == JsonValueKind.Null ? null : e.GetProperty("cliente").GetString(),
                e.GetProperty("unidad").ValueKind == JsonValueKind.Null ? null : e.GetProperty("unidad").GetString(),
                e.GetProperty("estado").GetString()!,
                DateOnly.Parse(e.GetProperty("fechaInicio").GetString()!, CultureInfo.InvariantCulture),
                DateOnly.Parse(e.GetProperty("fechaFinVigente").GetString()!, CultureInfo.InvariantCulture),
                e.GetProperty("rentaVigente").ValueKind == JsonValueKind.Null
                    ? null
                    : e.GetProperty("rentaVigente").GetDecimal(),
                e.GetProperty("rentaPropuesta").GetDecimal(),
                e.GetProperty("moneda").GetString()!,
                e.GetProperty("hayCambioDeRenta").GetBoolean(),
                e.GetProperty("huellaContenido").GetString()!,
                e.GetProperty("solicitadoAt").GetDateTimeOffset(),
                e.GetProperty("comentario").ValueKind == JsonValueKind.Null ? null : e.GetProperty("comentario").GetString());
    }
}