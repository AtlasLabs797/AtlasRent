using System.Security.Cryptography;
using System.Text;
using AtlasRent.Api.Services;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AtlasRent.Tests.Clientes;

/// <summary>
/// F1-09, cifrado en reposo e índice ciego (AGENTS.md §6): AES-256-GCM con nonce aleatorio y
/// versión de clave, y el HMAC del documento normalizado.
/// </summary>
public sealed class CifradoTests
{
    private static readonly byte[] Clave = CrearClave(32);
    private static readonly byte[] ClaveHmac = CrearClave(32);

    [Fact]
    public void CifrarDosVecesElMismoValorDaTextosDistintosYLosDosDescifranIgual()
    {
        // Reutilizar un nonce con la misma clave rompe el cifrado de forma recuperable, así que dos
        // cifrados del mismo texto tienen que salir distintos y descifrar al mismo original.
        var servicio = Servicio();

        var primero = servicio.Cifrar("001-1234567-8");
        var segundo = servicio.Cifrar("001-1234567-8");

        primero.Should().NotBe(segundo);
        servicio.Descifrar(primero).Should().Be("001-1234567-8");
        servicio.Descifrar(segundo).Should().Be("001-1234567-8");
    }

    [Fact]
    public void ElTextoCifradoLlevaVersionYNonceYNoContieneElClaro()
    {
        var servicio = Servicio();

        var cifrado = servicio.Cifrar("001-1234567-8");

        var partes = cifrado.Split(':');
        partes.Should().HaveCount(3);
        partes[0].Should().Be("v1", "la versión de clave viaja con el valor para poder rotarla");
        Convert.FromBase64String(partes[1]).Should().HaveCount(ServicioCifrado.BytesNonce);

        // El documento no sale legible del texto cifrado. La comprobación va sobre los bytes
        // descodificados, no buscando "001" dentro del texto: el nonce y el texto cifrado son
        // aleatorios, y una secuencia de tres caracteres sale sola cada pocas mil ejecuciones
        // (unas 78 posiciones sobre un alfabeto de 64, ~0,03%) sin que el cifrado tenga nada que
        // ver. Lo que sí es cierto siempre es que el documento en claro no está entre los bytes
        // que se guardan, y eso es lo que se comprueba.
        var documento = Encoding.UTF8.GetBytes("001-1234567-8");
        Contiene(Convert.FromBase64String(partes[1]), documento)
            .Should().BeFalse("el nonce es aleatorio y no lleva el documento");
        Contiene(Convert.FromBase64String(partes[2]), documento)
            .Should().BeFalse("del texto cifrado no se puede leer el documento sin la clave");
    }

