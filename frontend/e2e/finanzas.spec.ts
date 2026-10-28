import type { Page } from '@playwright/test';
import { problema } from './apiFalsa';
import { capturarClaroOscuro } from './capturas';
import { COBRO, COBRO_ID, CUENTA_ID, FACTURA, FACTURA_ID, FIANZA_ID, FORMA_ID, SIMULACION, catalogos, cuentasRespuesta } from './finanzas-fixtures';
import { EMPLEADO, conSesion, identidad } from './fixtures';
import { expect, test } from './prueba';
import { ACCIONES } from '../src/permisos';

/** El diálogo del DS no lleva nombre accesible: se localiza por su título. */
const dlg = (page: Page, titulo: string) => page.getByRole('dialog').filter({ has: page.getByRole('heading', { name: titulo }) });

const GERENTE = identidad({
  nombreAcceso: 'gerente', nombre: 'Rosa Díaz', rol: 'gerente',
  permisosEfectivos: Object.values(ACCIONES).filter((a) => a !== ACCIONES.configuracionEditar),
});
const EMPLEADO_FIN = identidad({ ...EMPLEADO, permisosEfectivos: [ACCIONES.clienteVer, ACCIONES.facturaVer, ACCIONES.facturaBorrador, ACCIONES.sesionVer, ACCIONES.sesionCerrar, ACCIONES.sesionPassword] });

test('cuentas: el panel de cobro en otra moneda convierte, simula y registra con Idempotency-Key', async ({ page, api }) => {
  conSesion(api);
  catalogos(api, true);
  api.responder('GET', '/cuentas', { body: cuentasRespuesta(true) });
  api.responder('GET', '/tipos-cambio/actual', { body: { fecha: '2026-10-28', moneda: 'DOP', valor: '59.500000', id: null, fuente: 'Manual' } });
  api.responder('POST', '/cobros/simular', { body: SIMULACION });
  api.responder('POST', '/cobros', { body: COBRO });
  const claves: string[] = [];
  page.on('request', (r) => {
    if (r.method() === 'POST' && r.url().endsWith('/api/v1/cobros')) claves.push(r.headers()['idempotency-key'] ?? '');
  });

  await page.goto('/cuentas');
  await expect(page.getByRole('heading', { name: 'Cuentas y cobros', level: 1 })).toBeVisible();
  // Columnas por moneda, sin convertir: la fianza va en DOP y las facturas en USD.
  await expect(page.getByText('59.500,00').first()).toBeVisible();
  // El administrador ve los totales por moneda que manda la API.
  await expect(page.getByRole('table', { name: 'Totales del filtro' })).toContainText('1.600,00');
  await capturarClaroOscuro(page, 'finanzas-cuentas', { paginaCompleta: true });

  await page.getByRole('checkbox', { name: 'Seleccionar 1-00412' }).check();
  await page.getByRole('button', { name: 'Registrar cobro' }).click();
  const panel = dlg(page, 'Registrar cobro');
  await expect(panel).toBeVisible();

  await panel.getByLabel('Moneda recibida').click();
  await page.getByRole('option', { name: 'DOP' }).click();
  await expect(panel.getByLabel('Tipo de cambio aplicado')).toHaveValue('59.50');
  await panel.getByLabel('Importe recibido').fill('59500');
  await expect(panel.getByText('59.500,00 DOP ÷ 59,50 = 1.000,00 USD')).toBeVisible();

  await panel.getByLabel('Forma de pago').click();
  await page.getByRole('option', { name: 'Transferencia' }).click();
  await panel.getByLabel('Cuenta receptora').click();
  await page.getByRole('option', { name: 'Cuenta DOP · DOP' }).click();

  // Sin simular no se puede registrar.
  await expect(panel.getByRole('button', { name: 'Registrar cobro' })).toBeDisabled();
  await panel.getByRole('button', { name: 'Simular reparto' }).click();
  await expect(panel.getByText('Recibido 59.500,00 DOP a 59,50')).toBeVisible();
  expect(api.ultimoCuerpo('POST', '/cobros/simular')).toMatchObject({
    clienteId: expect.any(String), importeRecibido: '59500.00', monedaRecibida: 'DOP', tipoCambioAplicado: '59.500000', facturasSeleccionadas: [FACTURA_ID],
  });
  await capturarClaroOscuro(page, 'finanzas-panel-cobro');

  // Cambiar el importe después de simular obliga a simular otra vez.
  await panel.getByLabel('Importe recibido').fill('60000');
  await expect(panel.getByRole('button', { name: 'Registrar cobro' })).toBeDisabled();
  await panel.getByLabel('Importe recibido').fill('59500');
  await panel.getByRole('button', { name: 'Simular reparto' }).click();
  await expect(panel.getByRole('button', { name: 'Registrar cobro' })).toBeEnabled();

  await panel.getByRole('button', { name: 'Registrar cobro' }).click();
  await expect(page.getByText('Cobro registrado')).toBeVisible();
  expect(api.ultimoCuerpo('POST', '/cobros')).toMatchObject({
    importeRecibido: '59500.00', monedaRecibida: 'DOP', tipoCambioAplicado: '59.500000', formaPagoId: FORMA_ID, cuentaReceptoraId: CUENTA_ID,
    asignaciones: [{ facturaId: FACTURA_ID, importe: '1000.00' }],
  });
  expect(claves).toHaveLength(1);
  expect(claves[0]).toMatch(/^[0-9a-f-]{36}$/);
});

