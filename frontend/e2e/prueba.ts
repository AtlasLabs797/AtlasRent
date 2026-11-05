import { expect, test as base } from '@playwright/test';
import { ApiFalsa } from './apiFalsa';

/**
 * `test` con la fixture `api`: instala la API simulada antes de cada prueba y, al terminar, exige que
 * no haya habido errores de JavaScript en la página ni llamadas a rutas sin respuesta simulada (una
 * llamada nueva de la pantalla no debe pasar inadvertida).
 */
export const test = base.extend<{ api: ApiFalsa }>({
  api: async ({ page }, use) => {
    const errores: string[] = [];
    page.on('pageerror', (e) => errores.push(e.message));
    const api = await ApiFalsa.instalar(page);
    await use(api);
    expect(errores, 'errores de JavaScript en la página').toEqual([]);
    expect(api.sinResponder, 'llamadas a la API sin respuesta simulada').toEqual([]);
  },
});

export { expect };
