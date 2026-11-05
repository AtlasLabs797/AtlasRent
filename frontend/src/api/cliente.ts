import axios, { isAxiosError, type AxiosInstance, type InternalAxiosRequestConfig } from 'axios';
import { aErrorApi } from './errores.ts';
import { crearRefrescador, debeRefrescar, refrescoDefinitivo } from './refresco.ts';

/**
 * Cliente HTTP de la API (DOC_TECNICA §6): JSON, cookie de sesión HttpOnly y doble envío de CSRF.
 * El interceptor lee la cookie en cada petición porque el backend la renueva al refrescar la sesión.
 *
 * Ante un 401 intenta UN `POST /auth/refresh` compartido por todas las peticiones concurrentes y
 * repite la petición una vez. Ante un 403 con SEG-010 avisa de que hay un cambio de contraseña
 * pendiente. El store de sesión se engancha con `registrarManejadoresSesion` (así este módulo no
 * importa el store y no hay dependencia circular).
 */
const COOKIE_CSRF = 'atlas-rent-csrf';
const CAMBIO_PASSWORD_REQUERIDO = 'SEG-010';

function tokenCsrf(): string {
  const prefijo = COOKIE_CSRF + '=';
  const parte = document.cookie.split('; ').find((c) => c.startsWith(prefijo));
  return parte ? decodeURIComponent(parte.slice(prefijo.length)) : '';
}

export const clienteHttp: AxiosInstance = axios.create({
  baseURL: '/api/v1',
  withCredentials: true,
  headers: { 'Content-Type': 'application/json' },
});

clienteHttp.interceptors.request.use((config) => {
  config.headers.set('X-CSRF-Token', tokenCsrf());
  return config;
});

export interface ManejadoresSesion {
  /** El refresco falló definitivamente: ya no hay sesión. */
  sesionPerdida: () => void;
  /** El refresco funcionó: identidad nueva (los permisos pueden haber cambiado). */
  identidadRefrescada: (identidad: unknown) => void;
  /** La API respondió SEG-010: hay que cambiar la contraseña antes de seguir. */
  passwordPendiente: () => void;
}

let manejadores: ManejadoresSesion = {
  sesionPerdida: () => {},
  identidadRefrescada: () => {},
  passwordPendiente: () => {},
};

export function registrarManejadoresSesion(nuevos: ManejadoresSesion): void {
  manejadores = nuevos;
}

type ConfigReintentable = InternalAxiosRequestConfig & { _reintentada?: boolean };

const refrescarSesion = crearRefrescador(async () => {
  const { data } = await clienteHttp.post<unknown>('/auth/refresh');
  manejadores.identidadRefrescada(data);
  return data;
});

clienteHttp.interceptors.response.use(
  (respuesta) => respuesta,
  async (error: unknown) => {
    if (!isAxiosError(error) || !error.config) throw error;
    const config = error.config as ConfigReintentable;

    if (debeRefrescar(error.response?.status, config.url, config._reintentada === true)) {
      try {
        await refrescarSesion();
      } catch (falloRefresco) {
        // Solo un 4xx del refresco significa "no hay sesión"; si la red falló, la sesión puede seguir viva.
        if (refrescoDefinitivo(aErrorApi(falloRefresco).status)) manejadores.sesionPerdida();
        throw error;
      }
      config._reintentada = true;
      return clienteHttp.request(config);
    }

    if (error.response?.status === 403 && aErrorApi(error).codigoReferencia === CAMBIO_PASSWORD_REQUERIDO) {
      manejadores.passwordPendiente();
    }
    throw error;
  },
);
