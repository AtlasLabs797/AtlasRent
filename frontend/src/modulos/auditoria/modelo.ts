import { esGuid } from '../bloqueos/bloqueos.ts';

/** `AuditoriaFila` de `GET /auditoria`. `datos` llega con las claves sensibles enmascaradas (`***`). */
export type AuditoriaFila = {
  id: string;
  fecha: string;
  usuarioId: string | null;
  usuarioNombre: string | null;
  ip: string;
  accion: string;
  entidad: string | null;
  entidadId: string | null;
  resultado: string | null;
  origen: string | null;
  datos: unknown;
};

export interface FiltrosAuditoria {
  desde: string;
  hasta: string;
  usuarioId: string;
  entidad: string;
  accion: string;
}

export const FILTROS_VACIOS: FiltrosAuditoria = { desde: '', hasta: '', usuarioId: '', entidad: '', accion: '' };

/** Parámetros de la consulta: solo los rellenos; el usuario solo si es un GUID válido (la API da 400 si no). */
export function paramsAuditoria(f: FiltrosAuditoria): Record<string, string> {
  const p: Record<string, string> = {};
  if (f.desde) p.desde = f.desde;
  if (f.hasta) p.hasta = f.hasta;
  if (esGuid(f.usuarioId.trim())) p.usuarioId = f.usuarioId.trim();
  if (f.entidad.trim()) p.entidad = f.entidad.trim();
  if (f.accion.trim()) p.accion = f.accion.trim();
  return p;
}

/** `datos` como texto plano formateado. Nunca HTML: la pantalla lo pinta como texto de un `<pre>`. */
export function datosComoTexto(datos: unknown): string {
  if (datos === null || datos === undefined) return '';
  return JSON.stringify(datos, null, 2);
}
