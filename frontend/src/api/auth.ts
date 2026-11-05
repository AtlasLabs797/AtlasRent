import { clienteHttp } from './cliente';

/**
 * Llamadas de autenticación (docs/DOC_TECNICA.md §6). Sin estado: la sesión vive en cookies y el
 * store de `sesion/` decide qué hacer con lo que devuelven.
 */

/** Identidad de la sesión, tal y como la devuelve `GET /auth/yo`. `permisosEfectivos` son identificadores de acción. */
export interface Identidad {
  id: string;
  nombreAcceso: string;
  nombre: string;
  email: string | null;
  rol: string;
  debeCambiarPassword: boolean;
  mfaActivado: boolean;
  mfaPendiente: boolean;
  accesoEmergencia: boolean;
  ultimoAcceso: string | null;
  permisosEfectivos: string[];
}

/** El login acertó pero falta el segundo factor: todavía no hay sesión, solo un token provisional en una cookie. */
export interface DesafioMfa {
  requiereMfa: true;
  requiereConfiguracion: boolean;
  nombreAcceso: string;
  caduca: string;
}

export interface ActivacionMfa {
  uri: string;
  digitos: number;
  periodoSegundos: number;
}

export type ResultadoLogin = { tipo: 'sesion'; identidad: Identidad } | { tipo: 'desafio'; desafio: DesafioMfa };

export async function login(nombreAcceso: string, contrasena: string): Promise<ResultadoLogin> {
  const { data } = await clienteHttp.post<Identidad | DesafioMfa>('/auth/login', { nombreAcceso, contrasena });
  return 'requiereMfa' in data && data.requiereMfa
    ? { tipo: 'desafio', desafio: data }
    : { tipo: 'sesion', identidad: data as Identidad };
}

/** Código TOTP del token provisional. */
export async function verificarSegundoFactor(codigo: string): Promise<Identidad> {
  return (await clienteHttp.post<Identidad>('/auth/mfa/verificar', { codigo })).data;
}

/** Código de recuperación (se gasta al usarlo). */
export async function usarCodigoRecuperacion(codigo: string): Promise<Identidad> {
  return (await clienteHttp.post<Identidad>('/auth/mfa/recuperacion', { codigo })).data;
}

/** Código de acceso de emergencia (break-glass) aprobado por otro administrador. */
export async function entrarConAccesoEmergencia(codigo: string): Promise<Identidad> {
  return (await clienteHttp.post<Identidad>('/auth/emergencia/entrar', { codigo })).data;
}

/** Empieza el alta del MFA. El secreto sale en esta respuesta y en ninguna otra. */
export async function iniciarAltaMfa(): Promise<ActivacionMfa> {
  return (await clienteHttp.post<ActivacionMfa>('/auth/mfa/activar')).data;
}

/** Confirma el alta con un código TOTP; devuelve los códigos de recuperación, que no se vuelven a mostrar. */
export async function confirmarAltaMfa(codigo: string): Promise<string[]> {
  const { data } = await clienteHttp.post<{ codigosRecuperacion: string[] }>('/auth/mfa/confirmar', { codigo });
  return data.codigosRecuperacion;
}

/** Cambia la contraseña propia. `contrasenaComprobada` es falso si HaveIBeenPwned no respondió. */
export async function cambiarPassword(contrasenaActual: string, contrasenaNueva: string): Promise<boolean> {
  const { data } = await clienteHttp.post<{ contrasenaComprobada: boolean }>('/auth/password', {
    contrasenaActual,
    contrasenaNueva,
  });
  return data.contrasenaComprobada;
}
