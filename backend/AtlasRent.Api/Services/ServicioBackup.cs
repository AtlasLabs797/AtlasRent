using System.Security.Cryptography;
using System.Text;
using AtlasRent.Api.Dominio;
using Serilog;

namespace AtlasRent.Api.Services;

/// <summary>
/// Resultado de una ejecución de copia de seguridad.
/// </summary>
/// <param name="Fecha">Fecha de negocio a la que pertenece la copia.</param>
/// <param name="RutaArchivo">Ruta del archivo cifrado en <c>Backup__CarpetaLocal</c>.</param>
/// <param name="Bytes">Tamaño del archivo cifrado.</param>
/// <param name="Documentos">Cuántos documentos de propiedad entran en la copia.</param>
/// <param name="YaExistia">
/// <c>true</c> cuando ya había copia de esa fecha y se ha reutilizado tal cual, sin volver a
/// generarla. Es la idempotencia que pide el plan: una copia por fecha.
/// </param>
/// <param name="Rotados">Copias antiguas que la rotación ha borrado al dejar sitio.</param>
public sealed record ResultadoCopia(
    DateOnly Fecha,
    string RutaArchivo,
    long Bytes,
    int Documentos,
    bool YaExistia,
    IReadOnlyList<string> Rotados);

/// <summary>
/// Una copia que se ha abierto, descifrado y comprobado de principio a fin. Lleva el contenido ya
/// descifrado en memoria porque toda la comprobación se hace sobre los bytes descifrados, no sobre
/// los del archivo.
/// </summary>
public sealed class CopiaVerificada(string rutaArchivo, DateOnly fecha, byte[] contenido)
{
    private const string EntradaDump = "bd.sql";
    private const string PrefijoDocumentos = "documentos/";

    private readonly Lazy<IReadOnlyList<(string Nombre, byte[] Datos)>> _entradas =
        new(() => ContenedorCopia.Desempaquetar(contenido));

    public string RutaArchivo { get; } = rutaArchivo;

    public DateOnly Fecha { get; } = fecha;

    public IReadOnlyList<(string Nombre, byte[] Datos)> Entradas => _entradas.Value;

    /// <summary>El volcado de la base de datos, ya descifrado.</summary>
    public byte[] Dump() => Entrada(EntradaDump)
        ?? throw new InvalidOperationException("La copia no trae el volcado de la base de datos.");

    /// <summary>Los documentos de propiedad de la copia, con su nombre en la entrada.</summary>
    public IReadOnlyList<(string Nombre, byte[] Datos)> Documentos() => Entradas
        .Where(entrada => entrada.Nombre.StartsWith(PrefijoDocumentos, StringComparison.Ordinal))
        .Select(entrada => (entrada.Nombre[PrefijoDocumentos.Length..], entrada.Datos))
        .ToList();

    private byte[]? Entrada(string nombre) => Entradas
        .FirstOrDefault(entrada => string.Equals(entrada.Nombre, nombre, StringComparison.Ordinal))
        .Datos;
}

/// <summary>
/// Cómo se meten el volcado y los documentos en un solo archivo antes de cifrarlo.
/// <para>
/// Es un contenedor de entradas con longitud delante, no un ZIP y no un TAR: son tres líneas de
/// formato y ninguna dependencia nueva, que es lo que hace falta para poder descifrar una copia en
/// el Watchdog sin instalar nada.
/// </para>
/// </summary>
internal static class ContenedorCopia
{
    private const string Marca = "ARBK1";

    public static byte[] Empaquetar(IReadOnlyList<(string Nombre, byte[] Datos)> entradas)
    {
        using var flujo = new MemoryStream();
        using var escritor = new BinaryWriter(flujo, Encoding.UTF8, leaveOpen: true);

        escritor.Write(Marca);
        foreach (var (nombre, datos) in entradas)
        {
            var nombreEnBytes = Encoding.UTF8.GetBytes(nombre);
            escritor.Write(nombreEnBytes.Length);
            escritor.Write(nombreEnBytes);
            escritor.Write((long)datos.Length);
            escritor.Write(datos);
        }

        // Fin de entradas: un nombre de longitud cero.
        escritor.Write(0);

        return flujo.ToArray();
    }

