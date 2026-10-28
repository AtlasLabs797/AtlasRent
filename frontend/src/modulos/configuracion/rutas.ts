import { ACCIONES } from '../../permisos.ts';
import type { DefinicionModulo } from '../tipos.ts';

/** Configuración: series, impuestos, pagos, subempresas, tipo de cambio, ajustes, mensajes y tokens de agente. Solo administrador. */
export const modulo: DefinicionModulo = {
  clave: 'configuracion',
  accion: ACCIONES.configuracionEditar,
  ruta: '/configuracion',
  rutas: [{ path: 'configuracion', cargar: () => import('./paginas/Configuracion') }],
};
