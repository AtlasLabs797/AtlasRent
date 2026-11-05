import { ACCIONES } from '../../permisos.ts';
import type { DefinicionModulo } from '../tipos.ts';

/**
 * Envíos (F4-02, F4-03, F4-05). El historial es `factura.ver` (metadato sin importes, también del
 * empleado); el envío masivo y los recordatorios son `mensajeria.masivo` y esas pestañas solo salen
 * con esa acción.
 */
export const modulo: DefinicionModulo = {
  clave: 'envios',
  accion: ACCIONES.facturaVer,
  ruta: '/envios',
  grupo: 'operacion',
  rutas: [{ path: 'envios', cargar: () => import('./paginas/Envios') }],
};
