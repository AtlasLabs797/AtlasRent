import { ACCIONES } from '../../permisos.ts';
import type { DefinicionModulo } from '../tipos.ts';

/** Desbloqueo de cuentas e IPs (`ip.desbloquear`) y aprobación del acceso de emergencia (pestaña propia, con su acción). */
export const modulo: DefinicionModulo = {
  clave: 'bloqueos',
  accion: ACCIONES.ipDesbloquear,
  ruta: '/bloqueos',
  rutas: [{ path: 'bloqueos', cargar: () => import('./paginas/Bloqueos') }],
};
