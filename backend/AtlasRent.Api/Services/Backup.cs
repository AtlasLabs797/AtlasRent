using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using Serilog;

namespace AtlasRent.Api.Services;

/// <summary>
/// Opciones de la copia de seguridad, de la sección <c>Backup</c> de la configuración
/// (<c>Backup__CarpetaLocal</c>, <c>Backup__ClaveCifrado</c>, <c>Backup__Conservar</c>).
/// <para>
/// Ni la carpeta ni la clave tienen valor por defecto: una copia de seguridad que sale bien pero se
/// queda en una carpeta equivocada, o que se cifra con una clave inventada, no es una copia de
/// seguridad. Por eso arrancar sin ellas falla y el nombre del fallo lleva el nombre de la variable.
/// </para>
/// </summary>
public sealed class OpcionesBackup
{
    public const string Seccion = "Backup";

    /// <summary>
    /// Cuántas copias se quedan. Cuatro es lo que dice el plan de F1-12: el más reciente más tres
    /// anteriores, que es lo que alcanza para deshacer un despliegue que salió mal.
    /// </summary>
    public const int ConservarPorDefecto = 4;

    /// <summary>Prefijo del archivo de copia. La fecha va justo detrás, y ordena las copias.</summary>
    public const string PrefijoArchivo = "atlasrent-";

    /// <summary>
    /// Extensión de la copia cifrada. Deliberadamente inventada (no es <c>.dump</c> ni <c>.sql</c>):
    /// cualquiera que abra la carpeta ve que ahí no hay un volcado en claro.
    /// </summary>
    public const string ExtensionArchivo = ".arbk";

    /// <summary>Carpeta donde se escriben las copias cifradas. Fuera del webroot.</summary>
    public required string CarpetaLocal { get; init; }

    /// <summary>
    /// Clave de AES-256-GCM de la copia, en bytes, de <c>Backup__ClaveCifrado</c>. Distinta de
    /// <c>Seguridad__CifradoClave</c> a propósito: si se roba una copia de seguridad no se descifran
    /// los documentos de identidad que están en la base, y cambiar una no obliga a cambiar la otra.
    /// </summary>
    public required byte[] ClaveCifrado { get; init; }

    /// <summary>Copias que se conservan, la más reciente siempre (<c>Backup__Conservar</c>).</summary>
    public int Conservar { get; init; } = ConservarPorDefecto;

    /// <summary>
    /// Tamano maximo del contenido (volcado + documentos) que se acepta. La copia entera vive en
    /// <c>byte[]</c>/<c>MemoryStream</c> y se copia al empaquetar y al cifrar (hasta ~3 veces en
    /// memoria), y un <c>byte[]</c> no pasa de ~2 GiB: con 1 GiB queda margen. Superarlo es un fallo
    /// con alerta clara, no un <c>OutOfMemoryException</c> a mitad del respaldo (BUG-045).
    /// </summary>
    public long LimiteBytesContenido { get; init; } = 1L << 30;

    public static OpcionesBackup Desde(IConfiguration configuracion)
    {
        var carpeta = configuracion[$"{Seccion}:CarpetaLocal"];
        if (string.IsNullOrWhiteSpace(carpeta))
        {
            throw new InvalidOperationException(
                "Falta la carpeta de las copias de seguridad. Definir Backup__CarpetaLocal "
                + "(en user-secrets en desarrollo y en el servicio en producción).");
        }

        var conservar = configuracion.GetValue($"{Seccion}:Conservar", ConservarPorDefecto);
        if (conservar < 1)
        {
            throw new InvalidOperationException(
                $"{Seccion}:Conservar tiene que ser al menos 1; se pidió {conservar}. "
                + "Una copia que no conserva ni la última no deja nada.");
        }

        return new OpcionesBackup
        {
            CarpetaLocal = carpeta,
            ClaveCifrado = LeerClave(configuracion),
            Conservar = conservar,
        };
    }

