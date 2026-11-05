using AtlasRent.Api.DTOs;
using AtlasRent.Api.Services;
using AwesomeAssertions;
using MailKit.Security;
using Xunit;

namespace AtlasRent.Tests.Mensajeria;

/// <summary>SEC-016 (TLS del SMTP) y SEC-028 (validación de correo y de texto de una línea).</summary>
public sealed class SeguridadCorreoYTextoTests
{
    [Fact]
    public void ConSslElStartTlsEsObligatorioYSinSslEsTlsImplicito()
    {
        ServicioCorreoMailKit.SeguridadDe(true).Should().Be(SecureSocketOptions.StartTls);
        ServicioCorreoMailKit.SeguridadDe(false).Should().Be(SecureSocketOptions.SslOnConnect);
    }

    [Theory]
    [InlineData("a@b.com")]
    [InlineData("persona.uno+tag@example.com")]
    [InlineData(null)]
    [InlineData("")]
    public void UnCorreoNormalEsValido(string? correo) => EmailSeguroAttribute.EsValido(correo).Should().BeTrue();

    [Theory]
    [InlineData("a@b.com\r\nBcc: x@y.z")]
    [InlineData("a@b.com\nBcc: x@y.z")]
    [InlineData("a@b.com\t")]
    [InlineData("a@b.com, x@y.z")]
    [InlineData("a@b.com;x@y.z")]
    [InlineData("sin-arroba")]
    public void UnCorreoConControlSeparadoresOMalFormadoNoEsValido(string correo) =>
        EmailSeguroAttribute.EsValido(correo).Should().BeFalse();

    [Fact]
    public void UnaLineaRechazaSaltosYAceptaTextoNormal()
    {
        var atributo = new UnaLineaAttribute();
        atributo.IsValid("Asunto: Factura 12").Should().BeTrue();
        atributo.IsValid("Asunto\r\nBcc: x@y.z").Should().BeFalse();
        atributo.IsValid("Nombre\tcon tab").Should().BeFalse();
    }
}
