using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Mensajeria;

/// <summary>
/// F4-03: la confirmación y la cola (docs/ESPECIFICACION.md §3.14: «confirmación, cola, progreso»).
/// <para>
/// Lo que se comprueba es que <b>confirmar encola y no manda</b>, y que es el trabajo de la cola el
/// que manda. Un trabajo por documento —como dice §3.14— y no uno por lote, para que un corte a mitad
/// no se lleve los cientos que quedaban.
/// </para>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class EnvioMasivoColaTests(ContenedorBdFixture bd, ApiTestFactoryMasivo api)
    : IClassFixture<ApiTestFactoryMasivo>
{
    /// <summary>
    /// Confirmar no manda nada en el acto: escribe las filas <c>Pendiente</c>, encola un trabajo por
    /// envío y devuelve. Mandar es cosa del trabajo, y solo del trabajo.
    /// </summary>
    [Fact]
    public async Task ConfirmarEncolaYNoMandaHastaQueElTrabajoCorre()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, usuario) = await AyudaEnviosMasivo.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        api.Cola.Vaciar();
        var correosAntes = api.Doble.Enviados.Count;

        var loteId = await AyudaEnviosMasivo.ConfirmarAsync(
            admin, AyudaEnviosMasivo.Peticion(escenario.ClienteId), ct);

        api.Cola.Encolados.Should().HaveCount(1, "un trabajo por envío: uno por documento");
        api.Doble.Enviados.Should().HaveCount(correosAntes, "encolar no es mandar");

        var fila = (await AyudaEnviosMasivo.FilasDelLoteAsync(bd, loteId, ct)).Should().ContainSingle().Which;
        fila.Estado.Should().Be(EstadoEnvio.Pendiente, "al encolar el envío nace pendiente");
        fila.LoteId.Should().Be(loteId);
        fila.Intentos.Should().Be(0, "nadie ha intentado mandarlo todavía");
        fila.PdfCopia.Should().BeEmpty("mientras está pendiente no ha salido ningún byte");
        fila.Id.Should().Be(api.Cola.Encolados[0], "lo encolado es exactamente el envío que se escribió");

        var progreso = await AyudaEnviosMasivo.ProgresoAsync(admin, loteId, ct);
        progreso.GetProperty("total").GetInt32().Should().Be(1);
        progreso.GetProperty("pendientes").GetInt32().Should().Be(1);
        progreso.GetProperty("enviados").GetInt32().Should().Be(0);
        progreso.GetProperty("fallidos").GetInt32().Should().Be(0);
    }

    /// <summary>
    /// El trabajo manda <b>un</b> mensaje por documento, con su texto y su adjunto, y la fila queda
    /// <c>Enviado</c> con la copia exacta de lo que salió.
    /// </summary>
    [Fact]
    public async Task ElTrabajoEnviaUnMensajePorDocumento()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, usuario) = await AyudaEnviosMasivo.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(11), 400m, ct);

        api.Cola.Vaciar();
        var correosAntes = api.Doble.Enviados.Count;

        var loteId = await AyudaEnviosMasivo.ConfirmarAsync(
            admin, AyudaEnviosMasivo.Peticion(escenario.ClienteId), ct);

        api.Cola.Encolados.Should().HaveCount(2, "un trabajo por documento");

        await AyudaEnviosMasivo.EjecutarTrabajoAsync(api, api.Cola.Encolados[0], usuario, ct);

        api.Doble.Enviados.Should().HaveCount(correosAntes + 1, "un trabajo, un mensaje");

        var enviado = api.Doble.Enviados[^1];

        // Las filas se releen después del trabajo: leerlas antes daría el estado de antes, que es
        // justo lo que hay que comprobar que ha cambiado.
        var filas = await AyudaEnviosMasivo.FilasDelLoteAsync(bd, loteId, ct);
        var salida = filas.Should().ContainSingle(f => f.Id == api.Cola.Encolados[0]).Which;

        // Lo que sale por el correo es exactamente lo que la fila del lote traía del resumen.
        enviado.Asunto.Should().Be(salida.Asunto);
        enviado.Cuerpo.Should().Be(salida.Cuerpo);
        enviado.DestinatarioEmail.Should().Be(salida.DestinatarioEmail);
        enviado.Asunto.Should().StartWith("Factura ");
        enviado.NombreAdjunto.Should().EndWith(".pdf");
        AyudaEnviosMasivo.EsPdf(enviado.Adjunto).Should().BeTrue("lo adjunto es el PDF del documento");

        salida.Estado.Should().Be(EstadoEnvio.Enviado);
        salida.Intentos.Should().Be(1, "un intento, el que acaba de hacer el trabajo");
        salida.EnviadoAt.Should().NotBeNull();
        salida.UltimoError.Should().BeNull();
        salida.PdfCopia.Should().Equal(enviado.Adjunto, "lo guardado es lo que salió, byte a byte");

        // La otra sigue en la cola: un trabajo por documento es lo que evita perder el resto del lote.
        var pendiente = filas.Should().ContainSingle(f => f.Id == api.Cola.Encolados[1]).Which;

        pendiente.Estado.Should().Be(EstadoEnvio.Pendiente);
        pendiente.PdfCopia.Should().BeEmpty();

        var progreso = await AyudaEnviosMasivo.ProgresoAsync(admin, loteId, ct);
        progreso.GetProperty("enviados").GetInt32().Should().Be(1);
        progreso.GetProperty("pendientes").GetInt32().Should().Be(1);
    }

    /// <summary>
    /// El trabajo no vuelve a mandar una fila que ya salió. Es la puerta que hace idempotente la cola:
    /// si un trabajo se entrega dos veces, el segundo no manda el mismo correo otra vez.
    /// </summary>
    [Fact]
    public async Task ElTrabajoNoVuelveAMandarLoQueYaSalio()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, usuario) = await AyudaEnviosMasivo.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        api.Cola.Vaciar();
        var loteId = await AyudaEnviosMasivo.ConfirmarAsync(
            admin, AyudaEnviosMasivo.Peticion(escenario.ClienteId), ct);

        var envioId = api.Cola.Encolados[0];
        var correosAntes = api.Doble.Enviados.Count;

        await AyudaEnviosMasivo.EjecutarTrabajoAsync(api, envioId, usuario, ct);
        await AyudaEnviosMasivo.EjecutarTrabajoAsync(api, envioId, usuario, ct);

        api.Doble.Enviados.Should().HaveCount(correosAntes + 1, "la segunda entrega no manda nada");
        (await AyudaEnviosMasivo.FilasDelLoteAsync(bd, loteId, ct)).Should().ContainSingle()
            .Which.Intentos.Should().Be(1, "el segundo intento ni se cuenta: no lo hubo");
    }

    /// <summary>
    /// La cola se escribe después de confirmar, y el lote es el conjunto de las filas que comparten
    /// <c>lote_id</c>: no hay tabla de lotes que se pueda desincronizar de ellas.
    /// </summary>
    [Fact]
    public async Task ElLoteEsElConjuntoDeEnviosQueLoCompartenYQuedaAuditado()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, usuario) = await AyudaEnviosMasivo.SesionAsync(bd, api, Roles.Admin, ct);

        var escenario = await AyudaCuentas.EscenarioAsync(admin, bd, ct);
        await AyudaCuentas.EmitidaAsync(admin, escenario, escenario.Hoy.AddDays(10), 300m, ct);

        var loteId = await AyudaEnviosMasivo.ConfirmarAsync(
            admin, AyudaEnviosMasivo.Peticion(escenario.ClienteId), ct);

        // Dos lotes distintos no se mezclan, que es el fallo que tendría una tabla de lotes aparte.
        var otroLote = await AyudaEnviosMasivo.ConfirmarAsync(
            admin,
            AyudaEnviosMasivo.Peticion(escenario.ClienteId, forzar: true),
            ct);

        otroLote.Should().NotBe(loteId);

        (await AyudaEnviosMasivo.FilasDelLoteAsync(bd, loteId, ct)).Should().ContainSingle()
            .Which.LoteId.Should().Be(loteId, "el lote es el identificador que comparten sus filas");

        // Y quién pidió el envío, con qué filtros y cuándo lo confirmó, queda en la auditoría.
        await using var sinRls = bd.CrearContexto(bd.CadenaSuperusuario);
        var confirmaciones = await sinRls.Auditoria.AsNoTracking()
            .Where(a => a.Accion == "envio.masivo.confirmado" && a.EntidadId == loteId.ToString("D"))
            .ToListAsync(ct);

        var auditoria = confirmaciones.Should().ContainSingle().Which;
        auditoria.Entidad.Should().Be("lote");
        auditoria.UsuarioId.Should().Be(usuario);
        auditoria.Ip.Should().NotBeNullOrWhiteSpace("toda mutación audita su IP");
        auditoria.Resultado.Should().Be("permitido");
        auditoria.Datos.Should().NotBeNull();
        auditoria.Datos!.RootElement.GetProperty("documentos").GetInt32().Should().Be(1);
        auditoria.Datos.RootElement.GetProperty("filtro").GetProperty("ClienteId").GetGuid()
            .Should().Be(escenario.ClienteId, "los filtros con los que se confirmó quedan escritos");
    }
}