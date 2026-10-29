import { ACCIONES } from '../src/permisos';
import { problema } from './apiFalsa';
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


const PROSPECTO = {
  id: 'cccccccc-0000-0000-0000-000000000001',
  nombre: 'Pro Spectos SRL',
  tipo: 'Empresa',
  email: null,
  telefono: '809-555-0199',
  activo: true,
  createdAt: '2026-04-01T10:00:00Z',
  clienteId: null,
  convertidoAt: null,
};

test('prospectos: lista, panel con el documento y conversión en cliente con los datos precargados', async ({ page, api }) => {
  conSesion(api);
  const nuevo = { ...RESUMEN, id: 'aaaaaaaa-0000-0000-0000-000000000009', numero: 1043, nombre: 'Pro Spectos SRL', tipo: 'Empresa' };
  api.responder('GET', '/prospectos', { body: pagina([PROSPECTO]) });
  api.responder('GET', `/prospectos/${PROSPECTO.id}`, { body: { ...PROSPECTO, documento: '1-31-00000-1', notas: 'Quiere oficina' } });
  api.responder('POST', `/prospectos/${PROSPECTO.id}/convertir`, { body: nuevo });
  api.responder('GET', `/clientes/${nuevo.id}`, { body: { ...nuevo, documento: '1-31-00000-1', direccion: null, notas: 'Quiere oficina' } });
  api.responder('GET', `/clientes/${nuevo.id}/contactos`, { body: [] });

  await page.goto('/clientes/prospectos');
  await expect(page.getByText('Pro Spectos SRL')).toBeVisible();
  await capturarClaroOscuro(page, 'prospectos-lista');
  await page.getByText('Pro Spectos SRL').click();
  await expect(page.getByLabel('Documento')).toHaveValue('1-31-00000-1');
  await expect(page.getByText(/sus cotizaciones y reservas pasan al cliente nuevo/)).toBeVisible();
  await capturarClaroOscuro(page, 'prospecto-panel');

  await page.getByRole('button', { name: 'Convertir en cliente' }).click();
  await expect(page.getByRole('heading', { name: 'Convertir prospecto en cliente' })).toBeVisible();
  await expect(page.getByLabel(/^Nombre o razón social/)).toHaveValue('Pro Spectos SRL');
  await expect(page.getByLabel('Documento')).toHaveValue('1-31-00000-1');
  await expect(page.getByLabel('Número de cliente')).toHaveCount(0);
  await capturarClaroOscuro(page, 'prospecto-convertir');
  await page.getByRole('button', { name: 'Guardar' }).click();

  await expect(page).toHaveURL(new RegExp(`/clientes/${nuevo.id}$`));
  const cuerpo = api.ultimoCuerpo('POST', `/prospectos/${PROSPECTO.id}/convertir`) as Record<string, unknown>;
  expect(cuerpo.nombre).toBe('Pro Spectos SRL');
  expect(cuerpo.documento).toBe('1-31-00000-1');
  expect(cuerpo.numero).toBeUndefined();
  expect(api.llamadas('POST', '/clientes')).toBe(0);
});

test('prospectos: 409 al convertir muestra el motivo de la API y marca el documento', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', '/prospectos', { body: pagina([PROSPECTO]) });
  api.responder('GET', `/prospectos/${PROSPECTO.id}`, { body: { ...PROSPECTO, documento: '1-31-00000-1', notas: null } });
  api.responder('POST', `/prospectos/${PROSPECTO.id}/convertir`, problema(409, 'CON-001', 'Conflicto', 'Ya hay un cliente con ese documento de identidad.'));

  await page.goto('/clientes/prospectos');
  await page.getByText('Pro Spectos SRL').click();
  await page.getByRole('button', { name: 'Convertir en cliente' }).click();
  await page.getByRole('button', { name: 'Guardar' }).click();
  await expect(page.getByText('Ya hay un cliente con ese documento de identidad.').first()).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Convertir prospecto en cliente' })).toBeVisible();
  await capturarClaroOscuro(page, 'prospecto-convertir-409');
});

test('prospecto ya convertido: enlaza a su cliente, no ofrece convertir y queda de solo lectura', async ({ page, api }) => {
  conSesion(api);
  const convertido = { ...PROSPECTO, activo: false, clienteId: RESUMEN.id, convertidoAt: '2026-10-01T10:00:00Z' };
  api.responder('GET', '/prospectos', { body: pagina([convertido]) });
  api.responder('GET', `/prospectos/${PROSPECTO.id}`, { body: { ...convertido, documento: null, notas: null } });
  api.responder('GET', `/clientes/${RESUMEN.id}`, { body: FICHA });
  api.responder('GET', `/clientes/${RESUMEN.id}/contactos`, { body: [] });

  await page.goto('/clientes/prospectos');
  await page.getByText('Pro Spectos SRL').click();
  await expect(page.getByText('Este prospecto ya es cliente.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Convertir en cliente' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Reactivar' })).toHaveCount(0);
  await capturarClaroOscuro(page, 'prospecto-convertido');
  await page.getByRole('button', { name: 'Ver cliente' }).click();
  await expect(page).toHaveURL(new RegExp(`/clientes/${RESUMEN.id}$`));
});

test('prospecto desactivado: se reactiva desde su panel', async ({ page, api }) => {
  conSesion(api);
  const inactivo = { ...PROSPECTO, activo: false };
  api.responder('GET', '/prospectos', { body: pagina([inactivo]) });
  api.responder('GET', `/prospectos/${PROSPECTO.id}`, { body: { ...inactivo, documento: null, notas: null } });
  api.responder('POST', `/prospectos/${PROSPECTO.id}/reactivar`, { body: { ...PROSPECTO } });

  await page.goto('/clientes/prospectos');
  await page.getByText('Pro Spectos SRL').click();
  await expect(page.getByText('Este prospecto está desactivado.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Convertir en cliente' })).toHaveCount(0);
  await page.getByRole('button', { name: 'Reactivar' }).click();
  await expect.poll(() => api.llamadas('POST', `/prospectos/${PROSPECTO.id}/reactivar`)).toBe(1);
});

test('cliente desactivado: el listado muestra inactivos, la ficha avisa y se reactiva', async ({ page, api }) => {
  conSesion(api);
  const inactivo = { ...RESUMEN, activo: false };
  api.responder('GET', '/clientes', { body: pagina([inactivo]) });
  api.responder('GET', `/clientes/${RESUMEN.id}`, { body: { ...FICHA, activo: false } });
  api.responder('GET', `/clientes/${RESUMEN.id}/contactos`, { body: [] });
  api.responder('POST', `/clientes/${RESUMEN.id}/reactivar`, { body: { ...RESUMEN } });

  await page.goto('/clientes');
  await page.getByText('Mostrar inactivos').click();
  await expect.poll(() => api.peticiones.some((p) => p.ruta === '/clientes' && p.query.includes('incluirInactivos=true'))).toBe(true);
  await page.getByText('Ana Pérez').click();
  await expect(page.getByText(/Este cliente está desactivado/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Desactivar' })).toHaveCount(0);
  await capturarClaroOscuro(page, 'clientes-ficha-inactivo');
  await page.getByRole('button', { name: 'Reactivar' }).click();
  await expect.poll(() => api.llamadas('POST', `/clientes/${RESUMEN.id}/reactivar`)).toBe(1);
});
