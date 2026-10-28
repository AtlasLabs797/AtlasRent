using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Contratos;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Inventario;

/// <summary>
/// F2-08: el inventario de la unidad se copia y se congela al activar el contrato
/// (docs/ESPECIFICACION.md §3.5: "el inventario se copia y congela al activar").
/// <para>
/// Este es el test que importa, y es una afirmación negativa por el mismo motivo que el de la
/// plantilla congelada de F2-07: lo que importa no es que la copia se creara, sino que <b>no se pueda
/// reescribir</b>. Editar el inventario base de la unidad después de que hay un contrato vigente sobre
/// ella no puede cambiar lo que ese contrato firmado tiene por suyo.
/// </para>
/// <list type="bullet">
/// <item>Al activar, el contrato se lleva sus tres elementos con los mismos valores.</item>
/// <item>Editar el base después no cambia ni uno de esos valores.</item>
/// <item>Y firmar la revisión de salida tampoco lo cambia: es la contra que se firmó, no un anexo.</item>
/// <item>Una unidad sin inventario base activa igual (§3.1 lo hace opcional).</item>
/// </list>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class InventarioCongeladoTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    private static readonly (string Nombre, decimal Cantidad, string? Notas)[] TresElementos =
    [
        ("Colchón", 2, "Doble, marca Topy"),
        ("Nevera", 1, "Undercounter"),
        ("Silla de oficina", 4, null),
    ];

    /// <summary>
    /// El criterio de aceptación: activar copia el inventario de la unidad y esa copia queda
    /// congelada. Editar el catálogo después no la toca.
    /// </summary>
    [Fact]
    public async Task ElInventarioSeCopiaYSeCongelaAlActivar()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaInventario.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaInventario.SesionAsync(api, bd, Roles.Gerente, ct);

        var (unidadId, contratoId) = await AyudaInventario.ContratoActivoConInventarioAsync(
            empleado, gerente, bd, TresElementos, desplazamientoInicio: 300, ct: ct);

        // Lo congelado es lo que había, con sus valores.
        var congelado = await AyudaInventario.InventarioContratoAsync(empleado, contratoId, ct);
        var elementos = congelado.EnumerateArray().ToList();
        elementos.Should().HaveCount(3, "la unidad tenía tres elementos y el contrato se lleva los tres");
        elementos.Select(e => e.GetProperty("nombre").GetString()).Should().Equal(
            TresElementos.Select(e => e.Nombre),
            "la copia sale en el orden en que el empleado los escribió");
        elementos.Select(e => e.GetProperty("cantidad").LeerDecimal()).Should().Equal(
            TresElementos.Select(e => e.Cantidad));
        elementos.Select(e => e.GetProperty("notas").GetString()).Should().Equal(
            TresElementos.Select(e => e.Notas));

        // El identificador del inventario base, que es el que se puede editar: el de la copia
        // congelada es otro distinto, y por eso editar la copia no existe como operación.
        var baseAntes = await empleado.GetAsync($"/api/v1/unidades/{unidadId:D}/inventario-base", ct);
        baseAntes.StatusCode.Should().Be(HttpStatusCode.OK, await baseAntes.Cuerpo());
        var idDelColchonBase = (await Elementos(baseAntes, ct))[0].GetProperty("id").GetGuid();

        // Editar el inventario base, que es exactamente lo que no puede cambiar este contrato.
        var edicion = await empleado.PutAsync(
            $"/api/v1/unidades/{unidadId:D}/inventario-base/{idDelColchonBase:D}",
            AyudaInventario.Elemento("Colchón de dos plazas", 3, "Cambiado después de la firma"),
            ct);
        edicion.StatusCode.Should().Be(HttpStatusCode.OK, await edicion.Cuerpo());

        // Y el catálogo sí cambió, para que no sea que la edición no llegó a hacer nada.
        var baseActual = await empleado.GetAsync($"/api/v1/unidades/{unidadId:D}/inventario-base", ct);
        baseActual.StatusCode.Should().Be(HttpStatusCode.OK, await baseActual.Cuerpo());
        var trasEditar = await Elementos(baseActual, ct);
        trasEditar[0].GetProperty("nombre").GetString().Should().Be("Colchón de dos plazas");
        trasEditar[0].GetProperty("cantidad").LeerDecimal().Should().Be(3);

        // Pero la copia congelada sigue con lo que había el día de la entrada.
        var trasActivar = await AyudaInventario.InventarioContratoAsync(empleado, contratoId, ct);
        var copia = trasActivar.EnumerateArray().ToList();
        copia.Should().HaveCount(3);
        copia.Select(e => e.GetProperty("nombre").GetString()).Should().Equal(
            TresElementos.Select(e => e.Nombre),
            "editar el inventario base no reescribe lo que ya se firmó");
        copia.Select(e => e.GetProperty("cantidad").LeerDecimal()).Should().Equal(
            TresElementos.Select(e => e.Cantidad));
        copia.Select(e => e.GetProperty("notas").GetString()).Should().Equal(
            TresElementos.Select(e => e.Notas));
    }

    /// <summary>
    /// §3.1 dice que el inventario base es opcional. Un contrato de una unidad sin inventario se
    /// activa igual y sale sin inventario: no es un fallo, es una unidad sin inventario.
    /// </summary>
    [Fact]
    public async Task UnContratoSinInventarioBaseActivaIgual()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaInventario.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaInventario.SesionAsync(api, bd, Roles.Gerente, ct);

        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);

        var contratoId = await AyudaContrato.CrearAsync(
            empleado,
            AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, AyudaContrato.Dia(301)),
            ct);
        await AyudaContrato.AceptarAsync(empleado, contratoId, "solicitud", new { }, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "aprobacion", null, ct);
        await AyudaContrato.AceptarAsync(gerente, contratoId, "activacion", null, ct);

        (await AyudaContrato.EstadoAsync(bd, contratoId, ct)).Should().Be(
            EstadoContrato.Activo,
            "una unidad sin inventario base no impide activar el contrato (§3.1)");

        var congelado = await AyudaInventario.InventarioContratoAsync(empleado, contratoId, ct);
        congelado.GetArrayLength().Should().Be(0, "no había inventario base que copiar");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.ContratoInventarioItem
            .CountAsync(i => i.ContratoId == contratoId, ct)).Should().Be(
            0, "no se inventa inventario para una unidad que no lo tiene");
        (await contexto.Contrato
            .Where(c => c.Id == contratoId)
            .Select(c => c.InventarioCongeladoAt)
            .SingleAsync(ct)).Should()
            .BeNull("sin inventario base no hay congelación que fechar");
    }

    /// <summary>
    /// Firmar la revisión de salida no borra ni toca el inventario congelado. Esa copia es la contra
    /// que el cliente acaba de firmar: si desaparecer, la revisión se quedaría sin contra y el
    /// documento histórico no valdría para nada.
    /// </summary>
    [Fact]
    public async Task FirmarLaRevisionNoBorraElInventarioDelContrato()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaInventario.SesionAsync(api, bd, Roles.Empleado, ct);
        using var gerente = await AyudaInventario.SesionAsync(api, bd, Roles.Gerente, ct);

        var (_, contratoId) = await AyudaInventario.ContratoActivoConInventarioAsync(
            empleado, gerente, bd, TresElementos, desplazamientoInicio: 302, ct: ct);

        var antes = await AyudaInventario.InventarioContratoAsync(empleado, contratoId, ct);

        await AyudaInventario.FirmarAsync(
            gerente,
            contratoId,
            antes,
            [("Presente", null), ("Presente", null), ("Ausente", "Falta una silla")],
            importePropuesto: 5_000m,
            ct: ct);

        var despues = await AyudaInventario.InventarioContratoAsync(empleado, contratoId, ct);
        var trasFirmar = despues.EnumerateArray().ToList();
        trasFirmar.Should().HaveCount(3, "firmar la revisión no borra la foto que se firmó");
        trasFirmar.Select(e => e.GetProperty("nombre").GetString()).Should().Equal(
            TresElementos.Select(e => e.Nombre));
        trasFirmar.Select(e => e.GetProperty("cantidad").LeerDecimal()).Should().Equal(
            TresElementos.Select(e => e.Cantidad));

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        (await contexto.ContratoInventarioItem
            .Where(i => i.ContratoId == contratoId)
            .CountAsync(ct)).Should().Be(3, "las filas siguen ahí: no hay DELETE en esta tabla");
    }

    private static async Task<List<JsonElement>> Elementos(
        HttpResponseMessage respuesta,
        CancellationToken ct) =>
        [.. (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).EnumerateArray()];
}