    private static byte[] LeerClave(IConfiguration configuracion)
    {
        var valor = configuracion[$"{Seccion}:ClaveCifrado"];
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new InvalidOperationException(
                $"Falta la clave de cifrado de las copias. Definir {Seccion}__ClaveCifrado "
                + $"({ServicioCifrado.BytesClave} bytes exactos en base64).");
        }

        byte[] clave;
        try
        {
            clave = Convert.FromBase64String(valor);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"{Seccion}__ClaveCifrado no es base64 válido.");
        }

        if (clave.Length != ServicioCifrado.BytesClave)
        {
            throw new InvalidOperationException(
                $"{Seccion}__ClaveCifrado necesita exactamente {ServicioCifrado.BytesClave} bytes; "
                + $"tiene {clave.Length}.");
        }

        return clave;
    }
}

/// <summary>
/// El volcado de la base de datos, detrás de una interfaz porque es el único punto de F1-12 que
/// depende de una herramienta externa. En producción es <c>pg_dump</c> del cliente de PostgreSQL;
/// en las pruebas es el mismo <c>pg_dump</c> de la imagen del contenedor, lanzado por
/// <c>docker exec</c> (esta máquina no tiene instaladas las herramientas de cliente).
/// </summary>
public interface IVolcadoBaseDatos
{
    /// <summary>
    /// Devuelve el volcado de la base de la cadena de conexión, en SQL plano.
    /// </summary>
    /// <exception cref="CopiaSeguridadFallidaException">El volcado no se pudo hacer.</exception>
    Task<byte[]> VolcarAsync(string cadenaConexion, CancellationToken ct);
}

/// <summary>
/// Volcado con <c>pg_dump</c> en formato SQL plano, leído de su salida estándar.
/// <para>
/// Tres decisiones, y las tres importan:
/// </para>
/// <list type="bullet">
/// <item>
/// <strong>La contraseña nunca va en la línea de órdenes.</strong> Solo se pasan host, puerto, usuario
/// y base; la contraseña viaja por <c>PGPASSWORD</c> en el entorno del proceso. Una línea de órdenes
/// se ve en la lista de procesos de la máquina.
/// </item>
/// <item>
/// <strong>El volcado no toca el disco.</strong> <c>pg_dump</c> escribe a su salida estándar y aquí se
/// lee como bytes, sin decodificar: el volcado es el único sitio donde el texto de la base está en
/// claro, y así no llega a ser un archivo temporal que alguien encuentre después.
/// </item>
/// <item>
/// <strong>Sin propietario ni permisos.</strong> El volcado lo hace <c>rent_backup</c>, que no es el
/// propietario de las tablas y no tiene permiso para leer los ACL. Con esas opciones la copia son
/// los datos y el esquema, y quien restaura vuelve a asignar el propietario (runbook FP-03).
/// </item>
/// </list>
/// </summary>
public sealed class VolcadoPgDump(string ejecutable, string[] argumentosBase) : IVolcadoBaseDatos
{
    /// <summary>El cliente de PostgreSQL, que en producción está en el <c>PATH</c> del servicio.</summary>
    public static VolcadoPgDump EnProduccion() => new("pg_dump", []);

    public async Task<byte[]> VolcarAsync(string cadenaConexion, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cadenaConexion);

        var conexion = new NpgsqlConnectionStringBuilder(cadenaConexion);
        if (string.IsNullOrWhiteSpace(conexion.Host)
            || string.IsNullOrWhiteSpace(conexion.Username)
            || string.IsNullOrWhiteSpace(conexion.Database))
        {
            throw new CopiaSeguridadFallidaException(
                "La cadena de conexión del backup tiene que traer host, usuario y base de datos.");
        }

