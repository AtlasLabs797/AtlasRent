using System.Globalization;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Jobs;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AtlasRent.Api.Services;

/// <summary>
/// Cola del correo de aviso de IP nueva (SEC-023). Va detrás de una interfaz por el mismo motivo
/// que <see cref="IColaEnvios"/>: en las pruebas no hay servidor de jobs y lo que hay que comprobar
/// es qué se encoló.
/// </summary>
public interface IColaAvisosAcceso
{
    /// <summary>Encola el correo a los administradores. No bloquea ni hace fallar el login.</summary>
    void EncolarIpNueva(Guid usuarioId, string ip, DateTimeOffset cuando);
}

/// <summary>Sin servidor de jobs no hay cola: queda la alerta y se omite el correo.</summary>
public sealed class ColaAvisosAccesoSinServidor : IColaAvisosAcceso
{
    public void EncolarIpNueva(Guid usuarioId, string ip, DateTimeOffset cuando)
    {
    }
}

/// <inheritdoc />
public sealed class ColaAvisosAccesoHangfire(IBackgroundJobClient cola) : IColaAvisosAcceso
{
    public void EncolarIpNueva(Guid usuarioId, string ip, DateTimeOffset cuando) =>
        cola.Enqueue<TrabajoAvisoIpNueva>(j => j.EjecutarAsync(usuarioId, ip, cuando, default(CancellationToken)));
}

/// <summary>
/// Aviso al administrador de un inicio de sesión desde una IP que el usuario no había usado
/// (docs/ESPECIFICACION.md §7, SEC-023).
/// <para>
/// Dos canales. La alerta de la tabla <c>alerta</c> es el fiable: se escribe en el acto, con clave
/// <c>ip-nueva:{usuario}:{ip}</c> para que la misma pareja no genere dos. El correo es un extra: se
/// encola solo si el servidor de correo está configurado y sale desde la cola, para que un SMTP
/// caído no pueda tirar el login.
/// </para>
/// <para>
/// El mensaje lleva usuario, IP y fecha y nada más: ni nombre completo, ni correo, ni el agente de
/// navegador del usuario.
/// </para>
/// </summary>
public sealed class ServicioAvisosAcceso(
    AtlasRentDbContext contexto,
    IRegistroAlertas alertas,
    IColaAvisosAcceso cola,
    IServicioCorreo correo,
    OpcionesCorreo opcionesCorreo,
    TimeProvider reloj)
{
    public const string TipoAlerta = "seguridad";

    public static string ClaveIpNueva(Guid usuarioId, string ip) => $"ip-nueva:{usuarioId:D}:{ip}";

    /// <summary>Deja la alerta y, si hay correo configurado, encola el aviso por correo.</summary>
    public async Task AvisarIpNuevaAsync(Guid usuarioId, string nombreAcceso, string ip, CancellationToken ct)
    {
        var cuando = reloj.GetUtcNow();

        await alertas.RegistrarAsync(
            TipoAlerta,
            ClaveIpNueva(usuarioId, ip),
            $"Inicio de sesión de «{nombreAcceso}» desde una IP nueva ({ip}).",
            ct);

        if (opcionesCorreo.Configurado)
        {
            cola.EncolarIpNueva(usuarioId, ip, cuando);
        }
    }

    /// <summary>
    /// Manda el correo a cada administrador activo con correo. Un fallo con uno no impide el resto
    /// ni reintenta el trabajo (repetiría el aviso a los que ya lo recibieron); la alerta ya está.
    /// </summary>
    public async Task EnviarIpNuevaAsync(Guid usuarioId, string ip, DateTimeOffset cuando, CancellationToken ct)
    {
        var nombreAcceso = await contexto.Usuario.AsNoTracking()
            .Where(u => u.Id == usuarioId)
            .Select(u => u.NombreAcceso)
            .SingleOrDefaultAsync(ct);
        if (nombreAcceso is null)
        {
            return;
        }

        var destinatarios = await contexto.UsuarioRol.AsNoTracking()
            .Where(r => r.RolClave == Roles.Admin
                && r.Usuario!.Activo
                && r.Usuario.DeletedAt == null
                && r.Usuario.Email != null)
            .Select(r => r.Usuario!.Email!)
            .Distinct()
            .ToListAsync(ct);

        var fecha = cuando.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
        var cuerpo = $"Se ha iniciado sesión con el usuario {nombreAcceso} desde una IP nueva.\n\n"
            + $"IP: {ip}\nFecha: {fecha}\n\n"
            + "Si no lo esperabas, cierra sus sesiones y cambia su contraseña desde Atlas Rent.";

        foreach (var email in destinatarios)
        {
            try
            {
                await correo.EnviarAsync(
                    new MensajeCorreo(email, null, "Atlas Rent: inicio de sesión desde una IP nueva", cuerpo, null, null),
                    ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Log.Warning("No se pudo enviar el aviso de IP nueva a un administrador: {TipoExcepcion}", e.GetType().Name);
            }
        }
    }
}
