import { ACCIONES } from '../../permisos.ts';
import type { DefinicionModulo } from '../tipos.ts';

export const modulo: DefinicionModulo = {
  clave: 'panel',
  accion: ACCIONES.sesionVer,
  ruta: '/',
  rutas: [{ path: '', cargar: () => import('../../paginas/Inicio') }],
};
