import { capturarClaroOscuro } from './capturas';
import { ALERTAS_PANEL, EMPLEADO, conSesion, identidad, panelAdmin } from './fixtures';
import { expect, test } from './prueba';

test('panel: gerente con KPI, comparación con el mes anterior y accesos', async ({ page, api }) => {
  conSesion(api, identidad());
  panelAdmin(api);
  await page.goto('/');
  await expect(page.getByText('Facturado · 10/2026')).toBeVisible();
  await expect(page.getByText(/Mes anterior \(09\/2026\)/).first()).toBeVisible();
  await expect(page.getByText('8.500,00').first()).toBeVisible();
  await expect(page.getByRole('main').getByRole('button', { name: 'Bandeja', exact: true })).toBeVisible();
  await expect(page.getByText('Registrar cobro')).toHaveCount(0);
  await expect(page.getByText('Alertas abiertas')).toBeVisible();
  await expect(page.getByText('Factura periódica fallida')).toBeVisible();
  await capturarClaroOscuro(page, 'panel-admin', { paginaCompleta: true });
});

test('panel: el empleado ve el hueco con RestrictedValue y no pide el dashboard', async ({ page, api }) => {
  conSesion(api, EMPLEADO);
  api.responder('GET', '/alertas', { body: ALERTAS_PANEL });
  await page.goto('/');
  await expect(page.getByText('Solo gerencia').first()).toBeVisible();
  expect(api.llamadas('GET', '/informes/dashboard')).toBe(0);
  await capturarClaroOscuro(page, 'panel-empleado');
});
