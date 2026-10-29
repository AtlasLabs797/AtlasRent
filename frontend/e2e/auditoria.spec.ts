import { capturarClaroOscuro } from './capturas';
import { EMPLEADO, conSesion, identidad } from './fixtures';
import { expect, test } from './prueba';
import { ACCIONES } from '../src/permisos';

const pagina = <T,>(data: T[]) => ({ data, total: data.length, page: 1, pageSize: 50, totalPages: 1 });

const FILA = (id: string, accion: string, extra: Record<string, unknown> = {}) => ({
  id,
  fecha: '2026-10-27T15:00:00Z',
  usuarioId: '11111111-1111-1111-1111-111111111111',
  usuarioNombre: 'Marta Peña',
  ip: '203.0.113.7',
  accion,
  entidad: 'factura',
  entidadId: 'f0000000-0000-4000-8000-000000000001',
  resultado: 'ok',
  origen: 'api',
  datos: { numero: 'FAC-A-2026-00001', password: '***', nota: '<img src=x onerror=alert(1)>' },
  ...extra,
});

const GERENTE = identidad({
  rol: 'gerente',
  permisosEfectivos: [ACCIONES.sesionVer, ACCIONES.clienteVer, ACCIONES.auditoriaVer],
});

for (const [nombre, quien] of [['administrador', identidad()], ['gerente', GERENTE]] as const) {
  test(`auditoría (${nombre}): lista, filtros y datos como texto`, async ({ page, api }) => {
    conSesion(api, quien);
    api.responder('GET', '/auditoria', (p) =>
      p.query.includes('entidad=cobro')
        ? { body: pagina([FILA('x2', 'cobro.registrado', { entidad: 'cobro', datos: null })]) }
        : { body: pagina([FILA('x1', 'factura.emitida'), FILA('x2', 'sesion.login', { usuarioId: null, usuarioNombre: null, entidad: null, datos: null })]) },
    );
    await page.goto('/auditoria');
    await expect(page.getByRole('heading', { name: 'Auditoría', level: 1 })).toBeVisible();
    await expect(page.getByText('factura.emitida')).toBeVisible();
    await expect(page.getByRole('cell', { name: 'Sistema' })).toBeVisible();

    await page.getByRole('button', { name: 'Ver los datos del evento factura.emitida' }).click();
    const detalle = page.locator('pre');
    await expect(detalle).toContainText('FAC-A-2026-00001');
    await expect(detalle).toContainText('<img src=x onerror=alert(1)>');
    await expect(page.locator('pre img')).toHaveCount(0);
    await capturarClaroOscuro(page, `auditoria-${nombre}`, { paginaCompleta: true });

    await page.getByLabel('Entidad').fill('cobro');
    await page.getByLabel('Desde').fill('2026-10-01');
    await page.getByRole('button', { name: 'Filtrar' }).click();
    await expect(page.getByText('cobro.registrado')).toBeVisible();
    await expect(page.getByText('factura.emitida')).toHaveCount(0);
    const ultima = api.peticiones.filter((p) => p.ruta === '/auditoria').at(-1)!;
    expect(ultima.query).toContain('entidad=cobro');
    expect(ultima.query).toContain('desde=2026-10-01');
  });
}

test('auditoría: el empleado no ve la entrada de menú ni la ruta', async ({ page, api }) => {
  conSesion(api, EMPLEADO);
  api.responder('GET', '/alertas', { body: pagina([]) });
  await page.goto('/');
  await expect(page.getByRole('link', { name: 'Auditoría' })).toHaveCount(0);
  await page.goto('/auditoria');
  await expect(page.getByRole('heading', { name: 'Auditoría', level: 1 })).toHaveCount(0);
});
