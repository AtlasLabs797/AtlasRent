using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtlasRent.Api.Controllers;

/// <summary>
/// Desbloqueo de IP y de cuenta (docs/ESPECIFICACION.md §2, acción 19). Solo el administrador: es la
/// única vía para levantar un bloqueo que por definición no caduca solo.
/// <para>
/// El permiso lo comprueba el filtro global; aquí se traduce el "no había nada que desbloquear" a un
/// 404. El bloqueo en sí, con su auditoría, es de <see cref="ServicioBloqueos"/>.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/seguridad/bloqueos")]
public sealed class BloqueosController(
    ServicioBloqueos bloqueos,
    ContextoSesion contextoSesion,
    IResolutorIpPeticion resolutorIp) : ControllerBase
{
    [HttpPost("ip/desbloquear")]
    [Accion(Acciones.IpDesbloquear)]
    public async Task<IActionResult> DesbloquearIp([FromBody] IpDesbloquearPeticion peticion, CancellationToken ct)
    {
        var ip = peticion.Ip.Trim();
        var desbloqueada = await bloqueos.DesbloquearIpAsync(ip, contextoSesion.UsuarioId!.Value, ct);

        return desbloqueada
            ? Ok(new { ip, desbloqueada = true })
            : Problemas.Construir(
                StatusCodes.Status404NotFound,
                Problemas.RecursoNoEncontrado,
                "IP no bloqueada",
                "Esa IP no tiene un bloqueo que levantar.");
    }

    [HttpPost("cuenta/desbloquear")]
    [Accion(Acciones.CuentaDesbloquear)]
    public async Task<IActionResult> DesbloquearCuenta(
        [FromBody] CuentaDesbloquearPeticion peticion,
        CancellationToken ct)
    {
        var desbloqueada = await bloqueos.DesbloquearCuentaAsync(
            peticion.UsuarioId, contextoSesion.UsuarioId!.Value, Ip(), ct);

        return desbloqueada
            ? Ok(new { usuarioId = peticion.UsuarioId, desbloqueada = true })
            : Problemas.Construir(
                StatusCodes.Status404NotFound,
                Problemas.RecursoNoEncontrado,
                "Usuario no encontrado",
                "Ese usuario no existe.");
    }

    private string Ip() => resolutorIp.Resolver(HttpContext);
}