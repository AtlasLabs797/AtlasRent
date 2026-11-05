using AtlasRent.Api.Services;

namespace AtlasRent.Api.Jobs;

/// <summary>SEC-023: manda el correo de aviso de IP nueva a los administradores. Solo dispara al servicio.</summary>
public sealed class TrabajoAvisoIpNueva(ServicioAvisosAcceso avisos)
{
    public Task EjecutarAsync(Guid usuarioId, string ip, DateTimeOffset cuando, CancellationToken ct) =>
        avisos.EnviarIpNuevaAsync(usuarioId, ip, cuando, ct);
}
