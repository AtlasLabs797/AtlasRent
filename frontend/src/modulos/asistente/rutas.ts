import { ACCIONES } from '../../permisos.ts';
import type { DefinicionModulo } from '../tipos.ts';

/** Módulo de referencia: ver `frontend/README.md` ("Cómo añadir un módulo"). */
export const modulo: DefinicionModulo = {
  clave: 'asistente',
  accion: ACCIONES.iaConsultar,
  ruta: '/asistente',
  rutas: [{ path: 'asistente', cargar: () => import('./paginas/Asistente') }],
};
