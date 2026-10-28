import { ACCIONES } from '../../permisos.ts';
import type { DefinicionModulo } from '../tipos.ts';

/**
 * Bandeja de alertas y aprobaciones (F2-10, §3.16). La acción del módulo es `contrato.estado`, la de
 * la bandeja de contratos (un empleado recibe 403 allí): gerente y administrador. Cada pestaña
 * comprueba además la suya (`ia.proponer`, `mensajeria.masivo`).
 */
export const modulo: DefinicionModulo = {
  clave: 'bandeja',
  accion: ACCIONES.contratoEstado,
  ruta: '/bandeja',
  grupo: 'operacion',
  rutas: [{ path: 'bandeja', cargar: () => import('./paginas/Bandeja') }],
};
