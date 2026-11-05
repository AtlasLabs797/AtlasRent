import { problema } from './apiFalsa';
import { capturarClaroOscuro } from './capturas';
import { ALERTA, EMPLEADO, DASHBOARD, conSesion, identidad } from './fixtures';
import { expect, test } from './prueba';
import { ACCIONES } from '../src/permisos';

const pagina = <T,>(data: T[]) => ({ data, total: data.length, page: 1, pageSize: 25, totalPages: 1 });

const SOLICITUD = {
  solicitudId: 'a0000000-0000-4000-8000-000000000001',
  contratoId: 'b0000000-0000-4000-8000-000000000001',
  solicitadoPorId: null,
  solicitadoPor: 'Luis Rosario',
  cliente: 'Cliente de prueba',
  unidad: 'Unidad 4B',
  estado: 'PendienteAprobacion',
  fechaInicio: '2026-11-01',
  fechaFinVigente: '2027-10-31',
  rentaVigente: '800.00',
  rentaPropuesta: '900.00',
  moneda: 'USD',
  hayCambioDeRenta: true,
  huellaContenido: 'abc',
  solicitadoAt: '2026-10-27T15:00:00Z',
  comentario: null,
};

const PROPUESTA = {
  id: 'c0000000-0000-4000-8000-000000000001',
  tipo: 'recordatorio_saldo',
  clienteId: 'd0000000-0000-4000-8000-000000000001',
  cliente: 'Cliente de prueba',
  recordatorioId: 'e0000000-0000-4000-8000-000000000001',
  seudonimo: 'CLI-A1',
  pendiente: '450.00 USD',
  huellaContenido: 'f1e2d3c4b5a69788',
  resultado: 'Pendiente',
  motivoCaducidad: null,
  comentario: 'Lleva 20 días vencida.',
  solicitadoAt: '2026-10-27T15:00:00Z',
  solicitadoPor: 'Marta Peña',
  resueltoAt: null,
  resueltoPor: null,
};

const GERENTE = identidad({
  rol: 'gerente',
  permisosEfectivos: [
    ACCIONES.sesionVer,
    ACCIONES.clienteVer,
    ACCIONES.contratoEstado,
    ACCIONES.iaProponer,
    ACCIONES.mensajeriaMasivo,
    ACCIONES.facturaVer,
    ACCIONES.informeVer,
  ],
});

test('bandeja: contratos con renta vigente y propuesta, 409 pide revisar y rechazar exige motivo', async ({ page, api }) => {
  conSesion(api, GERENTE);
  api.responder('GET', '/informes/dashboard', { body: DASHBOARD });
  api.responder('GET', '/contratos/solicitudes', { body: pagina([SOLICITUD]) });
  api.responder('GET', '/ia/propuestas', { body: pagina([PROPUESTA]) });
  api.responder(
    'POST',
    `/contratos/solicitudes/${SOLICITUD.solicitudId}/aprobacion`,
    problema(409, 'CON-009', 'Conflicto', 'La solicitud cambió: ya no es la vigente de este contrato. Vuelve a revisarla.'),
  );
  api.responder('POST', `/contratos/solicitudes/${SOLICITUD.solicitudId}/rechazo`, { body: {} });

  await page.goto('/bandeja');
  await expect(page.getByRole('heading', { name: 'Bandeja', level: 1 })).toBeVisible();
  await expect(page.getByText('Unidad 4B')).toBeVisible();
  await expect(page.getByText('800,00')).toBeVisible();
  await expect(page.getByText('900,00').first()).toBeVisible();
  await capturarClaroOscuro(page, 'bandeja-contratos');

  await page.getByRole('button', { name: 'Aprobar' }).click();
  await expect(page.getByText('La solicitud ha cambiado')).toBeVisible();
  await expect(page.getByText(/Vuelve a revisarla\./)).toBeVisible();
  expect(api.llamadas('POST', `/contratos/solicitudes/${SOLICITUD.solicitudId}/aprobacion`)).toBe(1);

  await page.getByRole('button', { name: 'Rechazar', exact: true }).click();
  const confirmar = page.getByRole('dialog').getByRole('button', { name: 'Rechazar solicitud' });
  await expect(confirmar).toBeDisabled();
  await page.getByLabel(/Motivo/).fill('Renta fuera de rango');
  await confirmar.click();
  await expect
    .poll(() => api.ultimoCuerpo('POST', `/contratos/solicitudes/${SOLICITUD.solicitudId}/rechazo`))
    .toEqual({ comentario: 'Renta fuera de rango' });
});

