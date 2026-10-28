/**
 * Lógica pura del refresco transparente de la sesión (sin Axios ni React: la carga el runner de
 * Node en `tests/api.test.ts`).
 */

/** Rutas donde un 401 no significa "sesión caducada": credenciales, segundo factor o cierre de sesión. */
const SIN_REFRESCO = ['/auth/login', '/auth/refresh', '/auth/logout', '/auth/mfa/', '/auth/emergencia/entrar'];

/**
 * Si un fallo justifica pedir `POST /auth/refresh` y repetir la petición: un 401 en una ruta que no
 * es de autenticación y que todavía no se ha repetido. Que el refresco y la repetición queden fuera
 * es lo que impide el bucle.
 */
export function debeRefrescar(estado: number | undefined, url: string | undefined, yaReintentada: boolean): boolean {
  if (estado !== 401 || yaReintentada) return false;
  const ruta = url ?? '';
  return !SIN_REFRESCO.some((prefijo) => ruta.startsWith(prefijo));
}

/**
 * Single-flight: mientras haya un refresco en curso, quien lo pida espera al mismo. Sin esto, N
 * peticiones simultáneas con el JWT caducado lanzarían N refrescos y la rotación del token de
 * refresco invalidaría N-1 de ellos.
 */
export function crearRefrescador<T>(refrescar: () => Promise<T>): () => Promise<T> {
  let enCurso: Promise<T> | null = null;
  return () => {
    enCurso ??= refrescar().finally(() => {
      enCurso = null;
    });
    return enCurso;
  };
}

/** Un fallo del refresco que significa "ya no hay sesión" (4xx), no "la red falló" (sin respuesta o 5xx). */
export function refrescoDefinitivo(estado: number | undefined): boolean {
  return estado !== undefined && estado >= 400 && estado < 500;
}
