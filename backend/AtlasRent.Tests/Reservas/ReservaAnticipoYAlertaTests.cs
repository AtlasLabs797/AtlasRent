using System.Data;
using System.Globalization;
using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Jobs;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Cuentas;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Informes;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Reservas;

/// <summary>
/// F2-03: el anticipo de la reserva y el aviso de los 7 días.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ReservaAnticipoYAlertaTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// D10: el anticipo es una nota con número. No hay pasarela de pago, así que una reserva con
    /// anticipo no puede haber generado un cobro ni una factura: ni los escribe la aplicación ni
    /// hay ningún camino de reserva a esas tablas.
    /// <para>
    /// Lo que se comprueba es doble. Primero, que el anticipo sí está guardado en la reserva, con su
    /// importe y su nota. Segundo, que <b>ninguna tabla de efecto económico que exista</b> haya
    /// cambiado al crearla, y se busca por nombre con un <c>LIKE</c> para que la lista crezca sola
    /// cuando F3 cree las suyas, sin que este test haya que reescribirse.
    /// </para>
    /// <para>
    /// Los recuentos son antes y después, no absolutos, porque la base de pruebas la comparten todas
    /// las clases de la colección y otras han dejado filas en ellas. Lo que importa no es que la
    /// tabla esté a cero, sino que crear la reserva no la haya movido.
    /// </para>
    /// <para>
    /// Este test mira las tablas (<c>cobro</c> y <c>factura</c> ya existen desde F3). Que el anticipo
    /// tampoco aparezca en saldos, cuentas ni informes lo prueba
    /// <see cref="ElAnticipoNoApareceEnSaldosCuentasNiInformes"/>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ElAnticipoNoEsUnCobro()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        var fecha = AyudaReserva.Dia(140);

        await using var contexto = bd.CrearContexto(bd.CadenaApp);

        var economicas = await TablasDeDineroAsync(contexto, ct);
        var antes = await ContarAsync(contexto, economicas, ct);

        var id = await AyudaReserva.CrearAsync(
            sesion,
            AyudaReserva.Corta(
                clienteId,
                unidadId,
                fecha,
                anticipo: 2500m,
                anticipoNota: "Se acordado en efectivo al reservar; si se cancela antes del día anterior se devuelve"),
            ct);

        // El anticipo sí está guardado, con su importe y su nota: es una nota, no un cobro, y por eso
        // vive en la reserva y no en otro sitio.
        var reserva = await AyudaReserva.Json(
            await sesion.GetAsync($"/api/v1/reservas/{id:D}", ct), ct);
        reserva.GetProperty("anticipoImporte").GetDecimal().Should().Be(2500m);
        reserva.GetProperty("anticipoNota").GetString().Should().Contain("devuelve");

        var despues = await ContarAsync(contexto, economicas, ct);
        foreach (var tabla in economicas)
        {
            despues[tabla].Should().Be(
                antes[tabla],
                $"un anticipo no escribe nada en {tabla}: el cobro se registra al pagar la factura, "
                + "que es de F3");
        }
    }

    /// <summary>
    /// El criterio de aceptación completo de F2-03, ahora que existen cobro, factura, vista única de
    /// cuentas e informes: un anticipo anotado en una reserva <b>no aparece</b> en el saldo del
    /// cliente, ni en la vista única de cuentas, ni en el resumen y el dashboard de informes, ni como
    /// cobro.
    /// <para>
    /// Se mira por dos lados. Con el cliente recién creado (sin más movimientos), nada de lo que las
    /// tres vistas devuelven puede llevar el importe del anticipo. Y el resumen global es idéntico
    /// antes y después de crear la reserva con anticipo.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ElAnticipoNoApareceEnSaldosCuentasNiInformes()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);
        const decimal anticipo = 2500m;

        var resumenAntes = (await AyudaInformes.ResumenAsync(sesion, string.Empty, ct)).GetRawText();

        await AyudaReserva.CrearAsync(
            sesion,
            AyudaReserva.Corta(clienteId, unidadId, AyudaReserva.Dia(150), anticipo: anticipo, anticipoNota: "Anticipo en efectivo"),
            ct);

        var filtro = $"clienteId={clienteId:D}";

        var vista = await AyudaCuentas.VistaAsync(sesion, filtro, ct);
        AyudaCuentas.Filas(vista).Should().BeEmpty("el anticipo no es ni factura ni cobro: no es una fila de la vista única");
        NumerosDe(vista).Should().NotContain(anticipo, "ni las filas ni los totales de la vista única lo suman");

        var cobros = await sesion.GetAsync($"/api/v1/cobros?{filtro}", ct);
        cobros.StatusCode.Should().Be(HttpStatusCode.OK);
        NumerosDe(await AyudaReserva.Json(cobros, ct)).Should().NotContain(anticipo, "el anticipo no es un cobro");

        NumerosDe(await AyudaInformes.ResumenAsync(sesion, filtro, ct))
            .Should().NotContain(anticipo, "el resumen de informes no lo cuenta como ingreso ni como saldo");
        NumerosDe(await AyudaInformes.DashboardAsync(sesion, filtro, ct))
            .Should().NotContain(anticipo, "el dashboard tampoco");

        (await AyudaInformes.ResumenAsync(sesion, string.Empty, ct)).GetRawText()
            .Should().Be(resumenAntes, "crear una reserva con anticipo no mueve ningún informe económico");
    }

    private static List<decimal> NumerosDe(System.Text.Json.JsonElement elemento)
    {
        var numeros = new List<decimal>();
        void Recorrer(System.Text.Json.JsonElement e)
        {
            switch (e.ValueKind)
            {
                case System.Text.Json.JsonValueKind.Number:
                    numeros.Add(e.GetDecimal());
                    break;
                case System.Text.Json.JsonValueKind.Array:
                    foreach (var hijo in e.EnumerateArray())
                    {
                        Recorrer(hijo);
                    }

                    break;
                case System.Text.Json.JsonValueKind.Object:
                    foreach (var propiedad in e.EnumerateObject())
                    {
                        Recorrer(propiedad.Value);
                    }

                    break;
            }
        }

        Recorrer(elemento);
        return numeros;
    }

    /// <summary>
    /// Las tablas donde un movimiento de dinero dejaría rastro, buscadas por prefijo sobre
    /// <c>information_schema</c>. Hoy devuelve las de F2 que ya guardan importes
    /// (<c>contrato_liquidacion</c>, <c>cotizacion</c>, <c>cotizacion_linea</c>) y nada más; cuando
    /// F3 cree <c>cobro</c>, <c>factura</c>, <c>asiento</c> o lo que haga falta, entrarán solas.
    /// <para>
    /// Es una búsqueda por nombre y, por lo tanto, heurística: si F3 le pone a una tabla un nombre
    /// que no case con ninguno de estos patrones, este test no la mirará. Si eso llega a pasar, lo
    /// que hay que hacer es añadir el patrón, no escribir un test nuevo.
    /// </para>
    /// </summary>
    private static async Task<List<string>> TablasDeDineroAsync(
        AtlasRentDbContext contexto,
        CancellationToken ct) =>
        await contexto.Database
            .SqlQueryRaw<string>(
                """
                SELECT table_name AS "Nombre"
                FROM information_schema.tables
                WHERE table_schema = 'public'
                  AND table_type = 'BASE TABLE'
                  AND (
                      table_name LIKE 'cobro%'
                      OR table_name LIKE 'factura%'
                      OR table_name LIKE 'asiento%'
                      OR table_name LIKE 'pago%'
                      OR table_name LIKE 'cotizacion%'
                      OR table_name LIKE '%liquidacion'
                  )
                ORDER BY table_name
                """)
            .ToListAsync(ct);

    private static async Task<Dictionary<string, long>> ContarAsync(
        AtlasRentDbContext contexto,
        List<string> tablas,
        CancellationToken ct)
    {
        var cuentas = new Dictionary<string, long>();

        // Por ADO.NET y no por EF: un identificador de tabla no se puede pasar como parámetro, y EF
        // (EF1002) no deja ni `SqlQueryRaw` con interpolación ni `FromSqlRaw` sobre un escalar.
        // La conexión se deja como estaba.
        var conexion = contexto.Database.GetDbConnection();
        var yaEstabaAbierta = conexion.State == ConnectionState.Open;
        if (!yaEstabaAbierta)
        {
            await conexion.OpenAsync(ct);
        }

        try
        {
            foreach (var tabla in tablas)
            {
                // El nombre va metido en el texto porque no hay otra forma, y sale de
                // information_schema de este propio esquema: antes de usarlo se comprueba que sea un
                // identificador normal, así que no hay por dónde colarse nada en la consulta.
                tabla.Should().MatchRegex("^[a-z_][a-z0-9_]*$", $"'{tabla}' es un nombre de tabla");

                await using var comando = conexion.CreateCommand();
                comando.CommandText = $"""SELECT count(*) FROM "{tabla}" """;
                var total = await comando.ExecuteScalarAsync(ct);

                cuentas[tabla] = Convert.ToInt64(total, CultureInfo.InvariantCulture);
            }
        }
        finally
        {
            if (!yaEstabaAbierta)
            {
                await conexion.CloseAsync();
            }
        }

        return cuentas;
    }

    /// <summary>
    /// §3.4: la reserva mensual que lleva 7 días sin convertirse avisa, una vez y de forma
    /// persistente (BUG-028). A los 6 días todavía no; a los 7 sí; el trabajo repetido no duplica y
    /// un día más tarde (el trabajo no corrió) sigue avisando.
    /// </summary>
    [Fact]
    public async Task LaReservaMensualSinConvertirAvisaALosSieteDiasYNoSeDuplica()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var hoy = TiempoNegocio.Hoy();

        var id = await AyudaReserva.CrearAsync(
            sesion, AyudaReserva.Mensual(clienteId, await AyudaReserva.UnidadAsync(sesion, ct), hoy.AddDays(60)), ct);
        var clave = TrabajoAlertaReserva.Clave(id);

        await EjecutarAsync(TimeSpan.FromDays(6), ct);
        (await AlertasDeAsync(clave, ct)).Should().BeEmpty("a los 6 días todavía no avisa");

        await EjecutarAsync(TimeSpan.FromDays(7), ct);
        await EjecutarAsync(TimeSpan.FromDays(8), ct);
        await EjecutarAsync(TimeSpan.FromDays(9), ct);

        var alertas = await AlertasDeAsync(clave, ct);
        alertas.Should().ContainSingle("tres ejecuciones son una alerta, no tres");
        alertas[0].Tipo.Should().Be("reserva");
        alertas[0].ResueltaAt.Should().BeNull();
        alertas[0].Repeticiones.Should().Be(3);
    }

    /// <summary>La reserva cancelada cierra el aviso que tuviera abierto.</summary>
    [Fact]
    public async Task UnaReservaCanceladaCierraElAviso()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sesion = await SesionAsync(ct);
        var clienteId = await AyudaReserva.ClienteAsync(sesion, ct);
        var unidadId = await AyudaReserva.UnidadAsync(sesion, ct);

        var id = await AyudaReserva.CrearAsync(
            sesion, AyudaReserva.Mensual(clienteId, unidadId, TiempoNegocio.Hoy().AddDays(60)), ct);
        var clave = TrabajoAlertaReserva.Clave(id);

        await EjecutarAsync(TimeSpan.FromDays(7), ct);
        (await AlertasDeAsync(clave, ct)).Should().ContainSingle().Which.ResueltaAt.Should().BeNull();

        (await sesion.DeleteAsync($"/api/v1/reservas/{id:D}", ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await EjecutarAsync(TimeSpan.FromDays(8), ct);

        (await AlertasDeAsync(clave, ct)).Should().ContainSingle()
            .Which.ResueltaAt.Should().NotBeNull("cancelada ya no hay nada que convertir");
    }

    private async Task EjecutarAsync(TimeSpan avance, CancellationToken ct)
    {
        var reloj = new RelojPrueba();
        reloj.Avanzar(avance);
        await using var contexto = bd.CrearContexto(bd.CadenaApp, new ContextoSesion());
        await new TrabajoAlertaReserva(contexto, new ServicioAlertas(contexto, reloj), reloj)
            .EjecutarAsync(ct);
    }

    private async Task<List<Alerta>> AlertasDeAsync(string clave, CancellationToken ct)
    {
        await using var contexto = bd.CrearContexto(bd.CadenaApp, new ContextoSesion());
        return await contexto.Alerta.AsNoTracking().Where(a => a.Clave == clave).ToListAsync(ct);
    }

    private async Task<SesionCliente> SesionAsync(CancellationToken ct)
    {
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        return await AyudaPermisos.SesionAsync(api, usuario, Contrasena, ct);
    }
}