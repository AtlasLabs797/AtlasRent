import { mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { Page } from '@playwright/test';

const CARPETA = join(dirname(fileURLToPath(import.meta.url)), 'capturas');

/** Misma clave que `src/tema/resolver.ts` (CLAVE_TEMA) y `public/tema-inicial.js`. */
const CLAVE_TEMA = 'atlas-rent.tema';

/**
 * Fija el tema como lo hace el selector de tema de la barra superior (`useTema.setPreferencia`):
 * guarda la preferencia en localStorage y pone `data-theme` en `<html>`. No recarga la página, así
 * que se puede capturar a mitad de un flujo.
 */
export async function fijarTema(page: Page, tema: 'claro' | 'oscuro'): Promise<void> {
  await page.emulateMedia({ colorScheme: tema === 'oscuro' ? 'dark' : 'light' });
  await page.evaluate(
    ([clave, preferencia, atributo]) => {
      localStorage.setItem(clave!, preferencia!);
      document.documentElement.setAttribute('data-theme', atributo!);
    },
    [CLAVE_TEMA, tema, tema === 'oscuro' ? 'dark' : 'light'],
  );
}

/**
 * Captura la pantalla actual en tema claro y en oscuro y devuelve las rutas de los PNG
 * (`e2e/capturas/<nombre>-claro.png` y `-oscuro.png`, ignoradas por git).
 */
export async function capturarClaroOscuro(page: Page, nombre: string, opciones: { paginaCompleta?: boolean } = {}): Promise<string[]> {
  mkdirSync(CARPETA, { recursive: true });
  const rutas: string[] = [];
  for (const tema of ['claro', 'oscuro'] as const) {
    await fijarTema(page, tema);
    // Deja que terminen las transiciones de color antes de capturar.
    await page.waitForTimeout(250);
    const ruta = join(CARPETA, `${nombre}-${tema}.png`);
    await page.screenshot({ path: ruta, fullPage: opciones.paginaCompleta ?? false });
    rutas.push(ruta);
  }
  // Deja el navegador como estaba: sin preferencia guardada, para que las páginas siguientes del mismo
  // test no arranquen con el selector de tema desfasado respecto a `data-theme`.
  await page.emulateMedia({ colorScheme: 'light' });
  await page.evaluate((clave) => {
    localStorage.removeItem(clave);
    document.documentElement.setAttribute('data-theme', 'light');
  }, CLAVE_TEMA);
  return rutas;
}
