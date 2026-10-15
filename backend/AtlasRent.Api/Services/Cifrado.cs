using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AtlasRent.Api.Services;

/// <summary>
/// Normalización del documento de identidad antes de calcular su índice ciego
/// (docs/DOC_TECNICA.md §4.3). Es determinista y es la misma al escribir y al buscar: sin ella,
/// "001-1234567-8" y "00112345678" serían dos clientes distintos.
/// <para>
/// <strong>Criterio (decidido en F1-09):</strong> mayúsculas invariantes y se quitan los separadores
/// que alguien escribe y otro no (espacios, guiones y guiones largos, y puntos). Se conservan
/// letras y dígitos porque un pasaporte tiene letras. El motivo es que un documento es una
/// identidad, no un texto: "001-12 34567-8", "00112345678" y "001 12-34567.8" tienen que ser el
/// mismo cliente, y el índice único es lo que impide que existan los tres.
/// </para>
/// </summary>
public static class DocumentoIdentidad
{
    private const char GuionLargo = '-';

    /// <summary>
    /// Deja el documento en su forma comparable: sin espacios ni separadores y en mayúsculas.
    /// Nulo, vacío o solo separadores dan cadena vacía, que el índice ciego trata como "sin
    /// documento" (el documento es opcional para un prospecto).
    /// </summary>
    public static string Normalizar(string documento)
    {
        var constructor = new StringBuilder(documento.Length);
        foreach (var caracter in documento)
        {
            if (EsSeparador(caracter))
            {
                continue;
            }

            constructor.Append(char.ToUpperInvariant(caracter));
        }

        return constructor.ToString();
    }

    /// <summary>
    /// Separadores que no forman parte del documento. Los guiones largos son los tres códigos
    /// Unicode que usan los teclados y las pegadas de texto (U+2010, U+2013 y U+2014): si no
    /// entran, dos personas tecleando el mismo documento crean dos clientes.
    /// </summary>
    private static bool EsSeparador(char caracter) =>
        char.IsWhiteSpace(caracter)
        || caracter is GuionLargo or '.' or '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2014';
}

/// <summary>
/// Cifrado simétrico en reposo con AES-256-GCM (AGENTS.md §6: cédula, RNC o pasaporte cifrados en
/// la base de datos).
/// <para>
/// La clave vive fuera de la base de datos, en <c>Seguridad__CifradoClave</c>, y el texto guardado
/// lleva la versión de la clave para poder rotarla sin reescribir la tabla entera. El nonce es
/// aleatorio y distinto en cada cifrado: reutilizarlo con la misma clave rompe el cifrado de
/// forma recuperable, y por eso lo genera <see cref="RandomNumberGenerator"/> y no un contador.
/// </para>
/// <para>
/// Lo que se guarda es <c>v{version}:{nonce}:{cifrado}</c>, con el nonce de 12 bytes y el
/// cifrado (texto cifrado y etiqueta de autenticación) en base64. El separador es <c>:</c>, que
/// no aparece en base64. El texto cifrado y el nonce no van a ningún log ni a la auditoría.
/// </para>
/// </summary>
public sealed class ServicioCifrado
{
    /// <summary>Longitud del nonce: los 12 bytes que recomienda NIST SP 800-38D para GCM.</summary>
    public const int BytesNonce = 12;

    /// <summary>
    /// Longitud de la etiqueta de autenticación: los 128 bits de GCM. Va al final del texto
    /// guardado, y es lo que hace que un byte cambiado en la base de datos se note al descifrar.
    /// </summary>
    public const int BytesEtiqueta = 16;

    /// <summary>Longitud exacta de la clave: AES-256 son 32 bytes, ni uno más ni uno menos.</summary>
    public const int BytesClave = 32;

    private const char Separador = ':';
    private const char MarcaVersion = 'v';

    private readonly byte[] _clave;
    private readonly int _version;

    /// <summary>
    /// La clave se valida aquí además de en la lectura de la configuración: arrancar con una clave
    /// que no sea de 32 bytes es peor que no arrancar, y esta es la última puerta antes de cifrar.
    /// </summary>
    public ServicioCifrado(OpcionesSeguridad opciones)
    {
        if (opciones.ClaveCifrado.Length != BytesClave)
        {
            throw new InvalidOperationException(
                $"Seguridad__CifradoClave tiene que tener exactamente {BytesClave} bytes (AES-256); "
                + $"tiene {opciones.ClaveCifrado.Length}.");
        }

        _clave = opciones.ClaveCifrado;
        _version = opciones.VersionCifrado;
    }

    /// <summary>La versión de clave con la que cifra esta instancia.</summary>
    public int Version => _version;

