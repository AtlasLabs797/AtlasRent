using System.Collections.Concurrent;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Reservas;
using AtlasRent.Tests.Infra;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace AtlasRent.Tests.Mensajeria;

/// <summary>
/// Fábrica con el servidor de correo configurado y <see cref="IServicioCorreo"/> sustituido por
/// <see cref="CorreoDoble"/>.
/// <para>
/// <b>Las pruebas de F4-02 no salen a internet en ningún caso.</b> El criterio de aceptación de la
/// tarea es qué pasa cuando el servidor de correo falla, y eso no se puede probar esperando al
/// servidor de verdad. Es el mismo motivo por el que F3-01 mete <c>IFuenteTipoCambio</c> detrás de una
/// interfaz.
/// </para>
/// <para>
/// Los valores de configuración son de ejemplo y no apuntan a ningún servidor real: el
/// <c>CorreoDoble</c> no los lee, y si algún día se leyera un 409 con el nombre del host sería la
/// prueba que lo detectaría.
/// </para>
/// <para>
/// No está sellada porque F4-03 hereda de ella para añadirle la cola de envíos en masa: la
/// configuración del correo y el doble de F4-02 no se copian en una segunda fábrica, se heredan.
/// </para>
/// </summary>
public class ApiTestFactoryCorreo(ContenedorBdFixture bd) : ApiTestFactory(bd)
{
    /// <summary>
    /// Fichero de log de esta instancia. Lo pone <c>Serilog:WriteTo:File</c> en configuración, que es
    /// de donde <c>Program.cs</c> lee su Serilog, y sirve para comprobar que al log no le llega nada
    /// de lo que falló con el correo.
    /// </summary>
    public string RutaLog { get; } =
        Path.Combine(Path.GetTempPath(), $"atlas-rent-correo-{Guid.NewGuid():N}.log");

    /// <summary>El doble con el que esta instancia manda correos.</summary>
    public CorreoDoble Doble { get; } = new();

    protected override void ConfigurarAjustes(IDictionary<string, string?> ajustes)
    {
        ajustes[$"{OpcionesCorreo.Seccion}:Host"] = "smtp.ejemplo.invalid";
        ajustes[$"{OpcionesCorreo.Seccion}:Puerto"] = "587";
        ajustes[$"{OpcionesCorreo.Seccion}:Usuario"] = "usuario-de-ejemplo";
        ajustes[$"{OpcionesCorreo.Seccion}:Clave"] = "clave-secreta-de-ejemplo";
        ajustes[$"{OpcionesCorreo.Seccion}:Ssl"] = "true";
        ajustes[$"{OpcionesCorreo.Seccion}:Remitente"] = "remitente@ejemplo.invalid";
        ajustes[$"{OpcionesCorreo.Seccion}:NombreRemitente"] = "Atlas Rent de ejemplo";

        // Sin este ajuste el log se iría a la consola de la prueba y no se podría comprobar. Serilog
        // lee sus destinos de la configuración, y la aplicación no los cambia en las pruebas.
        ajustes["Serilog:WriteTo:File:path"] = RutaLog;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(servicios =>
        {
            // RemoveAll y no AddScoped encima: el registro de Program.cs es singleton y, sin quitarlo,
            // quedaría el primero que ganara al resolver y el doble no se usaría nunca.
            servicios.RemoveAll<IServicioCorreo>();
            servicios.AddSingleton<IServicioCorreo>(Doble);
        });
    }
}

/// <summary>
/// Correo de mentira. Guarda lo que se le mandó y puede fallar con la excepción que le pidan.
/// <para>
/// <b>No sale nada a ningún sitio</b>: es el garante de que la colección de pruebas de F4-02 no depende
/// de la red ni de que haya un servidor de correo.
/// </para>
/// </summary>
public sealed class CorreoDoble : IServicioCorreo
{
    private readonly ConcurrentQueue<MensajeCorreo> _enviados = new();

    /// <summary>Si está a null, el envío sale bien y se guarda. Si no, se lanza esta excepción.</summary>
    public Exception? Fallar { get; set; }

    /// <summary>Se ejecuta al entrar en <c>EnviarAsync</c>, antes de nada (pruebas de concurrencia).</summary>
    public Action? AntesDeEnviar { get; set; }

    /// <summary>Se ejecuta tras dar el mensaje por enviado, antes de volver (pruebas de fallo al guardar).</summary>
    public Action? DespuesDeEnviar { get; set; }

    /// <summary>Lo que el doble ha recibido, en orden.</summary>
    public IReadOnlyList<MensajeCorreo> Enviados => [.. _enviados];

    /// <summary>Lo último que se mandó, para no depender de la posición en la lista.</summary>
    public MensajeCorreo? Ultimo => Enviados.LastOrDefault();

    public Task EnviarAsync(MensajeCorreo mensaje, CancellationToken ct)
    {
        AntesDeEnviar?.Invoke();

        if (Fallar is { } fallo)
        {
            // Se propaga como excepción cualquiera y no como una propia: el criterio de F3-01 dice que
            // de una excepción que no es del sistema solo se copia el tipo, y esto es lo que lo comprueba.
            throw fallo;
        }

        _enviados.Enqueue(mensaje);
        DespuesDeEnviar?.Invoke();

        return Task.CompletedTask;
    }

    /// <summary>Deja de fallar, para lo que venga después.</summary>
    public void Restablecer() => Fallar = null;
}