test('bandeja: propuesta de la IA con evidencia; si caduca se enseña el motivo y no hay Modificar', async ({ page, api }) => {
  conSesion(api, GERENTE);
  api.responder('GET', '/informes/dashboard', { body: DASHBOARD });
  api.responder('GET', '/contratos/solicitudes', { body: pagina([]) });
  api.responder('GET', '/ia/propuestas', { body: pagina([PROPUESTA]) });
  api.responder('POST', `/ia/propuestas/${PROPUESTA.id}/aprobacion`, {
    body: { ...PROPUESTA, resultado: 'Caducada', motivoCaducidad: 'El saldo cambió desde que se propuso.' },
  });

  await page.goto('/bandeja');
  await page.getByRole('tab', { name: /Propuestas de la IA/ }).click();
  await expect(page.getByText('Lleva 20 días vencida.')).toBeVisible();
  await expect(page.getByText(/Evidencia/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Modificar y aprobar' })).toHaveCount(0);
  await capturarClaroOscuro(page, 'bandeja-propuestas');

  await page.getByRole('button', { name: 'Aprobar' }).click();
  await expect(page.getByText('La propuesta ha caducado y no se ha enviado nada')).toBeVisible();
  await expect(page.getByText('El saldo cambió desde que se propuso.').first()).toBeVisible();
});

const ALERTAS_ABIERTAS = [
  ALERTA('a1', 'seguridad', 'Entrada desde una IP nueva', { repeticiones: 2 }),
  ALERTA('a2', 'factura', 'Factura periódica fallida'),
];

test('bandeja: alertas abiertas con repeticiones, filtro de tipo y cerrar', async ({ page, api }) => {
  conSesion(api, GERENTE);
  api.responder('GET', '/informes/dashboard', { body: DASHBOARD });
  api.responder('GET', '/contratos/solicitudes', { body: pagina([]) });
  api.responder('GET', '/ia/propuestas', { body: pagina([]) });
  api.responder('GET', '/alertas', (p) =>
    p.query.includes('tipo=factura')
      ? { body: pagina([ALERTAS_ABIERTAS[1]!]) }
      : { body: pagina(ALERTAS_ABIERTAS) },
  );
  api.responder('POST', '/alertas/a1/cerrar', { body: { ...ALERTAS_ABIERTAS[0], resueltaAt: '2026-10-28T10:00:00Z' } });

  await page.goto('/bandeja');
  await page.getByRole('tab', { name: 'Alertas' }).click();
  await expect(page.getByText('Una alerta cerrada vuelve a abrirse si el problema persiste.')).toBeVisible();
  await expect(page.getByText('Entrada desde una IP nueva')).toBeVisible();
  await expect(page.getByText('2 veces')).toBeVisible();
  expect(api.peticiones.find((p) => p.ruta === '/alertas')?.query).toContain('estado=abiertas');
  await capturarClaroOscuro(page, 'bandeja-alertas');

  await page.getByLabel('Tipo').selectOption('factura');
  await expect(page.getByText('Entrada desde una IP nueva')).toHaveCount(0);
  await expect(page.getByText('Factura periódica fallida')).toBeVisible();
  await page.getByLabel('Tipo').selectOption('');

  await page.getByRole('row', { name: /IP nueva/ }).getByRole('button', { name: 'Cerrar' }).click();
  await expect.poll(() => api.llamadas('POST', '/alertas/a1/cerrar')).toBe(1);
});

test('bandeja: el empleado solo ve la pestaña de alertas, con lo que la API le deja ver', async ({ page, api }) => {
  conSesion(api, EMPLEADO);
  // La API ya filtra por rol: al empleado solo le llegan alertas de contrato y reserva.
  api.responder('GET', '/alertas', {
    body: pagina([ALERTA('a3', 'contrato', 'Contrato por renovar'), ALERTA('a4', 'reserva', 'Reserva sin convertir')]),
  });
  await page.goto('/bandeja');
  await expect(page.getByRole('heading', { name: 'Bandeja', level: 1 })).toBeVisible();
  await expect(page.getByRole('tab', { name: /Contratos/ })).toHaveCount(0);
  await expect(page.getByRole('tab', { name: 'Alertas' })).toBeVisible();
  await expect(page.getByText('Contrato por renovar')).toBeVisible();
  await expect(page.getByText('Reserva sin convertir')).toBeVisible();
  await expect(page.getByText('Entrada desde una IP nueva')).toHaveCount(0);
  await capturarClaroOscuro(page, 'bandeja-alertas-empleado');
});

test('bandeja: el administrador ve el estado vacío de las alertas', async ({ page, api }) => {
  conSesion(api, identidad());
  api.responder('GET', '/informes/dashboard', { body: DASHBOARD });
  api.responder('GET', '/contratos/solicitudes', { body: pagina([]) });
  api.responder('GET', '/ia/propuestas', { body: pagina([]) });
  api.responder('GET', '/alertas', { body: pagina([]) });
  await page.goto('/bandeja');
  await page.getByRole('tab', { name: 'Alertas' }).click();
  await expect(page.getByText('No hay alertas')).toBeVisible();
});