    /// <summary>Cifra un texto y devuelve el valor que se guarda en la columna.</summary>
    public string Cifrar(string textoClaro)
    {
        ArgumentNullException.ThrowIfNull(textoClaro);

        var nonce = RandomNumberGenerator.GetBytes(BytesNonce);
        var claro = Encoding.UTF8.GetBytes(textoClaro);
        var cifrado = new byte[claro.Length];
        var etiqueta = new byte[BytesEtiqueta];

        using var aes = new AesGcm(_clave, BytesEtiqueta);

        // Argumentos con nombre a propósito: Encrypt y Decrypt no ordenan igual los parámetros y
        // un cruce silencioso cifraría con el nonce equivocado sin quejarse.
        aes.Encrypt(
            nonce: nonce,
            plaintext: claro,
            ciphertext: cifrado,
            tag: etiqueta,
            associatedData: ReadOnlySpan<byte>.Empty);

        // El valor guardado es el texto cifrado seguido de la etiqueta.
        var salida = new byte[cifrado.Length + etiqueta.Length];
        cifrado.CopyTo(salida, 0);
        etiqueta.CopyTo(salida, cifrado.Length);

        return string.Concat(
            MarcaVersion.ToString(),
            _version.ToString(CultureInfo.InvariantCulture),
            Separador.ToString(),
            Convert.ToBase64String(nonce),
            Separador.ToString(),
            Convert.ToBase64String(salida));
    }

    /// <summary>
    /// Descifra un valor guardado. Si alguien cambió un byte, la etiqueta de autenticación no
    /// cuadra y <see cref="CryptographicException"/> salta: es preferible a devolver basura.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// El valor no tiene la forma <c>v{version}:{nonce}:{cifrado}</c>.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// El valor está cifrado con otra versión de la clave, o la etiqueta no cuadra.
    /// </exception>
    public string Descifrar(string textoCifrado)
    {
        ArgumentNullException.ThrowIfNull(textoCifrado);

        var partes = textoCifrado.Split(Separador);
        if (partes.Length != 3 || partes[0].Length < 2 || partes[0][0] != MarcaVersion)
        {
            throw new InvalidOperationException(
                $"El valor cifrado no tiene la forma v{_version}:{BytesNonce}-bytes:texto.");
        }

        var version = int.Parse(partes[0][1..], CultureInfo.InvariantCulture);
        if (version != _version)
        {
            throw new CryptographicException(
                $"El valor está cifrado con la versión {version} de la clave y la aplicación está "
                + $"usando la {_version}. Faltan las claves de las versiones antiguas.");
        }

        var nonce = Convert.FromBase64String(partes[1]);
        var salida = Convert.FromBase64String(partes[2]);
        if (salida.Length < BytesEtiqueta)
        {
            throw new InvalidOperationException(
                "El valor cifrado es más corto que la etiqueta de autenticación: está corrupto.");
        }

        var etiqueta = salida.AsSpan(^BytesEtiqueta).ToArray();
        var cifrado = salida.AsSpan(0, salida.Length - BytesEtiqueta).ToArray();
        var claro = new byte[cifrado.Length];

        using var aes = new AesGcm(_clave, BytesEtiqueta);

        // Argumentos con nombre a propósito: Decrypt no ordena los parámetros como Encrypt y un
        // cruce silencioso no quejarseía hasta descifrar basura.
        aes.Decrypt(
            nonce: nonce,
            ciphertext: cifrado,
            tag: etiqueta,
            plaintext: claro,
            associatedData: ReadOnlySpan<byte>.Empty);

        return Encoding.UTF8.GetString(claro);
    }
}

/// <summary>
/// Índice ciego del documento (docs/DOC_TECNICA.md §4.3): <c>documento_hmac</c> es el HMAC-SHA256
/// del documento <em>normalizado</em>, con clave propia.
/// <para>
/// Con esto se busca por documento exacto y se detectan duplicados sin poder descifrar la tabla:
/// el HMAC es determinista (el mismo documento da el mismo valor) y no es reversible con la clave
/// de cifrado, que es justo lo que se quiere si alguien lee la base de datos.
/// </para>
/// </summary>
public sealed class IndiceCiegoDocumento(OpcionesSeguridad opciones)
{
    /// <summary>Longitud del HMAC-SHA256 en hexadecimal.</summary>
    public const int LongitudHuella = 64;

    /// <summary>
    /// La clave del índice es distinta de la de cifrado a propósito: una sola compromised no
    /// descifra los documentos, y cambiar una no obliga a cambiar la otra.
    /// </summary>
    private readonly byte[] _clave = Comprobar(opciones.ClaveHmacDocumento);

    private static byte[] Comprobar(byte[] clave)
    {
        if (clave.Length < ServicioCifrado.BytesClave)
        {
            throw new InvalidOperationException(
                $"Seguridad__HmacDocumentoClave necesita al menos {ServicioCifrado.BytesClave} bytes; "
                + $"tiene {clave.Length}.");
        }

        return clave;
    }

    /// <summary>
    /// Huella del documento, o <c>null</c> si no hay documento. Un documento vacío no puede
    /// repetirse: no tiene nada que identificar.
    /// </summary>
    public string? Huella(string? documento)
    {
        if (string.IsNullOrWhiteSpace(documento))
        {
            return null;
        }

        var normalizado = DocumentoIdentidad.Normalizar(documento);
        if (normalizado.Length == 0)
        {
            return null;
        }

        return Convert.ToHexStringLower(
            HMACSHA256.HashData(_clave, Encoding.UTF8.GetBytes(normalizado)));
    }
}