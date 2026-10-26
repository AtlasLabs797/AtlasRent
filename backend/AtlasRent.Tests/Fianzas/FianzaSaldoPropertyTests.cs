using System.Net;
using System.Net.Http.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cobros;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Fianzas;

/// <summary>
/// F3-09: <b>el saldo de una fianza nunca queda negativo</b>, por muchos importes aleatorios que se le
/// pidan.
/// <para>
/// No es una comprobación de un caso, es la regla que sostiene la pantalla: si el saldo puede ser
/// negativo, la vista de cuentas (§3.13) enseña un número que no significa nada. Y hay dos caminos por
/// los que se podría llegar: devolver más de lo cobrado —que el servicio rechaza con 409— y anular el
/// cobro que pagó la fianza después de haber devuelto parte de ella, que el saldo lo deja en negativo
/// por aritmética y no por un error de quien escribe.
/// </para>
/// <para>
/// Se prueba lo segundo porque lo primero es una validación entrada por entrada y ya está en
/// <see cref="FianzaDevolucionTests"/>: aquí lo interesante es que, sea cual sea el camino, el
/// número que sale de la base nunca es negativo.
/// </para>
[Collection(nameof(ColeccionBd))]
public sealed class FianzaSaldoPropertyTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(25)]
    public async Task ElSaldoNuncaQuedaNegativoConImportesAleatorios(int semilla)
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        var exigido = 3_000m;
        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, exigido, Moneda.USD, 1, ct);
        await AyudaFianzas.CobradaAsync(admin, clienteId, contratoId, fianzaId, exigido, Moneda.USD, ct);

        var aleatorio = new Random(semilla);
        var saldo = exigido;

        // Importes que van de cero a más del doble de lo cobrado: la mitad tienen que acabar en 409 y
        // la otra mitad en un saldo exacto. La propiedad no es «siempre 200» sino «el saldo que sale es
        // el que corresponde y nunca es negativo», y esa se comprueba en los dos ramos.
        var devolver = Redondeo.Importe(aleatorio.Next(0, (int)(saldo * 2)));
        var descontado = Redondeo.Importe(aleatorio.Next(0, 500));
        var total = Redondeo.Importe(devolver + descontado);
        var motivo = $"Semilla {semilla}: devolución aleatoria";

        var respuesta = await admin.PostAsync(
            $"/api/v1/fianzas/{fianzaId:D}/devolver",
            AyudaFianzas.PeticionDevolucion(devolver, descontado, motivo),
            ct);

        var cifrasTras = await AyudaFianzas.CifrasAsync(bd, fianzaId, ct);

        // El orden de las reglas del servicio es el que decide cuál de las cuatro salidas toca, y se
        // comprueba en ese mismo orden: primero lo que no devuelve nada, después el exceso, después la
        // parcial sin descuento y, si no, la buena.
        if (total <= 0m)
        {
            respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        else if (total > saldo)
        {
            // Devolver más de lo cobrado: 409 con el saldo y lo pedido, y el saldo no se toca.
            respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await respuesta.Cuerpo()).Should().Contain("3000");
            cifrasTras.Saldo.Should().Be(saldo, "un 409 no toca el saldo");
            cifrasTras.Devuelto.Should().Be(0m);
            return;
        }
        else if (total < saldo && descontado <= 0m)
        {
            respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await respuesta.Cuerpo()).Should().Contain("descontado");
            cifrasTras.Saldo.Should().Be(saldo, "un 400 no toca el saldo");
            return;
        }

        respuesta.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await respuesta.Cuerpo()
            + $" (devolviendo {devolver}, reteniendo {descontado}, sobre un saldo de {saldo})");

        cifrasTras.Devuelto.Should().Be(devolver);
        cifrasTras.Descontado.Should().Be(descontado);
        cifrasTras.Saldo.Should().Be(
            Redondeo.Importe(saldo - devolver - descontado),
            $"con semilla {semilla}: el saldo es lo que quedaba menos lo devuelto y lo retenido");
        cifrasTras.Saldo.Should().BeGreaterThanOrEqualTo(0m, "y nunca puede quedar en negativo");
    }

    /// <summary>
    /// BUG-034 (a): anular el cobro que pagó la fianza cuando ya se devolvió parte de ella dejaría
    /// devuelto + descontado mayor que cobrado. Es un 409, el cobro sigue registrado y el saldo no se toca.
    /// </summary>
    [Fact]
    public async Task AnularElCobroDeUnaFianzaYaDevueltaEs409()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, 500m, Moneda.USD, 1, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(admin, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(admin, Moneda.USD, ct);

        var registrado = await admin.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.USD, 500m, formaPagoId, cuentaId,
                asignaciones: new[] { new { fianzaId, importe = 500m } }),
            ct);
        registrado.StatusCode.Should().Be(HttpStatusCode.OK, await registrado.Cuerpo());
        var cobroId = (await registrado.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct))
            .GetProperty("id").GetGuid();

        var devuelta = await admin.PostAsync(
            $"/api/v1/fianzas/{fianzaId:D}/devolver",
            AyudaFianzas.PeticionDevolucion(500m),
            ct);
        devuelta.StatusCode.Should().Be(HttpStatusCode.OK, await devuelta.Cuerpo());

        var anulacion = await admin.PostAsync(
            $"/api/v1/cobros/{cobroId:D}/anular",
            new { motivo = "Anulo el cobro de la fianza después de devolverla" },
            ct);
        anulacion.StatusCode.Should().Be(HttpStatusCode.Conflict, await anulacion.Cuerpo());

        var cifras = await AyudaFianzas.CifrasAsync(bd, fianzaId, ct);
        cifras.Cobrado.Should().Be(500m, "el cobro sigue vigente");
        cifras.Devuelto.Should().Be(500m);
        cifras.Saldo.Should().Be(0m);
    }

    /// <summary>
    /// BUG-034 (a): si lo devuelto cabe en lo que queda cobrado tras anular, la anulación es válida.
    /// Dos cobros de 300 y 200; se devuelven 200 y se retienen 100; se anula el de 200: queda 300.
    /// </summary>
    [Fact]
    public async Task AnularUnCobroDeFianzaQueNoDejaMasDevueltoQueCobradoSigueValiendo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);

        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, 500m, Moneda.USD, 1, ct);

        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(admin, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(admin, Moneda.USD, ct);

        async Task<Guid> CobrarAsync(decimal importe)
        {
            var r = await admin.PostAsync(
                "/api/v1/cobros",
                AyudaCobros.Peticion(
                    clienteId, contratoId, hoy, Moneda.USD, importe, formaPagoId, cuentaId,
                    asignaciones: new[] { new { fianzaId, importe } }),
                ct);
            r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Cuerpo());
            return (await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct)).GetProperty("id").GetGuid();
        }

        await CobrarAsync(300m);
        var segundo = await CobrarAsync(200m);

        var devuelta = await admin.PostAsync(
            $"/api/v1/fianzas/{fianzaId:D}/devolver",
            AyudaFianzas.PeticionDevolucion(200m, 100m, "Faltantes del inventario"),
            ct);
        devuelta.StatusCode.Should().Be(HttpStatusCode.OK, await devuelta.Cuerpo());

        var anulacion = await admin.PostAsync(
            $"/api/v1/cobros/{segundo:D}/anular",
            new { motivo = "Cobro duplicado por error" },
            ct);
        anulacion.StatusCode.Should().Be(HttpStatusCode.OK, await anulacion.Cuerpo());

        var cifras = await AyudaFianzas.CifrasAsync(bd, fianzaId, ct);
        cifras.Cobrado.Should().Be(300m);
        cifras.Saldo.Should().Be(0m);
    }

    /// <summary>BUG-034 (b): el saldo no esconde un negativo con un clamp.</summary>
    [Fact]
    public void ElSaldoNegativoSeVeYNoSeEscondeEnCero()
    {
        CalculoFianza.Saldo(0m, 500m, 0m).Should().Be(-500m);
        CalculoFianza.Saldo(300m, 200m, 100m).Should().Be(0m);
    }
}
