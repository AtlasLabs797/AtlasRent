using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.DTOs;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Alta de usuarios, cambio de rol y permisos por usuario (docs/ESPECIFICACION.md §2, acción 1).
/// <para>
/// El permiso por usuario se guarda solo cuando difiere del techo del rol, y nunca por encima del
/// techo: eso se comprueba aquí, al guardar, no al usarlo (D17).
/// </para>
/// </summary>
public sealed class ServicioUsuarios(
    AtlasRentDbContext contexto,
    ServicioContrasenas contrasenas,
    ValidadorContrasena validador,
    ServicioSesiones sesiones,
    ServicioBloqueos bloqueos,
    IPolicyService politica,
    AuditoriaService auditoria,
    TimeProvider reloj)
{
    public async Task<IReadOnlyList<UsuarioResumen>> ListarAsync(CancellationToken ct)
    {
        var usuarios = await contexto.Usuario
            .AsNoTracking()
            .Where(u => u.DeletedAt == null)
            .OrderBy(u => u.NombreAcceso)
            .Select(u => new
            {
                u.Id,
                u.NombreAcceso,
                u.Nombre,
                u.Email,
                u.Activo,
                Rol = u.UsuarioRol!.RolClave,
            })
            .ToListAsync(ct);

        return [.. usuarios.Select(u => new UsuarioResumen(
            u.Id, u.NombreAcceso, u.Nombre, u.Email, u.Activo, u.Rol))];
    }

    public async Task<UsuarioPermisosRespuesta> PermisosAsync(Guid usuarioId, CancellationToken ct)
    {
        var rol = await politica.RolDeAsync(usuarioId, ct)
            ?? throw new KeyNotFoundException("El usuario no tiene rol.");

        var permisos = await politica.PermisosDeAsync(usuarioId, ct);

        return new UsuarioPermisosRespuesta(usuarioId, rol, [.. permisos.Order(StringComparer.Ordinal)]);
    }

    public async Task<UsuarioCreadoRespuesta> CrearAsync(
        UsuarioCrearPeticion peticion,
        Guid usuarioAdmin,
        string ip,
        CancellationToken ct)
    {
        if (!MatrizPermisos.PorRol.ContainsKey(peticion.Rol))
        {
            throw new KeyNotFoundException($"No existe el rol '{peticion.Rol}'.");
        }

        var nombreAcceso = Usuario.NormalizarNombreAcceso(peticion.NombreAcceso);

        // Antes de la transacción: es una llamada de red y no debe tenerla abierta.
        var comprobada = await validador.ValidarAsync(peticion.Contrasena, ct);

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        if (await contexto.Usuario.AsNoTracking().AnyAsync(u => u.NombreAcceso == nombreAcceso, ct))
        {
            throw new InvalidOperationException($"El nombre de acceso '{nombreAcceso}' ya existe.");
        }

        var ahora = reloj.GetUtcNow();
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            NombreAcceso = nombreAcceso,
            Nombre = peticion.Nombre.Trim(),
            Email = peticion.Email,
            PasswordHash = contrasenas.Hashar(peticion.Contrasena),
            Activo = true,
            // La contraseña la elige el administrador: no nace pendiente de cambio.
            CreatedAt = ahora,
            CreatedBy = usuarioAdmin,
        };

        contexto.Usuario.Add(usuario);
        contexto.UsuarioRol.Add(new UsuarioRol
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            RolClave = peticion.Rol,
            CreatedAt = ahora,
            CreatedBy = usuarioAdmin,
        });

        // "Enviar cotizaciones" (y cualquier otra acción marcada como "Permiso" en la
        // especificación) llega desactivada: está en el techo del rol, pero no se concede sola.
        foreach (var accion in MatrizPermisos.DesactivadasAlCrearUsuario)
        {
            if (!MatrizPermisos.EnTecho(peticion.Rol, accion))
            {
                continue;
            }

            contexto.UsuarioPermiso.Add(new UsuarioPermiso
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuario.Id,
                Accion = accion,
                Permitido = false,
                CreatedAt = ahora,
                CreatedBy = usuarioAdmin,
            });
        }

        await contexto.SaveChangesAsync(ct);

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = "usuario.creado",
            Ip = ip,
            UsuarioId = usuarioAdmin,
            Entidad = "usuario",
            EntidadId = usuario.Id.ToString("D"),
            // Ni la contraseña ni el hash: solo el rol con el que nace.
            DatosJson = DatosAuditoria(comprobada, ("rol", peticion.Rol)),
            Resultado = "permitido",
            Origen = "api",
        }, ct);

        await transaccion.CommitAsync(ct);

        return new UsuarioCreadoRespuesta(usuario.Id, usuario.NombreAcceso, peticion.Rol, comprobada);
    }

    /// <summary>
    /// Activa o quita un permiso de un usuario. Activar algo fuera del techo de su rol se rechaza
    /// aquí; quitar siempre vale.
    /// </summary>
    public async Task FijarPermisoAsync(
        Guid usuarioId,
        UsuarioPermisoPeticion peticion,
        Guid usuarioAdmin,
        string ip,
        CancellationToken ct)
    {
        if (!Acciones.Todas.Contains(peticion.Accion))
        {
            throw new KeyNotFoundException($"No existe la acción '{peticion.Accion}'.");
        }

        await politica.ExigirDentroDelTechoAsync(usuarioId, peticion.Accion, peticion.Permitido, ct);

        // SEC-025: a un administrador no se le quitan las salvaguardas de seguridad (ni a otro ni a
        // uno mismo): un admin comprometido o malicioso se las quitaría a los demás para suplantarlos.
        if (!peticion.Permitido
            && peticion.Accion is Acciones.MfaObligatorio or Acciones.SeguridadAccesoEmergencia
            && await EsAdminAsync(usuarioId, ct))
        {
            throw new ReglaDeAdministradorException(
                $"A un administrador no se le puede quitar el permiso '{peticion.Accion}'.");
        }

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        var fila = await contexto.UsuarioPermiso
            .SingleOrDefaultAsync(p => p.UsuarioId == usuarioId && p.Accion == peticion.Accion, ct);

        var ahora = reloj.GetUtcNow();
        if (fila is null)
        {
            fila = new UsuarioPermiso
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuarioId,
                Accion = peticion.Accion,
                CreatedAt = ahora,
                CreatedBy = usuarioAdmin,
            };
            contexto.UsuarioPermiso.Add(fila);
        }

        fila.Permitido = peticion.Permitido;
        fila.UpdatedAt = ahora;
        fila.UpdatedBy = usuarioAdmin;
        await contexto.SaveChangesAsync(ct);

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = "usuario.permiso",
            Ip = ip,
            UsuarioId = usuarioAdmin,
            Entidad = "usuario",
            EntidadId = usuarioId.ToString("D"),
            DatosJson = System.Text.Json.JsonSerializer.Serialize(new { accion = peticion.Accion, permitido = peticion.Permitido }),
            Resultado = "permitido",
            Origen = "api",
        }, ct);

        await transaccion.CommitAsync(ct);
    }

    /// <summary>
    /// Cambia el rol del usuario. Se guarda en <c>usuario_rol</c> y a partir de la siguiente
    /// petición ya manda el techo del nuevo; los permisos por usuario que se queden fuera de ese
    /// techo dejan de contar (no se amplían nunca).
    /// </summary>
    public async Task CambiarRolAsync(
        Guid usuarioId,
        string rol,
        Guid usuarioAdmin,
        string ip,
        CancellationToken ct)
    {
        if (!MatrizPermisos.PorRol.ContainsKey(rol))
        {
            throw new KeyNotFoundException($"No existe el rol '{rol}'.");
        }

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        var fila = await contexto.UsuarioRol.SingleOrDefaultAsync(r => r.UsuarioId == usuarioId, ct);
        if (fila is null)
        {
            throw new KeyNotFoundException("El usuario no tiene rol.");
        }

        // SEC-025: no se puede quitar el rol de administrador al último administrador activo.
        if (fila.RolClave == Roles.Admin && rol != Roles.Admin)
        {
            var otrosAdminsActivos = await contexto.UsuarioRol.CountAsync(
                r => r.RolClave == Roles.Admin
                    && r.UsuarioId != usuarioId
                    && contexto.Usuario.Any(u => u.Id == r.UsuarioId && u.Activo && u.DeletedAt == null),
                ct);
            if (otrosAdminsActivos == 0)
            {
                throw new ReglaDeAdministradorException(
                    "No se puede quitar el rol de administrador al último administrador activo.");
            }
        }

        fila.RolClave = rol;
        fila.UpdatedAt = reloj.GetUtcNow();
        fila.UpdatedBy = usuarioAdmin;
        await contexto.SaveChangesAsync(ct);

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = "usuario.rol",
            Ip = ip,
            UsuarioId = usuarioAdmin,
            Entidad = "usuario",
            EntidadId = usuarioId.ToString("D"),
            DatosJson = System.Text.Json.JsonSerializer.Serialize(new { rol }),
            Resultado = "permitido",
            Origen = "api",
        }, ct);

        await transaccion.CommitAsync(ct);
    }

    /// <summary>
    /// JSON de auditoría de una operación con contraseña. Si HaveIBeenPwned no pudo comprobarla,
    /// lo deja anotado (<c>hibp: no_disponible</c>). Nunca lleva la contraseña.
    /// </summary>
    private static string DatosAuditoria(bool comprobada, params (string Clave, object Valor)[] datos)
    {
        var diccionario = datos.ToDictionary(d => d.Clave, d => d.Valor);
        if (!comprobada)
        {
            diccionario["hibp"] = "no_disponible";
        }

        return JsonSerializer.Serialize(diccionario);
    }

    /// <summary>
    /// Activa o desactiva a un usuario. Al desactivar, revoca todas sus sesiones en la misma
    /// transacción y vacía su caché (SEC-019): la siguiente petición suya da 401 sin esperar a los
    /// 30 s de la caché. Un administrador no puede desactivarse a sí mismo.
    /// </summary>
    public async Task FijarActivoAsync(
        Guid usuarioId,
        bool activo,
        Guid usuarioAdmin,
        string ip,
        CancellationToken ct)
    {
        if (!activo && usuarioId == usuarioAdmin)
        {
            throw new InvalidOperationException("No puedes desactivar tu propia cuenta.");
        }

        IReadOnlyList<Guid> revocadas = [];
        await using (var transaccion = await contexto.Database.BeginTransactionAsync(ct))
        {
            var usuario = await contexto.Usuario.SingleOrDefaultAsync(
                u => u.Id == usuarioId && u.DeletedAt == null, ct)
                ?? throw new KeyNotFoundException("El usuario no existe.");

            usuario.Activo = activo;
            usuario.UpdatedAt = reloj.GetUtcNow();
            usuario.UpdatedBy = usuarioAdmin;
            await contexto.SaveChangesAsync(ct);

            if (!activo)
            {
                revocadas = await sesiones.RevocarTodasDelUsuarioAsync(
                    usuarioId, MotivosRevocacion.UsuarioDesactivado, excepto: null, ct);
            }

            await auditoria.RegistrarAsync(new RegistroAuditoria
            {
                Accion = activo ? "usuario.activado" : "usuario.desactivado",
                Ip = ip,
                UsuarioId = usuarioAdmin,
                Entidad = "usuario",
                EntidadId = usuarioId.ToString("D"),
                DatosJson = DatosAuditoria(true, ("sesiones_revocadas", revocadas.Count)),
                Resultado = "permitido",
                Origen = "api",
            }, ct);

            await transaccion.CommitAsync(ct);
        }

        sesiones.InvalidarCache(revocadas);
    }

    /// <summary>
    /// Restablece la contraseña de otro usuario: el usuario queda obligado a cambiarla y todas sus
    /// sesiones caen. Devuelve si HaveIBeenPwned pudo comprobarla.
    /// </summary>
    public async Task<bool> RestablecerPasswordAsync(
        Guid usuarioId,
        string contrasena,
        Guid usuarioAdmin,
        string ip,
        CancellationToken ct)
    {
        var comprobada = await validador.ValidarAsync(contrasena, ct);

        IReadOnlyList<Guid> revocadas;
        await using (var transaccion = await contexto.Database.BeginTransactionAsync(ct))
        {
            var usuario = await contexto.Usuario.SingleOrDefaultAsync(
                u => u.Id == usuarioId && u.DeletedAt == null, ct)
                ?? throw new KeyNotFoundException("El usuario no existe.");

            usuario.PasswordHash = contrasenas.Hashar(contrasena);
            usuario.DebeCambiarPassword = true;
            usuario.UpdatedAt = reloj.GetUtcNow();
            usuario.UpdatedBy = usuarioAdmin;
            await contexto.SaveChangesAsync(ct);

            revocadas = await sesiones.RevocarTodasDelUsuarioAsync(
                usuarioId, MotivosRevocacion.PasswordCambiada, excepto: null, ct);

            await auditoria.RegistrarAsync(new RegistroAuditoria
            {
                Accion = "usuario.password_restablecida",
                Ip = ip,
                UsuarioId = usuarioAdmin,
                Entidad = "usuario",
                EntidadId = usuarioId.ToString("D"),
                DatosJson = DatosAuditoria(comprobada, ("sesiones_revocadas", revocadas.Count)),
                Resultado = "permitido",
                Origen = "api",
            }, ct);

            await transaccion.CommitAsync(ct);
        }

        sesiones.InvalidarCache(revocadas);
        return comprobada;
    }

    /// <summary>
    /// Cambio de la contraseña propia. Exige la actual (un fallo cuenta como login fallido para el
    /// bloqueo de cuenta e IP), aplica las reglas de <see cref="ValidadorContrasena"/>, quita el
    /// cambio pendiente y revoca las demás sesiones del usuario; la actual sigue viva.
    /// </summary>
    public async Task<bool> CambiarPasswordPropiaAsync(
        Guid usuarioId,
        Guid sesionActual,
        string contrasenaActual,
        string contrasenaNueva,
        string ip,
        CancellationToken ct)
    {
        var existente = await contexto.Usuario.AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == usuarioId && u.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException("El usuario no existe.");

        if (!contrasenas.Verificar(contrasenaActual, existente.PasswordHash))
        {
            await bloqueos.RegistrarIntentoFallidoAsync(usuarioId, existente.NombreAcceso, ip, ct);
            throw new ContrasenaRechazadaException("La contraseña actual no es correcta.");
        }

        var comprobada = await validador.ValidarAsync(contrasenaNueva, ct);

        IReadOnlyList<Guid> revocadas;
        await using (var transaccion = await contexto.Database.BeginTransactionAsync(ct))
        {
            var usuario = await contexto.Usuario.SingleAsync(u => u.Id == usuarioId, ct);
            usuario.PasswordHash = contrasenas.Hashar(contrasenaNueva);
            usuario.DebeCambiarPassword = false;
            usuario.UpdatedAt = reloj.GetUtcNow();
            usuario.UpdatedBy = usuarioId;
            await contexto.SaveChangesAsync(ct);

            revocadas = await sesiones.RevocarTodasDelUsuarioAsync(
                usuarioId, MotivosRevocacion.PasswordCambiada, excepto: sesionActual, ct);

            await auditoria.RegistrarAsync(new RegistroAuditoria
            {
                Accion = "usuario.password_cambiada",
                Ip = ip,
                UsuarioId = usuarioId,
                Entidad = "usuario",
                EntidadId = usuarioId.ToString("D"),
                DatosJson = DatosAuditoria(comprobada, ("sesiones_revocadas", revocadas.Count)),
                Resultado = "permitido",
                Origen = "api",
            }, ct);

            await transaccion.CommitAsync(ct);
        }

        sesiones.InvalidarCache(revocadas);
        // La sesión actual sigue viva, pero su estado en caché lleva el cambio pendiente.
        sesiones.InvalidarCache(sesionActual);
        return comprobada;
    }

    private Task<bool> EsAdminAsync(Guid usuarioId, CancellationToken ct) =>
        contexto.UsuarioRol.AnyAsync(r => r.UsuarioId == usuarioId && r.RolClave == Roles.Admin, ct);
}
