import { problema } from './apiFalsa';
import { capturarClaroOscuro } from './capturas';
import { EMPLEADO, conSesion, identidad } from './fixtures';
import { expect, test } from './prueba';

const lista = (data: unknown[]) => ({ body: { data, total: data.length, page: 1, pageSize: 200, totalPages: 1 } });
const SERIE_A = '77777777-7777-7777-7777-777777777771';
const SERIE_B = '77777777-7777-7777-7777-777777777772';

// Formas exactas de `SerieRespuesta` (ConfiguracionDtos.cs).
const SERIES = [
  { id: SERIE_A, numero: 1, prefijo: 'A', moneda: 'USD', tipo: 'Normal', plantillaPdf: null, condicionesPago: null, sumaAFacturado: true, sumaASaldos: true, generaComprobante: true, siguiente: 42, activa: true },
  { id: SERIE_B, numero: 2, prefijo: 'B', moneda: null, tipo: 'Fiscal', plantillaPdf: null, condicionesPago: 'Pago a 30 días', sumaAFacturado: true, sumaASaldos: true, generaComprobante: true, siguiente: 1, activa: true },
];

test('series: con números emitidos moneda, prefijo y tipo quedan bloqueados y el 409 de la API se enseña', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', '/config/series', lista(SERIES));
  api.responder('PUT', `/config/series/${SERIE_A}`, problema(409, 'VAL-002', 'No se puede guardar el cambio', 'La serie 1 ya tiene facturas emitidas y no admite cambiar «genera comprobante» (de sí a no). Crea una serie nueva.'));

  await page.goto('/configuracion');
  await expect(page.getByRole('heading', { name: 'Configuración', level: 1 })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Libre', exact: false }).or(page.getByText('Moneda libre')).first()).toBeVisible();
  await capturarClaroOscuro(page, 'configuracion-series');

  await page.getByRole('row', { name: /USD/ }).click();
  await expect(page.getByText('Esta serie ya tiene números emitidos: no se puede cambiar su moneda, su prefijo ni su tipo.')).toBeVisible();
  await expect(page.getByLabel('Prefijo')).toBeDisabled();
  await page.getByRole('switch', { name: 'Genera comprobante fiscal' }).click();
  await page.getByRole('button', { name: 'Guardar' }).click();
  await expect(page.getByText('La serie 1 ya tiene facturas emitidas', { exact: false })).toBeVisible();
  const cuerpo = api.ultimoCuerpo('PUT', `/config/series/${SERIE_A}`) as Record<string, unknown>;
  expect(cuerpo).toMatchObject({ numero: 1, prefijo: 'A', moneda: 'USD', tipo: 'Normal', generaComprobante: false, plantillaPdf: null });
  await capturarClaroOscuro(page, 'configuracion-series-409');
});

test('series: alta de una serie nueva con moneda libre', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', '/config/series', lista(SERIES));
  api.responder('POST', '/config/series', { body: { ...SERIES[0], id: 'nueva', numero: 3, prefijo: 'C', moneda: null } });

  await page.goto('/configuracion');
  await page.getByRole('button', { name: 'Nueva serie' }).click();
  await page.getByLabel('Número de serie').fill('3');
  await page.getByLabel('Prefijo').fill('C');
  await page.getByRole('button', { name: 'Guardar' }).click();
  await expect.poll(() => api.llamadas('POST', '/config/series')).toBe(1);
  expect(api.ultimoCuerpo('POST', '/config/series')).toMatchObject({ numero: 3, prefijo: 'C', moneda: null, tipo: 'Normal', activa: true });
});

