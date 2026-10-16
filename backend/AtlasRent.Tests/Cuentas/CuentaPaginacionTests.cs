using AtlasRent.Api.Authorization;
using AtlasRent.Tests.Infra;
using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Cuentas;

/// <summary>
/// F3-10: la paginación de la vista única, que mezcla facturas y fianzas en la misma respuesta.
/// <para>
/// El orden es fecha y, a igualdad de fecha, identificador. El desempate por identificador es lo
/// único que hace la paginación estable: sin él, dos filas con la misma fecha pueden salir en
/// posición distinta en dos peticiones y una aparecer dos veces mientras otra no aparece ninguna.
/// </para>
/// <para>
/// Se prueba con las dos clases de fila mezcladas porque es justo donde se rompe una paginación
/// mal hecha: si se cortara por separado y se uniera sin cuidado, la tercera página repetiría filas.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class CuentaPaginacionTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    /// <summary>
    /// Tres páginas de dos filas sobre cinco documentos (cuatro facturas y una fianza) sin repeticiones
    /// ni huecos, con el mismo orden, y el recuento y las páginas que dicen las de la vista.
    /// </summary>
    [Fact]
    public async Task LasPaginasNoSeRepitenNiSePierdenFilasYMantienenElOrden()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);

        // Las facturas salen con fechas distintas para que el orden sea comprobable de un vistazo.
        var esperadas = new List<Guid>();
        for (var dia = 1; dia <= 4; dia++)
        {
            esperadas.Add(await AyudaCuentas.EmitidaAsync(
                admin, escenario, escenario.Hoy.AddDays(-dia * 30), 100m * dia, ct));
        }

        esperadas.Add(await AyudaCuentas.FianzaAsync(admin, escenario, 500m, ct));

        var consulta = $"clienteId={escenario.ClienteId:D}&sortBy=fecha&sortDir=asc";

        var primera = await AyudaCuentas.VistaAsync(admin, consulta, ct, page: 1, pageSize: 2);
        var segunda = await AyudaCuentas.VistaAsync(admin, consulta, ct, page: 2, pageSize: 2);
        var tercera = await AyudaCuentas.VistaAsync(admin, consulta, ct, page: 3, pageSize: 2);

        var filas = new List<JsonElement>();
        filas.AddRange(AyudaCuentas.Filas(primera));
        filas.AddRange(AyudaCuentas.Filas(segunda));
        filas.AddRange(AyudaCuentas.Filas(tercera));

        filas.Select(f => f.GetProperty("id").GetGuid())
            .Should().OnlyHaveUniqueItems("una fila no puede aparecer en dos páginas");

        filas.Select(f => f.GetProperty("id").GetGuid())
            .Should().BeEquivalentTo(esperadas, "entre las tres páginas tienen que estar las cinco filas");

        // La clave de orden, como la compararía alguien mirando la tabla: la fecha y el identificador
        // de la fila. La fecha es «yyyy-MM-dd», así que compararla como texto es compararla por
        // fecha, y el identificador desempata lo que la fecha no puede.
        var claves = filas
            .Select(f => $"{f.GetProperty("fecha").GetString()}|{f.GetProperty("id").GetGuid():D}")
            .ToList();

        claves.Should().BeInAscendingOrder("el orden es por fecha y, a igualdad de fecha, por identificador");

        foreach (var pagina in new[] { primera, segunda, tercera })
        {
            pagina.GetProperty("total").GetInt32().Should().Be(5);
            pagina.GetProperty("pageSize").GetInt32().Should().Be(2);
            pagina.GetProperty("totalPages").GetInt32().Should().Be(3);
        }

        AyudaCuentas.Filas(primera).Should().HaveCount(2);
        AyudaCuentas.Filas(segunda).Should().HaveCount(2);
        AyudaCuentas.Filas(tercera).Should().HaveCount(1);
    }

    /// <summary>
    /// La página 1 es la misma dos veces seguidas. No es una prueba redundante: una lista cuyo orden
    /// depende del plan de ejecución cambia entre peticiones aunque los datos no hayan cambiado, y eso
    /// es lo que hace que el botón de «siguiente» de la pantalla a veces repita una fila.
    /// </summary>
    [Fact]
    public async Task LaPrimeraPaginaEsLaMismaEnDosPeticionesSeguidas()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCuentas.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(11), 400m, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(12), 500m, ct);

        var consulta = $"clienteId={escenario.ClienteId:D}&sortBy=fecha&sortDir=desc&pageSize=2";

        var primera = AyudaCuentas.Filas(await AyudaCuentas.VistaAsync(admin, consulta, ct));
        var repetida = AyudaCuentas.Filas(await AyudaCuentas.VistaAsync(admin, consulta, ct));

        repetida.Select(f => f.GetProperty("id").GetGuid())
            .Should().Equal(primera.Select(f => f.GetProperty("id").GetGuid()));
    }
}