    public static IReadOnlyList<(string Nombre, byte[] Datos)> Desempaquetar(byte[] contenido)
    {
        using var lector = new BinaryReader(new MemoryStream(contenido, writable: false), Encoding.UTF8);

        if (lector.ReadString() != Marca)
        {
            throw new InvalidOperationException("El contenido descifrado no es un contenedor de copia.");
        }

        var entradas = new List<(string Nombre, byte[] Datos)>();
        while (true)
        {
            var longitudNombre = lector.ReadInt32();
            if (longitudNombre == 0)
            {
                return entradas;
            }

            var nombre = Encoding.UTF8.GetString(lector.ReadBytes(longitudNombre));
            var longitud = lector.ReadInt64();

            // Un archivo corrupto no puede pedir una reserva de memoria arbitraria: si lo que dice
            // la longitud no cabe ni de lejos en lo que queda, está dañado.
            if (longitud < 0 || longitud > contenido.Length)
            {
                throw new InvalidOperationException(
                    "El contenedor de la copia está dañado: una entrada declara un tamaño imposible.");
            }

            entradas.Add((nombre, lector.ReadBytes((int)longitud)));
        }
    }
}

/// <summary>
/// Copia de seguridad cifrada de la base de datos y de los documentos de propiedad, con rotación.
/// <para>
/// El orden de las operaciones es el que hace que esto sirva de algo:
/// </para>
/// <list type="number">
/// <item>Si ya hay copia de hoy y verifica, se reutiliza (una copia por fecha).</item>
/// <item>Se vuelca la base con <c>rent_backup</c> y <b>se comprueba que trae filas</b>.</item>
/// <item>Se cifran volcado y documentos, y se escribe el archivo.</item>
/// <item>Se vuelve a abrir <b>el archivo del disco</b> y se comprueba.</item>
/// <item>Solo entonces se rota.</item>
/// </list>
/// <para>
/// Los pasos 2 y 4 son los que evitan el peor fallo posible: una copia que dice "correcto" y está
/// vacía. Sin ellos, un <c>pg_dump</c> al que la RLS le oculta todas las filas sale con código 0 y
/// deja un archivo que parece un backup y no tiene ni un dato.
/// </para>
/// </summary>
public sealed class ServicioBackup(
    OpcionesBackup opciones,
    IVolcadoBaseDatos volcado,
    AlmacenDocumentos almacen,
    ServicioCifradoCopia cifrado,
    TimeProvider reloj,
    IAlmacenRemoto? almacenRemoto = null)
{
    private const string EntradaDump = "bd.sql";
    private const string PrefijoDocumentos = "documentos/";

    /// <summary>
    /// La línea con la que PostgreSQL cierra un bloque <c>COPY</c>: una barra invertida y un punto.
    /// Va en cadena literal verbatim para que la barra invertida no se coma a sí misma al escapar.
    /// </summary>
    private static ReadOnlySpan<byte> FinDeCopia => @"\."u8;

    /// <summary>Carpeta de las copias, ya normalizada.</summary>
    public string Carpeta => Path.GetFullPath(opciones.CarpetaLocal);

    public DateOnly FechaDeHoy => TiempoNegocio.FechaEnZonaNegocio(reloj.GetUtcNow());

    public static string NombreDeCopia(DateOnly fecha) =>
        $"{OpcionesBackup.PrefijoArchivo}{fecha:yyyy-MM-dd}{OpcionesBackup.ExtensionArchivo}";

    /// <summary>
    /// Genera la copia de hoy, o reutiliza la que ya había. Lanza
    /// <see cref="CopiaSeguridadFallidaException"/> si algo sale mal, y en ese caso no se rota nada:
    /// la rotación va al final y solo cuando la copia nueva ya está escrita y comprobada.
    /// </summary>
    public async Task<ResultadoCopia> EjecutarAsync(string cadenaConexion, CancellationToken ct)
    {
        var fecha = FechaDeHoy;
        var ruta = Path.Combine(Carpeta, NombreDeCopia(fecha));
        Directory.CreateDirectory(Carpeta);

        var previa = await VerificarAsync(ruta, ct);
        if (previa is not null)
        {
            Log.Information(
                "La copia del {Fecha} ya existe y verifica; no se vuelve a generar ({Bytes} entradas)",
                fecha, previa.Entradas.Count);

            return new ResultadoCopia(fecha, ruta, new FileInfo(ruta).Length, previa.Documentos().Count, true, []);
        }

        if (File.Exists(ruta))
        {
            // Estaba pero no verifica: un intento anterior se quedó a medias o el archivo está
            // dañado. Se rehace, porque una copia ilegible no es una copia.
            Log.Warning("La copia del {Fecha} no verifica; se vuelve a generar", fecha);
        }

        var contenido = await ConstruirAsync(cadenaConexion, ct);
        await EscribirAsync(ruta, cifrado.Cifrar(contenido), ct);

        // La comprobación se hace sobre el archivo que hay en el disco, no sobre los bytes que
        // aún estaban en memoria: lo que se conserva es lo que se acaba de comprobar.
        var copia = await VerificarAsync(ruta, ct)
            ?? throw new CopiaSeguridadFallidaException(
                $"La copia del {fecha} se escribió pero no supera la verificación. No se rota nada.");

        // TODO(A#): aquí está el gancho de aviso. El fallo sale por Serilog (Error/Critical, con el
        // motivo y sin datos de la base), que es lo único que hay hoy; el envío de correo al
        // responsable llega con el sistema de alertas, en fases posteriores. Lo que no puede esperar
        // es que quede en un log que nadie mira: cuando exista, este throw es el punto desde el que
        // hay que disparar la alerta, y el fallo del remoto de más abajo también.

        var rotados = Rotar();
        Log.Information(
            "Copia del {Fecha} correcta: {Entradas} entradas, {Documentos} documentos; {Rotados} copias antiguas borradas",
            fecha, copia.Entradas.Count, copia.Documentos().Count, rotados.Count);

        await SubirAsync(ruta, ct);

        return new ResultadoCopia(
            fecha, ruta, new FileInfo(ruta).Length, copia.Documentos().Count, false, rotados);
    }

    /// <summary>
    /// Abre, descifra y comprueba una copia. Devuelve <c>null</c> si el archivo no existe, no se
    /// puede descifrar o no trae datos: quien llama decide si eso es un fallo, y no se tira una
    /// excepción que podría ser un simple "hoy todavía no hay copia".
    /// </summary>
    public async Task<CopiaVerificada?> VerificarAsync(string rutaArchivo, CancellationToken ct)
    {
        if (!File.Exists(rutaArchivo))
        {
            return null;
        }

        var fecha = FechaDelNombre(rutaArchivo);
        if (fecha is null)
        {
            Log.Error(
                "El archivo {Archivo} no es una copia con nombre de fecha y no se intenta abrir",
                Path.GetFileName(rutaArchivo));
            return null;
        }

        byte[] descifrado;
        try
        {
            descifrado = cifrado.Descifrar(await File.ReadAllBytesAsync(rutaArchivo, ct));
        }
        catch (Exception e) when (e is CryptographicException or InvalidOperationException or FormatException)
        {
            Log.Error(
                "La copia {Archivo} no se puede descifrar ({Tipo}); se considera inválida: {Motivo}",
                Path.GetFileName(rutaArchivo), e.GetType().Name, e.Message);
            return null;
        }

        var copia = new CopiaVerificada(rutaArchivo, fecha.Value, descifrado);

        byte[] dump;
        try
        {
            dump = copia.Dump();
        }
        catch (InvalidOperationException e)
        {
            Log.Error(
                "La copia {Archivo} no trae volcado de la base de datos: {Motivo}",
                Path.GetFileName(rutaArchivo), e.Message);
            return null;
        }

        if (!TieneFilas(dump))
        {
            // Es el fallo silencioso que motiva la comprobación: el archivo sale bien formado y
            // sin una sola fila. Casi siempre es RLS quitándole las filas al rol de backup.
            Log.Error(
                "La copia {Archivo} no contiene ninguna fila: se considera inválida. "
                + "Revisar los permisos de lectura de rent_backup y las políticas de RLS",
                Path.GetFileName(rutaArchivo));
            return null;
        }

        return copia;
    }

    /// <summary>
    /// La copia más reciente que verifica, o <c>null</c> si no hay ninguna. Es lo que exige el
    /// Watchdog antes de tocar nada: sin una copia previa buena, no se restaura ni se actualiza.
    /// </summary>
    public async Task<CopiaVerificada?> UltimaCopiaValidaAsync(CancellationToken ct)
    {
        foreach (var ruta in CopiasOrdenadas())
        {
            var copia = await VerificarAsync(ruta, ct);
            if (copia is not null)
            {
                return copia;
            }
        }

        return null;
    }

    /// <summary>
    /// Copias de la carpeta, de la más reciente a la más antigua. Solo mira los archivos cuyo
    /// nombre es el del plan, para no intentar descifrar lo que hubiera suelto alguien por ahí.
    /// </summary>
    public IReadOnlyList<string> CopiasOrdenadas() => Directory
        .EnumerateFiles(Carpeta, $"{OpcionesBackup.PrefijoArchivo}*{OpcionesBackup.ExtensionArchivo}")
        .Where(ruta => FechaDelNombre(ruta) is not null)
        .OrderByDescending(ruta => FechaDelNombre(ruta))
        .ThenByDescending(ruta => ruta, StringComparer.Ordinal)
        .ToList();

    private async Task<byte[]> ConstruirAsync(string cadenaConexion, CancellationToken ct)
    {
        var dump = await volcado.VolcarAsync(cadenaConexion, ct);

        if (!TieneFilas(dump))
        {
            throw new CopiaSeguridadFallidaException(
                "El volcado de la base de datos salió sin ninguna fila. Con la RLS activa esto es lo "
                + "que pasa si el rol de backup no tiene política de lectura: el proceso termina "
                + "bien y el archivo no tiene nada. No se escribe ninguna copia.");
        }

        var entradas = new List<(string Nombre, byte[] Datos)> { (EntradaDump, dump) };
        entradas.AddRange(await DocumentosAsync(ct));

        Log.Information(
            "Volcado de {Bytes} bytes con filas y {Documentos} documentos para empaquetar",
            dump.Length, entradas.Count - 1);

        return ContenedorCopia.Empaquetar(entradas);
    }

    private async Task<IReadOnlyList<(string Nombre, byte[] Datos)>> DocumentosAsync(CancellationToken ct)
    {
        var entradas = new List<(string Nombre, byte[] Datos)>();
        if (!Directory.Exists(almacen.Carpeta))
        {
            return entradas;
        }

        // El nombre en la entrada es el relativo a la carpeta de documentos, con separador "/", para
        // que una copia hecha en Windows se pueda restaurar en el servidor y al revés.
        foreach (var ruta in Directory.EnumerateFiles(almacen.Carpeta, "*", SearchOption.AllDirectories))
        {
            var relativo = Path.GetRelativePath(almacen.Carpeta, ruta).Replace('\\', '/');
            entradas.Add((
                PrefijoDocumentos + relativo,
                await File.ReadAllBytesAsync(ruta, ct)));
        }

        return entradas;
    }

    /// <summary>
    /// Escribe por un camino provisional y deja lo anterior intacto hasta el final: si el proceso se cae a
    /// mitad, el archivo de la copia anterior sigue siendo un archivo entero y descifrable, y no un
    /// trozo que parece una copia.
    /// </summary>
    private static async Task EscribirAsync(string ruta, byte[] contenido, CancellationToken ct)
    {
        var provisional = ruta + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(provisional, contenido, ct);
            File.Move(provisional, ruta, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(provisional);
            }
            catch (IOException)
            {
                // Un temporal que no se puede borrar no invalida la copia ya escrita.
            }
        }
    }

    /// <summary>
    /// Deja solo las <c>Backup__Conservar</c> más recientes. La más reciente nunca se borra aunque
    /// el número a conservar sea uno, y el borrado va siempre después de que la copia nueva esté
    /// escrita y verificada.
    /// </summary>
    private IReadOnlyList<string> Rotar()
    {
        var copias = CopiasOrdenadas();
        var sobrantes = copias.Skip(opciones.Conservar).ToList();

        foreach (var ruta in sobrantes)
        {
            File.Delete(ruta);
            Log.Information("Copia antigua borrada por rotación: {Archivo}", Path.GetFileName(ruta));
        }

        return sobrantes.Select(Path.GetFileName).OfType<string>().ToList();
    }

    private async Task SubirAsync(string ruta, CancellationToken ct)
    {
        if (almacenRemoto is null)
        {
            // TODO(A#): la subida a Google Drive espera a F0-04 (crear las carpetas y registrar el
            // cliente OAuth). Hasta entonces la copia vive solo en local, e IAlmacenRemoto es el
            // punto donde engancharla.
            Log.Information(
                "Copia guardada solo en local: no hay almacén remoto configurado (F0-04 pendiente)");
            return;
        }

        try
        {
            await almacenRemoto.SubirAsync(ruta, ct);
        }
        catch (NotImplementedException e)
        {
            // La copia local ya está escrita y comprobada: que la remota no exista no la invalida,
            // pero tiene que quedar en el registro y no pasar desapercibido.
            Log.Warning("No se pudo subir la copia al almacén remoto: {Motivo}", e.Message);
        }
    }

    /// <summary>
    /// Si el volcado trae alguna fila. En formato SQL plano, <c>pg_dump</c> escribe cada tabla como
    /// <c>COPY tabla (columnas) FROM stdin;</c> seguido de las filas y cerrado con <c>\.</c>. Una
    /// tabla sin filas deja el bloque vacío: por eso se mira el contenido del bloque y no que el
    /// bloque exista, que siempre existe aunque no haya datos.
    /// </summary>
    internal static bool TieneFilas(ReadOnlySpan<byte> dump)
    {
        var copia = dump.ToArray();
        var inicio = 0;
        var dentroDeCopia = false;

        while (inicio < copia.Length)
        {
            var fin = Array.IndexOf(copia, (byte)'\n', inicio);
            if (fin < 0)
            {
                fin = copia.Length;
            }

            var linea = copia.AsSpan(inicio, fin - inicio);

            if (!dentroDeCopia && EsCabeceraCopy(linea))
            {
                dentroDeCopia = true;
            }
            else if (dentroDeCopia)
            {
                if (linea.SequenceEqual(FinDeCopia))
                {
                    dentroDeCopia = false;
                }
                else if (!linea.IsEmpty && !EsSoloEspacios(linea))
                {
                    return true;
                }
            }

            inicio = fin + 1;
        }

        return false;
    }

    private static bool EsCabeceraCopy(ReadOnlySpan<byte> linea) =>
        linea.StartsWith("COPY "u8) && linea.EndsWith(" FROM stdin;"u8);

    /// <summary>Si la línea no tiene nada más que espacios y tabuladores.</summary>
    private static bool EsSoloEspacios(ReadOnlySpan<byte> linea)
    {
        foreach (var byte_ in linea)
        {
            if (byte_ is not ((byte)' ' or (byte)'\t'))
            {
                return false;
            }
        }

        return true;
    }

    private static DateOnly? FechaDelNombre(string ruta)
    {
        var nombre = Path.GetFileNameWithoutExtension(ruta);
        var fecha = nombre.Length > OpcionesBackup.PrefijoArchivo.Length
            ? nombre[OpcionesBackup.PrefijoArchivo.Length..]
            : string.Empty;

        return DateOnly.TryParseExact(fecha, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var valor)
            ? valor
            : null;
    }
}

