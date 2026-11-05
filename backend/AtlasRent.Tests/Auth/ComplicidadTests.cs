using System.Net;
using System.Security.Cryptography;
using System.Text;
using AtlasRent.Api.Services;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Auth;

/// <summary>
/// F1-04: comprobación contra HaveIBeenPwned por k-anonimato. Lo que sale hacia la API es el
/// prefijo de 5 caracteres del SHA-1 y nada más: ni la contraseña ni el hash completo.
/// </summary>
public sealed class ComplicidadTests
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>Sufijo que devuelve la API falsa: el resto del SHA-1 en mayúsculas.</summary>
    private static string Sufijo => Sha1(Contrasena)[5..];

    [Fact]
    public async Task SoloSeEnviaElPrefijoDeCincoCaracteresDelSha1()
    {
        var manejador = new ManejadorFalso($"{Sufijo}:42\r\nABCDEF:1\r\n");
        var servicio = new ServicioComplicidad(new HttpClient(manejador));

        var resultado = await servicio.ComprobarAsync(Contrasena, TestContext.Current.CancellationToken);

        var peticion = manejador.Peticiones.Should().ContainSingle().Which;
        peticion.Method.Should().Be(HttpMethod.Get);
        peticion.RequestUri!.AbsolutePath.Should().Be($"/range/{Sha1(Contrasena)[..5]}");
        peticion.RequestUri.AbsolutePath.Should().HaveLength("/range/".Length + 5);

        // Ni la contraseña ni el hash completo salen de la máquina.
        peticion.RequestUri.AbsoluteUri.Should().NotContain(Contrasena);
        peticion.RequestUri.AbsoluteUri.Should().NotContain(Sha1(Contrasena));
        manejador.Cuerpos.Should().BeEmpty();

        resultado.Comprobada.Should().BeTrue();
        resultado.ApareceEnFiltraciones.Should().BeTrue();
        resultado.Apariciones.Should().Be(42);
    }

    [Fact]
    public async Task UnaContrasenaQueNoEstaFiltradaNoSeMarca()
    {
        var manejador = new ManejadorFalso("ABCDEF:1\r\n");
        var servicio = new ServicioComplicidad(new HttpClient(manejador));

        var resultado = await servicio.ComprobarAsync(Contrasena, TestContext.Current.CancellationToken);

        resultado.Comprobada.Should().BeTrue();
        resultado.ApareceEnFiltraciones.Should().BeFalse();
        resultado.Apariciones.Should().Be(0);
    }

    [Fact]
    public async Task SiHaveIBeenPwnedNoRespondeNoFallaYDejaConstancia()
    {
        var manejador = new ManejadorFalso(null) { Error = new HttpRequestException("sin red") };
        var servicio = new ServicioComplicidad(new HttpClient(manejador));

        var resultado = await servicio.ComprobarAsync(Contrasena, TestContext.Current.CancellationToken);

        // No se bloquea el cambio de contraseña, pero el intento queda marcado como no comprobado.
        resultado.Comprobada.Should().BeFalse();
        resultado.ApareceEnFiltraciones.Should().BeFalse();
    }

    private static string Sha1(string valor) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(valor))).ToUpperInvariant();

    /// <summary>Sustituye a HaveIBeenPwned y guarda lo que se le pide.</summary>
    private sealed class ManejadorFalso(string? cuerpo) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Peticiones { get; } = [];

        public List<string> Cuerpos { get; } = [];

        public Exception? Error { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage peticion,
            CancellationToken cancellationToken)
        {
            Peticiones.Add(peticion);
            if (peticion.Content is not null)
            {
                Cuerpos.Add(await peticion.Content.ReadAsStringAsync(cancellationToken));
            }

            if (Error is not null)
            {
                throw Error;
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(cuerpo ?? string.Empty) };
        }
    }
}