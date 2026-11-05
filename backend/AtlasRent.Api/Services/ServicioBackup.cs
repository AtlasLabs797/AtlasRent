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
/// <para>
/// La <see cref="Fecha"/> es la que dice el **contenido**, no la del nombre del archivo. El nombre
/// es una etiqueta que alguien puede reescribir; el contenido va firmado por la etiqueta GCM.
/// </para>
/// </summary>
public sealed class CopiaVerificada(string rutaArchivo, DateOnly fecha, byte[] contenido)
{
    private const string EntradaDump = "bd.sql";
    private const string PrefijoDocumentos = "documentos/";

    private readonly Lazy<(DateOnly Fecha, IReadOnlyList<(string Nombre, byte[] Datos)> Entradas)> _abierto =
        new(() => ContenedorCopia.Desempaquetar(contenido));

    public string RutaArchivo { get; } = rutaArchivo;

    /// <summary>Fecha según el nombre del archivo. Puede no ser la real.</summary>
    public DateOnly FechaDelNombre { get; } = fecha;

    /// <summary>Fecha según el contenido descifrado, que es la que vale.</summary>
    public DateOnly Fecha => _abierto.Value.Fecha;

    public IReadOnlyList<(string Nombre, byte[] Datos)> Entradas => _abierto.Value.Entradas;

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
/// Cómo se meten la fecha, el volcado y los documentos en un solo archivo antes de cifrarlo.
/// <para>
/// Es un contenedor de entradas con longitud delante, no un ZIP y no un TAR: son unas pocas líneas de
/// formato y ninguna dependencia nueva, que es lo que hace falta para poder descifrar una copia en
/// el Watchdog sin instalar nada.
/// </para>
/// <para>
/// <strong>La fecha va dentro del contenedor, no solo en el nombre del archivo.</strong> Eso no es
/// decoración: si la fecha viviera únicamente en el nombre, quien tuviera escritura en la carpeta
/// podría renombrar una copia antigua con la fecha de hoy. La copia "de hoy" verificaría —es un
/// archivo auténtico y descifrable—, la generación se saltaría y la rotación acabaría borrando las
/// copias buenas creyendo que son viejas. Como la fecha está en el texto cifrado, la etiqueta GCM la
/// protege y un nombre que no coincida con ella se rechaza. Es el escenario clásico de subversión de
/// copias, y atarlo al contenido es lo que lo cierra.
/// </para>
/// </summary>
internal static class ContenedorCopia
{
    private const string Marca = "ARBK2";