test('comprobantes: alertas de agotado, vencido y sin configurar, y guardado del numerador', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', '/config/series', lista(SERIES));
  // `NumeradorFiscalRespuesta`: la serie 1 está agotada; la 2 no tiene numerador.
  api.responder('GET', '/config/numeradores-fiscales', lista([
    { id: 'n1', serieId: SERIE_A, serieNumero: 1, tipoPrefijo: 'B01', rangoDesde: 1, rangoHasta: 500, fechaVencimiento: '2099-12-31', siguienteNumero: 501, umbralAlerta: 20, restantes: 0 },
  ]));
  api.responder('PUT', `/config/numeradores-fiscales/${SERIE_B}`, { body: { id: 'n2' } });

  await page.goto('/configuracion?seccion=fiscal');
  await expect(page.getByText('La serie 1 no tiene comprobantes disponibles')).toBeVisible();
  await expect(page.getByText('La serie 2 genera comprobante y no tiene numerador')).toBeVisible();
  await expect(page.getByText('Agotado')).toBeVisible();
  await capturarClaroOscuro(page, 'configuracion-fiscal');

  await page.getByRole('row', { name: /Sin configurar/ }).click();
  await page.getByLabel('Tipo o prefijo del comprobante').fill('B02');
  await page.getByLabel('Primer número autorizado').fill('1');
  await page.getByLabel('Último número autorizado').fill('100');
  await page.getByLabel('Vencimiento del rango').fill('2027-06-30');
  await page.getByLabel('Siguiente número a emitir').fill('150');
  await page.getByRole('button', { name: 'Guardar' }).click();
  await expect(page.getByText('El siguiente número debe estar entre el primero y el último más uno.')).toBeVisible();
  expect(api.llamadas('PUT', `/config/numeradores-fiscales/${SERIE_B}`)).toBe(0);

  await page.getByLabel('Siguiente número a emitir').fill('1');
  await page.getByRole('button', { name: 'Guardar' }).click();
  await expect.poll(() => api.ultimoCuerpo('PUT', `/config/numeradores-fiscales/${SERIE_B}`)).toEqual({
    tipoPrefijo: 'B02',
    rangoDesde: 1,
    rangoHasta: 100,
    fechaVencimiento: '2027-06-30',
    siguienteNumero: 1,
    umbralAlerta: 0,
  });
});

test('impuestos: el porcentaje viaja como string y las retenciones muestran su base', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', '/config/tipos-itbis', lista([{ id: 'i1', nombre: 'ITBIS general', porcentaje: '18.00', activo: true }]));
  api.responder('GET', '/config/tipos-retencion', lista([{ id: 'r1', nombre: 'Retención ITBIS', porcentaje: '30.00', base: 'Itbis', esDefecto: true, activo: true }]));
  api.responder('POST', '/config/tipos-itbis', { body: { id: 'i2' } });

  await page.goto('/configuracion?seccion=impuestos');
  await expect(page.getByText('18,00 %')).toBeVisible();
  await expect(page.getByText('30,00 %')).toBeVisible();
  await capturarClaroOscuro(page, 'configuracion-impuestos');

  await page.getByRole('button', { name: 'Nuevo tipo de ITBIS' }).click();
  await page.getByLabel('Nombre').fill('ITBIS reducido');
  await page.getByLabel('Porcentaje').fill('16,5');
  await page.getByRole('button', { name: 'Guardar' }).click();
  await expect.poll(() => api.ultimoCuerpo('POST', '/config/tipos-itbis')).toEqual({ nombre: 'ITBIS reducido', porcentaje: '16.5', activo: true });
});