        var argumentos = new List<string>(argumentosBase)
        {
            "--host", conexion.Host,
            "--port", conexion.Port.ToString(CultureInfo.InvariantCulture),
            "--username", conexion.Username,
            "--dbname", conexion.Database,
            "--format=plain",
            "--no-owner",
            "--no-privileges",
        };

        var inicio = new ProcessStartInfo(ejecutable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,

            // UTF-8 explícito para que el lector no intente detectar la marca de orden de bytes: el
            // volcado se copia como bytes y no se decodifica en ningún momento.
            StandardOutputEncoding = Encoding.UTF8,
        };

        foreach (var argumento in argumentos)
        {
            inicio.ArgumentList.Add(argumento);
        }

        inicio.Environment["PGPASSWORD"] = conexion.Password ?? string.Empty;

        using var proceso = Process.Start(inicio)
            ?? throw new CopiaSeguridadFallidaException(
                $"No se pudo arrancar '{ejecutable}'. Las herramientas de cliente de PostgreSQL "
                + "tienen que estar instaladas en el servidor.");

        // La salida de error se lee a la vez que sale el volcado: si se llena la tubería y nadie la
        // lee, pg_dump se queda bloqueado esperando a que alguien la vacíe.
        var error = proceso.StandardError.ReadToEndAsync(ct);
        using var volcado = new MemoryStream();
        await proceso.StandardOutput.BaseStream.CopyToAsync(volcado, ct);

        await proceso.WaitForExitAsync(ct);
        var textoError = await error;

        if (proceso.ExitCode != 0)
        {
            throw new CopiaSeguridadFallidaException(
                $"pg_dump terminó con el código {proceso.ExitCode}: {Recortar(textoError)}");
        }

        if (volcado.Length == 0)
        {
            throw new CopiaSeguridadFallidaException(
                "pg_dump terminó bien pero no devolvió nada por su salida estándar.");
        }

        return volcado.ToArray();
    }

    private static string Recortar(string texto) =>
        texto.Length <= 500 ? texto.Trim() : texto[..500].Trim() + "…";
}

/// <summary>
/// Cifrado del archivo de copia de seguridad: AES-256-GCM con nonce aleatorio, como el resto del
/// proyecto, pero en binario y no en texto.
/// <para>
/// No se reutiliza <see cref="ServicioCifrado"/> porque su formato es una cadena
/// (<c>v1:nonce:base64</c>) pensada para una columna de base de datos: un volcado de varios megas
/// en base64 ocupa un tercio más, y la clave es otra (<c>Backup__ClaveCifrado</c>), no
/// <c>Seguridad__CifradoClave</c>. El nonce es de 12 bytes y distinto en cada copia, que es lo
/// único que no se puede relajar: reutilizarlo con la misma clave rompe el cifrado de forma
/// recuperable.
/// </para>
/// <para>
/// El archivo en disco es:
/// </para>
/// <code>
/// "ARBK" | versión (1 byte) | nonce (12) | etiqueta (16) | SHA-256 del contenido en claro (32) | cifrado
/// </code>
/// <para>
/// La cabecera va en claro porque tiene que poder leerse sin la clave (para saber qué versión es y
/// dar un error útil). El contenido va cifrado y autenticado. La etiqueta de GCM ya detecta cualquier
/// byte cambiado, y el SHA-256 del contenido en claro es la suma de verificación que deja
/// constancia de con qué se construyó el archivo.
/// </para>
/// </summary>
public sealed class ServicioCifradoCopia
{
    /// <summary>Firma del formato, para no intentar descifrar un archivo que no es una copia.</summary>
    public static ReadOnlySpan<byte> Magic => "ARBK"u8;

    private const byte VersionFormato = 1;

    private const int BytesVersion = 1;
    private const int BytesHuella = 32;

    private readonly byte[] _clave;

    public ServicioCifradoCopia(OpcionesBackup opciones) => _clave = opciones.ClaveCifrado;