    [Fact]
    public void UnByteCambiadoEnElValorGuardadoNoDescifra()
    {
        // La etiqueta de autenticación es lo que detecta la manipulación: es preferible un error
        // a devolver basura disfrazada de documento.
        var servicio = Servicio();
        var partes = servicio.Cifrar("001-1234567-8").Split(':');
        var carga = Convert.FromBase64String(partes[2]);
        carga[^1] ^= 0x01;

        var manipulado = string.Join(':', partes[0], partes[1], Convert.ToBase64String(carga));

        var alManipular = () => servicio.Descifrar(manipulado);
        alManipular.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void UnValorConOtraVersionDeClaveNoDescifra()
    {
        var servicio = Servicio();

        var alDescifrar = () => servicio.Descifrar("v2:AAAAAAAAAAAAAAAA:AAAAAAAAAAAAAAAAAAAAAA");
        alDescifrar.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void LaNormalizacionQuitaSeparadoresYDejaElDocumentoEnMayusculas()
    {
        // Criterio de F1-09: mayúsculas invariantes y sin espacios, guiones ni puntos. Se conservan
        // letras y dígitos porque un pasaporte tiene letras.
        DocumentoIdentidad.Normalizar("001-1234567-8").Should().Be("00112345678");
        DocumentoIdentidad.Normalizar(" 001 12-34567.8 ").Should().Be("00112345678");
        DocumentoIdentidad.Normalizar("00112345678").Should().Be("00112345678");
        DocumentoIdentidad.Normalizar("001\u20131234567\u20138").Should().Be("00112345678");
        DocumentoIdentidad.Normalizar("a1234567").Should().Be("A1234567", "un pasaporte tiene letras");
    }

    [Fact]
    public void DosEscriturasDistintasDelMismoDocumentoDanLaMismaHuella()
    {
        // Es lo que hace que "001-1234567-8" y "00112345678" sean el mismo cliente y no dos.
        var indice = Indice();

        indice.Huella("001-1234567-8").Should().Be(indice.Huella("00112345678"));
        indice.Huella("001-1234567-8").Should().HaveLength(IndiceCiegoDocumento.LongitudHuella);
        indice.Huella("00112345678").Should().NotBe(indice.Huella("00112345679"));
    }

    [Fact]
    public void UnDocumentoVacioNoTieneHuella()
    {
        // Varios clientes pueden venir sin documento de la importación; sin huella no chocan.
        var indice = Indice();

        indice.Huella(null).Should().BeNull();
        indice.Huella("   ").Should().BeNull();
        indice.Huella(" - . ").Should().BeNull();
    }

    [Fact]
    public void LaHuellaNoSirveParaRecuperarElDocumento()
    {
        // La huella es determinista (buscar) y no reversible (no leer). Se comprueba que el HMAC no
        // es el documento ni el texto cifrado, y que otra clave da otra huella.
        // <para>
        // «No es el documento» se compara con el documento, no buscando un trozo de él dentro de
        // la huella: la huella son 64 hexes aleatorios y «001» sale dentro por casualidad con una
        // probabilidad de unas 62/16³, algo más del 1,5% de las ejecuciones. Esa comprobación
        // hacía fallar la prueba sola de vez en cuando sin que hubiera nada roto.
        // </para>
        var indice = Indice();

        var huella = indice.Huella("001-1234567-8")!;

        huella.Should().NotBe("00112345678", "la huella no es el documento: es el HMAC de su forma normalizada");
        huella.Should().NotBe("001-1234567-8");
        huella.Should().NotBe(Servicio().Cifrar("001-1234567-8"));

        var otraClave = new IndiceCiegoDocumento(Opciones(Clave, CrearClave(32))).Huella("001-1234567-8");
        otraClave.Should().NotBe(huella, "la clave del índice es propia: cambiarla cambia la huella");
    }

    [Fact]
    public void SinClaveDeCifradoLaAplicacionNoArranca()
    {
        // Arrancar con una clave débil es peor que no arrancar: el fallo dice qué variable falta.
        Capturar(() => OpcionesSeguridad.Desde(Configuracion(CifradoClave: null, HmacDocumentoClave: HmacValido)))
            .Should().Contain("Seguridad__CifradoClave");
        Capturar(() => OpcionesSeguridad.Desde(Configuracion(CifradoClave: CifradoValido, HmacDocumentoClave: null)))
            .Should().Contain("Seguridad__HmacDocumentoClave");
    }

    [Fact]
    public void UnaClaveQueNoSon32BytesFallaConElNombreDeLaVariable()
    {
        var base64 = Convert.ToBase64String(CrearClave(16));

        Capturar(() => OpcionesSeguridad.Desde(Configuracion(CifradoClave: base64, HmacDocumentoClave: HmacValido)))
            .Should().Contain("Seguridad__CifradoClave").And.Contain("32");
        Capturar(() => OpcionesSeguridad.Desde(Configuracion(CifradoClave: "no-es-base64", HmacDocumentoClave: HmacValido)))
            .Should().Contain("base64");
    }

    [Fact]
    public void ElServicioCifradoSeNiegaAArrancarConUnaClaveQueNoEsDe32Bytes()
    {
        var opciones = Opciones(CrearClave(16));

        Capturar(() => new ServicioCifrado(opciones)).Should().Contain("32");
    }

    // ---------------------------------------------------------------- apoyo

    private static ServicioCifrado Servicio() => new(Opciones(Clave));

    private static IndiceCiegoDocumento Indice() => new(Opciones(Clave));

    private static OpcionesSeguridad Opciones(byte[] claveCifrado, byte[]? claveHmac = null) => new()
    {
        ClaveJwt = new byte[64],
        ClaveCifrado = claveCifrado,
        ClaveHmacDocumento = claveHmac ?? ClaveHmac,
        VersionCifrado = 1,
    };

    private static byte[] CrearClave(int bytes) => RandomNumberGenerator.GetBytes(bytes);

    private static readonly string ClaveValida = Convert.ToBase64String(new byte[64]);
    private static readonly string CifradoValido = Convert.ToBase64String(new byte[32]);
    private static readonly string HmacValido = Convert.ToBase64String(new byte[32]);

    private static IConfiguration Configuracion(string? CifradoClave, string? HmacDocumentoClave) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seguridad:JwtClave"] = ClaveValida,
                ["Seguridad:CifradoClave"] = CifradoClave,
                ["Seguridad:CifradoVersion"] = "1",
                ["Seguridad:HmacDocumentoClave"] = HmacDocumentoClave,
            })
            .Build();

    private static string Capturar(Action accion) =>
        (accion.Should().Throw<Exception>()).Which.Message;

    /// <summary>
    /// Si <paramref name="aguja"/> aparece como bloque dentro de <paramref name="paja"/>.
    /// <para>
    /// Va sobre los bytes descodificados y no sobre el texto: en el Base64 o el hexadecimal, una
    /// secuencia corta sale por casualidad y la comprobación depende del azar.
    /// </para>
    /// </summary>
    private static bool Contiene(byte[] paja, byte[] aguja)
    {
        for (var i = 0; i <= paja.Length - aguja.Length; i++)
        {
            if (paja.AsSpan(i, aguja.Length).SequenceEqual(aguja))
            {
                return true;
            }
        }

        return false;
    }
}