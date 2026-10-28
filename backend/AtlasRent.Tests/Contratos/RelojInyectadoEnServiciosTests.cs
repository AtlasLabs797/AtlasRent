using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Contratos;

/// <summary>
/// BUG-052: <see cref="ServicioContratos"/> tiene <c>TimeProvider</c> inyectado y no puede leer el
/// reloj del sistema por debajo. Con un reloj adelantado 400 días, las fechas que escribe tienen que
/// ser las de ese reloj y no las de hoy de verdad. Va por el servicio y no por HTTP porque el JWT se
/// valida contra el reloj del sistema y una sesión con el reloj de la API adelantado no entra.
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class RelojInyectadoEnServiciosTests(ContenedorBdFixture bd, ApiTestFactory api)
    : IClassFixture<ApiTestFactory>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    [Fact]
    public async Task ElBorradorYLaSolicitudUsanElRelojInyectado()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojPrueba();
        reloj.Avanzar(TimeSpan.FromDays(400));
        var ahora = reloj.GetUtcNow();
        var hoy = TiempoNegocio.FechaEnZonaNegocio(ahora);

        using var empleado = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct), Contrasena, ct);
        var gerenteUsuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct);
        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);

        var (servicio, contexto) = ServicioMontadoPara(gerenteUsuario.Id, Roles.Gerente, reloj);
        await using var _ = contexto;

        var creado = await servicio.CrearAsync(
            new ContratoPeticion
            {
                ClienteId = clienteId,
                UnidadId = unidadId,
                FacturadoraId = facturadoraId,
                FechaInicio = hoy.AddDays(30),
                DuracionInicialMeses = 12,
                Renta = 50_000m,
                Moneda = Moneda.DOP,
                DiaPago = 5,
                Fianza = 20_000m,
                ReglaRenovacion = ReglaRenovacion.Anual,
                RenovacionMeses = 12,
                PlantillaContratoId = "estandar",
            },
            gerenteUsuario.Id,
            "10.0.0.1",
            null,
            ct);

        await using (var lectura = bd.CrearContexto(bd.CadenaApp))
        {
            (await lectura.Contrato.AsNoTracking().Where(c => c.Id == creado.Id)
                .Select(c => c.EstadoActualDesde).SingleAsync(ct))
                .Should().Be(hoy, "el borrador nace con el día del reloj inyectado, no con el de hoy de verdad");
        }

        await servicio.EnviarAprobacionAsync(
            creado.Id, new ContratoEnvioPeticion(), gerenteUsuario.Id, "10.0.0.1", null, ct);
        await servicio.AprobarAsync(creado.Id, gerenteUsuario.Id, "10.0.0.1", null, ct);

        await using (var lectura = bd.CrearContexto(bd.CadenaApp))
        {
            var solicitud = await lectura.SolicitudAprobacion.AsNoTracking()
                .SingleAsync(s => s.ContratoId == creado.Id, ct);
            solicitud.ResueltoAt.Should().BeCloseTo(ahora, TimeSpan.FromHours(1));
            solicitud.UpdatedAt.Should().BeCloseTo(ahora, TimeSpan.FromHours(1));
        }
    }

    private (ServicioContratos Servicio, AtlasRentDbContext Contexto) ServicioMontadoPara(
        Guid usuario,
        string rol,
        TimeProvider reloj)
    {
        var sesion = new ContextoSesion { UsuarioId = usuario, Rol = rol };
        var contexto = bd.CrearContexto(bd.CadenaApp, sesion);
        var auditoria = new AuditoriaService(contexto);
        var idempotencia = new ServicioIdempotencia(contexto, reloj);
        var disponibilidad = new ServicioDisponibilidad(contexto, reloj);
        var politica = new PolicyService(
            contexto, sesion, auditoria, new ResolutorIpPeticion(), new HttpContextAccessor());

        return (
            new ServicioContratos(
                contexto,
                auditoria,
                idempotencia,
                disponibilidad,
                new ServicioReservas(
                    contexto, auditoria, idempotencia, disponibilidad, new ServicioAlertas(contexto, reloj), reloj),
                new ServicioPlantillas(
                    contexto,
                    auditoria,
                    idempotencia,
                    new ServicioCifrado(new OpcionesSeguridad
                    {
                        ClaveJwt = new byte[64],
                        ClaveCifrado = ApiTestFactory.ClaveCifrado,
                        ClaveHmacDocumento = api.ClaveHmacDocumento,
                    }),
                    reloj),
                new ServicioInventario(contexto, auditoria, idempotencia, politica, reloj),
                new RegistroAlertasNulo(),
                politica,
                reloj),
            contexto);
    }
}