test('cuentas: el gerente no puede leer los catálogos de configuración y la pantalla lo dice', async ({ page, api }) => {
  conSesion(api, GERENTE);
  catalogos(api, false);
  api.responder('GET', '/cuentas', { body: cuentasRespuesta(true) });
  api.responder('GET', '/config/formas-pago', problema(403, 'AUT-003', 'Acceso denegado', 'No tienes permiso.'));
  api.responder('GET', '/config/cuentas', problema(403, 'AUT-003', 'Acceso denegado', 'No tienes permiso.'));

  await page.goto('/cuentas');
  await page.getByRole('checkbox', { name: 'Seleccionar 1-00412' }).check();
  await page.getByRole('button', { name: 'Registrar cobro' }).click();
  await expect(page.getByText('No se pueden cargar los catálogos de configuración')).toBeVisible();
});

test('cuentas: el empleado ve RestrictedValue en los totales y no puede cobrar', async ({ page, api }) => {
  conSesion(api, EMPLEADO_FIN);
  catalogos(api, false);
  api.responder('GET', '/cuentas', { body: cuentasRespuesta(false) });

  await page.goto('/cuentas');
  await expect(page.getByText('Solo gerente y administrador')).toBeVisible();
  await expect(page.getByRole('table', { name: 'Totales del filtro' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Registrar cobro' })).toHaveCount(0);
  await expect(page.getByRole('checkbox', { name: /^Seleccionar/ })).toHaveCount(0);
  await capturarClaroOscuro(page, 'finanzas-cuentas-empleado');
});

test('factura: ficha, anular con efecto sobre los cobros y emitir con comprobante agotado', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', `/facturas/${FACTURA_ID}`, { body: FACTURA });
  api.responder('POST', `/facturas/${FACTURA_ID}/simular-anulacion`, {
    body: {
      facturaId: FACTURA_ID, numero: 412, estado: 'Emitida', netoACobrar: '1000.00', comprobanteFiscalSeAnula: true, numeroSeConserva: true,
      cobrosQueSeAnulan: [{ cobroId: COBRO_ID, fechaEfectiva: '2026-10-10', moneda: 'USD', importeRecibido: '400.00', importeDeEstaFactura: '400.00', importeQueSigueVigente: '0.00', cambioDelSaldoAFavor: '0.00', seAnulaEntero: true }],
      cobrosQueSeToCan: [],
    },
  });
  api.responder('POST', `/facturas/${FACTURA_ID}/anular`, { body: { ...FACTURA, estado: 'Anulada' } });

  await page.goto(`/facturas/${FACTURA_ID}`);
  await expect(page.getByRole('heading', { name: '1-00412', level: 1 })).toBeVisible();
  await expect(page.getByText('Renta octubre').first()).toBeVisible();
  await capturarClaroOscuro(page, 'finanzas-factura', { paginaCompleta: true });

  await page.getByRole('button', { name: 'Más acciones' }).click();
  await page.getByRole('menuitem', { name: 'Anular' }).click();
  const dialogo = dlg(page, 'Anular factura');
  await expect(dialogo.getByText('su parte de los cobros queda como devuelta')).toBeVisible();
  await expect(dialogo.getByText('400,00 USD').first()).toBeVisible();
  await expect(dialogo.getByRole('button', { name: 'Anular factura' })).toBeDisabled();
  await capturarClaroOscuro(page, 'finanzas-anular-factura');
  await dialogo.getByLabel('Motivo').fill('Error de cliente');
  await dialogo.getByRole('button', { name: 'Anular factura' }).click();
  expect(api.ultimoCuerpo('POST', `/facturas/${FACTURA_ID}/anular`)).toEqual({ motivo: 'Error de cliente' });
});

test('factura en borrador: emitir enseña el motivo del 409', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', `/facturas/${FACTURA_ID}`, { body: { ...FACTURA, estado: 'Borrador', numero: null, fechaEmision: null, pendiente: '1000.00' } });
  api.responder('POST', `/facturas/${FACTURA_ID}/emitir`, problema(409, 'VAL-409', 'No se puede guardar el cambio', 'El numerador fiscal de la serie 3 está agotado.'));

  await page.goto(`/facturas/${FACTURA_ID}`);
  await page.getByRole('button', { name: 'Emitir factura' }).click();
  await expect(page.getByText('El numerador fiscal de la serie 3 está agotado.')).toBeVisible();
  await expect(page.getByText('la administración debe cargar un rango nuevo')).toBeVisible();
});

test('facturación periódica: simula, genera y enseña las fallidas con su motivo', async ({ page, api }) => {
  conSesion(api);
  catalogos(api, true);
  const item = {
    contratoId: '12345678-aaaa-bbbb-cccc-000000000001', clienteId: 'c', unidadId: 'u', moneda: 'USD', periodoInicio: '2026-11-01', periodoFin: '2026-11-30',
    diasDelMes: 30, diasFacturables: 30, renta: '1000.00', importe: '1000.00', prorrateado: false, vencimiento: '2026-11-05', serieId: 's', serieNumero: 1,
    serieGeneraComprobante: false, yaExiste: false, facturaExistenteId: null, facturaExistenteNumero: null, motivoOmision: null, facturaId: null, facturaNumero: null, seEmite: true,
  };
  api.responder('POST', '/facturas/periodicas/simular', {
    body: { periodo: '2026-11-01', periodoInicio: '2026-11-01', periodoFin: '2026-11-30', diasDelMes: 30, serieId: 's', serieNumero: 1, serieTipo: 'Normal', serieGeneraComprobante: false, importeTotal: '1000.00', items: [item] },
  });
  api.responder('POST', '/facturas/periodicas/generar', {
    body: {
      periodo: '2026-11-01', periodoInicio: '2026-11-01', periodoFin: '2026-11-30', serieId: 's', serieNumero: 1, serieGeneraComprobante: false, contratosIndicados: true,
      creadas: 0, omitidas: 0, importeEmitido: '0.00', facturas: [], omitidos: [], fallidas: 1, fallidos: [{ contratoId: item.contratoId, motivo: 'La serie no admite la moneda del contrato.' }],
    },
  });

  await page.goto('/facturas/periodicas');
  await page.getByLabel('Serie').click();
  await page.getByRole('option', { name: /Normal/ }).click();
  await page.getByRole('button', { name: 'Simular' }).click();
  await expect(page.getByText('1 se emitirán · 0 se omiten')).toBeVisible();
  await capturarClaroOscuro(page, 'finanzas-periodicas');
  await page.getByRole('button', { name: 'Generar facturas' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Generar facturas' }).click();
  await expect(page.getByText('Contratos que han fallado: 1')).toBeVisible();
  await expect(page.getByText('La serie no admite la moneda del contrato.')).toBeVisible();
  expect(api.ultimoCuerpo('POST', '/facturas/periodicas/generar')).toMatchObject({ contratosIds: [item.contratoId], periodo: expect.stringMatching(/-01$/) });
});

test('cobro: ficha con asignaciones y anulación con 409 que dice qué anular antes', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', `/cobros/${COBRO_ID}`, { body: COBRO });
  api.responder('POST', `/cobros/${COBRO_ID}/anular`, problema(409, 'VAL-409', 'No se puede guardar el cambio', 'El crédito de este cobro ya se gastó en el cobro del 20/10/2026: anúlalo antes.'));

  await page.goto(`/cobros/${COBRO_ID}`);
  await expect(page.getByText('TRF-0001')).toBeVisible();
  await expect(page.getByText('Vigente')).toBeVisible();
  await capturarClaroOscuro(page, 'finanzas-cobro');
  await page.getByRole('button', { name: 'Anular cobro' }).click();
  const dialogo = dlg(page, 'Anular cobro');
  await dialogo.getByLabel('Motivo').fill('Cobro duplicado');
  await dialogo.getByRole('button', { name: 'Anular cobro' }).click();
  await expect(dialogo.getByText('anúlalo antes')).toBeVisible();
});

test('fianza: resumen y devolución parcial exige motivo', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', `/fianzas/${FIANZA_ID}`, {
    body: {
      fianza: { id: FIANZA_ID, contratoId: 'bbbbbbbb-0000-0000-0000-000000000001', importeExigido: '1000.00', moneda: 'USD', numeroFianzas: 1, cobrado: '1000.00', devuelto: '0.00', descontado: '0.00', saldo: '1000.00', estado: 'Retenida', retenidaAt: null, enLiquidacionAt: null, devueltaAt: null, createdAt: '2026-01-01T00:00:00Z' },
      devoluciones: [],
    },
  });
  api.responder('POST', `/fianzas/${FIANZA_ID}/devolver`, { body: {} });

  await page.goto(`/fianzas/${FIANZA_ID}`);
  await expect(page.getByText('Todavía no hay devoluciones.')).toBeVisible();
  await capturarClaroOscuro(page, 'finanzas-fianza');
  await page.getByRole('button', { name: 'Devolver fianza' }).click();
  const dialogo = dlg(page, 'Devolver fianza');
  await dialogo.getByLabel('Importe devuelto').fill('800');
  await expect(dialogo.getByRole('button', { name: 'Registrar devolución' })).toBeDisabled();
  await dialogo.getByLabel('Motivo').fill('Desperfectos en la cocina');
  await dialogo.getByLabel('Importe descontado').fill('200');
  await dialogo.getByRole('button', { name: 'Registrar devolución' }).click();
  expect(api.ultimoCuerpo('POST', `/fianzas/${FIANZA_ID}/devolver`)).toEqual({ importe: '800.00', importeDescontado: '200.00', motivo: 'Desperfectos en la cocina' });
});
