using System.Text.Json;
using System.Text.Json.Nodes;
using AtlasRent.Api.Authorization;
using AtlasRent.Api.Data;
using AtlasRent.Api.Dominio;
using AtlasRent.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AtlasRent.Api.Services;

/// <summary>
/// Asistente de consultas de solo lectura (F6-02). Es el camino único por el que la IA lee datos de
/// Atlas Rent, tanto desde <c>POST /api/v1/ia/consulta</c> (sesión de navegador) como desde
/// <c>POST /api/v1/agente/consulta</c> (token de agente).
/// <para>
/// <b>El orden de las comprobaciones es la seguridad de esta tarea</b>, y por eso está escrito en
/// este orden y no al revés:
/// </para>
/// <list type="number">
/// <item><b>permiso del alcance</b>, que es la acción que hoy protege el endpoint equivalente más
/// la que exige el token si la llamada viene de un agente;</item>
/// <item><b>límites</b> (por token y día, y global del mes), antes de tocar un proveedor de pago;</item>
/// <item><b>cargar datos</b> con los servicios de lectura que ya existen y con sus mismos filtros;</item>
/// <item><b>seudonimizar</b>, para que ningún dato personal salga del proceso;</item>
/// <item><b>preguntar</b> al proveedor, sin herramientas ni funciones: el modelo no elige tablas ni
/// filas, así que una inyección de prompt no tiene nada que ejecutar;</item>
/// <item><b>reidentificar</b>, para que quien pregunta vea los nombres de verdad;</item>
/// <item><b>auditar</b> qué se consultó, nunca con qué se preguntó ni qué se respondió.</item>
/// </list>
/// <para>
/// <b>Sin estado entre peticiones.</b> No hay historial, ni memoria del modelo, ni hilo de
/// conversación: cada pregunta va sola con su alcance.
/// </para>
/// </summary>
public sealed class ServicioConsultaIa(
    IEnumerable<IProveedorIA> proveedores,
    OpcionesIa opciones,
    IPolicyService politica,
    ContextoSesion sesion,
    ServicioClientes clientes,
    ServicioCuentas cuentas,
    ServicioFacturas facturas,
    ServicioContratos contratos,
    ServicioInformes informes,
    AuditoriaService auditoria,
    AtlasRentDbContext contexto,
    TimeProvider reloj)
{
    /// <summary>Acción con la que se audita toda consulta al asistente.</summary>
    public const string ConsultaAccion = "ia.consulta";

    /// <summary>Entidad que se registra en la auditoría. No lleva fila propia: es una lectura.</summary>
    private const string Entidad = "ia";

    /// <summary>Resultado de la fila que reserva un cupo antes de llamar al proveedor (SEC-029).</summary>
    private const string ResultadoReserva = "reservado";

    /// <summary>Clave del <c>pg_advisory_xact_lock</c> que serializa el conteo y la reserva de cupo.</summary>
    private const long CerrojoLimites = 7_412_029;

    /// <summary>
    /// Filas que se cargan por consulta. Es una página corta a propósito: el modelo solo razona
    /// sobre lo que ve, y 25 filas caben en su contexto sin recortarse, mientras que «tráeme el
    /// histórico entero» no cabe en ningún sitio y además es caro.
    /// </summary>
    private const int FilasPorConsulta = 25;

    /// <summary>
    /// Alcance → acciones que exige. <b>Son las que ya protegen cada endpoint de lectura</b>, ni una
    /// más: <c>cuentas</c> pide además <c>informe.ver</c> porque el pie de totales de la vista
    /// única es de esa acción (ServicioCuentas.cs) y ese bloque sale en la respuesta.
    /// <para>
    /// Se comprueba en el servicio y no con <c>[Accion]</c> porque el alcance no se conoce hasta
    /// leer el cuerpo, y porque en una llamada con token de agente la acción tiene que estar
    /// <b>también</b> en la lista del token. <c>ia.consultar</c> la exige el endpoint, en los dos.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> AccionesPorAlcance =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["clientes"] = [Acciones.ClienteVer],
            ["cuentas"] = [Acciones.FacturaVer, Acciones.InformeVer],
            ["facturas"] = [Acciones.FacturaVer],
            ["contratos"] = [Acciones.ClienteVer],
            ["informes"] = [Acciones.InformeVer],
        };

    /// <summary>
    /// Los alcances que acepta el endpoint. Es exactamente la lista del frontend
    /// (<c>frontend/src/ia/consulta.ts</c>): un alcance de más aquí sería una superficie que la
    /// interfaz no puede ni pedir.
    /// </summary>
    public static IReadOnlyList<string> Alcances => [.. AccionesPorAlcance.Keys];

    private static readonly JsonSerializerOptions AlSerializar = new(JsonSerializerDefaults.Web);

    public async Task<ConsultaIaRespuesta> ConsultarAsync(
        string pregunta,
        string alcance,
        string ip,
        CancellationToken ct)
    {
        if (!AccionesPorAlcance.TryGetValue(alcance.Trim(), out var acciones))
        {
            throw new ArgumentException(
                $"El alcance '{alcance}' no existe. Los que hay: {string.Join(", ", Alcances)}.");
        }

        // 1) Permiso del alcance: el del rol y el recorte por usuario, más la lista del token si la
        //    llamada viene de un agente. Va antes que el proveedor a propósito: mirar antes qué
        //    proveedor está elegido y si tiene clave le diría a quien no tiene permiso cuál es y si
        //    esta instalación lo tiene configurado, que no le corresponde saber.
        foreach (var accion in acciones)
        {
            await ExigirAsync(accion, $"ia/{alcance.Trim().ToLowerInvariant()}", ct);
        }

        var proveedor = DelProveedorElegido();

        // 2) Límites, antes de gastar un céntimo. Va después del permiso: no tiene sentido contar
        //    consultas de quien no podía ver los datos.
        await ExigirLimitesAsync(alcance.Trim().ToLowerInvariant(), ip, ct);

        // 3) Datos, con los servicios de lectura que ya usa la API y con sus mismos filtros. Por eso
        //    lo que ve el asistente cuadra con la vista única: son las mismas cifras, no un cálculo
        //    paralelo que se pueda desincronizar.
        var mapa = new MapaSeudonimos();
        string datos;
        try
        {
            datos = await CargarAsync(alcance.Trim().ToLowerInvariant(), mapa, ct);
        }
        catch (ProveedorIaException)
        {
            // SEC-026: la red de seguridad de la seudonimización saltó. No se llama al proveedor.
            await AuditarAsync(alcance, ip, "denegado", "fuga_datos_personales", null, null, null, ct);
            throw;
        }

        // 4) Pregunta seudonimizada: el usuario puede haber escrito un nombre a mano.
        var preguntaAnonima = mapa.AnonimizarTexto(pregunta);

        var inicio = reloj.GetUtcNow();
        RespuestaProveedor respuesta;
        try
        {
            respuesta = await proveedor.ConsultarAsync(
                new PeticionProveedor(PromptSistemaIa.Texto, preguntaAnonima, datos),
                ct);
        }
        catch (ProveedorIaException)
        {
            await AuditarAsync(alcance, ip, "permitido", "proveedor_fallido", null, null, null, ct);
            throw;
        }

        var duracion = (int)(reloj.GetUtcNow() - inicio).TotalMilliseconds;

        // 5) De vuelta a los nombres reales, antes de que la respuesta salga del proceso.
        var texto = mapa.Reidentificar(respuesta.Texto);

        // 6) Auditoría sin contenido: qué se preguntó a qué, con qué modelo y qué costó. Ni la
        //    pregunta ni la respuesta, que ahí van nombres de personas.
        await AuditarAsync(
            alcance,
            ip,
            "permitido",
            "ok",
            opciones.Del(opciones.Proveedor).Modelo,
            respuesta.TokensEntrada,
            respuesta.TokensSalida,
            ct,
            duracion);

        return new ConsultaIaRespuesta(
            texto,
            alcance.Trim().ToLowerInvariant(),
            OpcionesIa.Nombre(opciones.Proveedor),
            opciones.Del(opciones.Proveedor).Modelo);
    }

    private IProveedorIA DelProveedorElegido()
    {
        if (!opciones.Activado)
        {
            throw new ProveedorIaException("El asistente de IA está desactivado en esta instalación.");
        }

        var configuracion = opciones.Del(opciones.Proveedor);
        if (!configuracion.Configurado)
        {
            throw new ProveedorIaException(
                $"No hay clave configurada para el proveedor «{OpcionesIa.Nombre(opciones.Proveedor)}».");
        }

        return proveedores.FirstOrDefault(p => p.Atiende(opciones.Proveedor))
            ?? throw new ProveedorIaException(
                $"El proveedor «{OpcionesIa.Nombre(opciones.Proveedor)}» no está registrado.");
    }

    private async Task ExigirAsync(string accion, string recurso, CancellationToken ct)
    {
        // Doble techo de F6-01: el token recorta, nunca amplía. La lista solo se consulta si la
        // llamada es de agente, porque fuera de ella está vacía.
        if (sesion.EsAgente && !sesion.AgenteAcciones.Contains(accion))
        {
            await politica.AuditarDenegadoAsync(accion, recurso, ct);
            throw new PermisoDenegadoException(accion, recurso);
        }

        if (politica.Puede(accion))
        {
            return;
        }

        await politica.AuditarDenegadoAsync(accion, recurso, ct);
        throw new PermisoDenegadoException(accion, recurso);
    }

    /// <summary>
    /// El límite diario del token (<c>agente_token.limite_consultas_dia</c>, que F6-01 guardaba y
    /// mostraba sin aplicar) y el tope global del mes. <b>Los dos cuentan consultas, no dólares</b>:
    /// los precios por millón de tokens cambian sin avisar y ponerlos en el código es una deuda que
    /// caduca mal.
    /// <para>
    /// Se cuentan las filas <c>ia.consulta</c> <c>reservado</c> (una por consulta que llega al
    /// proveedor), no las denegadas: una consulta rechazada por el límite no debe sumar al contador que la rechazó, o un cliente en bucle dejaría
    /// el mes entero bloqueado para todos. La fila de auditoría es a la vez el registro y el
    /// contador, así que <b>esta tarea no necesita migración</b>.
    /// </para>
    /// </summary>
    private async Task ExigirLimitesAsync(string alcance, string ip, CancellationToken ct)
    {
        var hoy = TiempoNegocio.FechaEnZonaNegocio(reloj.GetUtcNow());

        // Los cortes van en UTC porque `auditoria.created_at` es `timestamptz` y Npgsql no acepta
        // un `DateTimeOffset` con otro desplazamiento: el día empieza en Santo Domingo, el
        // parámetro va en UTC y son el mismo instante.
        var desplazamiento = TiempoNegocio.Zona.GetUtcOffset(reloj.GetUtcNow());
        var inicioDia = new DateTimeOffset(hoy.ToDateTime(TimeOnly.MinValue), desplazamiento).ToUniversalTime();
        var inicioMes = new DateTimeOffset(new DateOnly(hoy.Year, hoy.Month, 1).ToDateTime(TimeOnly.MinValue), desplazamiento)
            .ToUniversalTime();

        // La RLS de auditoría solo deja leer a admin y gerente, y `ia.consultar` es justo eso. Sin
        // transacción explícita `app.rol` iría vacío y el contador saldría siempre en cero, que es
        // un límite que no limita nada.
        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);

        // SEC-029: contar y reservar tienen que ser un solo paso. Con el cerrojo de transacción, dos
        // peticiones en paralelo se turnan: la segunda cuenta ya la reserva de la primera. El
        // cerrojo se suelta al terminar la transacción, antes de llamar al proveedor.
        await contexto.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock({CerrojoLimites})", ct);

        if (sesion.AgenteTokenId is { } tokenId)
        {
            var limiteDia = await LimiteDiarioDelTokenAsync(tokenId, ct);
            var usadasHoy = await contexto.Auditoria
                .CountAsync(a => a.Accion == ConsultaAccion
                    && a.EntidadId == tokenId.ToString("D")
                    && a.CreatedAt >= inicioDia
                    && a.Resultado == ResultadoReserva,
                    ct);

            if (usadasHoy >= limiteDia)
            {
                await transaccion.RollbackAsync(ct);
                await AuditarAsync(alcance, ip, "denegado", "limite_diario", null, null, null, ct);
                throw new LimiteIaExcedidoException(
                    $"Este token de agente agotó sus {limiteDia} consultas de hoy.");
            }
        }

        var usadasMes = await contexto.Auditoria
            .CountAsync(a => a.Accion == ConsultaAccion
                && a.CreatedAt >= inicioMes
                && a.Resultado == ResultadoReserva,
                ct);

        if (usadasMes >= opciones.TopeConsultasMes)
        {
            await transaccion.RollbackAsync(ct);
            await AuditarAsync(alcance, ip, "denegado", "tope_mensual", null, null, null, ct);
            throw new LimiteIaExcedidoException(
                "Se alcanzó el tope de consultas al asistente de este mes.");
        }

        // La reserva es la fila que cuentan los dos límites; la fila "permitido" de después es solo
        // el registro de lo que costó. Auditoría es append-only, así que no se puede completar una
        // sola fila a posteriori.
        await AuditarAsync(alcance, ip, ResultadoReserva, "reserva", null, null, null, ct);
        await transaccion.CommitAsync(ct);
    }

    private async Task<int> LimiteDiarioDelTokenAsync(Guid tokenId, CancellationToken ct)
    {
        var limite = await contexto.AgenteToken
            .AsNoTracking()
            .Where(token => token.Id == tokenId)
            .Select(token => (int?)token.LimiteConsultasDia)
            .SingleAsync(ct);

        // Un token con el límite a cero no consulta: se trata como uno, para que un valor mal puesto
        // en el alta cierre el acceso en lugar de abrirlo de par en par.
        return Math.Max(limite ?? 1, 1);
    }

    /// <summary>
    /// Los datos del alcance, serializados como los devuelve la API. Se serializan y no se inventan
    /// campos: lo que el modelo ve es exactamente lo que vería el usuario en la misma pantalla, y
    /// por eso los importes cuadran con la vista única por construcción y no por coincidencia.
    /// </summary>
    private async Task<string> CargarAsync(string alcance, MapaSeudonimos mapa, CancellationToken ct)
    {
        object datos;
        var paginacion = new ConsultaLista(1, FilasPorConsulta, null, "asc");

        switch (alcance)
        {
            case "clientes":
                datos = await clientes.ListarAsync(paginacion, null, null, null, false, ct);
                break;

            case "cuentas":
                datos = await cuentas.ListarAsync(
                    paginacion,
                    anio: null,
                    serieId: null,
                    moneda: null,
                    estado: null,
                    estadoCobro: null,
                    clienteId: null,
                    unidadId: null,
                    vencidas: false,
                    modificadas: false,
                    noEnviadas: false,
                    envioFallido: false,
                    modificadasTrasEnvio: false,
                    incluirProformas: false,
                    incluirFianzas: true,
                    ct: ct);
                break;

            case "facturas":
                datos = await facturas.ListarAsync(
                    paginacion, null, null, null, null, null, null, null, ct);
                break;

            case "contratos":
                datos = await contratos.ListarAsync(paginacion, null, null, null, null, ct);
                break;

            default:
                datos = await informes.ResumenAsync(
                    new FiltroInformes(null, null, null, null, null, null, null, null, null, null, null),
                    ct);
                break;
        }

        return mapa.Anonimizar(JsonSerializer.SerializeToNode(datos, AlSerializar));
    }

    /// <summary>
    /// Una fila <c>ia.consulta</c> en su propia transacción, porque aquí no hay mutación que la
    /// traiga. El <c>datos_json</c> lleva alcance, proveedor, modelo, tokens, duración y motivo:
    /// <b>ni la pregunta ni la respuesta</b>, que ahí van nombres de personas (AGENTS.md §6).
    /// </summary>
    private async Task AuditarAsync(
        string alcance,
        string ip,
        string resultado,
        string motivo,
        string? modelo,
        int? tokensEntrada,
        int? tokensSalida,
        CancellationToken ct,
        int? duracionMs = null)
    {
        var carga = new Dictionary<string, object?>
        {
            ["alcance"] = alcance,
            ["proveedor"] = OpcionesIa.Nombre(opciones.Proveedor),
            ["modelo"] = modelo,
            ["tokens_entrada"] = tokensEntrada,
            ["tokens_salida"] = tokensSalida,
            ["duracion_ms"] = duracionMs,
            ["motivo"] = motivo,
        };

        var necesitaTransaccion = contexto.Database.CurrentTransaction is null;
        if (necesitaTransaccion)
        {
            await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);
            await EscribirAuditoriaAsync(ip, resultado, carga, ct);
            await transaccion.CommitAsync(ct);
            return;
        }

        await EscribirAuditoriaAsync(ip, resultado, carga, ct);
    }

    private Task EscribirAuditoriaAsync(
        string ip,
        string resultado,
        Dictionary<string, object?> carga,
        CancellationToken ct) =>
        auditoria.RegistrarAsync(new RegistroAuditoria
        {
            Accion = ConsultaAccion,
            Entidad = Entidad,
            EntidadId = sesion.AgenteTokenId?.ToString("D"),
            UsuarioId = sesion.UsuarioId,
            Ip = ip,
            Resultado = resultado,
            // F6-01: una consulta hecha con token se distingue de una hecha con la sesión, para que
            // el soporte sepa de dónde vino.
            Origen = sesion.EsAgente ? "agente" : "api",
            DatosJson = JsonSerializer.Serialize(carga),
        }, ct);
}