test('tipo de cambio: histórico con 6 decimales y carga manual como string', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', '/tipos-cambio/actual', problema(409, 'VAL-003', 'Conflicto', 'No hay tipo de cambio de DOP para el 28/10/2026.'));
  api.responder('GET', '/tipos-cambio', {
    body: {
      data: [
        { id: 't1', fecha: '2026-10-27', moneda: 'DOP', valor: '62.350000', fuente: 'exchange_rate_api', registradoAt: '2026-10-27T10:00:00Z', registradoPor: null, actualizadoAt: null },
        { id: 't2', fecha: '2026-10-26', moneda: 'DOP', valor: '62.123456', fuente: 'manual', registradoAt: '2026-10-26T15:00:00Z', registradoPor: '11111111-1111-1111-1111-111111111111', actualizadoAt: null },
      ],
      total: 2,
      page: 1,
      pageSize: 15,
      totalPages: 1,
    },
  });
  api.responder('POST', '/tipos-cambio', { body: { id: 't3' } });

  await page.goto('/configuracion?seccion=cambio');
  await expect(page.getByText('62,350000')).toBeVisible();
  await expect(page.getByText('62,123456')).toBeVisible();
  await expect(page.getByText('No hay tipo de cambio de hoy. Cárgalo a mano.')).toBeVisible();
  await capturarClaroOscuro(page, 'configuracion-cambio');

  await page.getByRole('button', { name: 'Cargar a mano' }).click();
  await page.getByLabel('Valor por 1 USD').fill('0');
  await page.getByRole('button', { name: 'Guardar' }).click();
  await expect(page.getByText('Escribe un valor mayor que cero con hasta 6 decimales.')).toBeVisible();
  await page.getByLabel('Valor por 1 USD').fill('62,5');
  await page.getByRole('button', { name: 'Guardar' }).click();
  await expect.poll(() => api.ultimoCuerpo('POST', '/tipos-cambio')).toEqual({ moneda: 'DOP', valor: '62.5' });
});

test('ajustes: tolerancias, modo de recordatorios y el interruptor de MFA de gerentes', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', '/config/ajustes', {
    body: { id: 'a1', toleranciaCambioUsd: '1.50', toleranciaCambioDop: null, modoRecordatorios: 'Manual', diasPrimerRecordatorio: 3, diasEntreRecordatorios: 7, mfaGerente: false },
  });
  api.responder('PUT', '/config/ajustes', { body: { id: 'a1' } });

  await page.goto('/configuracion?seccion=ajustes');
  await expect(page.getByLabel('Tolerancia en USD')).toHaveValue('1.50');
  await capturarClaroOscuro(page, 'configuracion-ajustes');
  await page.getByRole('switch', { name: 'Exigir el segundo factor también a los gerentes' }).click();
  await page.getByLabel('Tolerancia en DOP').fill('50,00');
  await page.getByRole('button', { name: 'Guardar' }).click();
  await expect(page.getByText('Ajustes guardados')).toBeVisible();
  expect(api.ultimoCuerpo('PUT', '/config/ajustes')).toEqual({
    toleranciaCambioUsd: '1.50',
    toleranciaCambioDop: '50.00',
    modoRecordatorios: 'Manual',
    diasPrimerRecordatorio: 3,
    diasEntreRecordatorios: 7,
    mfaGerente: true,
  });
});

test('mensajes: guardar es de plantilla.editar; sin ella se ve en solo lectura', async ({ page, api }) => {
  const plantillas = lista([
    { id: 'p1', tipo: 'Recordatorio', idioma: 'es', asunto: 'Factura vencida', cuerpo: 'Hola {cliente}', activa: true, createdAt: '2026-10-01T10:00:00Z', updatedAt: null },
  ]);
  conSesion(api);
  api.responder('GET', '/config/plantillas-mensaje', plantillas);
  api.responder('PUT', '/config/plantillas-mensaje', { body: { id: 'p1' } });

  await page.goto('/configuracion?seccion=mensajes');
  await expect(page.getByText('Variables: {cliente}', { exact: false })).toBeVisible();
  await page.getByRole('row', { name: /Factura vencida/ }).click();
  await expect(page.getByLabel('Idioma')).toBeDisabled();
  await page.getByLabel('Asunto').fill('Aviso de factura vencida');
  await page.getByRole('button', { name: 'Guardar' }).click();
  await expect.poll(() => api.ultimoCuerpo('PUT', '/config/plantillas-mensaje')).toEqual({
    tipo: 'Recordatorio',
    idioma: 'es',
    asunto: 'Aviso de factura vencida',
    cuerpo: 'Hola {cliente}',
    activa: true,
  });

  // Mismo admin pero con el permiso recortado: ve las plantillas y no puede guardarlas.
  const sinEditar = identidad({ permisosEfectivos: identidad().permisosEfectivos.filter((a: string) => a !== 'plantilla.editar') });
  conSesion(api, sinEditar);
  await page.goto('/configuracion?seccion=mensajes');
  await expect(page.getByText('Tu usuario puede ver las plantillas pero no editarlas.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Nueva plantilla' })).toHaveCount(0);
  await page.getByRole('row', { name: /Factura vencida/ }).click();
  await expect(page.getByRole('button', { name: 'Guardar' })).toHaveCount(0);
});

