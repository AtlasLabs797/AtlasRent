import type { Page, Route } from '@playwright/test';

/**
 * API simulada para los E2E: intercepta `/api/v1/**` con `page.route` y responde con fixtures por
 * ruta. Sin backend, sin base de datos y sin red.
 *
 * Uso:
 *   const api = await ApiFalsa.instalar(page);
 *   api.responder('GET', '/auth/yo', { status: 401 });
 *   api.responder('POST', '/auth/login', (peticion) => ({ body: identidad }));
 *   expect(api.llamadas('POST', '/auth/refresh')).toBe(1);
 *
 * Una ruta sin respuesta registrada devuelve 501 y se anota en `sinResponder`: los tests lo
 * comprueban al final para que una llamada nueva de la pantalla no pase inadvertida.
 */
export interface PeticionFalsa {
  metodo: string;
  /** Ruta sin el prefijo `/api/v1` ni la query: `/auth/login`. */
  ruta: string;
  query: string;
  cuerpo: unknown;
  /** Cuántas veces se ha llamado ya a este método y ruta, contando esta (empieza en 1). */
  numero: number;
}

export interface RespuestaFalsa {
  status?: number;
  body?: unknown;
  headers?: Record<string, string>;
}

export type Respuesta = RespuestaFalsa | ((peticion: PeticionFalsa) => RespuestaFalsa);

/** ProblemDetails como los construye `Problemas.Construir` del backend. */
export function problema(status: number, codigoReferencia: string, title: string, detail: string, errors?: Record<string, string[]>): RespuestaFalsa {
  return { status, body: { status, title, detail, codigoReferencia, ...(errors ? { errors } : {}) } };
}

export class ApiFalsa {
  readonly peticiones: PeticionFalsa[] = [];
  readonly sinResponder: string[] = [];
  private readonly respuestas = new Map<string, Respuesta>();
  private readonly contadores = new Map<string, number>();

  private constructor() {}

  static async instalar(page: Page): Promise<ApiFalsa> {
    const api = new ApiFalsa();
    await page.route('**/api/v1/**', (route) => api.atender(route));
    return api;
  }

  /** Registra (o sustituye) la respuesta de un método y ruta. */
  responder(metodo: string, ruta: string, respuesta: Respuesta): this {
    this.respuestas.set(`${metodo.toUpperCase()} ${ruta}`, respuesta);
    return this;
  }

  /** Cuántas veces se ha llamado a un método y ruta. */
  llamadas(metodo: string, ruta: string): number {
    return this.contadores.get(`${metodo.toUpperCase()} ${ruta}`) ?? 0;
  }

  /** Último cuerpo JSON recibido en un método y ruta. */
  ultimoCuerpo(metodo: string, ruta: string): unknown {
    const clave = `${metodo.toUpperCase()} ${ruta}`;
    return [...this.peticiones].reverse().find((p) => `${p.metodo} ${p.ruta}` === clave)?.cuerpo;
  }

  private async atender(route: Route): Promise<void> {
    const peticion = route.request();
    const url = new URL(peticion.url());
    const ruta = url.pathname.replace(/^\/api\/v1/, '');
    const metodo = peticion.method().toUpperCase();
    const clave = `${metodo} ${ruta}`;
    const numero = (this.contadores.get(clave) ?? 0) + 1;
    this.contadores.set(clave, numero);

    let cuerpo: unknown;
    try {
      cuerpo = peticion.postDataJSON();
    } catch {
      cuerpo = peticion.postData();
    }
    const registro: PeticionFalsa = { metodo, ruta, query: url.search, cuerpo, numero };
    this.peticiones.push(registro);

    const definida = this.respuestas.get(clave);
    if (!definida) {
      this.sinResponder.push(clave);
      await route.fulfill({ status: 501, contentType: 'application/json', body: JSON.stringify({ title: 'Sin respuesta simulada', detail: clave }) });
      return;
    }
    const { status = 200, body, headers } = typeof definida === 'function' ? definida(registro) : definida;
    await route.fulfill({
      status,
      contentType: status >= 400 ? 'application/problem+json' : 'application/json',
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  }
}
