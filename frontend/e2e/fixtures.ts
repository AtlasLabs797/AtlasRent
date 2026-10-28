import { ACCIONES } from '../src/permisos';
import { problema, type ApiFalsa } from './apiFalsa';

/** Identidades de muestra con la forma de `GET /auth/yo`. Datos inventados. */
export function identidad(sobrescribir: Record<string, unknown> = {}) {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    nombreAcceso: 'admin',
    nombre: 'Marta Peña',
    email: null,
    rol: 'admin',
    debeCambiarPassword: false,
    mfaActivado: true,
    mfaPendiente: false,
    accesoEmergencia: false,
    ultimoAcceso: '2026-10-27T14:30:00Z',
    permisosEfectivos: Object.values(ACCIONES),
    ...sobrescribir,
  };
}

export const EMPLEADO = identidad({
  nombreAcceso: 'empleado',
  nombre: 'Luis Rosario',
  rol: 'empleado',
  permisosEfectivos: [ACCIONES.clienteVer, ACCIONES.sesionVer, ACCIONES.sesionCerrar, ACCIONES.sesionPassword],
});

export const DESAFIO_MFA = { requiereMfa: true, requiereConfiguracion: false, nombreAcceso: 'admin', caduca: '2026-10-28T12:05:00Z' };
export const DESAFIO_ALTA_MFA = { ...DESAFIO_MFA, requiereConfiguracion: true };

/** Sin sesión: `/auth/yo` da 401 y el refresco también (lo que ve quien abre la app sin entrar). */
export function sinSesion(api: ApiFalsa): void {
  api.responder('GET', '/auth/yo', problema(401, 'AUT-002', 'No autenticado', 'Falta la cookie de sesión o no es válida.'));
  api.responder('POST', '/auth/refresh', problema(401, 'AUT-004', 'No autenticado', 'Falta el token de refresco.'));
}

/** Con sesión de la identidad dada. */
export function conSesion(api: ApiFalsa, quien = identidad()): void {
  api.responder('GET', '/auth/yo', { body: quien });
  api.responder('POST', '/auth/logout', { status: 204 });
}