    public static byte[] Empaquetar(DateOnly fecha, IReadOnlyList<(string Nombre, byte[] Datos)> entradas)
    {
        using var flujo = new MemoryStream();
        using var escritor = new BinaryWriter(flujo, Encoding.UTF8, leaveOpen: true);

        escritor.Write(Marca);
        escritor.Write($"{fecha:yyyy-MM-dd}");
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

    /// <summary>Abre el contenedor y devuelve la fecha que dice su propio contenido.</summary>
    public static (DateOnly Fecha, IReadOnlyList<(string Nombre, byte[] Datos)> Entradas) Desempaquetar(
        byte[] contenido)
    {
        using var lector = new BinaryReader(new MemoryStream(contenido, writable: false), Encoding.UTF8);

        var marca = lector.ReadString();
        if (marca != Marca)
        {
            throw new InvalidOperationException(
                $"El contenido descifrado no es un contenedor de copia (marca '{marca}').");
        }

        // La fecha sale del contenido cifrado, que es lo único que no se puede reescribir sin que la
        // etiqueta GCM deje de cuadrar.
        if (!DateOnly.TryParseExact(
                lector.ReadString(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var fecha))
        {
            throw new InvalidOperationException("El contenedor de la copia no lleva una fecha legible.");
        }

        var entradas = new List<(string Nombre, byte[] Datos)>();
        while (true)
        {
            var longitudNombre = lector.ReadInt32();
            if (longitudNombre == 0)
            {
                return (fecha, entradas);
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
    IAlmacenRemoto? almacenRemoto = null,
    IRegistroAlertas? alertas = null)
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
    /// <para>
    /// Cualquier fallo pasa antes por <see cref="AlentarAsync"/>. Es el "fallo → alerta" del criterio
    /// de aceptación de F1-12: un respaldo que falla en silencio no es un respaldo, es la ausencia
    /// de un respaldo con la apariencia de que todo va bien.
    /// </para>
    /// </summary>
    public async Task<ResultadoCopia> EjecutarAsync(string cadenaConexion, CancellationToken ct)
    {
        var fecha = FechaDeHoy;

        try
        {
            return await GenerarAsync(fecha, cadenaConexion, ct);
        }
        catch (Exception e)
        {
            await AlentarAsync(fecha, e);
            throw;
        }
    }

    private async Task<ResultadoCopia> GenerarAsync(DateOnly fecha, string cadenaConexion, CancellationToken ct)
    {
        var ruta = Path.Combine(Carpeta, NombreDeCopia(fecha));
        Directory.CreateDirectory(Carpeta);

        var previa = await VerificarAsync(ruta, ct);
        if (previa is not null)
        {
            Log.Information(
                "La copia del {Fecha} ya existe y verifica; no se vuelve a generar ({Bytes} entradas)",
                fecha, previa.Entradas.Count);

            // La copia local esta, pero puede que la subida de ese dia fallara: se reintenta hasta que conste.
            await SubirAsync(fecha, ruta, ct);

            return new ResultadoCopia(fecha, ruta, new FileInfo(ruta).Length, previa.Documentos().Count, true, []);
        }

        if (File.Exists(ruta))
        {
            // Estaba pero no verifica: un intento anterior se quedó a medias o el archivo está
            // dañado. Se rehace, porque una copia ilegible no es una copia.
            Log.Warning("La copia del {Fecha} no verifica; se vuelve a generar", fecha);
        }

        var contenido = await ConstruirAsync(fecha, cadenaConexion, ct);
        await EscribirAsync(ruta, cifrado.Cifrar(contenido), ct);

        // La comprobación se hace sobre el archivo que hay en el disco, no sobre los bytes que
        // aún estaban en memoria: lo que se conserva es lo que se acaba de comprobar.
        var copia = await VerificarAsync(ruta, ct)
            ?? throw new CopiaSeguridadFallidaException(
                $"La copia del {fecha} se escribió pero no supera la verificación. No se rota nada.");

        var rotados = await RotarAsync(ruta, ct);
        Log.Information(
            "Copia del {Fecha} correcta: {Entradas} entradas, {Documentos} documentos; {Rotados} copias antiguas borradas",
            fecha, copia.Entradas.Count, copia.Documentos().Count, rotados.Count);

        await SubirAsync(fecha, ruta, ct);

        return new ResultadoCopia(
            fecha, ruta, new FileInfo(ruta).Length, copia.Documentos().Count, false, rotados);
    }

    /// <summary>
    /// Deja la alerta del fallo del día. La clave lleva la fecha, así que un respaldo que falla cada
    /// hora deja una alerta con el contador de repeticiones subiendo, no veinticuatro filas.
    /// <para>
    /// El motivo solo se traslada si es del propio servicio. Una excepción inesperada puede llevar
    /// dentro la cadena de conexión o una sentencia de PostgreSQL, y una alerta es texto que se lee
    /// fuera; ahí solo queda el tipo de excepción y el detalle se queda en el log.
    /// </para>
    /// </summary>
    private async Task AlentarAsync(DateOnly fecha, Exception e)
    {
        var motivo = e is CopiaSeguridadFallidaException
            ? e.Message
            : $"fallo inesperado ({e.GetType().Name}); el detalle está en el log";
        var mensaje = $"La copia de seguridad del {fecha:yyyy-MM-dd} falló: {motivo}";

        // Se pasa como plantilla con sus propiedades y no el mensaje ya compuesto: Serilog trata
        // las llaves del texto como marcadores, y el motivo viene de una excepción.
        Log.Fatal("La copia de seguridad del {Fecha} falló: {Motivo}", fecha, motivo);

        if (alertas is not null)
        {
            await alertas.RegistrarAsync("backup", $"backup:{fecha:yyyy-MM-dd}", mensaje, CancellationToken.None);
        }
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

        // El nombre dice una fecha y el contenido dice otra. No puede ser una copia legítima: solo
        // significa que alguien renombró el archivo. Se rechaza en lugar de aceptarlo, porque aceptar
        // es justo lo que haría daño: una copia vieja pasaría por la de hoy, se saltaría la generación
        // y la rotación acabaría borrando las buenas.
        DateOnly fechaDelContenido;
        try
        {
            fechaDelContenido = copia.Fecha;
        }
        catch (InvalidOperationException e)
        {
            Log.Error(
                "La copia {Archivo} no se puede abrir como contenedor: {Motivo}",
                Path.GetFileName(rutaArchivo), e.Message);
            return null;
        }

        if (fechaDelContenido != copia.FechaDelNombre)
        {
            Log.Error(
                "La copia {Archivo} dice {FechaEnElNombre} en el nombre y {FechaEnElContenido} en el "
                + "contenido: no es una copia legítima, se rechaza",
                Path.GetFileName(rutaArchivo), copia.FechaDelNombre, fechaDelContenido);
            return null;
        }

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

    private async Task<byte[]> ConstruirAsync(DateOnly fecha, string cadenaConexion, CancellationToken ct)
    {
        ComprobarTamanoDocumentos();
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

        var total = entradas.Sum(entrada => (long)entrada.Datos.Length);
        if (total > opciones.LimiteBytesContenido)
        {
            throw new CopiaSeguridadFallidaException(
                $"El contenido de la copia ocupa {total / (1024 * 1024)} MiB y supera el limite seguro de "
                + $"{opciones.LimiteBytesContenido / (1024 * 1024)} MiB: la copia se arma entera en memoria (el "
                + "contenedor y el cifrado la duplican) y pasarse la reventaria. Hay que pasar el backup a "
                + "streams antes de que la base crezca mas (BUG-045).");
        }

        Log.Information(
            "Volcado de {Bytes} bytes con filas y {Documentos} documentos para empaquetar",
            dump.Length, entradas.Count - 1);

        // La fecha entra en el contenedor, no solo en el nombre del archivo: es lo que impide que un
        // archivo renombrado a una fecha futura se haga pasar por la copia de hoy.
        return ContenedorCopia.Empaquetar(fecha, entradas);
    }

    /// <summary>Corta antes de leer nada si solo los documentos ya pasan el limite.</summary>
    private void ComprobarTamanoDocumentos()
    {
        if (!Directory.Exists(almacen.Carpeta))
        {
            return;
        }

        var bytes = Directory.EnumerateFiles(almacen.Carpeta, "*", SearchOption.AllDirectories)
            .Sum(ruta => new FileInfo(ruta).Length);
        if (bytes > opciones.LimiteBytesContenido)
        {
            throw new CopiaSeguridadFallidaException(
                $"Los documentos de propiedad ocupan {bytes / (1024 * 1024)} MiB y superan el limite seguro "
                + $"de {opciones.LimiteBytesContenido / (1024 * 1024)} MiB de la copia en memoria (BUG-045).");
        }
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
    /// <para>
    /// El orden es por nombre, no por la fecha del contenido, para no tener que descifrar todas las
    /// copias en cada ejecución. Un archivo cuyo nombre no case con su contenido ya ha sido
    /// rechazado en <see cref="VerificarAsync"/>, así que no se cuela como copia válida en ninguna
    /// parte; aquí solo puede ocupar un hueco, nunca pasar por una copia buena.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<string>> RotarAsync(string rutaNueva, CancellationToken ct)
    {
        // Una copia que no verifica no ocupa hueco de las que se conservan (BUG-045): si contara, una
        // copia corrupta entre las recientes empujaria fuera a una buena. Tampoco se borra por ser
        // invalida (puede estar cifrada con otra clave y ser recuperable): solo se borra lo que queda
        // mas alla de las N validas mas recientes. La nueva ya esta verificada y no se vuelve a abrir.
        var sobrantes = new List<string>();
        var validas = 0;
        foreach (var ruta in CopiasOrdenadas())
        {
            if (validas >= opciones.Conservar)
            {
                sobrantes.Add(ruta);
            }
            else if (string.Equals(ruta, rutaNueva, StringComparison.OrdinalIgnoreCase)
                     || await VerificarAsync(ruta, ct) is not null)
            {
                validas++;
            }
            else
            {
                Log.Warning(
                    "La copia {Archivo} no verifica: no cuenta entre las {Conservar} a conservar",
                    Path.GetFileName(ruta), opciones.Conservar);
            }
        }

        foreach (var ruta in sobrantes)
        {
            File.Delete(ruta);
            File.Delete(RutaMarcaSubida(ruta));
            Log.Information("Copia antigua borrada por rotación: {Archivo}", Path.GetFileName(ruta));
        }

        return sobrantes.Select(Path.GetFileName).OfType<string>().ToList();
    }

    /// <summary>
    /// Archivo vacio junto a la copia que dice que ya se subio. Sin el, cada ejecucion del dia
    /// reintenta la subida; con el, no se sube dos veces. No termina en la extension de las copias,
    /// asi que ni se lista ni se verifica como tal.
    /// </summary>
    private static string RutaMarcaSubida(string ruta) => ruta + ".subida";

    private async Task SubirAsync(DateOnly fecha, string ruta, CancellationToken ct)
    {
        if (almacenRemoto is null)
        {
            // La subida a Google Drive espera a F0-04 (crear las carpetas y registrar el cliente
            // OAuth). Hasta entonces la copia vive solo en local, e IAlmacenRemoto es el punto donde
            // engancharla.
            Log.Information(
                "Copia guardada solo en local: no hay almacén remoto configurado (F0-04 pendiente)");
            return;
        }

        if (File.Exists(RutaMarcaSubida(ruta)))
        {
            return;
        }

        try
        {
            await almacenRemoto.SubirAsync(ruta, ct);
            await File.WriteAllTextAsync(RutaMarcaSubida(ruta), $"{reloj.GetUtcNow():O}", ct);
            if (alertas is not null)
            {
                await alertas.ResolverAsync($"backup-remoto:{fecha:yyyy-MM-dd}", ct);
            }
        }
        catch (Exception e)
        {
            // La copia local ya está escrita y comprobada: que la remota no exista no la invalida,
            // pero tiene que quedar en el registro y no pasar desapercibido. Un almacén remoto
            // configurado que falla es un fallo de verdad, así que también avienta alerta.
            Log.Warning("No se pudo subir la copia al almacén remoto: {Motivo}", e.Message);

            if (alertas is not null)
            {
                var mensaje =
                    $"La copia del {fecha:yyyy-MM-dd} está hecha pero no se pudo subir al almacén remoto: {e.Message}";
                await alertas.RegistrarAsync(
                    "backup", $"backup-remoto:{fecha:yyyy-MM-dd}", mensaje, ct);
            }
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

            // Un volcado con fin de línea CRLF deja un \r al final de cada línea.
            if (!linea.IsEmpty && linea[^1] == (byte)'\r')
            {
                linea = linea[..^1];
            }

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