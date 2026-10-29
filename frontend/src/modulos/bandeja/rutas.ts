import { ACCIONES } from '../../permisos.ts';
import type { DefinicionModulo } from '../tipos.ts';

/**
 * Bandeja de alertas y aprobaciones (F2-10, §3.16). La acción del módulo es `cliente.ver`, la de las
 * alertas (todos los roles; la API filtra por tipo). Cada pestaña comprueba además la suya
 * (`contrato.estado`, `ia.proponer`, `mensajeria.masivo`).
 */
export const modulo: DefinicionModulo = {
  clave: 'bandeja',
  accion: ACCIONES.clienteVer,
  ruta: '/bandeja',
  grupo: 'operacion',
  rutas: [{ path: 'bandeja', cargar: () => import('./paginas/Bandeja') }],
};
