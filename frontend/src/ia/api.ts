import { clienteHttp } from '../api/cliente';
import type { CuerpoConsultaIa } from './consulta.ts';

/**
 * Consulta al asistente (F6-03). Sesión normal con cookies: es la misma sesión de la web, no el
 * token de agente.
 *
 * El endpoint lo declara el backend con la acción `ia.consultar`, que en el frontend es
 * `ACCIONES.iaConsultar`: el menú y la ruta se filtran con ese mismo identificador, así que una
 * entrada visible nunca es un 403 garantizado.
 */
export interface RespuestaConsultaIa {
  respuesta: string;
  alcance: string;
  proveedor: string;
  modelo: string;
}

export async function preguntarIa(cuerpo: CuerpoConsultaIa): Promise<RespuestaConsultaIa> {
  const { data } = await clienteHttp.post<RespuestaConsultaIa>('/ia/consulta', cuerpo);
  return data;
}