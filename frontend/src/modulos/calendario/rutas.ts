import { ACCIONES } from '../../permisos.ts';
import type { DefinicionModulo } from '../tipos.ts';

/** Calendario de disponibilidad. `GET /calendario` exige `cliente.ver` (CalendarioController). */
export const modulo: DefinicionModulo = {
  clave: 'calendario',
  accion: ACCIONES.clienteVer,
  ruta: '/calendario',
  grupo: 'operacion',
  rutas: [{ path: 'calendario', cargar: () => import('./paginas/Calendario') }],
};
