import { defineConfig, devices } from '@playwright/test';

/**
 * E2E con la API simulada (`e2e/apiFalsa.ts`): no hace falta backend ni base de datos. Se sirve el
 * build con `vite preview` en un puerto fijo y Playwright lo cierra al terminar (AGENTS.md §12: ningún
 * servidor se queda vivo). Solo Chromium.
 */
const PUERTO = 4173;

export default defineConfig({
  testDir: 'e2e',
  testMatch: '**/*.spec.ts',
  fullyParallel: true,
  forbidOnly: true,
  retries: 0,
  reporter: [['list']],
  timeout: 30_000,
  expect: { timeout: 7_000 },
  outputDir: 'e2e/resultados',
  use: {
    baseURL: `http://127.0.0.1:${PUERTO}`,
    locale: 'es-DO',
    timezoneId: 'America/Santo_Domingo',
    viewport: { width: 1280, height: 800 },
    trace: 'off',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 800 } } }],
  webServer: {
    command: `npm run build && npx vite preview --host 127.0.0.1 --port ${PUERTO} --strictPort`,
    url: `http://127.0.0.1:${PUERTO}`,
    reuseExistingServer: false,
    timeout: 120_000,
    stdout: 'ignore',
    stderr: 'pipe',
  },
});
