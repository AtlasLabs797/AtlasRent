using System.Text.Json;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AtlasRent.Api.Services;

/// <summary>Token de agente resuelto: quién es y qué puede pedir.</summary>
public sealed record AgenteResuelto(Guid TokenId, Guid UsuarioId, IReadOnlySet<string> Acciones);

/// <summary>
/// F6-01: alta, revocación, listado y resolución de los tokens de agente.
/// <para>
/// El secreto se genera aquí y se devuelve una sola vez, en la respuesta del alta. A la base de
/// datos solo va su hash SHA-256, el mismo patrón que el token de refresco
/// (<see cref="EmisorTokens.HashToken"/>).
/// </para>
/// <para>
/// Ni el alta ni la revocación pasan por <c>GuardadoAuditado</c>: la respuesta del alta lleva el
/// secreto y esa respuesta se guarda para el reintento idempotente, que es exactamente donde el
/// token en claro no puede acabar. Escriben su propia transacción, con la auditoría dentro.
/// </para>
/// </summary>
public sealed class ServicioAgentes(
    AtlasRentDbContext contexto,
    AuditoriaService auditoria,
    TimeProvider reloj,
    OpcionesAgentes opciones)
{
    /// <summary>
    /// Emite un token para un usuario y devuelve el secreto. Caduca siempre: un token sin fecha es
    /// una credencial que nadie recuerda ni revoke.
    /// </summary>
    public async Task<(AgenteToken fila, string secreto)> CrearAsync(
        string nombre,
        Guid usuarioId,
        string[] acciones,
        int? diasValidez,
        int? limiteConsultasDia,
        Guid usuarioAdmin,
        string ip,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            throw new ArgumentException("El token necesita un nombre que lo reconozca en el listado.");
        }

        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("El token necesita un usuario propietario.");
        }

        var permitidas = acciones
            .Where(accion => !string.IsNullOrWhiteSpace(accion))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (permitidas.Length == 0)
        {
            throw new ArgumentException("El token necesita al menos una acción.");
        }

        var desconocidas = permitidas.Where(accion => !Acciones.Todas.Contains(accion)).ToArray();
        if (desconocidas.Length > 0)
        {
            // Solo se comprueba que la acción exista, no que el propietario pueda con ella: el
            // token nunca amplía el techo del rol y eso se comprueba en cada petición, cuando el
            // rol puede haber cambiado desde que se emitió.
            throw new ArgumentException($"Acción desconocida: {string.Join(", ", desconocidas)}.");
        }

        var dias = diasValidez ?? opciones.VidaDiasToken;
        if (dias < 1 || dias > opciones.VidaDiasTokenMaxima)
        {
            throw new ArgumentException(
                $"La validez va de 1 a {opciones.VidaDiasTokenMaxima} días; se pidió {dias}.");
        }

        var limite = limiteConsultasDia ?? opciones.LimiteConsultasDia;
        if (limite < 1)
        {
            throw new ArgumentException($"El límite de consultas por día tiene que ser 1 o más; se pidió {limite}.");
        }

        var existe = await contexto.Usuario.AsNoTracking().AnyAsync(u => u.Id == usuarioId, ct);
        if (!existe)
        {
            throw new KeyNotFoundException($"El usuario {usuarioId:D} no existe.");
        }

        var ahora = reloj.GetUtcNow();
        var secreto = EmisorTokens.TokenAleatorio();
        var fila = new AgenteToken
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            Nombre = nombre.Trim(),
            HashToken = EmisorTokens.HashToken(secreto),
            Acciones = permitidas,
            Creado = ahora,
            Caduca = ahora.AddDays(dias),
            LimiteConsultasDia = limite,
        };

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);
        contexto.AgenteToken.Add(fila);
        await contexto.SaveChangesAsync(ct);

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = "agente.token.emitido",
            Ip = ip,
            UsuarioId = usuarioAdmin,
            Entidad = "agente_token",
            EntidadId = fila.Id.ToString("D"),
            // Sin el secreto y sin su hash: la auditoría no es un sitio para credenciales.
            DatosJson = DatosDe(fila),
            Resultado = "permitido",
            Origen = "api",
        }, ct);

        await transaccion.CommitAsync(ct);

        return (fila, secreto);
    }

    /// <summary>Listado para el administrador. No lleva ningún secreto: no hay ninguno guardado.</summary>
    public async Task<IReadOnlyList<AgenteToken>> ListarAsync(CancellationToken ct) =>
        await contexto.AgenteToken
            .AsNoTracking()
            .OrderByDescending(token => token.Creado)
            .ThenBy(token => token.Id)
            .ToListAsync(ct);

    /// <summary>
    /// Revoca un token. Nunca se borra: la fila se queda diciendo qué token hubo y desde cuándo dejó
    /// servir. Revocar uno ya revocado no hace nada y no vuelve a auditar.
    /// </summary>
    public async Task RevocarAsync(Guid id, Guid usuarioAdmin, string ip, CancellationToken ct)
    {
        var fila = await contexto.AgenteToken.FirstOrDefaultAsync(token => token.Id == id, ct)
            ?? throw new KeyNotFoundException($"El token {id:D} no existe.");

        if (fila.Revocado is not null)
        {
            return;
        }

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);
        fila.Revocado = reloj.GetUtcNow();
        await contexto.SaveChangesAsync(ct);

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = "agente.token.revocado",
            Ip = ip,
            UsuarioId = usuarioAdmin,
            Entidad = "agente_token",
            EntidadId = fila.Id.ToString("D"),
            DatosJson = DatosDe(fila),
            Resultado = "permitido",
            Origen = "api",
        }, ct);

        await transaccion.CommitAsync(ct);
    }

    /// <summary>
    /// Resuelve el secreto recibido en la cabecera. <c>null</c> para todo lo que no vale: token
    /// desconocido, revocado, caducado o cuyo propietario ya no puede entrar. El middleware responde
    /// 401 en todos esos casos, sin decir cuál fue.
    /// </summary>
    public async Task<AgenteResuelto?> ResolverAsync(string secreto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(secreto))
        {
            return null;
        }

        var fila = await contexto.AgenteToken
            .AsNoTracking()
            .Where(token => token.HashToken == EmisorTokens.HashToken(secreto))
            .Select(token => new { token.Id, token.UsuarioId, token.Acciones, token.Caduca, token.Revocado })
            .SingleOrDefaultAsync(ct);

        if (fila is null)
        {
            return null;
        }

        if (fila.Revocado is not null)
        {
            return null;
        }

        if (fila.Caduca is { } caduca && caduca <= reloj.GetUtcNow())
        {
            return null;
        }

        // El token corre como su propietario: si el usuario se desactivó, el token deja de valer.
        var activo = await contexto.Usuario.AsNoTracking()
            .AnyAsync(usuario => usuario.Id == fila.UsuarioId && usuario.Activo, ct);
        if (!activo)
        {
            return null;
        }

        return new AgenteResuelto(
            fila.Id,
            fila.UsuarioId,
            new HashSet<string>(fila.Acciones, StringComparer.Ordinal));
    }

    /// <summary>
    /// Anota el último uso del token. Va en su propio <c>UPDATE</c> para no ensuciar la transacción
    /// de la petición, que es de solo lectura y puede no tener ninguna.
    /// </summary>
    public Task MarcarUsoAsync(Guid id, CancellationToken ct) =>
        contexto.AgenteToken
            .Where(token => token.Id == id)
            .ExecuteUpdateAsync(
                ajustes => ajustes.SetProperty(token => token.UltimoUso, reloj.GetUtcNow()),
                ct);

    /// <summary>
    /// Audita una llamada hecha con token. Como aquí no hay mutación, la escritura va en su propia
    /// transacción, igual que hace la denegación de permisos: la petición ya ha terminado y la suya
    /// está cerrada.
    /// </summary>
    public async Task AuditarPeticionAsync(
        Guid tokenId,
        Guid usuarioId,
        string metodo,
        string ruta,
        string resultado,
        string ip,
        CancellationToken ct)
    {
        IDbContextTransaction? transaccion = null;
        if (contexto.Database.CurrentTransaction is null)
        {
            transaccion = await contexto.Database.BeginTransactionAsync(ct);
        }

        try
        {
            await auditoria.RegistrarAsync(new RegistroAuditoria
            {
                Accion = PeticionAccion,
                Ip = ip,
                UsuarioId = usuarioId,
                Entidad = "agente_token",
                EntidadId = tokenId.ToString("D"),
                DatosJson = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["metodo"] = metodo,
                    ["ruta"] = ruta,
                    ["resultado"] = resultado,
                }),
                Resultado = resultado,
                Origen = "agente",
            }, ct);

            if (transaccion is not null)
            {
                await transaccion.CommitAsync(ct);
            }
        }
        finally
        {
            if (transaccion is not null)
            {
                await transaccion.DisposeAsync();
            }
        }
    }

    /// <summary>Acción con la que se audita toda llamada atendida con un token de agente.</summary>
    public const string PeticionAccion = "agente.peticion";

    private static string DatosDe(AgenteToken fila) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["nombre"] = fila.Nombre,
        ["usuario_id"] = fila.UsuarioId.ToString("D"),
        ["acciones"] = fila.Acciones,
        ["caduca"] = fila.Caduca,
        ["limite_consultas_dia"] = fila.LimiteConsultasDia,
    });
}