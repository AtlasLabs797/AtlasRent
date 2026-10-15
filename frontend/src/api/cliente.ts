import axios, { type AxiosInstance } from 'axios';

/**
 * Cliente HTTP de la API (DOC_TECNICA §6): JSON, cookie de sesión HttpOnly y doble envío de CSRF.
 * El interceptor lee la cookie en cada petición porque el backend la renueva al refrescar la sesión.
 */
const COOKIE_CSRF = 'atlas-rent-csrf';

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