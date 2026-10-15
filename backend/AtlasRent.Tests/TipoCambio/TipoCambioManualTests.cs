using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.TipoCambio;

/// <summary>
/// F3-01: la carga manual por API y la regla de precedencia —un valor escrito por una persona no lo
/// pisa nunca la fuente— que es lo que hace que "carga manual" sea el camino de recuperación que
/// dice docs/ESPECIFICACION.md §3.12.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class TipoCambioManualTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// La carga manual queda con su fuente, con su autor y con su IP, y la pasada siguiente de la
    /// fuente no la toca.
    /// <para>
    /// Se va por la API de verdad y no por el servicio por dos razones: es lo que hace una persona
    /// (y hay que comprobar que queda auditado con la IP que pidió la mutación, no con la del
    /// servidor), y el criterio de aceptación habla de la carga manual como camino de recuperación,
    /// que es un camino de la interfaz, no una llamada interna.
    /// </para>
    /// </summary>
    [Fact]
    public async Task LaCargaManualQuedaAuditadaYLaFuenteNoLaPisa()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);
        var fecha = AyudaTipoCambio.Dia(10);
        Guid idFila;

        var respuesta = await sesion.PostAsync(
            "/api/v1/tipos-cambio",
            new { fecha = fecha.ToString("yyyy-MM-dd"), moneda = "DOP", valor = 62.5m },
            ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var contexto = AyudaTipoCambio.ContextoDelTrabajo(bd))
        {
            var fila = await contexto.TipoCambio.AsNoTracking()
                .SingleAsync(t => t.Moneda == Moneda.DOP && t.Fecha == fecha, ct);

            fila.Fuente.Should().Be(FuentesTipoCambio.Manual, "lo que escribe una persona es manual");
            fila.CreatedBy.Should().Be(admin.Id, "la fila dice quién lo cargó");

            idFila = fila.Id;
        }

        // Y la auditoría lleva la IP de quien hizo la petición, no la del servidor: sin eso no se
        // puede saber a quién preguntar por un valor cargado a deshora. Se lee con el rol de
        // superusuario, como el resto de pruebas que miran `auditoria` (tiene RLS).
        await using (var contexto = bd.CrearContexto(bd.CadenaSuperusuario))
        {
            var auditoria = await contexto.Auditoria.AsNoTracking()
                .SingleAsync(
                    a => a.Entidad == "tipo_cambio"
                        && a.EntidadId == idFila.ToString("D")
                        && a.Accion == "tipo_cambio.registrado",
                    ct);

            auditoria.Ip.Should().Be(api.IpCliente);
            auditoria.UsuarioId.Should().Be(admin.Id);
            auditoria.Origen.Should().Be("api");
        }

        // Ahora la fuente, que trae otro valor para ese mismo día. Su día de negocio es otro
        // distinto del de la carga manual: así se ve que la fuente carga el día que le toca y deja
        // en paz lo que hay en los demás.
        var reloj = AyudaTipoCambio.RelojDePrueba(21);
        var fuente = FuenteTipoCambioFalsa.ConValor(61.1m);

        DateOnly hoy;
        await using (var contexto = AyudaTipoCambio.ContextoDelTrabajo(bd))
        {
            var servicio = AyudaTipoCambio.ServicioDe(contexto, fuente, reloj);
            hoy = servicio.FechaDeHoy();

            var resumen = await servicio.ActualizarDesdeLaFuenteAsync(ct);

            resumen.Fallo.Should().BeFalse();
        }

        await using (var contexto = AyudaTipoCambio.ContextoDelTrabajo(bd))
        {
            var manual = await contexto.TipoCambio.AsNoTracking()
                .SingleAsync(t => t.Moneda == Moneda.DOP && t.Fecha == fecha, ct);

            manual.Valor.Should().Be(62.5m, "un valor cargado a mano no lo pisa la fuente");
            manual.Fuente.Should().Be(FuentesTipoCambio.Manual);

            // Y el trabajo sí ha cargado lo del día, que es lo suyo: lo que no hace es pisar lo
            // que alguien escribió a mano.
            (await contexto.TipoCambio.CountAsync(t => t.Moneda == Moneda.DOP && t.Fecha == hoy, ct))
                .Should().Be(1, "el trabajo carga el día y respeta lo manual");

            var hoyFila = await contexto.TipoCambio.AsNoTracking()
                .SingleAsync(t => t.Moneda == Moneda.DOP && t.Fecha == hoy, ct);
            hoyFila.Fuente.Should().Be(FuentesTipoCambio.Api);
        }
    }

    /// <summary>
    /// La carga manual del día que falló la fuente es el camino de recuperación, y tiene que poder
    /// escribirse en ese mismo día sin pelearse con el trabajo de las 05:00.
    /// </summary>
    [Fact]
    public async Task LaCargaManualDelDiaCorrigeLoQueLaFuenteNoTrajo()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        using var sesion = await AyudaPermisos.SesionAsync(api, admin, Contrasena, ct);

        // La fuente falla, como ayer: queda la alerta y no queda valor. El día de negocio es el de
        // esta prueba, adelantado en el reloj: la fecha se manda explícita en la carga manual, así
        // que hace falta el reloj solo para la clave de la alerta y para el trabajo.
        var reloj = AyudaTipoCambio.RelojDePrueba(22);
        var fuente = FuenteTipoCambioFalsa.ConValor(61m);
        fuente.Fallo = new FalloTipoCambioException("open.er-api.com no devolvió un JSON legible.");

        DateOnly hoy;
        await using (var contexto = AyudaTipoCambio.ContextoDelTrabajo(bd))
        {
            var servicio = AyudaTipoCambio.ServicioDe(contexto, fuente, reloj);
            hoy = servicio.FechaDeHoy();
            await AyudaTipoCambio.Trabajo(servicio).EjecutarAsync(ct);
        }

        // La persona carga el valor de ese mismo día.
        (await sesion.PostAsync(
            "/api/v1/tipos-cambio",
            new { fecha = hoy.ToString("yyyy-MM-dd"), moneda = "DOP", valor = 61.9m },
            ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var contexto = AyudaTipoCambio.ContextoDelTrabajo(bd))
        {
            var fila = await contexto.TipoCambio.AsNoTracking()
                .SingleAsync(t => t.Moneda == Moneda.DOP && t.Fecha == hoy, ct);

            fila.Valor.Should().Be(61.9m);
            fila.Fuente.Should().Be(FuentesTipoCambio.Manual);
        }

        // Y una pasada posterior de la fuente (o un reintento del trabajo) no lo cambia.
        var otra = FuenteTipoCambioFalsa.ConValor(62.2m);
        await using (var contexto = AyudaTipoCambio.ContextoDelTrabajo(bd))
        {
            await AyudaTipoCambio.ServicioDe(contexto, otra, reloj).ActualizarDesdeLaFuenteAsync(ct);
        }

        await using (var verificacion = AyudaTipoCambio.ContextoDelTrabajo(bd))
        {
            var fila = await verificacion.TipoCambio.AsNoTracking()
                .SingleAsync(t => t.Moneda == Moneda.DOP && t.Fecha == hoy, ct);

            fila.Valor.Should().Be(61.9m, "el reintento del trabajo tampoco puede pisar lo manual");
        }

        // Con el cambio ya cargado a mano, la alerta de ese día deja de ser cierta y se cierra.
        await using (var lectura = bd.CrearContexto(bd.CadenaSuperusuario))
        {
            var alerta = await lectura.Alerta.AsNoTracking()
                .SingleAsync(a => a.Clave == ServicioTiposCambio.ClaveFallo(hoy), ct);

            alerta.ResueltaAt.Should().NotBeNull(
                "un aviso que sobrevive a su problema entrena a quien mira a ignorar el panel");
        }
    }
}
