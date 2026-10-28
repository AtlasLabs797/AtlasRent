using System.Security.Cryptography;
using System.Text;
using AtlasRent.Api.Services;
using AwesomeAssertions;
using Xunit;

namespace AtlasRent.Tests.Backup;

/// <summary>
/// F1-12: el cifrado del archivo de copia y el contenedor que junta el volcado y los documentos.
/// <para>
/// No tocan la base de datos: son comprobaciones de que lo que se escribe en disco no está en claro
/// y de que lo cifrado se puede volver a abrir con la clave.
/// </para>
/// </summary>
public sealed class CifradoCopiaTests
{
    private static OpcionesBackup Opciones(byte[] clave) => new()
    {
        CarpetaLocal = Path.Combine(Path.GetTempPath(), $"atlas-rent-copia-{Guid.NewGuid():N}"),
        ClaveCifrado = clave,
    };

    /// <summary>
    /// El contenido se puede volver a leer con la clave. Sin esto, un backup que no se puede
    /// restaurar no sirve de nada aunque se haya escrito sin errores.
    /// </summary>
    [Fact]
    public void LoCifradoSeVuelveADescifrarConLaClave()
    {
        var clave = RandomNumberGenerator.GetBytes(32);
        var claro = Encoding.UTF8.GetBytes("-- esto es el volcado con datos de la base\nCOPY public.cliente FROM stdin;");
        var cifrado = new ServicioCifradoCopia(Opciones(clave)).Cifrar(claro);

        cifrado.Should().NotEqual(claro, "el archivo escrito no puede ser el contenido en claro");
        new ServicioCifradoCopia(Opciones(clave)).Descifrar(cifrado).Should().Equal(claro);
    }

    /// <summary>
    /// Dos cifrados del mismo contenido son distintos, porque el nonce es aleatorio. Si fueran
    /// iguales, dos copias del mismo volcado se delatarían entre sí y, peor, reutilizar el nonce
    /// con la misma clave rompe el cifrado de forma recuperable.
    /// </summary>
    [Fact]
    public void CifrarDosVecesElMismoContenidoDaArchivosDistintos()
    {
        var clave = RandomNumberGenerator.GetBytes(32);
        var servicio = new ServicioCifradoCopia(Opciones(clave));
        var claro = Encoding.UTF8.GetBytes("el mismo contenido, dos veces");

        servicio.Cifrar(claro).Should().NotEqual(servicio.Cifrar(claro));
    }

    /// <summary>Un solo byte cambiado hace que el archivo no se pueda descifrar.</summary>
    [Fact]
    public void UnByteCambiadoDejaElArchivoIlegible()
    {
        var clave = RandomNumberGenerator.GetBytes(32);
        var servicio = new ServicioCifradoCopia(Opciones(clave));
        var cifrado = servicio.Cifrar(Encoding.UTF8.GetBytes("contenido"));

        cifrado[^1] ^= 0x01;

        var accion = () => servicio.Descifrar(cifrado);
        accion.Should().Throw<CryptographicException>();
    }

    /// <summary>Con otra clave no se abre, y el motivo lo dice sin filtrar nada.</summary>
    [Fact]
    public void ConOtraClaveNoSePuedeDescifrar()
    {
        var cifrado = new ServicioCifradoCopia(Opciones(RandomNumberGenerator.GetBytes(32)))
            .Cifrar(Encoding.UTF8.GetBytes("secreto"));

        var otro = new ServicioCifradoCopia(Opciones(RandomNumberGenerator.GetBytes(32)));

        var accion = () => otro.Descifrar(cifrado);
        accion.Should().Throw<CryptographicException>();
    }

    /// <summary>El archivo lleva la firma del formato para no intentar descifrar cualquier cosa.</summary>
    [Fact]
    public void UnArchivoQueNoEsUnaCopiaSeRechazaSinIntentarDescifrar()
    {
        var servicio = new ServicioCifradoCopia(Opciones(RandomNumberGenerator.GetBytes(32)));

        var accion = () => servicio.Descifrar(Encoding.UTF8.GetBytes("esto no es una copia de Atlas Rent"));
        accion.Should().Throw<InvalidOperationException>();
    }

