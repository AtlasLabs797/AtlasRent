import { ACCIONES } from '../../permisos.ts';
import type { DefinicionModulo } from '../tipos.ts';

export const modulo: DefinicionModulo = {
  clave: 'informes',
  accion: ACCIONES.informeVer,
  ruta: '/informes',
  rutas: [{ path: 'informes', cargar: () => import('../../paginas/Informes') }],
};