    /// <summary>Tamaño fijo de la cabecera en claro.</summary>
    public static int BytesCabecera => Magic.Length + BytesVersion + ServicioCifrado.BytesNonce + ServicioCifrado.BytesEtiqueta + BytesHuella;

    /// <summary>Cifra el contenido y devuelve el archivo completo, cabecera incluida.</summary>
    public byte[] Cifrar(ReadOnlySpan<byte> claro)
    {
        var nonce = RandomNumberGenerator.GetBytes(ServicioCifrado.BytesNonce);
        var etiqueta = new byte[ServicioCifrado.BytesEtiqueta];
        var cifrado = new byte[claro.Length];

        using var aes = new AesGcm(_clave, ServicioCifrado.BytesEtiqueta);

        // Argumentos con nombre: Encrypt y Decrypt no ordenan igual los parámetros y un cruce
        // silencioso cifraría con el nonce equivocado sin quejarse.
        aes.Encrypt(
            nonce: nonce,
            plaintext: claro,
            ciphertext: cifrado,
            tag: etiqueta,
            associatedData: ReadOnlySpan<byte>.Empty);

        var archivo = new byte[BytesCabecera + cifrado.Length];
        var destino = archivo.AsSpan();
        Magic.CopyTo(destino);
        destino[Magic.Length] = VersionFormato;

        var cursor = Magic.Length + BytesVersion;
        nonce.CopyTo(destino[cursor..]);
        cursor += ServicioCifrado.BytesNonce;
        etiqueta.CopyTo(destino[cursor..]);
        cursor += ServicioCifrado.BytesEtiqueta;
        SHA256.HashData(claro).CopyTo(destino[cursor..]);
        cursor += BytesHuella;
        cifrado.CopyTo(destino[cursor..]);

        return archivo;
    }

    /// <summary>
    /// Descifra el archivo y comprueba la huella del contenido. Si un byte del archivo cambió, la
    /// etiqueta de GCM no cuadra y salta <see cref="CryptographicException"/>: es preferible a
    /// devolver basura.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// El archivo no es una copia de Atlas Rent, o su contenido no cuadra con la huella guardada.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// La etiqueta no cuadra: el archivo está dañado o se cifró con otra clave.
    /// </exception>
    public byte[] Descifrar(ReadOnlySpan<byte> archivo)
    {
        if (archivo.Length < BytesCabecera
            || !archivo[..Magic.Length].SequenceEqual(Magic)
            || archivo[Magic.Length] != VersionFormato)
        {
            throw new InvalidOperationException(
                "El archivo no es una copia de seguridad de Atlas Rent recognizable.");
        }

        var cursor = Magic.Length + BytesVersion;
        var nonce = archivo.Slice(cursor, ServicioCifrado.BytesNonce).ToArray();
        cursor += ServicioCifrado.BytesNonce;
        var etiqueta = archivo.Slice(cursor, ServicioCifrado.BytesEtiqueta).ToArray();
        cursor += ServicioCifrado.BytesEtiqueta;
        var huellaEsperada = archivo.Slice(cursor, BytesHuella).ToArray();
        cursor += BytesHuella;

        var cifrado = archivo[cursor..].ToArray();
        var claro = new byte[cifrado.Length];

        using var aes = new AesGcm(_clave, ServicioCifrado.BytesEtiqueta);
        aes.Decrypt(
            nonce: nonce,
            ciphertext: cifrado,
            tag: etiqueta,
            plaintext: claro,
            associatedData: ReadOnlySpan<byte>.Empty);

        if (!SHA256.HashData(claro).AsSpan().SequenceEqual(huellaEsperada))
        {
            throw new InvalidOperationException(
                "El contenido descifrado no cuadra con la huella guardada en la cabecera.");
        }

        return claro;
    }
}

/// <summary>La copia de seguridad falló. Se propaga sin envolver para que el motivo llegue entero.</summary>
public sealed class CopiaSeguridadFallidaException(string mensaje) : Exception(mensaje);