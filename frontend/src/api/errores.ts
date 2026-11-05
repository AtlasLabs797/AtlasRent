import { isAxiosError } from 'axios';

/**
 * Error de la API convertido desde `ProblemDetails` (docs/DOC_TECNICA.md §6). Los 400 de validación
 * traen además `errors` por campo (`ValidationProblemDetails`), con la clave en PascalCase del DTO.
 */
export class ErrorApi extends Error {
  /** Código HTTP, o `undefined` si no hubo respuesta (red caída, CORS, timeout). */
  readonly status: number | undefined;
  readonly title: string;
  readonly detail: string | undefined;
  /** Código de referencia del backend (AUT-001, SEG-010…): es lo que el soporte busca en el log. */
  readonly codigoReferencia: string | undefined;
  /** Mensajes por campo, con la clave tal como llega del backend. */
  readonly errors: Readonly<Record<string, readonly string[]>>;

  constructor(datos: {
    status?: number;
    title: string;
    detail?: string;
    codigoReferencia?: string;
    errors?: Record<string, string[]>;
  }) {
    super(datos.detail ?? datos.title);
    this.name = 'ErrorApi';
    this.status = datos.status;
    this.title = datos.title;
    this.detail = datos.detail;
    this.codigoReferencia = datos.codigoReferencia;
    this.errors = datos.errors ?? {};
  }
}

const SIN_RESPUESTA = 'No se ha podido contactar con el servidor.';

function texto(valor: unknown): string | undefined {
  return typeof valor === 'string' && valor.length > 0 ? valor : undefined;
}

function erroresDe(valor: unknown): Record<string, string[]> {
  if (typeof valor !== 'object' || valor === null) return {};
  const salida: Record<string, string[]> = {};
  for (const [campo, mensajes] of Object.entries(valor)) {
    if (Array.isArray(mensajes)) salida[campo] = mensajes.filter((m): m is string => typeof m === 'string');
  }
  return salida;
}

/** Convierte lo que lance Axios (o cualquier cosa) en un `ErrorApi`. No lanza nunca. */
export function aErrorApi(error: unknown): ErrorApi {
  if (error instanceof ErrorApi) return error;
  if (isAxiosError(error)) {
    const datos: unknown = error.response?.data;
    const problema = typeof datos === 'object' && datos !== null ? (datos as Record<string, unknown>) : {};
    const detalle = texto(problema.detail);
    const codigo = texto(problema.codigoReferencia);
    return new ErrorApi({
      ...(error.response ? { status: error.response.status } : {}),
      title: texto(problema.title) ?? (error.response ? `Error ${error.response.status}` : SIN_RESPUESTA),
      ...(detalle ? { detail: detalle } : {}),
      ...(codigo ? { codigoReferencia: codigo } : {}),
      errors: erroresDe(problema.errors),
    });
  }
  return new ErrorApi({ title: error instanceof Error ? error.message : SIN_RESPUESTA });
}

/** Texto para el usuario: el `detail` del backend si lo hay; sin respuesta, el de reserva; si no, el `title`. */
export function mensajeDeError(error: unknown, reserva: string): string {
  const e = aErrorApi(error);
  return e.detail ?? (e.status === undefined ? reserva : e.title);
}

/** "ContrasenaNueva" -> "contrasenaNueva"; "Lineas[0].Precio" -> "lineas.0.precio" (ruta de React Hook Form). */
export function claveDeCampo(claveApi: string): string {
  return claveApi
    .replace(/\[(\d+)\]/g, '.$1')
    .split('.')
    .map((segmento) => segmento.charAt(0).toLowerCase() + segmento.slice(1))
    .join('.');
}

/** Primer mensaje de cada campo, con las claves en camelCase. */
export function erroresPorCampo(error: unknown): Record<string, string> {
  const salida: Record<string, string> = {};
  for (const [campo, mensajes] of Object.entries(aErrorApi(error).errors)) {
    if (mensajes[0] !== undefined) salida[claveDeCampo(campo)] = mensajes[0];
  }
  return salida;
}

/**
 * Reparte el error de un envío entre los campos que el formulario conoce y un mensaje general. Lo
 * que el formulario no pinta (otro campo, un 409, un 500) no se pierde: va al mensaje general.
 */
export function repartirErrores(
  error: unknown,
  camposConocidos: readonly string[],
  reserva: string,
): { porCampo: Record<string, string>; general: string | null } {
  const e = aErrorApi(error);
  const todos = erroresPorCampo(e);
  const porCampo: Record<string, string> = {};
  const sueltos: string[] = [];
  for (const [campo, mensaje] of Object.entries(todos)) {
    if (camposConocidos.includes(campo)) porCampo[campo] = mensaje;
    else sueltos.push(mensaje);
  }
  const hayCampos = Object.keys(todos).length > 0;
  const general = hayCampos ? (sueltos[0] ?? null) : mensajeDeError(e, reserva);
  return { porCampo, general };
}