const TOKEN = {
  id: 'k1',
  nombre: 'Asistente de cobros',
  usuarioId: '11111111-1111-1111-1111-111111111111',
  acciones: ['ia.consultar', 'factura.ver'],
  creado: '2026-10-20T10:00:00Z',
  caduca: '2027-01-18T10:00:00Z',
  revocado: null,
  ultimoUso: null,
  limiteConsultasDia: 200,
};

test('agentes: emitir muestra el token una sola vez y revocar lo marca', async ({ page, api }) => {
  conSesion(api);
  let revocado = false;
  api.responder('GET', '/usuarios', lista([{ id: TOKEN.usuarioId, nombreAcceso: 'admin', nombre: 'Marta Peña', email: null, activo: true, rol: 'admin' }]));
  api.responder('GET', '/agente/tokens', () => ({ body: { data: [{ ...TOKEN, revocado: revocado ? '2026-10-28T12:00:00Z' : null }] } }));
  api.responder('POST', '/agente/tokens', { body: { ...TOKEN, id: 'k2', token: 'arg_EJEMPLO_secreto_de_prueba' } });
  api.responder('POST', '/agente/tokens/k1/revocar', () => {
    revocado = true;
    return { status: 204 };
  });

  await page.goto('/configuracion?seccion=agentes');
  await expect(page.getByText('Asistente de cobros')).toBeVisible();
  await capturarClaroOscuro(page, 'configuracion-agentes');

  await page.getByRole('button', { name: 'Emitir token' }).click();
  // Solo la lista blanca: ni cobros ni administración.
  await expect(page.getByRole('checkbox')).toHaveCount(5);
  await page.getByLabel('Nombre').fill('Agente de informes');
  await page.getByRole('button', { name: 'Propietario' }).click();
  await page.getByRole('option', { name: /Marta Peña/ }).click();
  await page.getByRole('checkbox', { name: /Ver informes/ }).check();
  await page.getByLabel('Validez (días)').fill('30');
  await page.getByRole('button', { name: 'Emitir', exact: true }).click();
  await expect(page.getByText('arg_EJEMPLO_secreto_de_prueba')).toBeVisible();
  await expect(page.getByText('No se puede volver a mostrar')).toBeVisible();
  expect(api.ultimoCuerpo('POST', '/agente/tokens')).toEqual({
    nombre: 'Agente de informes',
    usuarioId: TOKEN.usuarioId,
    acciones: ['ia.consultar', 'informe.ver'],
    diasValidez: 30,
  });
  await capturarClaroOscuro(page, 'configuracion-agentes-secreto');
  await page.getByRole('button', { name: 'Ya lo he copiado' }).click();
  await expect(page.getByText('arg_EJEMPLO_secreto_de_prueba')).toHaveCount(0);

  await page.getByRole('button', { name: 'Revocar', exact: true }).click();
  await page.getByRole('button', { name: 'Revocar token' }).last().click();
  await expect(page.getByText('Revocado')).toBeVisible();
});

test('configuración: gerente y empleado no ven la entrada y por URL llegan a sin permiso; sin agente.token no hay pestaña de agentes', async ({ page, api }) => {
  conSesion(api, EMPLEADO);
  await page.goto('/configuracion');
  await expect(page.getByRole('heading', { name: 'Sin permiso', level: 1 })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Configuración' })).toHaveCount(0);

  conSesion(api, identidad({ permisosEfectivos: identidad().permisosEfectivos.filter((a: string) => a !== 'agente.token') }));
  api.responder('GET', '/config/series', lista([]));
  await page.goto('/configuracion');
  await expect(page.getByRole('tab', { name: 'Series' })).toBeVisible();
  await expect(page.getByRole('tab', { name: 'Agentes de IA' })).toHaveCount(0);
});
