import { ACCIONES } from '../../permisos.ts';
import type { DefinicionModulo } from '../tipos.ts';

/** Lectura de la auditoría (`auditoria.ver`: administrador y gerente). Solo lectura. */
export const modulo: DefinicionModulo = {
  clave: 'auditoria',
  accion: ACCIONES.auditoriaVer,
  ruta: '/auditoria',
  grupo: 'control',
  rutas: [{ path: 'auditoria', cargar: () => import('./paginas/Auditoria') }],
};
