import { ACCIONES } from '../src/permisos';
import { problema } from './apiFalsa';
import { capturarClaroOscuro } from './capturas';
import { conSesion, identidad } from './fixtures';
import { expect, test } from './prueba';

const INMUEBLE = {
  id: 'dddddddd-0000-0000-0000-000000000001',
  nombre: 'Torre Coral',
  direccion: 'Av. Barceló, Bávaro',
  tipo: 'Edificio',
  propietariaId: 'eeeeeeee-0000-0000-0000-000000000001',
  plantillaContratoPorDefecto: null,
  renovacionDefectoMeses: 12,
  activo: true,
  createdAt: '2026-01-10T12:00:00Z',
};
const UNIDAD = {
  id: 'ffffffff-0000-0000-0000-000000000001',
  inmuebleId: INMUEBLE.id,
  nombre: 'Apto 2B',
  tipo: 'Apartamento',
  propietariaId: INMUEBLE.propietariaId,
  facturadoraPorDefectoId: null,
  estadoManual: 'Disponible',
  modalidades: ['Mensual', 'DiaCompleto'],
  tarifas: [
    { modalidad: 'Mensual', moneda: 'USD', importe: '1250.50' },
    { modalidad: 'DiaCompleto', moneda: 'USD', importe: '90.00' },
  ],
  metrosCuadrados: '82.50',
  capacidad: 4,
  habitaciones: 2,
  banos: '1.50',
  medidor: null,
  notas: null,
  activo: true,
  createdAt: '2026-01-11T12:00:00Z',
};
const UNIDAD_MANT = { ...UNIDAD, id: 'ffffffff-0000-0000-0000-000000000002', nombre: 'Apto 3A', estadoManual: 'Mantenimiento', tarifas: [] };
const FICHA = { ...INMUEBLE, notas: 'Edificio de 6 plantas', unidades: [UNIDAD, UNIDAD_MANT] };
const DOC = { id: '99999999-0000-0000-0000-000000000001', inmuebleId: INMUEBLE.id, categoria: 'Titulo', version: 2, nombreOriginal: 'titulo-torre.pdf', tamanoBytes: 2_411_724, tipoContenido: 'application/pdf', createdAt: '2026-05-02T15:00:00Z' };
const pagina = (data: unknown[]) => ({ data, total: data.length, page: 1, pageSize: 25, totalPages: 1 });

test('inmuebles: lista y ficha con unidades, estado manual y tarifas', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', '/inmuebles', { body: pagina([INMUEBLE]) });
  api.responder('GET', `/inmuebles/${INMUEBLE.id}`, { body: FICHA });
  api.responder('GET', `/inmuebles/${INMUEBLE.id}/documentos`, { body: pagina([DOC]) });

  await page.goto('/unidades');
  await expect(page.getByRole('heading', { name: 'Unidades e inmuebles', level: 1 })).toBeVisible();
  await capturarClaroOscuro(page, 'inmuebles-lista');
  await page.getByText('Torre Coral').click();

  await expect(page.getByRole('heading', { name: 'Torre Coral', level: 1 })).toBeVisible();
  await expect(page.getByText('Apto 2B')).toBeVisible();
  await expect(page.getByText('1.250,50')).toBeVisible();
  await expect(page.getByText('Sin tarifa')).toBeVisible();
  await capturarClaroOscuro(page, 'inmueble-ficha', { paginaCompleta: true });
});

test('unidad: edición manda tarifas como string y conserva la propietaria', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', `/inmuebles/${INMUEBLE.id}`, { body: FICHA });
  api.responder('GET', `/inmuebles/${INMUEBLE.id}/documentos`, { body: pagina([]) });
  api.responder('PUT', `/inmuebles/unidades/${UNIDAD.id}`, { body: UNIDAD });

  await page.goto(`/unidades/${INMUEBLE.id}`);
  await page.getByRole('button', { name: /Apto 2B/ }).click();
  await expect(page.getByLabel('Mensual · USD')).toHaveValue('1250.50');
  await page.getByLabel('Mensual · USD').fill('1300,5');
  await capturarClaroOscuro(page, 'unidad-edicion');
  await page.getByRole('button', { name: 'Guardar' }).click();
  await expect.poll(() => api.llamadas('PUT', `/inmuebles/unidades/${UNIDAD.id}`)).toBe(1);
  const cuerpo = api.ultimoCuerpo('PUT', `/inmuebles/unidades/${UNIDAD.id}`) as { tarifas: { modalidad: string; importe: unknown }[]; propietariaId: string; estadoManual: string };
  expect(cuerpo.tarifas.find((t) => t.modalidad === 'Mensual')?.importe).toBe('1300.50');
  expect(cuerpo.propietariaId).toBe(INMUEBLE.propietariaId);
  expect(cuerpo.estadoManual).toBe('Disponible');
});

test('documentos: gerente sube un PDF, ve el error del servidor y descarga', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', `/inmuebles/${INMUEBLE.id}`, { body: FICHA });
  api.responder('GET', `/inmuebles/${INMUEBLE.id}/documentos`, { body: pagina([DOC]) });
  api.responder('POST', `/inmuebles/${INMUEBLE.id}/documentos`, (p) =>
    p.numero === 1 ? problema(400, 'VAL-001', 'Documento no válido', 'El archivo no tiene firma de PDF.') : { body: DOC },
  );

  await page.goto(`/unidades/${INMUEBLE.id}`);
  await page.getByRole('tab', { name: 'Documentos' }).click();
  await expect(page.getByText('titulo-torre.pdf')).toBeVisible();
  await expect(page.getByText('v2')).toBeVisible();
  await capturarClaroOscuro(page, 'inmueble-documentos');

  // Cliente: un archivo que no es PDF ni llega a la API.
  await page.locator('#documento-archivo').setInputFiles({ name: 'foto.png', mimeType: 'image/png', buffer: Buffer.from('x') });
  await page.getByRole('button', { name: 'Subir PDF' }).click();
  await expect(page.getByText('El archivo no es un PDF. Solo se admiten documentos PDF.')).toBeVisible();
  expect(api.llamadas('POST', `/inmuebles/${INMUEBLE.id}/documentos`)).toBe(0);

  // Servidor: 400 con su detalle, y luego éxito.
  const pdf = { name: 'escritura.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4 prueba') };
  await page.locator('#documento-archivo').setInputFiles(pdf);
  await page.getByRole('button', { name: 'Subir PDF' }).click();
  await expect(page.getByText('El archivo no tiene firma de PDF.')).toBeVisible();
  await page.locator('#documento-archivo').setInputFiles(pdf);
  await page.getByRole('button', { name: 'Subir PDF' }).click();
  await expect(page.getByText('Documento subido.')).toBeVisible();
});

test('empleado: ve inmuebles y unidades pero no la pestaña de documentos ni pide la lista', async ({ page, api }) => {
  conSesion(api, identidad({ rol: 'empleado', permisosEfectivos: [ACCIONES.clienteVer, ACCIONES.clienteEditar] }));
  api.responder('GET', `/inmuebles/${INMUEBLE.id}`, { body: FICHA });

  await page.goto(`/unidades/${INMUEBLE.id}`);
  await expect(page.getByText('Apto 2B')).toBeVisible();
  await expect(page.getByRole('tab', { name: 'Documentos' })).toHaveCount(0);
  expect(api.llamadas('GET', `/inmuebles/${INMUEBLE.id}/documentos`)).toBe(0);
});
