import { aErrorApi } from '../../api/errores.ts';
import { textosBandeja } from './textos.ts';

/**
 * Contratos de la bandeja (F2-10, F6-03). Lógica pura, sin React ni Axios: la cargan los tests de
 * Node. Los enums llegan como string; los importes como número JSON y se formatean sin operar.
 */

export type MonedaDoc = 'USD' | 'DOP';
export type ResultadoSolicitud = 'Pendiente' | 'Aprobada' | 'Rechazada' | 'Caducada';

/** `AprobacionBandejaRespuesta`: una solicitud de contrato pendiente. */
export interface SolicitudContrato {
  solicitudId: string;
  contratoId: string;
  solicitadoPorId: string | null;
  solicitadoPor: string | null;
  cliente: string | null;
  unidad: string | null;
  estado: string;
  fechaInicio: string;
  fechaFinVigente: string;
  rentaVigente: string | null;
  rentaPropuesta: string;
  moneda: MonedaDoc;
  hayCambioDeRenta: boolean;
  huellaContenido: string;
  solicitadoAt: string;
  comentario: string | null;
}

/** `PropuestaIaRespuesta`: propuesta del asistente. `id` es el de la solicitud. */
export interface PropuestaIa {
  id: string;
  tipo: string;
  clienteId: string;
  cliente: string;
  recordatorioId: string;
  seudonimo: string;
  pendiente: string;
  huellaContenido: string;
  resultado: ResultadoSolicitud;
  motivoCaducidad: string | null;
  comentario: string | null;
  solicitadoAt: string;
  solicitadoPor: string | null;
  resueltoAt: string | null;
  resueltoPor: string | null;
}

/**
 * La API contesta 409 cuando la solicitud ya no es la vigente del contrato (BUG-042): alguien la
 * decidió o el contrato cambió. La pantalla no reintenta: recarga la lista y avisa.
 */
export function esSolicitudCambiada(error: unknown): boolean {
  return aErrorApi(error).status === 409;
}

/** Texto del aviso de un 409: el `detail` de la API, y si no viene, el del producto. */
export function avisoSolicitudCambiada(error: unknown): string {
  return aErrorApi(error).detail ?? textosBandeja.contratos.solicitudCambiada;
}

/** Cuerpo del rechazo: el comentario es obligatorio (§3.5) y se manda recortado. */
export function cuerpoRechazo(comentario: string): { comentario: string } {
  const limpio = comentario.trim();
  if (limpio.length === 0) throw new Error(textosBandeja.rechazo.motivoObligatorio);
  return { comentario: limpio };
}

/**
 * Qué enseñar en «valor actual → valor propuesto» de un contrato. Sin renta vigente el contrato es
 * nuevo y se dice así: un cero inventado sería una diferencia que no existe.
 */
export function cambioDeRenta(
  s: Pick<SolicitudContrato, 'rentaVigente' | 'rentaPropuesta' | 'hayCambioDeRenta'>,
): { actual: string | null; propuesta: string; cambia: boolean; nuevo: boolean } {
  return {
    actual: s.rentaVigente,
    propuesta: s.rentaPropuesta,
    cambia: s.hayCambioDeRenta,
    nuevo: s.rentaVigente === null,
  };
}

/** Motivo de caducidad si la respuesta de aprobar es una propuesta caducada (no se envió nada). */
export function caducidadDePropuesta(respuesta: Pick<PropuestaIa, 'resultado' | 'motivoCaducidad'>): string | null {
  if (respuesta.resultado !== 'Caducada') return null;
  return respuesta.motivoCaducidad ?? textosBandeja.propuestas.caducadaSinMotivo;
}

/** Nombre legible del tipo de propuesta; un tipo desconocido se enseña tal cual. */
export function accionDePropuesta(tipo: string): string {
  const conocidas = textosBandeja.propuestas.tipos as Record<string, string>;
  return conocidas[tipo] ?? tipo;
}
