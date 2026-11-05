import { ACCIONES } from '../../permisos.ts';
import type { DefinicionModulo } from '../tipos.ts';

/** Usuarios, roles y permisos por usuario. Todos sus endpoints piden `configuracion.editar` (solo administrador). */
export const modulo: DefinicionModulo = {
  clave: 'usuarios',
  accion: ACCIONES.configuracionEditar,
  ruta: '/usuarios',
  rutas: [{ path: 'usuarios', cargar: () => import('./paginas/Usuarios') }],
};
