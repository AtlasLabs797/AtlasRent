using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.Services;
using AtlasRent.Tests.Infra;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasRent.Tests.TipoCambio;

/// <summary>
/// F3-01 contra la fuente: guarda el valor del día, no duplica al repetirse y, cuando la fuente
/// falla, avisa sin guardar nada y sin tumbar el trabajo (docs/PLAN.md: "Fallo de API → alerta y
/// carga manual").
/// </summary>
[Collection(nameof(ColeccionBd))]
public sealed class TipoCambioFuenteTests(ContenedorBdFixture bd)
{
    private const string Contrasena = "Clave-de-prueba-2026";

    /// <summary>
    /// La pasada de la fuente guarda el valor de hoy con su fecha de negocio y con su fuente, y
    /// correr el trabajo dos veces el mismo día deja una sola fila.
    /// <para>
    /// Lo que se comprueba es el criterio de aceptación entero: la fecha es la del día de negocio
    /// (Santo Domingo) y no la de UTC, y la fuente queda guardada porque de eso depende que una
    /// carga manual sea respetada mañana. Y la segunda pasada no crea una segunda fila ni falla: lo
    /// primero es el índice único (fecha, moneda), así que un trabajo reintentado por un corte de red
    /// no deja basura.
    /// </para>
    /// </summary>
    [Fact]
    public async Task LaFuenteGuardaElValorDelDiaYRepetirElTrabajoNoCreaUnaSegundaFila()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = AyudaTipoCambio.RelojDePrueba(11);
        var fuente = FuenteTipoCambioFalsa.ConValor(61.2345678m);

        await using var contexto = AyudaTipoCambio.ContextoDelTrabajo(bd);
        var servicio = AyudaTipoCambio.ServicioDe(contexto, fuente, reloj);
        var trabajo = AyudaTipoCambio.Trabajo(servicio);
        var hoy = servicio.FechaDeHoy();
        var antes = await contexto.TipoCambio.CountAsync(ct);

        // Dos ejecuciones del trabajo. Si la segunda creara otra fila, el índice único saltaría y
        // este test fallaría aquí, que es exactamente lo que hay que comprobar.
        await trabajo.EjecutarAsync(ct);
        await trabajo.EjecutarAsync(ct);

        var fila = await contexto.TipoCambio.AsNoTracking()
            .SingleAsync(t => t.Moneda == Moneda.DOP && t.Fecha == hoy, ct);

        fila.Valor.Should().Be(61.234568m, "el valor se redondea a los 6 decimales del numeric(18,6)");
        fila.Fuente.Should().Be(FuentesTipoCambio.Api,
            "el valor lo trajo la fuente, y eso es lo que lo hace pisable mañana");
        fila.CreatedBy.Should().BeNull("el trabajo no tiene persona detrás: lo dice origen = \"job\"");

        (await contexto.TipoCambio.CountAsync(ct)).Should().Be(antes + 1,
            "dos pasadas del trabajo el mismo día son una sola fila, no dos");