    /// <summary>El contenedor mete y saca las entradas con su nombre, incluidos nombres con acentos.</summary>
    [Fact]
    public void ElContenedorConservaLosNombresDeLasEntradas()
    {
        var fecha = new DateOnly(2026, 10, 10);
        var entradas = new (string, byte[])[]
        {
            ("bd.sql", Encoding.UTF8.GetBytes("volcado")),
            ("documentos/título.pdf", Encoding.UTF8.GetBytes("%PDF-1.7 con acentos: áéíóúñ")),
            ("documentos/subcarpeta/otro.bin", [0x00, 0xFF, 0x10, 0x00]),
        };

        var abierto = ContenedorCopia.Desempaquetar(ContenedorCopia.Empaquetar(fecha, entradas));

        abierto.Entradas.Select(e => e.Nombre).Should().Equal(entradas.Select(e => e.Item1));
        abierto.Entradas[1].Datos.Should().Equal(entradas[1].Item2, "los bytes van y vienen tal cual");
        abierto.Entradas[2].Datos.Should().Equal(entradas[2].Item2, "también los que no son texto");
    }

    /// <summary>
    /// La fecha viaja dentro del contenedor. Si viviera solo en el nombre del archivo, renombrar una
    /// copia vieja con la fecha de hoy la haría pasar por la copia vigente.
    /// </summary>
    [Fact]
    public void LaFechaDelContenedorEsLaDelContenidoYNoLaDelNombre()
    {
        var fecha = new DateOnly(2026, 10, 10);

        var abierto = ContenedorCopia.Desempaquetar(
            ContenedorCopia.Empaquetar(fecha, [("bd.sql", Encoding.UTF8.GetBytes("volcado"))]));

        abierto.Fecha.Should().Be(fecha);
    }

    /// <summary>
    /// Un contenedor con una entrada que declara un tamaño imposible se rechaza en vez de pedir
    /// una reserva de memoria arbitraria: es lo que hace un archivo corrupto.
    /// </summary>
    [Fact]
    public void UnContenedorCorruptoSeRechaza()
    {
        var contenedor = ContenedorCopia.Empaquetar(
            new DateOnly(2026, 10, 10), [("bd.sql", Encoding.UTF8.GetBytes("hola"))]);

        // Se localiza la entrada por su nombre y se falsea justo delante la longitud de sus datos,
        // que es lo que dice el archivo al leer. Se busca el nombre en vez de calcular la posición a
        // mano para que este test no dependa del formato exacto del contenedor.
        var nombre = Encoding.UTF8.GetBytes("bd.sql");
        var inicioNombre = Array.IndexOf(contenedor, nombre[0]);
        while (inicioNombre >= 0
            && !contenedor.AsSpan(inicioNombre, nombre.Length).SequenceEqual(nombre))
        {
            inicioNombre = Array.IndexOf(contenedor, nombre[0], inicioNombre + 1);
        }

        inicioNombre.Should().BeGreaterThan(0, "el contenedor tiene que llevar el nombre de la entrada");
        var posicionLongitud = inicioNombre + nombre.Length;
        var longitud = BitConverter.ToInt32(contenedor, posicionLongitud);
        BitConverter.GetBytes(longitud + 1_000_000).CopyTo(contenedor, posicionLongitud);

        var accion = () => ContenedorCopia.Desempaquetar(contenedor);
        accion.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// Lo que decide si un volcado trae datos o sale vacío. El caso vacío es el que rompe: con la
    /// RLS activa, <c>pg_dump</c> termina con código 0 y escribe el esquema entero con las tablas
    /// sin una sola fila.
    /// </summary>
    [Fact]
    public void ElVolcadoSeConsideraVacioSiNingunBloqueCopyTieneFilas()
    {
        var conTablasPeroSinFilas = """
            --
            -- PostgreSQL database dump
            --
            COPY public.cliente (id, nombre) FROM stdin;
            \.
            COPY public.inmueble (id, nombre) FROM stdin;
            \.
            """;

        ServicioBackup.TieneFilas(Encoding.UTF8.GetBytes(conTablasPeroSinFilas))
            .Should().BeFalse("bloques COPY vacíos es exactamente lo que sale sin permiso de lectura");
    }

    [Fact]
    public void ElVolcadoConUnaFilaSeConsideraConDatos()
    {
        var conFila = """
            COPY public.cliente (id, nombre) FROM stdin;
            8f0e0000-0000-0000-0000-000000000001	Puerta del Este
            \.
            COPY public.inmueble (id) FROM stdin;
            \.
            """;

        ServicioBackup.TieneFilas(Encoding.UTF8.GetBytes(conFila))
            .Should().BeTrue("con que una sola tabla tenga filas, la copia sirve");
    }

    /// <summary>Un volcado sin ningún bloque COPY no tiene filas y se rechaza igual que uno vacío. Una base real nunca está así (la migración siembra roles y permisos), así que es mejor no dar por buena una copia sin datos.</summary>
    [Fact]
    public void UnVolcadoSinNingunBloqueCopyNoTieneFilas()
    {
        ServicioBackup.TieneFilas(Encoding.UTF8.GetBytes("-- solo el esquema, sin tablas"))
            .Should().BeFalse();
    }
}