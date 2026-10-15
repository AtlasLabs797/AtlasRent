using System.Net;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AtlasRent.Tests.Reservas;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.Contratos;

/// <summary>
/// F2-05: quién puede aprobar un contrato.
/// <list type="bullet">
/// <item>
/// El criterio de aceptación del plan: el empleado no puede aprobar ni por API. Recibe 403.
/// </item>
/// <item>
/// Y tampoco por servicio, que es la mitad difícil: si el atributo del endpoint fuera lo único que
/// protege la regla, bastaría un endpoint mal cableado, un trabajo o una llamada directa para
/// saltárselo.
/// </item>
/// <item>
/// Gerente y administrador sí, que son los dos con techo para <c>contrato.estado</c> en la matriz
/// de docs/ESPECIFICACION.md §2.
/// </item>
/// </list>
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class ContratoAprobacionTests(ContenedorBdFixture bd, ApiTestFactoryBloqueos api)
    : IClassFixture<ApiTestFactoryBloqueos>
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// El criterio de aceptación del plan, tal cual: el empleado llama al endpoint de aprobación y
    /// recibe 403. Ni 200 ni 400: el filtro global lo corta antes de que el servicio mire nada.
    /// </summary>
    [Fact]
    public async Task ElEmpleadoNoPuedeAprobarPorApi()
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct), Contrasena, ct);
        using var gerente = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct), Contrasena, ct);

        var contratoId = await PendienteDeAprobacionAsync(empleado, gerente, ct);

        var respuesta = await AyudaContrato.Boton(empleado, contratoId, "aprobacion", ct: ct);

        respuesta.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "contrato.estado no le viene al empleado en la matriz de la especificación §2");

        // Y el contrato sigue esperando, no se aprobó por el camino de dentro.
        (await AyudaContrato.EstadoAsync(bd, contratoId, ct)).ToString()
            .Should().Be("PendienteAprobacion");
    }

    /// <summary>
    /// La regla no vive solo en el atributo. Aquí se llama al servicio <b>sin pasar por la API</b>:
    /// no hay filtro global, no hay atributo y no hay petición. Si el servicio approve sin mirar quién
    /// pregunta, este test lo caza; si el contrato no cambia y salta la excepción de permiso, la
    /// regla se sostiene donde tiene que sostenerse.
    /// </summary>
    [Fact]
    public async Task ElEmpleadoTampocoAprobarPorServicio()
    {
        var ct = TestContext.Current.CancellationToken;
        var empleadoUsuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct);
        using var empleado = await AyudaPermisos.SesionAsync(api, empleadoUsuario, Contrasena, ct);
        using var gerente = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Gerente, ct: ct), Contrasena, ct);

        var contratoId = await PendienteDeAprobacionAsync(empleado, gerente, ct);

        // El servicio montado a mano, con la sesión del empleado, como si alguien lo llamara por
        // dentro saltándose el filtro global.
        var (servicio, contexto) = ServicioMontadoPara(ct, empleadoUsuario.Id, Roles.Empleado);
        try
        {
            var excepcion = await Assert.ThrowsAsync<PermisoDenegadoException>(
                () => servicio.AprobarAsync(contratoId, empleadoUsuario.Id, "198.51.100.1", null, ct));

            excepcion.Accion.Should().Be(Acciones.ContratoEstado);
        }
        finally
        {
            await contexto.DisposeAsync();
        }

        // Lo que importa: el contrato no se aprobó y la denegación quedó auditada.
        (await AyudaContrato.EstadoAsync(bd, contratoId, ct)).ToString()
            .Should().Be("PendienteAprobacion", "un empleado no aprueba ni aunque se salte la API");

        // Con el superusuario porque "auditoria" tiene RLS y solo gerente y administrador la leen:
        // con el rol de la aplicación, esta misma cuenta daría cero y el test pasaría sin comprobar
        // nada.
        await using var verificacion = bd.CrearContexto(bd.CadenaSuperusuario);
        var denegaciones = await verificacion.Auditoria.AsNoTracking()
            .CountAsync(a => a.Accion == $"permiso.denegado.{Acciones.ContratoEstado}", ct);
        denegaciones.Should().BeGreaterThan(0, "un intento no permitido se audita (docs/ESPECIFICACION.md §2)");
    }

    /// <summary>Los dos roles con techo para <c>contrato.estado</c> aprueban.</summary>
    [Theory]
    [InlineData(Roles.Gerente)]
    [InlineData(Roles.Admin)]
    public async Task ElGerenteONAdministradorAprueban(string rol)
    {
        var ct = TestContext.Current.CancellationToken;
        using var empleado = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Empleado, ct: ct), Contrasena, ct);
        using var aprobador = await AyudaPermisos.SesionAsync(
            api, await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: rol, ct: ct), Contrasena, ct);

        var contratoId = await PendienteDeAprobacionAsync(empleado, aprobador, ct);

        await AyudaContrato.AceptarAsync(aprobador, contratoId, "aprobacion", null, ct);

        (await AyudaContrato.EstadoAsync(bd, contratoId, ct)).ToString().Should().Be("Aprobado");
    }

    /// <summary>
    /// Un contrato ya pendiente de aprobación, creado por el empleado y enviado. Devuelve el
    /// identificador.
    /// </summary>
    private async Task<Guid> PendienteDeAprobacionAsync(
        SesionCliente empleado,
        SesionCliente aprobador,
        CancellationToken ct)
    {
        var clienteId = await AyudaReserva.ClienteAsync(empleado, ct);
        var unidadId = await AyudaReserva.UnidadAsync(empleado, ct);
        var facturadoraId = await AyudaContrato.SubempresaAsync(bd, ct);

        var id = await AyudaContrato.CrearAsync(
            empleado,
            AyudaContrato.Peticion(clienteId, unidadId, facturadoraId, AyudaContrato.Dia(40 + Desplazamiento())),
            ct);

        await AyudaContrato.AceptarAsync(empleado, id, "solicitud", new { }, ct);

        return id;
    }

    /// <summary>Días distintos por contrato, para que dos pruebas no compartan unidad.</summary>
    private static int Desplazamiento() => Random.Shared.Next(1, 400);

    /// <summary>
    /// El servicio de contratos montado a mano con la sesión de quien llama. Es lo que permite
    /// probar la regla sin pasar por HTTP, que es justo lo que hay que probar.
    /// </summary>
    private (ServicioContratos Servicio, AtlasRentDbContext Contexto) ServicioMontadoPara(
        CancellationToken ct,
        Guid usuario,
        string rol)
    {
        var sesion = new ContextoSesion { UsuarioId = usuario, Rol = rol };
        var contexto = bd.CrearContexto(bd.CadenaApp, sesion);
        var auditoria = new AuditoriaService(contexto);
        var idempotencia = new ServicioIdempotencia(contexto, api.Reloj);
        var disponibilidad = new ServicioDisponibilidad(contexto, api.Reloj);
        var politica = new PolicyService(
            contexto, sesion, auditoria, new ResolutorIpPeticion(), new HttpContextAccessor());

        return (
            new ServicioContratos(
                contexto,
                auditoria,
                idempotencia,
                disponibilidad,
                new ServicioReservas(contexto, auditoria, idempotencia, disponibilidad, api.Reloj),
                // F2-07: la aprobación congela la plantilla, y congelarla necesita el catálogo y el
                // cifrado (para el PDF, que no se prueba aquí).
                new ServicioPlantillas(
                    contexto,
                    auditoria,
                    idempotencia,
                    new ServicioCifrado(new OpcionesSeguridad
                    {
                        ClaveJwt = new byte[64],
                        ClaveCifrado = api.ClaveCifrado,
                        ClaveHmacDocumento = api.ClaveHmacDocumento,
                    }),
                    api.Reloj),
                // F2-08: la activación congela el inventario de la unidad al contrato, dentro de la
                // misma transacción que el cambio de estado.
                new ServicioInventario(contexto, auditoria, idempotencia, politica, api.Reloj),
                politica,
                api.Reloj),
            contexto);
    }
}