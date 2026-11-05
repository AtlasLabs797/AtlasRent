using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cobros;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Fianzas;

/// <summary>
/// F3-09: <c>fianza.mover</c> es de gerente y administrador; el empleado no crea fianzas, no las cobra,
/// no las devuelve y no las liquida (§2, D15).
/// <para>
/// Va por la API real, que es donde vive el filtro <c>[Accion]</c>, y comprueba que el intento denegado
/// queda en la auditoría firmada. Se comprueba también que el gerente puede, porque un permiso que se
/// deniega a todos parece correcto y no sirve de nada.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class FianzaPermisosTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    [Fact]
    public async Task ElEmpleadoNoPuedeCrearCobrarDevolverNiLiquidarUnaFianza()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuarioEmpleado = await AyudaAuth.CrearUsuarioAsync(
            bd, AyudaFacturas.Contrasena, rol: Roles.Empleado, ct: ct);

        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);
        using var empleado = await AyudaPermisos.SesionAsync(api, usuarioEmpleado, AyudaFacturas.Contrasena, ct);

        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);

        // Crear: 403 y ninguna fianza creada.
        var creada = await empleado.PostAsync(
            "/api/v1/fianzas",
            new { contratoId, importeExigido = 500m, moneda = nameof(Moneda.USD), numeroFianzas = 1 },
            ct);
        creada.StatusCode.Should().Be(HttpStatusCode.Forbidden, await creada.Cuerpo());

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            (await contexto.Fianza.AsNoTracking().CountAsync(f => f.ContratoId == contratoId, ct))
                .Should().Be(0, "un 403 no crea la fianza");
        }

        // La crea el administrador, y sobre ella se prueba el resto de denegaciones.
        var fianzaId = await AyudaFianzas.AltaAsync(admin, contratoId, 500m, Moneda.USD, 1, ct);
        await AyudaFianzas.CobradaAsync(admin, clienteId, contratoId, fianzaId, 500m, Moneda.USD, ct);

        // Cobrar: el permiso de la fianza va en la línea de la asignación, y el cobro es de
        // `cobro.registrar`, que el empleado tampoco tiene. Se comprueba por la vía que existe: un
        // employee tampoco puede registrar el cobro que la pagaría.
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        var formaPagoId = await AyudaCobros.FormaPagoAsync(admin, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(admin, Moneda.USD, ct);

        var cobro = await empleado.PostAsync(
            "/api/v1/cobros",
            AyudaCobros.Peticion(
                clienteId, contratoId, hoy, Moneda.USD, 500m, formaPagoId, cuentaId,
                asignaciones: new[] { new { fianzaId, importe = 500m } }),
            ct);
        cobro.StatusCode.Should().Be(HttpStatusCode.Forbidden, "cobrar una fianza es registrar un cobro");

        // Devolver y liquidar.
        var devuelta = await empleado.PostAsync(
            $"/api/v1/fianzas/{fianzaId:D}/devolver",
            AyudaFianzas.PeticionDevolucion(500m),
            ct);
        devuelta.StatusCode.Should().Be(HttpStatusCode.Forbidden, await devuelta.Cuerpo());

        var liquidada = await empleado.PostAsync($"/api/v1/fianzas/{fianzaId:D}/liquidar", new { }, ct);
        liquidada.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var retenida = await empleado.PostAsync($"/api/v1/fianzas/{fianzaId:D}/retener", new { }, ct);
        retenida.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var contextoFinal = bd.CrearContexto(bd.CadenaApp);
        (await contextoFinal.Fianza.AsNoTracking().CountAsync(f => f.Id == fianzaId, ct)).Should().Be(1);
        (await contextoFinal.FianzaDevolucion.AsNoTracking().CountAsync(d => d.FianzaId == fianzaId, ct))
            .Should().Be(0, "ni un 403 devuelve ni un céntimo");
        (await contextoFinal.Fianza.AsNoTracking().Where(f => f.Id == fianzaId).Select(f => f.Estado).SingleAsync(ct))
            .Should().Be(EstadoFianza.Pendiente, "ni un 403 decide el estado");

        // Los cuatro intentos quedan en la auditoría firmada, con su rol.
        await using var sinRls = bd.CrearContexto(bd.CadenaSuperusuario);
        var denegadas = await sinRls.Auditoria.AsNoTracking()
            .Where(a => a.UsuarioId == usuarioEmpleado.Id
                && (a.Accion == $"permiso.denegado.{Acciones.FianzaMover}"
                    || a.Accion == $"permiso.denegado.{Acciones.CobroRegistrar}"))
            .ToListAsync(ct);

        denegadas.Should().NotBeEmpty("el intento denegado queda en la auditoría firmada");
        denegadas.Should().OnlyContain(a => a.Resultado == "denegado" && a.Ip != null);
        denegadas.Should().Contain(a => a.Accion == $"permiso.denegado.{Acciones.FianzaMover}");
        denegadas.Should().Contain(a => a.Datos!.RootElement.GetProperty("rol").GetString() == Roles.Empleado);
    }

    /// <summary>
    /// El gerente puede hacer las cuatro cosas. Sin esta prueba, un permiso que estuviera sembrado mal
    /// y se denegara a todos pasaría el test anterior.
    /// </summary>
    [Fact]
    public async Task ElGerentePuedeCrearCobrarDevolverYLiquidar()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);
        using var gerente = await AyudaCobros.SesionAsync(bd, api, Roles.Gerente, ct);

        // La forma de pago y la cuenta las crea el administrador porque crearlas es configuración
        // (F1-04), que es de administrador. Si las creara el gerente, el 403 sería del permiso de
        // configuración y esta prueba estaría midiendo otra cosa.
        var formaPagoId = await AyudaCobros.FormaPagoAsync(admin, ct);
        var cuentaId = await AyudaCobros.CuentaAsync(admin, Moneda.USD, ct);

        var (clienteId, contratoId) = await AyudaFianzas.ContratoAsync(gerente, bd, ct);
        var fianzaId = await AyudaFianzas.AltaAsync(gerente, contratoId, 500m, Moneda.USD, 1, ct);
        await AyudaFianzas.CobradaAsync(
            gerente, clienteId, contratoId, fianzaId, 500m, Moneda.USD, ct, formaPagoId, cuentaId);

        var retenida = await gerente.PostAsync($"/api/v1/fianzas/{fianzaId:D}/retener", new { }, ct);
        retenida.StatusCode.Should().Be(HttpStatusCode.OK, await retenida.Cuerpo());

        var liquidada = await gerente.PostAsync($"/api/v1/fianzas/{fianzaId:D}/liquidar", new { }, ct);
        liquidada.StatusCode.Should().Be(HttpStatusCode.OK, await liquidada.Cuerpo());

        (await AyudaFianzas.EstadoGuardadoAsync(bd, fianzaId, ct)).Should().Be(EstadoFianza.EnLiquidacion);

        var devuelta = await gerente.PostAsync(
            $"/api/v1/fianzas/{fianzaId:D}/devolver",
            AyudaFianzas.PeticionDevolucion(500m),
            ct);
        devuelta.StatusCode.Should().Be(HttpStatusCode.OK, await devuelta.Cuerpo());

        (await AyudaFianzas.CifrasAsync(bd, fianzaId, ct)).Saldo.Should().Be(0m);
    }

    /// <summary>
    /// Ver una fianza no es moverla: el listado usa <c>factura.ver</c>, que es el permiso de lectura que
    /// ya traían todas las cuentas de la casa. Un empleado puede ver cuánto queda de una fianza y no
    /// puede moverlo, que es exactamente lo que §2 dice.
    /// </summary>
    [Fact]
    public async Task ElEmpleadoPuedeVerElListadoDeFianzas()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuarioEmpleado = await AyudaAuth.CrearUsuarioAsync(
            bd, AyudaFacturas.Contrasena, rol: Roles.Empleado, ct: ct);

        using var admin = await AyudaCobros.SesionAsync(bd, api, Roles.Admin, ct);
        using var empleado = await AyudaPermisos.SesionAsync(api, usuarioEmpleado, AyudaFacturas.Contrasena, ct);

        var (_, contratoId) = await AyudaFianzas.ContratoAsync(admin, bd, ct);
        await AyudaFianzas.AltaAsync(admin, contratoId, 500m, Moneda.USD, 1, ct);

        var listado = await empleado.GetAsync($"/api/v1/fianzas?contratoId={contratoId:D}", ct);

        listado.StatusCode.Should().Be(HttpStatusCode.OK, await listado.Cuerpo());
    }
}