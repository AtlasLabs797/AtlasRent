using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// F6-01: tokens de agente y las consultas de solo lectura que el asistente hace con ellos.
/// <para>
/// Dos superficies distintas en la misma ruta. Los <c>tokens</c> se administran con sesión normal
/// de administrador, como cualquier otro dato de configuración. Las consultas de lectura son
/// endpoints de la API como los demás: se pueden llamar con token de agente o con una sesión
/// corriente, y en los dos casos el <c>[Accion]</c> decide.
/// </para>
/// <para>
/// Las rutas de lectura exigen <c>ia.consultar</c> además de la acción del dato, y ninguna declara
/// una acción de totales: el empleado no recibe datos agregados por aquí ni por ningún otro sitio.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/agente")]
public sealed class AgenteController(
    ServicioAgentes agentes,
    ServicioClientes clientes,
    ServicioConsultaIa consultas,
    ServicioIdempotencia idempotencia,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp)
    : ControladorApiBase(idempotencia, contextoSesion, resolutorIp)
{
    /// <summary>
    /// Emite un token y devuelve el secreto. Es la única vez que sale: ni la base de datos ni el
    /// listado lo pueden volver a mostrar.
    /// </summary>
    [HttpPost("tokens")]
    [Accion(Acciones.AgenteToken)]
    public async Task<IActionResult> Emitir(AgenteTokenPeticion peticion, CancellationToken ct)
    {
        try
        {
            var (fila, secreto) = await agentes.CrearAsync(
                peticion.Nombre,
                peticion.UsuarioId,
                peticion.Acciones,
                peticion.DiasValidez,
                peticion.LimiteConsultasDia,
                UsuarioDeLaPeticion(),
                Ip(),
                ct);

            return Ok(AgenteTokenRespuesta.De(fila, secreto));
        }
        catch (ArgumentException invalida)
        {
            return Problemas.Construir(
                StatusCodes.Status400BadRequest,
                Problemas.Validacion,
                "Datos no válidos",
                invalida.Message);
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
    }

    /// <summary>Listado de tokens. Sin secretos, porque no hay ninguno guardado en claro.</summary>
    [HttpGet("tokens")]
    [Accion(Acciones.AgenteToken)]
    public async Task<IActionResult> Listar(CancellationToken ct)
    {
        var filas = await agentes.ListarAsync(ct);
        return Ok(new { data = filas.Select(fila => AgenteTokenRespuesta.De(fila)) });
    }

    /// <summary>Revoca un token. La fila se queda: la auditoría necesita poder explicar qué hubo.</summary>
    [HttpPost("tokens/{id:guid}/revocar")]
    [Accion(Acciones.AgenteToken)]
    public async Task<IActionResult> Revocar(Guid id, CancellationToken ct)
    {
        try
        {
            await agentes.RevocarAsync(id, UsuarioDeLaPeticion(), Ip(), ct);
            return NoContent();
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
    }

    /// <summary>
    /// Consulta de clientes para el asistente. Reutiliza el mismo servicio que la API de clientes:
    /// ni una consulta nueva ni un DTO nuevo que mantener en dos sitios.
    /// </summary>
    [HttpGet("clientes")]
    [Accion(Acciones.IaConsultar, Acciones.ClienteVer)]
    public async Task<IActionResult> Clientes(
        [FromQuery] string? buscar,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct) =>
        await EnvoltorioAsync(clientes.ListarAsync(
            Paginacion(page, pageSize, null, null), buscar, null, null, false, ct));

    /// <summary>Ficha de un cliente para el asistente.</summary>
    [HttpGet("clientes/{id:guid}")]
    [Accion(Acciones.IaConsultar, Acciones.ClienteVer)]
    public async Task<IActionResult> Cliente(Guid id, CancellationToken ct)
    {
        try
        {
            return Ok(await clientes.LeerAsync(id, ct));
        }
        catch (KeyNotFoundException noExiste)
        {
            return NoEncontrado(noExiste.Message);
        }
    }

    /// <summary>
    /// Consulta al asistente con token de agente (F6-02). <b>Mismo servicio, mismo contrato y mismo
    /// código de error que <c>POST /api/v1/ia/consulta</c></b>: lo único que cambia es la
    /// credencial.
    /// <para>
    /// Lo que un agente no puede es responder más que su propietario. Aquí se junta el doble techo de
    /// F6-01 (<c>ia.consultar</c> en la lista del token y en el techo del rol) con el del alcance
    /// (las acciones del endpoint de lectura equivalente), y con el límite diario de
    /// <c>agente_token.limite_consultas_dia</c> que F6-01 guardaba sin aplicar.
    /// </para>
    /// </summary>
    [HttpPost("consulta")]
    [Accion(Acciones.IaConsultar)]
    public async Task<IActionResult> Consultar(ConsultaIaPeticion peticion, CancellationToken ct)
    {
        try
        {
            return Ok(await consultas.ConsultarAsync(peticion.Pregunta, peticion.Alcance, Ip(), ct));
        }
        catch (ArgumentException invalida)
        {
            return Problemas.Construir(
                StatusCodes.Status400BadRequest,
                Problemas.Validacion,
                "Datos no válidos",
                invalida.Message);
        }
        catch (PermisoDenegadoException)
        {
            // La excepción la lanza el servicio; la denegación ya está auditada, también con origen
            // «agente». Es el mismo camino que siguen el resto de servicios que comprueban permisos
            // por su cuenta.
            return Problemas.Construir(
                StatusCodes.Status403Forbidden,
                Problemas.PermisoDenegado,
                "Permiso denegado",
                "Este token no tiene permiso para consultar este alcance.");
        }
        catch (LimiteIaExcedidoException pasado)
        {
            return Problemas.Construir(
                StatusCodes.Status429TooManyRequests,
                Problemas.LimiteConsultasIa,
                "Límite de consultas alcanzado",
                pasado.Message);
        }
        catch (ProveedorIaException noDisponible)
        {
            return Problemas.Construir(
                StatusCodes.Status503ServiceUnavailable,
                Problemas.ProveedorIaNoDisponible,
                "Asistente no disponible",
                noDisponible.Message);
        }
    }
}