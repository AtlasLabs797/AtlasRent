using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Npgsql;
using Xunit;

namespace AtlasRent.Tests.Contratos;

/// <summary>
/// BUG-052: la plantilla congelada, el inventario congelado y las versiones del contrato son copias
/// que no cambian una vez escritas, así que <c>rent_app</c> no tiene <c>UPDATE</c> sobre ellas (el
/// permiso de la base es la última barrera si un bug o una inyección intentara reescribir un contrato
/// firmado). Sí pueden leerse e insertarse.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class TablasCongeladasSinUpdateTests(ContenedorBdFixture bd)
{
    [Theory]
    [InlineData("contrato_plantilla_congelada")]
    [InlineData("contrato_inventario_item")]
    [InlineData("contrato_version")]
    public async Task LaAplicacionNoPuedeActualizarUnaCopiaCongelada(string tabla)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = new NpgsqlConnection(bd.CadenaApp);
        await conexion.OpenAsync(ct);

        await using var privilegios = new NpgsqlCommand(
            "SELECT has_table_privilege('rent_app', @tabla, 'UPDATE'), has_table_privilege('rent_app', @tabla, 'INSERT'), has_table_privilege('rent_app', @tabla, 'SELECT')",
            conexion);
        privilegios.Parameters.AddWithValue("tabla", $"public.{tabla}");
        await using var lector = await privilegios.ExecuteReaderAsync(ct);
        (await lector.ReadAsync(ct)).Should().BeTrue();

        lector.GetBoolean(0).Should().BeFalse($"{tabla} es una copia congelada: no se actualiza");
        lector.GetBoolean(1).Should().BeTrue("la copia se escribe una vez");
        lector.GetBoolean(2).Should().BeTrue("y se lee");
    }
}
