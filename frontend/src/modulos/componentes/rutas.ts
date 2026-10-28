import { ACCIONES } from '../../permisos.ts';
import type { DefinicionModulo } from '../tipos.ts';

/** Muestra de componentes del design system (F1-13): herramienta de quien mantiene la interfaz. */
export const modulo: DefinicionModulo = {
  clave: 'componentes',
  accion: ACCIONES.configuracionEditar,
  ruta: '/componentes',
  rutas: [{ path: 'componentes', cargar: () => import('../../paginas/DesignSystem') }],
};
