using AtlasRent.Api.Services;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AtlasRent.Tests.Auth;

/// <summary>
/// F1-04: BCrypt con 12 rondas o más. La contraseña no aparece en el hash ni se guarda en claro.
/// </summary>
public sealed class ContrasenasTests
{
    private const string Contrasena = "Clave-de-prueba-2026";
    private const string Otra = "Clave-distinta-2026";

    private static ServicioContrasenas Servicio(int rondas = OpcionesSeguridad.RondasBcryptMinimas) =>
        new(new OpcionesSeguridad { ClaveJwt = new byte[64], RondasBcrypt = rondas });

    [Fact]
    public void ElHashNoContieneLaContrasena()
    {
        var hash = Servicio().Hashar(Contrasena);

        hash.Should().NotBe(Contrasena);
        hash.Should().NotContain(Contrasena);
        // Formato de BCrypt: $2<variante>$<rondas>$<sal y hash>.
        hash.Split('$')[2].Should().Be(OpcionesSeguridad.RondasBcryptMinimas.ToString());
    }

    [Fact]
    public void VerificarAceptaLaCorrectaYRechazaLaIncorrecta()
    {
        var servicio = Servicio();
        var hash = servicio.Hashar(Contrasena);

        servicio.Verificar(Contrasena, hash).Should().BeTrue();
        servicio.Verificar(Otra, hash).Should().BeFalse();
        servicio.Verificar(Contrasena + " ", hash).Should().BeFalse();
    }

    [Fact]
    public void DosHashesDeLaMismaContrasenaSonDistintosPorLaSal()
    {
        var servicio = Servicio();

        servicio.Hashar(Contrasena).Should().NotBe(servicio.Hashar(Contrasena));
    }

    [Fact]
    public void ElCostePorDefectoEs12YNoSePuedeBajarDe12()
    {
        new OpcionesSeguridad { ClaveJwt = new byte[64] }.RondasBcrypt.Should().Be(12);

        var excepciones = new List<string>();
        excepciones.Add(Capturar(() => Servicio(rondas: 10).Hashar(Contrasena)));
        excepciones.Add(Capturar(() => OpcionesSeguridad.Desde(
            Configuracion(ClaveValida, BcryptRondas: "10"))));

        excepciones.Should().AllSatisfy(mensaje => mensaje.ToLowerInvariant().Should().Contain("bcrypt"));
    }

    [Fact]
    public void SinClaveJwtLasOpcionesFallanConElNombreDeLaVariable()
    {
        Capturar(() => OpcionesSeguridad.Desde(Configuracion(JwtClave: null)))
            .Should().Contain("Seguridad__JwtClave");
        Capturar(() => OpcionesSeguridad.Desde(Configuracion(JwtClave: "no-es-base64")))
            .Should().Contain("base64");
    }

    private static readonly string ClaveValida = Convert.ToBase64String(new byte[64]);

    private static IConfiguration Configuracion(string? JwtClave, string? BcryptRondas = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seguridad:JwtClave"] = JwtClave,
                ["Seguridad:BcryptRondas"] = BcryptRondas,
            })
            .Build();

    private static string Capturar(Action accion) =>
        (accion.Should().Throw<Exception>()).Which.Message;
}