import { ACCIONES } from '../../permisos.ts';
import type { DefinicionModulo } from '../tipos.ts';

/** "Mi cuenta": datos propios, cambio de contraseña y alta del MFA. Todo usuario con sesión; no sale en el menú lateral. */
export const modulo: DefinicionModulo = {
  clave: 'cuenta',
  accion: ACCIONES.sesionPassword,
  ruta: '/cuenta',
  rutas: [{ path: 'cuenta', cargar: () => import('./paginas/MiCuenta') }],
};
