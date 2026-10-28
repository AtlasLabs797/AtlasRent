import { capturarClaroOscuro } from './capturas';
import { EMPLEADO, conSesion, identidad } from './fixtures';
import { expect, test } from './prueba';
import { ACCIONES } from '../src/permisos';

const pagina = <T,>(data: T[]) => ({ data, total: data.length, page: 1, pageSize: 25, totalPages: 1 });

const ENVIO = (id: string, estado: string, extra = {}) => ({
  id,
  tipo: 'Factura',
  documentoId: 'd0000000-0000-4000-8000-000000000001',
  documentoAnio: 2026,
  clienteId: 'c0000000-0000-4000-8000-000000000001',
  destinatarioEmail: 'cliente@ejemplo.test',
  destinatarioNombre: 'Cliente de prueba',
  asunto: 'Factura',
  idioma: 'es',
  estado,
  nombreArchivo: 'FAC-A-2026-00001.pdf',
  documentoModificadoAt: null,
  intentos: 1,
  ultimoError: null,
  enviadoAt: '2026-10-27T15:00:00Z',
  createdAt: '2026-10-27T14:59:00Z',
  ...extra,
});

const LOTE = 'f0000000-0000-4000-8000-000000000001';
const GERENTE = identidad({
  rol: 'gerente',
  permisosEfectivos: [ACCIONES.sesionVer, ACCIONES.facturaVer, ACCIONES.mensajeriaMasivo, ACCIONES.clienteEditar, ACCIONES.cotizacionEnviar],
});

test('envíos: historial con Enviando y Fallido, y el empleado no ve masivo ni recordatorios', async ({ page, api }) => {
  conSesion(api, identidad({ ...EMPLEADO, permisosEfectivos: [...EMPLEADO.permisosEfectivos, ACCIONES.facturaVer] }));
  api.responder('GET', '/envios', {
    body: pagina([ENVIO('e1', 'Enviando'), ENVIO('e2', 'Fallido', { ultimoError: 'Servidor de correo no disponible' })]),
  });
  await page.goto('/envios');
  await expect(page.getByRole('heading', { name: 'Envíos', level: 1 })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Enviando' })).toBeVisible();
  await expect(page.getByText('Servidor de correo no disponible')).toBeVisible();
  await expect(page.getByRole('tab', { name: 'Envío masivo' })).toHaveCount(0);
  await expect(page.getByRole('tab', { name: 'Recordatorios' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Reintentar' })).toHaveCount(0);
  await capturarClaroOscuro(page, 'envios-historial');
});

test('envío masivo: preparar, revisar con vista previa, confirmar y reintentar fallidos', async ({ page, api }) => {
  conSesion(api, GERENTE);
  api.responder('GET', '/envios', { body: pagina([]) });
  api.responder('POST', '/envios/masivo/preparar', {
    body: {
      total: 1,
      totalExcluidos: 1,
      documentos: [
        {
          documentoId: 'd1',
          tipoDocumento: 'Factura',
          numero: 'FAC-A-2026-00001',
          clienteId: 'c1',
          cliente: 'Cliente de prueba',
          destinatarioEmail: 'cliente@ejemplo.test',
          destinatarioNombre: 'Cliente de prueba',
          asunto: 'Factura FAC-A-2026-00001',
          cuerpo: 'Adjuntamos su factura.',
          nombreArchivo: 'FAC-A-2026-00001.pdf',
          idioma: 'es',
        },
      ],
      excluidos: [
        { documentoId: 'd2', tipo: 'Factura', numero: 'FAC-A-2026-00002', cliente: 'Otro cliente', motivo: 'SinDireccionCorreo', detalle: 'Sin correo' },
      ],
      filtro: {},
      idioma: 'es',
      aviso: '',
    },
  });
  api.responder('POST', '/envios/masivo/confirmar', { body: { loteId: LOTE, total: 1, excluidos: 1, idioma: 'es', aviso: '' } });
  api.responder('GET', `/envios/masivo/${LOTE}`, {
    body: {
      loteId: LOTE,
      total: 1,
      enviados: 0,
      fallidos: 1,
      pendientes: 0,
      fallidosDetalle: [
        { envioId: 'e2', documentoId: 'd1', tipoDocumento: 'Factura', cliente: 'Cliente de prueba', destinatarioEmail: 'cliente@ejemplo.test', intentos: 3, motivo: 'Buzón lleno' },
      ],
    },
  });
  api.responder('POST', `/envios/masivo/${LOTE}/reintentar`, {
    body: { loteId: LOTE, reintentados: 1, pendientes: 1, aviso: 'Vuelve a encolar solo los fallidos.' },
  });

  await page.goto('/envios');
  await page.getByRole('tab', { name: 'Envío masivo' }).click();
  await page.getByRole('button', { name: 'Preparar envío' }).click();
  await expect(page.getByText('Documentos a enviar: 1 · Excluidos: 1')).toBeVisible();
  await expect(page.getByText(/Adjuntamos su factura\./)).toBeVisible();
  await expect(page.getByText('Sin dirección de correo')).toBeVisible();
  await capturarClaroOscuro(page, 'envios-masivo-revision', { paginaCompleta: true });

  await page.getByRole('button', { name: 'Confirmar y enviar' }).click();
  await expect(page.getByText('El lote ha terminado con envíos fallidos')).toBeVisible();
  await expect(page.getByText('Buzón lleno')).toBeVisible();
  expect(api.ultimoCuerpo('POST', '/envios/masivo/confirmar')).toEqual(api.ultimoCuerpo('POST', '/envios/masivo/preparar'));

  await page.getByRole('button', { name: 'Reintentar fallidos' }).click();
  await expect(page.getByText('1 envíos vueltos a encolar')).toBeVisible();
});

test('recordatorios: confirmar un aviso que vuelve Omitido muestra el motivo, sin error', async ({ page, api }) => {
  conSesion(api, GERENTE);
  api.responder('GET', '/envios', { body: pagina([]) });
  const fila = {
    id: 'r1',
    clienteId: 'c1',
    cliente: 'Cliente de prueba',
    unidadId: null,
    unidad: 'Unidad 4B',
    fecha: '2026-10-28',
    estado: 'Pendiente',
    envioId: null,
    destinatario: null,
    motivoOmision: null,
    createdAt: '2026-10-28T13:00:00Z',
  };
  api.responder('GET', '/recordatorios', { body: pagina([fila]) });
  api.responder('POST', '/recordatorios/r1/enviar', { body: { ...fila, estado: 'Omitido', motivoOmision: 'El cliente ya pagó.' } });

  await page.goto('/envios');
  await page.getByRole('tab', { name: 'Recordatorios' }).click();
  await expect(page.getByText('Unidad 4B')).toBeVisible();
  await capturarClaroOscuro(page, 'envios-recordatorios');
  await page.getByRole('button', { name: 'Confirmar y enviar' }).click();
  await expect(page.getByText('El aviso se ha omitido')).toBeVisible();
  await expect(page.getByText('El cliente ya pagó.').first()).toBeVisible();
});
