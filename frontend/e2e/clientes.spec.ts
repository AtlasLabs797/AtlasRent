import { ACCIONES } from '../src/permisos';
import { capturarClaroOscuro } from './capturas';
import { conSesion, identidad } from './fixtures';
import { expect, test } from './prueba';

const EMPLEADO_EDITOR = identidad({
  nombreAcceso: 'empleado',
  nombre: 'Luis Rosario',
  rol: 'empleado',
  permisosEfectivos: [ACCIONES.clienteVer, ACCIONES.clienteEditar, ACCIONES.sesionVer, ACCIONES.sesionCerrar, ACCIONES.sesionPassword],
});

const RESUMEN = {
  id: 'aaaaaaaa-0000-0000-0000-000000000001',
  numero: 1042,
  nombre: 'Ana Pérez',
  tipo: 'PersonaFisica',
  email: 'ana@example.com',
  telefono: '809-555-0100',
  agenteRetencion: false,
  enGestionCobro: true,
  recordatoriosPausados: false,
  activo: true,
  createdAt: '2026-03-05T14:30:00Z',
};
const FICHA = { ...RESUMEN, documento: '001-0000000-1', direccion: 'Calle 1, Bávaro', notas: null };
const CONTACTO = { id: 'bbbbbbbb-0000-0000-0000-000000000001', clienteId: RESUMEN.id, nombre: 'Luis Gómez', relacion: 'Administrador', email: 'luis@example.com', telefono: '809-555-0101', esPrincipal: true };
const pagina = (data: unknown[]) => ({ data, total: data.length, page: 1, pageSize: 25, totalPages: 1 });

test('lista de clientes: búsqueda por nombre, búsqueda por documento en el cuerpo y alta con número solo para admin', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', '/clientes', { body: pagina([RESUMEN]) });
  api.responder('POST', '/clientes/busqueda', { body: pagina([RESUMEN]) });
  api.responder('POST', '/clientes', { body: RESUMEN });
  api.responder('GET', `/clientes/${RESUMEN.id}`, { body: FICHA });
  api.responder('GET', `/clientes/${RESUMEN.id}/contactos`, { body: [CONTACTO] });

  await page.goto('/clientes');
  await expect(page.getByRole('heading', { name: 'Clientes', level: 1 })).toBeVisible();
  await expect(page.getByText('Ana Pérez')).toBeVisible();
  await expect(page.getByText('1042')).toBeVisible();
  await capturarClaroOscuro(page, 'clientes-lista');

  // Búsqueda por documento: POST con el documento en el cuerpo, nada en la URL.
  await page.getByPlaceholder('Buscar por documento').fill('001-0000000-1');
  await page.getByRole('button', { name: 'Buscar', exact: true }).click();
  await expect.poll(() => api.llamadas('POST', '/clientes/busqueda')).toBeGreaterThan(0);
  expect((api.ultimoCuerpo('POST', '/clientes/busqueda') as { documento: string }).documento).toBe('001-0000000-1');
  expect(page.url()).not.toContain('001-0000000-1');
  expect(api.peticiones.some((p) => p.query.includes('001-0000000-1'))).toBe(false);

  // Alta como admin: aparece el campo de número y viaja en el cuerpo.
  await page.getByRole('button', { name: 'Nuevo cliente' }).click();
  await page.getByLabel(/^Nombre o razón social/).fill('Empresa Sol SRL');
  await page.getByLabel('Número de cliente').fill('5000');
  await capturarClaroOscuro(page, 'clientes-alta');
  await page.getByRole('button', { name: 'Guardar' }).click();
  await expect(page.getByRole('heading', { name: 'Ana Pérez', level: 1 })).toBeVisible();
  const cuerpo = api.ultimoCuerpo('POST', '/clientes') as Record<string, unknown>;
  expect(cuerpo.nombre).toBe('Empresa Sol SRL');
  expect(cuerpo.numero).toBe(5000);
  expect(cuerpo.documento).toBeNull();
});

test('ficha de cliente: documento completo, contactos y marcas', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', `/clientes/${RESUMEN.id}`, { body: FICHA });
  api.responder('GET', `/clientes/${RESUMEN.id}/contactos`, { body: [CONTACTO] });

  await page.goto(`/clientes/${RESUMEN.id}`);
  await expect(page.getByRole('heading', { name: 'Ana Pérez', level: 1 })).toBeVisible();
  await expect(page.getByText('001-0000000-1')).toBeVisible();
  await expect(page.getByText('Luis Gómez')).toBeVisible();
  await expect(page.getByText('Principal')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Estado de cuenta (PDF)' })).toBeVisible();
  await capturarClaroOscuro(page, 'clientes-ficha', { paginaCompleta: true });
});

test('empleado: edita clientes pero no fija el número ni descarga el estado de cuenta', async ({ page, api }) => {
  conSesion(api, EMPLEADO_EDITOR);
  api.responder('GET', `/clientes/${RESUMEN.id}`, { body: FICHA });
  api.responder('GET', `/clientes/${RESUMEN.id}/contactos`, { body: [] });
  api.responder('GET', '/clientes', { body: pagina([RESUMEN]) });

  await page.goto('/clientes');
  await page.getByRole('button', { name: 'Nuevo cliente' }).click();
  await expect(page.getByLabel(/^Nombre o razón social/)).toBeVisible();
  await expect(page.getByLabel('Número de cliente')).toHaveCount(0);
  await page.getByRole('button', { name: 'Cancelar' }).click();

  await page.goto(`/clientes/${RESUMEN.id}`);
  await expect(page.getByRole('heading', { name: 'Ana Pérez', level: 1 })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Estado de cuenta (PDF)' })).toHaveCount(0);
  await expect(page.getByText('Sin contactos')).toBeVisible();
});

test('empleado solo lectura: no ve botones de alta ni de edición', async ({ page, api }) => {
  conSesion(api, identidad({ rol: 'empleado', permisosEfectivos: [ACCIONES.clienteVer] }));
  api.responder('GET', '/clientes', { body: pagina([RESUMEN]) });
  await page.goto('/clientes');
  await expect(page.getByText('Ana Pérez')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Nuevo cliente' })).toHaveCount(0);
});

test('prospectos: lista y edición abre el panel con el documento', async ({ page, api }) => {
  conSesion(api);
  const P = { id: 'cccccccc-0000-0000-0000-000000000001', nombre: 'Pro Spectos SRL', tipo: 'Empresa', email: null, telefono: '809-555-0199', activo: true, createdAt: '2026-04-01T10:00:00Z' };
  api.responder('GET', '/prospectos', { body: pagina([P]) });
  api.responder('GET', `/prospectos/${P.id}`, { body: { ...P, documento: '1-31-00000-1', notas: 'Quiere oficina' } });

  await page.goto('/clientes/prospectos');
  await expect(page.getByText('Pro Spectos SRL')).toBeVisible();
  await capturarClaroOscuro(page, 'prospectos-lista');
  await page.getByText('Pro Spectos SRL').click();
  await expect(page.getByLabel('Documento')).toHaveValue('1-31-00000-1');
  await expect(page.getByRole('button', { name: 'Crear cliente con estos datos' })).toBeVisible();
});