/// <summary>
/// Destino fuera de la máquina de la copia de seguridad. Existe para que subir a Google Drive sea
/// cambiar una implementación, no reescribir el servicio de copia.
/// </summary>
public interface IAlmacenRemoto
{
    /// <summary>Sube el archivo cifrado ya escrito en disco. Nunca recibe la clave ni el contenido en claro.</summary>
    Task SubirAsync(string rutaArchivo, CancellationToken ct);
}

/// <summary>
/// Implementación pendiente de Google Drive. Lanza <see cref="NotImplementedException"/> a propósito:
/// simular una subida que no existe daría una falsa sensación de que el backup está a salvo fuera.
/// <para>
/// TODO(A#): implementar cuando F0-04 deje las carpetas de Drive creadas y el cliente OAuth
/// registrado (<c>Backup__GoogleDrive__Carpeta</c> y <c>Backup__GoogleDrive__Credenciales</c>).
/// </para>
/// </summary>
public sealed class AlmacenRemotoDriveNoDisponible : IAlmacenRemoto
{
    public Task SubirAsync(string rutaArchivo, CancellationToken ct) => throw new NotImplementedException(
        "La subida a Google Drive no está implementada: depende de F0-04 (carpetas de Drive creadas "
        + "y cliente OAuth registrado). La copia sigue estando en Backup__CarpetaLocal, que es donde "
        + "de momento se conserva y desde donde el Watchdog restaura.");
}