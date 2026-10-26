using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Usuarios, roles y permisos por usuario (docs/ESPECIFICACION.md §2, acción 1). Solo el
/// administrador: el filtro global ya lo ha comprobado antes de llegar aquí.
/// <para>
/// Las listas usan el envoltorio de <c>docs/DOC_TECNICA.md §6</c>.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/usuarios")]
public sealed class UsuariosController(
    ServicioUsuarios usuarios,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp) : ControllerBase
{
    [HttpGet]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> Listar(CancellationToken ct)
    {
        var lista = await usuarios.ListarAsync(ct);
        return Ok(new { data = lista, total = lista.Count, page = 1, pageSize = lista.Count, totalPages = 1 });
    }

    /// <summary>Alta de usuario con su rol. Los permisos "Permiso" llegan desactivados.</summary>
    [HttpPost]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> Crear([FromBody] UsuarioCrearPeticion peticion, CancellationToken ct)
    {
        try
        {
            var creado = await usuarios.CrearAsync(peticion, UsuarioDeLaPeticion(), Ip(), ct);
            return Ok(creado);
        }
        catch (ContrasenaRechazadaException rechazada)
        {
            return ContrasenaRechazada(rechazada);
        }
        catch (InvalidOperationException duplicado)
        {
            return Problemas.Construir(
                StatusCodes.Status409Conflict,
                Problemas.Validacion,
                "No se puede crear el usuario",
                duplicado.Message);
        }
    }

    /// <summary>Rol y permisos efectivos de un usuario: lo que el frontend necesita para los botones.</summary>
    [HttpGet("{id:guid}/permisos")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> Permisos(Guid id, CancellationToken ct)
    {
        try
        {
            return Ok(await usuarios.PermisosAsync(id, ct));
        }
        catch (KeyNotFoundException)
        {
            return NoEncontrado();
        }
    }

    /// <summary>Activa o quita un permiso dentro del techo del rol; por encima, se rechaza (D17).</summary>
    [HttpPut("{id:guid}/permisos")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> FijarPermiso(
        Guid id,
        [FromBody] UsuarioPermisoPeticion peticion,
        CancellationToken ct)
    {
        try
        {
            await usuarios.FijarPermisoAsync(id, peticion, UsuarioDeLaPeticion(), Ip(), ct);
            return NoContent();
        }
        catch (PermisoFueraDeTechoException fueraDeTecho)
        {
            return Problemas.Construir(
                StatusCodes.Status400BadRequest,
                Problemas.PermisoFueraDeTecho,
                "Permiso fuera del techo del rol",
                $"El rol '{fueraDeTecho.Rol}' no trae el permiso '{fueraDeTecho.Accion}': los permisos "
                + "de un usuario solo recortan los de su rol, nunca los amplían.");
        }
        catch (KeyNotFoundException)
        {
            return NoEncontrado();
        }
    }

    [HttpPut("{id:guid}/rol")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> CambiarRol(Guid id, [FromBody] UsuarioRolPeticion peticion, CancellationToken ct)
    {
        try
        {
            await usuarios.CambiarRolAsync(id, peticion.Rol, UsuarioDeLaPeticion(), Ip(), ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NoEncontrado();
        }
    }

    /// <summary>Activa o desactiva un usuario. Desactivar corta sus sesiones al instante (SEC-019).</summary>
    [HttpPut("{id:guid}/activo")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> FijarActivo(Guid id, [FromBody] UsuarioActivoPeticion peticion, CancellationToken ct)
    {
        try
        {
            await usuarios.FijarActivoAsync(id, peticion.Activo, UsuarioDeLaPeticion(), Ip(), ct);
            return NoContent();
        }
        catch (InvalidOperationException noPermitido)
        {
            return Problemas.Construir(
                StatusCodes.Status409Conflict,
                Problemas.Validacion,
                "No se puede desactivar el usuario",
                noPermitido.Message);
        }
        catch (KeyNotFoundException)
        {
            return NoEncontrado();
        }
    }

    /// <summary>Restablece la contraseña: el usuario deberá cambiarla y sus sesiones caen.</summary>
    [HttpPut("{id:guid}/password")]
    [Accion(Acciones.ConfiguracionEditar)]
    public async Task<IActionResult> RestablecerPassword(
        Guid id,
        [FromBody] UsuarioPasswordPeticion peticion,
        CancellationToken ct)
    {
        try
        {
            var comprobada = await usuarios.RestablecerPasswordAsync(
                id, peticion.Contrasena, UsuarioDeLaPeticion(), Ip(), ct);
            return Ok(new PasswordCambiadaRespuesta(comprobada));
        }
        catch (ContrasenaRechazadaException rechazada)
        {
            return ContrasenaRechazada(rechazada);
        }
        catch (KeyNotFoundException)
        {
            return NoEncontrado();
        }
    }

    internal static IActionResult ContrasenaRechazada(ContrasenaRechazadaException rechazada) =>
        Problemas.Construir(
            StatusCodes.Status400BadRequest,
            Problemas.ContrasenaRechazada,
            "Contraseña no válida",
            rechazada.Message);

    private Guid UsuarioDeLaPeticion() => contextoSesion.UsuarioId!.Value;

    private string Ip() => resolutorIp.Resolver(HttpContext);

    private IActionResult NoEncontrado() =>
        Problemas.Construir(
            StatusCodes.Status404NotFound,
            Problemas.RecursoNoEncontrado,
            "Usuario no encontrado",
            "Ese usuario no existe o no tiene rol asignado.");
}