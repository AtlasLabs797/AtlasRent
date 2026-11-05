using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cobros;
using AtlasRent.Tests.Comprobantes;
using AtlasRent.Tests.Facturas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.CicloFactura;

/// <summary>
/// F3-08: las seis operaciones del ciclo de vida —modificar, anular, duplicar, convertir proforma,
/// rehacer en otra moneda y simular la anulación— usan <c>factura.anular</c>, el permiso que §2 da
/// exactamente a este trabajo («modificar o anular una factura ya emitida»). No <c>factura.emitir</c>:
/// emitir es sacar número y comprobante, que es otra cosa.
/// <para>
/// Van por la API real, que es donde vive el filtro <c>[Accion]</c>. La denegación se comprueba con el
/// identificador de la acción exacta —un 403 de otra acción no demostraría nada— y además se comprueba
/// que el gerente, que sí lo tiene, pasa: sin esa mitad, «el empleado no puede» no distingue un
/// permiso bien puesto de uno puesto donde no toca.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class FacturaCicloPermisosTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    [Fact]
    public async Task ElEmpleadoNoPuedeCerrarElCicloDeVidaDeUnaFactura()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuarioEmpleado = await AyudaAuth.CrearUsuarioAsync(
            bd, AyudaFacturas.Contrasena, rol: Roles.Empleado, ct: ct);

        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        using var empleado = await AyudaPermisos.SesionAsync(api, usuarioEmpleado, AyudaFacturas.Contrasena, ct);

        var clienteId = await AyudaFacturas.CrearClienteAsync(admin, ct);
        var serieUsd = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var serieProforma = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Proforma, null, ct);
        var serieFiscal = await AyudaComprobante.SerieFiscalAsync(admin, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        await AyudaComprobante.NumeradorAsync(admin, serieFiscal, hoy, ct);

        // Una factura emitida sin comprobante fiscal: para que el 409 de la modificación no se confunda
        // con el 403 del permiso, que es lo que esta prueba comprueba.
        var facturaId = await AyudaFacturas.BorradorConLineaAsync(admin, clienteId, serieUsd, Moneda.USD, ct);
        await admin.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);

        var proformaId = await AyudaFacturas.BorradorConLineaAsync(admin, clienteId, serieProforma, Moneda.USD, ct);
        await admin.PostAsync($"/api/v1/facturas/{proformaId:D}/emitir", new { }, ct);

        var detalle = await admin.GetAsync($"/api/v1/facturas/{facturaId:D}", ct);
        var lineaId = (await detalle.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("lineas")[0].GetProperty("id").GetGuid();

        var modificar = await empleado.PutAsync(
            $"/api/v1/facturas/{facturaId:D}/modificar",
            new
            {
                lineas = new[]
                {
                    new
                    {
                        lineaId,
                        tipo = nameof(TipoLineaFactura.Renta),
                        concepto = "No me dejan",
                        cantidad = 1m,
                        precioUnitario = 1m,
                    },
                },
            },
            ct);
        modificar.StatusCode.Should().Be(HttpStatusCode.Forbidden, await modificar.Cuerpo());

        var anular = await empleado.PostAsync(
            $"/api/v1/facturas/{facturaId:D}/anular", new { motivo = "No me dejan" }, ct);
        anular.StatusCode.Should().Be(HttpStatusCode.Forbidden, await anular.Cuerpo());

        var simular = await empleado.PostAsync($"/api/v1/facturas/{facturaId:D}/simular-anulacion", new { }, ct);
        simular.StatusCode.Should().Be(HttpStatusCode.Forbidden, "simular es parte de anular");

        var duplicar = await empleado.PostAsync($"/api/v1/facturas/{facturaId:D}/duplicar", new { }, ct);
        duplicar.StatusCode.Should().Be(HttpStatusCode.Forbidden, await duplicar.Cuerpo());

        var convertir = await empleado.PostAsync(
            $"/api/v1/facturas/{proformaId:D}/convertir-proforma", new { serieId = serieFiscal }, ct);
        convertir.StatusCode.Should().Be(HttpStatusCode.Forbidden, await convertir.Cuerpo());

        var rehacer = await empleado.PostAsync(
            $"/api/v1/facturas/{facturaId:D}/rehacer-moneda",
            new
            {
                moneda = nameof(Moneda.DOP),
                serieId = serieFiscal,
                total = 500m,
                motivo = "No me dejan",
            },
            ct);
        rehacer.StatusCode.Should().Be(HttpStatusCode.Forbidden, await rehacer.Cuerpo());

        // Un 403 no toca nada: la factura sigue emitida y sin modificar, y la proforma sin convertir.
        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var fila = await contexto.Factura.AsNoTracking().SingleAsync(f => f.Id == facturaId, ct);
        fila.Estado.Should().Be(EstadoFactura.Emitida);
        fila.ModificadaAt.Should().BeNull();
        fila.AnuladoAt.Should().BeNull();
        fila.Total.Should().Be(100m, "el precio de la línea original");

        (await contexto.Factura.AsNoTracking().SingleAsync(f => f.Id == proformaId, ct)).Estado
            .Should().Be(EstadoFactura.Emitida, "convertir es emitir: un 403 no convierte nada");

        (await contexto.Factura.AsNoTracking().CountAsync(f => f.ClienteId == clienteId, ct))
            .Should().Be(2, "un 403 no crea ninguna factura nueva, ni al duplicar ni al rehacer");

        // Y los seis intentos denegados quedan en la auditoría firmada con la acción exacta y la ruta que se
        // quiso tocar. El recurso lo pone el filtro global con método y plantilla de ruta.
        await using var sinRls = bd.CrearContexto(bd.CadenaSuperusuario);
        var denegadas = await sinRls.Auditoria.AsNoTracking()
            .Where(a => a.Accion == $"permiso.denegado.{Acciones.FacturaAnular}"
                && a.UsuarioId == usuarioEmpleado.Id)
            .ToListAsync(ct);

        denegadas.Should().HaveCount(6, "los seis intentos de esta prueba, de uno en uno");
        denegadas.Should().OnlyContain(a => a.Resultado == "denegado" && a.Ip != null && a.Origen == "api");
        denegadas.Should().OnlyContain(
            a => a.Datos!.RootElement.GetProperty("accion").GetString() == Acciones.FacturaAnular,
            "la denegación es de factura.anular y no de otro permiso");

        var recursos = denegadas
            .Select(a => a.Datos!.RootElement.GetProperty("recurso").GetString()!)
            .ToList();

        recursos.Should().Contain(r => r.Contains("modificar", StringComparison.Ordinal));
        recursos.Should().Contain(r => r.Contains("/anular", StringComparison.Ordinal));
        recursos.Should().Contain(r => r.Contains("simular-anulacion", StringComparison.Ordinal));
        recursos.Should().Contain(r => r.Contains("duplicar", StringComparison.Ordinal));
        recursos.Should().Contain(r => r.Contains("convertir-proforma", StringComparison.Ordinal));
        recursos.Should().Contain(r => r.Contains("rehacer-moneda", StringComparison.Ordinal));
    }

    /// <summary>
    /// La otra mitad: el gerente sí puede. <c>factura.anular</c> está en su techo y en el del
    /// administrador, así que un 403 aquí significaría que el permiso se puso en un sitio del que
    /// nadie lo puede usar.
    /// </summary>
    [Fact]
    public async Task ElGerenteSiPuedeAnularConFacturaAnular()
    {
        var ct = TestContext.Current.CancellationToken;
        using var gerente = await AyudaFacturas.SesionAsync(bd, api, Roles.Gerente, ct);

        // La serie y el numerador son configuracion.editar, que es de admin: se montan con una sesión
        // de admin y lo que hace el gerente es emitir y anular, que es lo que esta prueba comprueba.
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var clienteId = await AyudaFacturas.CrearClienteAsync(admin, ct);
        var serieId = await AyudaComprobante.SerieFiscalAsync(admin, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);
        await AyudaComprobante.NumeradorAsync(admin, serieId, hoy, ct);

        var facturaId = await AyudaFacturas.BorradorConLineaAsync(admin, clienteId, serieId, Moneda.USD, ct);

        // Y emite también con su cuenta: si no tuviera factura.emitir, este 200 ya fallaría antes.
        var emitida = await gerente.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);
        emitida.StatusCode.Should().Be(HttpStatusCode.OK, await emitida.Cuerpo());

        var anulada = await gerente.PostAsync(
            $"/api/v1/facturas/{facturaId:D}/anular",
            new { motivo = "Prueba de que el gerente puede" },
            ct);

        anulada.StatusCode.Should().Be(HttpStatusCode.OK, await anulada.Cuerpo());
        (await AyudaCiclo.FilaAsync(bd, facturaId, ct)).Estado.Should().Be(EstadoFactura.Anulada);

        // Duplicar una factura **anulada** sí se permite, y es un caso de uso real: se anula, se
        // copia corregida y la copia sale en borrador, sin número y sin comprobar —duplicar no
        // reutiliza nada del número anulado (D28)—. Lo que importa aquí es que el permiso no es lo
        // que lo impide: un 403 en este endpoint significaría que `factura.anular` está colgado
        // donde el gerente no llega.
        var duplicada = await gerente.PostAsync($"/api/v1/facturas/{facturaId:D}/duplicar", new { }, ct);
        duplicada.StatusCode.Should().Be(HttpStatusCode.OK, await duplicada.Cuerpo());

        var copia = (await duplicada.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("factura").GetProperty("id").GetGuid();
        var filaCopia = await AyudaCiclo.FilaAsync(bd, copia, ct);
        filaCopia.Estado.Should().Be(EstadoFactura.Borrador, "la copia nace en borrador");
        filaCopia.Numero.Should().BeNull("y sin número propio hasta que se emita");
    }
}