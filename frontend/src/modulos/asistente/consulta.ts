import { textosAsistente } from './textos.ts';

/**
 * Consulta al asistente de IA (F6-03) contra `POST /api/v1/ia/consulta`.
 *
 * Módulo puro, sin React ni Axios: el cuerpo de la petición y la traducción de errores son
 * lógica, así que van aquí y los carga el runner de Node en `tests/asistente.test.ts`. La llamada
 * HTTP está en `ia/api.ts`.
 *
 * El backend es sin estado: no hay conversación ni historial, cada pregunta va sola y lo que viene
 * es la respuesta a esa pregunta.
 */

/** Ámbitos que acepta el endpoint. Son los del contrato, no los del menú: el menú se filtra por
 * permisos, esto por lo que la API entiende. */
export const ALCANCES = ['clientes', 'cuentas', 'facturas', 'contratos', 'informes'] as const;
export type AlcanceConsulta = (typeof ALCANCES)[number];

export interface CuerpoConsultaIa {
  pregunta: string;
  alcance: AlcanceConsulta;
}

/** Lo que la pantalla necesita saber de un fallo para enseñarlo. */
export interface AvisoErrorIa {
  tono: 'danger' | 'warning';
  titulo: string;
  mensaje: string;
  /** Si tiene sentido ofrecer el botón de reintentar: reenviar la misma pregunta. */
  reintentable: boolean;
}

/**
 * El cuerpo de la petición. La pregunta se recorta antes de mandarse (espacios de los dos lados) y
 * una pregunta vacía no sale nunca: sería un 400 garantizado. El alcance solo pasa si es uno de los
 * del contrato, aunque la pantalla ya lo elija de una lista.
 */
export function cuerpoConsulta(pregunta: string, alcance: string): CuerpoConsultaIa {
  const limpia = pregunta.trim();
  if (limpia.length === 0) throw new Error(textosAsistente.formulario.preguntaVacia);

  const exacto = ALCANCES.find((valor) => valor === alcance);
  if (exacto === undefined) throw new Error(textosAsistente.formulario.alcanceInvalido);

  return { pregunta: limpia, alcance: exacto };
}

/**
 * Traduce un fallo de la API al aviso que ve el usuario. Los errores son `ProblemDetails`
 * (DOC_TECNICA §6): si viene `detail`, ese texto manda — la API sabe más del caso concreto que este
 * archivo — y el título de aquí solo pone nombre al aviso.
 *
 * El reintento se decide por el estado y no por el texto: 401 y 403 no mejoran al repetir la misma
 * petición (la sesión hay que renovarla y el permiso hay que pedirlo); 429 y 503 sí, porque las dos
 * son situaciones que se acaban solas.
 */
export function avisoDeError(error: unknown): AvisoErrorIa {
  const fallo = error as { response?: { status?: number; data?: unknown } } | null;
  const estado = fallo?.response?.status ?? null;
  const detalle = detalleDeProblema(fallo?.response?.data);

  const e = textosAsistente.errores;
  switch (estado) {
    case 401:
      return aviso('warning', e.sesionCaducada.titulo, detalle ?? e.sesionCaducada.cuerpo, false);
    case 403:
      return aviso('warning', e.sinPermiso.titulo, detalle ?? e.sinPermiso.cuerpo, false);
    case 429:
      return aviso('warning', e.limite.titulo, detalle ?? e.limite.cuerpo, true);
    case 503:
      return aviso('warning', e.noDisponible.titulo, detalle ?? e.noDisponible.cuerpo, true);
    default:
      return aviso('danger', e.generico.titulo, detalle ?? e.generico.cuerpo, true);
  }
}

function aviso(tono: AvisoErrorIa['tono'], titulo: string, mensaje: string, reintentable: boolean): AvisoErrorIa {
  return { tono, titulo, mensaje, reintentable };
}

/** `detail` de `ProblemDetails`, si el cuerpo es un objeto con ese campo y trae texto. */
function detalleDeProblema(cuerpo: unknown): string | null {
  if (typeof cuerpo !== 'object' || cuerpo === null) return null;
  const detalle = (cuerpo as { detail?: unknown }).detail;
  return typeof detalle === 'string' && detalle.trim().length > 0 ? detalle : null;
}