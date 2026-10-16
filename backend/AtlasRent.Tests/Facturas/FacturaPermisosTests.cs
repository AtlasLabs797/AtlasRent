using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Facturas;

/// <summary>
/// F3-02: los permisos de la factura. El empleado tiene <c>factura.ver</c> y <c>factura.borrador</c>
/// pero no <c>factura.emitir</c>: puede preparar una factura y no puede emitirla (D15, §2).
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class FacturaPermisosTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    [Fact]
    public async Task ElEmpleadoPreparaLaFacturaYNoLaEmite()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        using var empleado = await AyudaFacturas.SesionAsync(bd, api, Roles.Empleado, ct);

        var clienteId = await AyudaFacturas.CrearClienteAsync(admin, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var hoy = await AyudaFacturas.HoyAsync(admin, ct);

        var creada = await empleado.PostAsync(
            "/api/v1/facturas",
            new
            {
                clienteId,
                serieId,
                moneda = "USD",
                fechaVencimiento = hoy.AddDays(30).ToString("yyyy-MM-dd"),
                concepto = "Borrador del empleado",
            },
            ct);

        creada.StatusCode.Should().Be(HttpStatusCode.OK, "el empleado puede dejar la factura en borrador");
        var facturaId = (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();

        var conLinea = await empleado.PostAsync(
            $"/api/v1/facturas/{facturaId:D}/lineas",
            new { tipo = "Renta", concepto = "Renta", cantidad = 1m, precioUnitario = 100m },
            ct);
        conLinea.StatusCode.Should().Be(HttpStatusCode.OK);

        var leida = await empleado.GetAsync($"/api/v1/facturas/{facturaId:D}", ct);
        leida.StatusCode.Should().Be(HttpStatusCode.OK, "el empleado ve facturas individuales");

        var emitida = await empleado.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct);

        emitida.StatusCode.Should().Be(HttpStatusCode.Forbidden, "D15: el empleado no emite");

        await using var contexto = bd.CrearContexto(bd.CadenaApp);
        var fila = await contexto.Factura.AsNoTracking().SingleAsync(f => f.Id == facturaId, ct);
        fila.Estado.Should().Be(EstadoFactura.Borrador);
        fila.Numero.Should().BeNull("el intento denegado no gastó el número");

        (await contexto.Serie.AsNoTracking().SingleAsync(s => s.Id == serieId, ct)).Siguiente
            .Should().Be(1);
    }

    /// <summary>
    /// La denegación queda en la auditoría firmada, con su IP: es lo que permite después ver quién
    /// intentó emitir sin poder.
    /// </summary>
    [Fact]
    public async Task ElIntentoDeEmitirSinPermisoQuedaAuditado()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuarioEmpleado = await AyudaAuth.CrearUsuarioAsync(bd, AyudaFacturas.Contrasena, rol: Roles.Empleado, ct: ct);
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);
        using var empleado = await AyudaPermisos.SesionAsync(api, usuarioEmpleado, AyudaFacturas.Contrasena, ct);

        var clienteId = await AyudaFacturas.CrearClienteAsync(admin, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var facturaId = await AyudaFacturas.BorradorConLineaAsync(empleado, clienteId, serieId, Moneda.USD, ct);

        (await empleado.PostAsync($"/api/v1/facturas/{facturaId:D}/emitir", new { }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // La denegación se audita con la acción completa en el nombre y con "permiso" como entidad.
        // El recurso lo pone el filtro global de acciones (método y plantilla de la ruta), y se lee
        // por la conexión de superusuario porque `auditoria` tiene RLS y solo la leen gerente y
        // administrador (MigracionInicial). Se filtra por usuario porque la base es compartida y
        // otros intentos denegados de la misma acción están ahí también.
        await using var contexto = bd.CrearContexto(bd.CadenaSuperusuario);
        var denegadas = await contexto.Auditoria.AsNoTracking()
            .Where(a => a.Accion == $"permiso.denegado.{Acciones.FacturaEmitir}"
                && a.UsuarioId == usuarioEmpleado.Id)
            .ToListAsync(ct);

        var denegada = denegadas.Should().ContainSingle(
            "el intento denegado de este usuario queda en la auditoría").Which;

        denegada.Ip.Should().NotBeNullOrWhiteSpace();
        denegada.Resultado.Should().Be("denegado");
        denegada.Origen.Should().Be("api");
        denegada.Datos!.RootElement.GetProperty("recurso").GetString().Should().Contain("emitir");
        denegada.Datos.RootElement.GetProperty("rol").GetString().Should().Be(Roles.Empleado);
    }

    /// <summary>
    /// El permiso también se comprueba en el servicio, no solo en el endpoint, porque F3-05 emite
    /// desde un trabajo de Hangfire sin sesión HTTP ni filtro. Esta prueba lo comprueba por el camino
    /// que tiene el trabajo: una llamada directa al servicio con el usuario del empleado.
    /// </summary>
    [Fact]
    public async Task ElServicioTambienExigeElPermisoDeEmitir()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await AyudaFacturas.SesionAsync(bd, api, Roles.Admin, ct);

        var clienteId = await AyudaFacturas.CrearClienteAsync(admin, ct);
        var serieId = await AyudaFacturas.CrearSerieAsync(admin, TipoSerie.Normal, Moneda.USD, ct);
        var facturaId = await AyudaFacturas.BorradorConLineaAsync(admin, clienteId, serieId, Moneda.USD, ct);

        var empleado = await AyudaAuth.CrearUsuarioAsync(bd, AyudaFacturas.Contrasena, rol: Roles.Empleado, ct: ct);

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var auditoria = new AuditoriaService(contexto);
            var idempotencia = new ServicioIdempotencia(contexto, api.Reloj);
            var tiposCambio = ConstruirTiposCambio(contexto, auditoria, idempotencia, api.Reloj);
            var servicio = new ServicioFacturas(
                contexto,
                auditoria,
                idempotencia,
                new ServicioSeries(contexto, auditoria, api.Reloj),
                // F3-03: numerador del comprobante fiscal. Esta serie no lo genera, así que aquí solo
                // hace falta que exista.
                new ServicioComprobantes(contexto, api.Reloj),
                tiposCambio,
                // F3-06: ServicioFacturas lo usa para resolver cobrado, pendiente y estado de cobro
                // de la factura en la lectura. En esta prueba solo hace falta que exista.
                ConstruirCobros(contexto, auditoria, idempotencia, tiposCambio, api.Reloj),
                new PoliticaSinEmitir(),
                api.Reloj);

            var actua = async () => await servicio.EmitirAsync(facturaId, empleado.Id, "10.0.0.1", null, ct);

            await actua.Should().ThrowAsync<PermisoDenegadoException>()
                .WithMessage("*factura.emitir*");
        }

        await using (var contexto = bd.CrearContexto(bd.CadenaApp))
        {
            var fila = await contexto.Factura.AsNoTracking().SingleAsync(f => f.Id == facturaId, ct);
            fila.Estado.Should().Be(EstadoFactura.Borrador);
            fila.Numero.Should().BeNull();
        }
    }

    /// <summary>
    /// <see cref="ServicioTiposCambio"/> con una fuente que no se usa: esta prueba va por USD, que
    /// vale 1 sin consultar la base (F3-01). La fuente solo entra en el trabajo diario.
    /// </summary>
    private static ServicioTiposCambio ConstruirTiposCambio(
        AtlasRent.Api.Data.AtlasRentDbContext contexto,
        AuditoriaService auditoria,
        ServicioIdempotencia idempotencia,
        TimeProvider reloj) =>
        new(
            contexto,
            auditoria,
            idempotencia,
            new ServicioAlertas(contexto, reloj),
            new FuenteQueNoSeUsa(),
            reloj);

    /// <summary>
    /// <c>ServicioCobros</c> (F3-06) con la fuente de tipos de cambio que ya se ha construido: aquí
    /// solo se usa para resolver el estado de cobro de una factura, no para mover dinero.
    /// <para>
    /// F3-07: recibe el mismo <c>ServicioConfiguracion</c> que en producción, de donde sale la
    /// tolerancia del ajuste por diferencia de cambio. Esta prueba no ajusta nada, así que no le afecta.
    /// </para>
    /// </summary>
    private static ServicioCobros ConstruirCobros(
        AtlasRent.Api.Data.AtlasRentDbContext contexto,
        AuditoriaService auditoria,
        ServicioIdempotencia idempotencia,
        ServicioTiposCambio tiposCambio,
        TimeProvider reloj) =>
        new(
            contexto,
            auditoria,
            idempotencia,
            tiposCambio,
            new ServicioConfiguracion(contexto, auditoria, idempotencia, reloj),
            new PoliticaSinEmitir(),
            reloj);

    private sealed class FuenteQueNoSeUsa : IFuenteTipoCambio
    {
        public Task<decimal> ObtenerAsync(Moneda moneda, CancellationToken ct) =>
            throw new NotSupportedException(
                "Esta prueba va por USD, que vale 1 sin consultar la fuente (F3-01).");
    }

    /// <summary>
    /// Política que solo deniega <c>factura.emitir</c>. Lo que se comprueba aquí no es el techo de
    /// permisos del empleado —eso lo prueba por la API real el primer test— sino que el servicio
    /// consulta el permiso antes de emitir y no se apoya solo en el filtro del controlador.
    /// </summary>
    private sealed class PoliticaSinEmitir : IPolicyService
    {
        private readonly HashSet<string> _denegadas = [Acciones.FacturaEmitir];

        public IReadOnlySet<string> Permisos { get; } = new HashSet<string>();

        public Task CargarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public bool Puede(string accion) => !_denegadas.Contains(accion);

        public void Exigir(string accion, string? recurso = null) =>
            throw new PermisoDenegadoException(accion, recurso);

        public Task<bool> PuedeAsync(string accion, Guid usuarioId, CancellationToken ct = default) =>
            Task.FromResult(!_denegadas.Contains(accion));

        public Task<IReadOnlySet<string>> PermisosDeAsync(Guid usuarioId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task<string?> RolDeAsync(Guid usuarioId, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task ExigirDentroDelTechoAsync(
            Guid usuarioId,
            string accion,
            bool activar,
            CancellationToken ct = default) => Task.CompletedTask;

        public Task AuditarDenegadoAsync(string accion, string? recurso, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
