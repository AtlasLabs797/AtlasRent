using System.Text.Json;
using AtlasRent.Api.Data;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Contactos de un cliente (docs/ESPECIFICACION.md §3.2): casillas de correo y teléfono, con uno
/// por defecto. Un contacto no tiene efecto económico, así que aquí sí se puede borrar.
/// </summary>
public sealed class ServicioContactos(
    AtlasRentDbContext contexto,
    AuditoriaService auditoria,
    ServicioIdempotencia idempotencia,
    TimeProvider reloj)
{
    public async Task<IReadOnlyList<ContactoRespuesta>> ListarAsync(Guid clienteId, CancellationToken ct)
    {
        await ExigirClienteAsync(clienteId, ct);

        return await contexto.ContactoCliente.AsNoTracking()
            .Where(c => c.ClienteId == clienteId)
            .OrderByDescending(c => c.EsPrincipal)
            .ThenBy(c => c.Nombre)
            .Select(c => new ContactoRespuesta(
                c.Id, c.ClienteId, c.Nombre, c.Relacion, c.Email, c.Telefono, c.EsPrincipal))
            .ToListAsync(ct);
    }

    public async Task<ContactoRespuesta> CrearAsync(
        Guid clienteId,
        ContactoPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        await ExigirClienteAsync(clienteId, ct);

        var contacto = new ContactoCliente { ClienteId = clienteId };
        Aplicar(contacto, peticion);

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.ContactoCliente, contacto,
            esAlta: true,
            accion: "contacto_cliente.creado",
            entidad: "contacto_cliente",
            usuario,
            ip,
            mutacion,
            proyectar: ARespuesta,
            datosAuditoria: c => new { c.ClienteId, c.Nombre, c.Relacion, c.EsPrincipal },
            antesDeGuardar: peticion.EsPrincipal ? () => QuitarPrincipalAsync(clienteId, null, usuario, ct) : null,
            ct: ct);
    }

    public async Task<ContactoRespuesta> ActualizarAsync(
        Guid clienteId,
        Guid contactoId,
        ContactoPeticion peticion,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var contacto = await CargarAsync(clienteId, contactoId, ct);

        Aplicar(contacto, peticion);

        return await GuardadoAuditado.GuardarAsync(
            contexto, auditoria, idempotencia, reloj,
            contexto.ContactoCliente, contacto,
            esAlta: false,
            accion: "contacto_cliente.actualizado",
            entidad: "contacto_cliente",
            usuario,
            ip,
            mutacion,
            proyectar: ARespuesta,
            datosAuditoria: c => new { c.ClienteId, c.Nombre, c.Relacion, c.EsPrincipal },
            antesDeGuardar: peticion.EsPrincipal
                ? () => QuitarPrincipalAsync(clienteId, contactoId, usuario, ct)
                : null,
            ct: ct);
    }

    /// <summary>
    /// Un contacto es una casilla de correo o un teléfono: no tiene efecto económico, así que
    /// borrarlo no rompe nada. Aun así la baja se audita en la misma transacción que el borrado.
    /// </summary>
    public async Task<ContactoRespuesta> EliminarAsync(
        Guid clienteId,
        Guid contactoId,
        Guid usuario,
        string ip,
        MutacionIdempotente? mutacion,
        CancellationToken ct)
    {
        var contacto = await CargarAsync(clienteId, contactoId, ct);
        var respuesta = ARespuesta(contacto);

        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        await auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = "contacto_cliente.eliminado",
            Ip = ip,
            UsuarioId = usuario,
            Entidad = "contacto_cliente",
            EntidadId = contacto.Id.ToString("D"),
            DatosJson = JsonSerializer.Serialize(new
            {
                Id = contacto.Id.ToString("D"),
                ClienteId = clienteId,
            }),
            Resultado = "permitido",
            Origen = "api",
        }, ct);

        contexto.ContactoCliente.Remove(contacto);
        await contexto.SaveChangesAsync(ct);

        await idempotencia.RegistrarAsync(usuario, mutacion, respuesta, ct);

        await transaccion.CommitAsync(ct);

        return respuesta;
    }

    // ---------------------------------------------------------------- internos

    private static ContactoRespuesta ARespuesta(ContactoCliente contacto) =>
        new(
            contacto.Id,
            contacto.ClienteId,
            contacto.Nombre,
            contacto.Relacion,
            contacto.Email,
            contacto.Telefono,
            contacto.EsPrincipal);

    private static void Aplicar(ContactoCliente contacto, ContactoPeticion peticion)
    {
        contacto.Nombre = peticion.Nombre.Trim();
        contacto.Relacion = Limpiar(peticion.Relacion);
        contacto.Email = Limpiar(peticion.Email);
        contacto.Telefono = Limpiar(peticion.Telefono);
        contacto.EsPrincipal = peticion.EsPrincipal;
    }

    private static string? Limpiar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private async Task<ContactoCliente> CargarAsync(Guid clienteId, Guid contactoId, CancellationToken ct) =>
        await contexto.ContactoCliente.FirstOrDefaultAsync(
            c => c.Id == contactoId && c.ClienteId == clienteId, ct)
        ?? throw new KeyNotFoundException($"No existe el contacto '{contactoId:D}' del cliente '{clienteId:D}'.");

    private async Task ExigirClienteAsync(Guid clienteId, CancellationToken ct)
    {
        if (!await contexto.Cliente.AnyAsync(c => c.Id == clienteId, ct))
        {
            throw new KeyNotFoundException($"No existe el cliente '{clienteId:D}'.");
        }
    }

    /// <summary>
    /// Deja un solo contacto principal por cliente. El índice único parcial de la tabla es la red
    /// por si acaso.
    /// </summary>
    private async Task QuitarPrincipalAsync(
        Guid clienteId,
        Guid? conservar,
        Guid usuario,
        CancellationToken ct)
    {
        var otros = await contexto.ContactoCliente
            .Where(c => c.ClienteId == clienteId && c.EsPrincipal && (conservar == null || c.Id != conservar))
            .ToListAsync(ct);

        var ahora = reloj.GetUtcNow();
        foreach (var otro in otros)
        {
            otro.EsPrincipal = false;
            otro.UpdatedAt = ahora;
            otro.UpdatedBy = usuario;
        }
    }
}