        fuente.Consultas.Should().Be(2, "una consulta por ejecución y por moneda, no una por fila");
    }

    /// <summary>
    /// Una fila que vino de la fuente sí se actualiza con el valor del día: la fuente manda mientras
    /// nadie la sustituya.
    /// <para>
    /// Es el otro lado de la regla de precedencia, y importa porque si no, el trabajo solo serviría
    /// el primer día: una fila que nadie puede volver a escribir dejaría congelado para siempre el
    /// valor con el que se creó.
    /// </para>
    /// </summary>
    [Fact]
    public async Task LaFuenteActualizaUnaFilaQueVinoDeLaFuente()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = AyudaTipoCambio.RelojDePrueba(12);

        await using var contexto = AyudaTipoCambio.ContextoDelTrabajo(bd);

        var primera = FuenteTipoCambioFalsa.ConValor(60m);
        await AyudaTipoCambio.ServicioDe(contexto, primera, reloj).ActualizarDesdeLaFuenteAsync(ct);

        // El segundo trabajo del mismo día, con la fuente ya corregida un poco.
        var reloj2 = AyudaTipoCambio.RelojDePrueba(12);
        reloj2.Avanzar(TimeSpan.FromHours(3));
        var segunda = FuenteTipoCambioFalsa.ConValor(60.5m);
        var servicio = AyudaTipoCambio.ServicioDe(contexto, segunda, reloj2);

        var resumen = await servicio.ActualizarDesdeLaFuenteAsync(ct);

        resumen.Fallo.Should().BeFalse();
        resumen.Cargados.Should().Be(1, "lo que vino de la fuente lo vuelve a escribir la fuente");
        resumen.Respetados.Should().Be(0);

        var fila = await contexto.TipoCambio.AsNoTracking()
            .SingleAsync(t => t.Moneda == Moneda.DOP && t.Fecha == servicio.FechaDeHoy(), ct);

        fila.Valor.Should().Be(60.5m);
        fila.Fuente.Should().Be(FuentesTipoCambio.Api);
    }

    /// <summary>
    /// La fuente que falla no deja valor y deja aviso, y el trabajo no se cae.
    /// <para>
    /// Esto es <i>el</i> criterio de aceptación de la tarea. Un trabajo que lanzara al fallar la API
    /// se caería cada vez que un tercero tiene un mal día, y el tipo de cambio es exactamente la
    /// dependencia externa que no puede permitirse eso: por eso avisa en vez de propagar.
    /// </para>
    /// <para>
    /// Se comprueba además que la alerta queda viva y con la clave del día: una que se cerrara al
    /// fallar el trabajo no avisaría de nada, y una clave sin fecha llenaría la bandeja con un aviso
    /// por día para siempre.
    /// </para>
    /// </summary>
    [Fact]
    public async Task LaFuenteQueFallaNoGuardaNadaAvisaYElTrabajoNoLanza()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = AyudaTipoCambio.RelojDePrueba(13);
        var fuente = FuenteTipoCambioFalsa.ConValor(61m);
        fuente.Fallo = new FalloTipoCambioException("open.er-api.com respondió 503 (Service Unavailable).");

        DateOnly hoy;
        int antes;

        await using (var contexto = AyudaTipoCambio.ContextoDelTrabajo(bd))
        {
            var servicio = AyudaTipoCambio.ServicioDe(contexto, fuente, reloj);
            hoy = servicio.FechaDeHoy();
            antes = await contexto.TipoCambio.CountAsync(ct);

            // Lo importante de estas dos líneas es que no lanzan: si el trabajo se cayera, el test
            // no llegaría al final y no habría nada que comprobar.
            await AyudaTipoCambio.Trabajo(servicio).EjecutarAsync(ct);
            await AyudaTipoCambio.Trabajo(servicio).EjecutarAsync(ct);

            (await contexto.TipoCambio.CountAsync(ct)).Should().Be(antes,
                "una fuente que falla no deja un valor a medias ni cambia el que ya había");
        }

        await using (var lectura = bd.CrearContexto(bd.CadenaSuperusuario))
        {
            var alerta = await lectura.Alerta.AsNoTracking()
                .SingleAsync(a => a.Clave == ServicioTiposCambio.ClaveFallo(hoy), ct);

            alerta.Tipo.Should().Be("tipo_cambio",
                "el tipo de aviso está previsto en la tabla desde antes de que existiera el trabajo");
            alerta.Mensaje.Should().Contain("No se pudo consultar el tipo de cambio");
            alerta.Mensaje.Should().Contain("503", "el motivo de la fuente es lo que dice qué pasó");
            alerta.ResueltaAt.Should().BeNull("el aviso está vivo mientras el cambio no llegue");
            alerta.Repeticiones.Should().Be(2,
                "fallar dos veces el mismo día actualiza el mismo aviso en vez de llenar la bandeja");
        }
    }

    /// <summary>
    /// La columna del valor es <c>numeric(18,6)</c> y el valor guardado llega redondeado a seis
    /// decimales.
    /// <para>
    /// Son dos cosas distintas y las dos importan. La escala fija los seis decimales
    /// (docs/DOC_TECNICA.md §4.2): con la convención global de dinero, <c>numeric(18,2)</c>,
    /// redondearían 61,234568 a 61,23, y ese número es el que congelaría la factura. Y el redondeo
    /// half-up de <c>Redondeo.TipoCambio</c> es lo que decide el último dígito.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ElValorSeGuardaConSeisDecimalesEnUnaColumnaNumericDeEscalaSeis()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = AyudaTipoCambio.RelojDePrueba();
        var usuario = await AyudaAuth.CrearUsuarioAsync(bd, Contrasena, rol: Roles.Admin, ct: ct);
        var fecha = AyudaTipoCambio.Dia(40);

        await using (var contexto = AyudaTipoCambio.ContextoDelTrabajo(bd))
        {
            var servicio = AyudaTipoCambio.ServicioDe(
                contexto, FuenteTipoCambioFalsa.ConValor(1m), reloj);

            await servicio.RegistrarAsync(
                fecha,
                Moneda.DOP,
                61.23456789m,
                FuentesTipoCambio.Manual,
                usuario.Id,
                "198.51.100.9",
                mutacion: null,
                ct);
        }

        await using (var lectura = bd.CrearContexto(bd.CadenaApp))
        {
            // Se lee desde la fila, no desde lo que devolvió el servicio: lo que importa es lo que
            // quedó en la base, que es lo que va a congelar una factura.
            var valor = await lectura.TipoCambio.AsNoTracking()
                .Where(t => t.Moneda == Moneda.DOP && t.Fecha == fecha)
                .Select(t => t.Valor)
                .SingleAsync(ct);

            valor.Should().Be(61.234568m, "seis decimales, half-up");

            var tipo = await lectura.Database
                .SqlQuery<string>(
                    $"""
                    SELECT format_type(a.atttypid, a.atttypmod) AS "Value"
                    FROM pg_attribute a
                    WHERE a.attrelid = 'tipo_cambio'::regclass
                      AND a.attname = 'valor'
                    """)
                .SingleAsync(ct);

            tipo.Should().Be("numeric(18,6)", "la columna es de tipo de cambio, no de dinero");
        }
    }
}
