import { problema } from './apiFalsa';
import { capturarClaroOscuro } from './capturas';
import { conSesion, identidad } from './fixtures';
import {
  CLIENTE_ID,
  CONTRATO_ID,
  FACTURADORA_ID,
  COTIZACION_ID,
  EMPLEADO_OPERACION,
  INMUEBLE_ID,
  RESERVA_ID,
  UNIDAD_ID,
  anexo,
  calendarioDe,
  contratoDetalle,
  contratoResumen,
  cotizacionDetalle,
  cotizacionResumen,
  incrementos,
  inventario,
  pagina,
  plantilla,
  reservaDetalle,
  reservaResumen,
  responderSelectores,
} from './operacionFixtures';
import { expect, test } from './prueba';

// ---------------------------------------------------------------- calendario

test('calendario: 150 unidades se pintan virtualizadas, una celda libre abre el detalle y ofrece crear la reserva', async ({ page, api }) => {
  conSesion(api);
  responderSelectores(api);
  api.responder('GET', '/calendario', (p) => {
    const desde = new URLSearchParams(p.query).get('desde')!;
    return { body: calendarioDe(desde.slice(0, 7), 150) };
  });

  await page.goto('/calendario');
  await expect(page.getByRole('heading', { name: 'Calendario de disponibilidad', level: 1 })).toBeVisible();
  await expect(page.getByText(/^150 unidades/)).toBeVisible();

  // Virtualización: solo se montan las filas visibles, no las 150.
  const montadas = await page.locator('.ar-cal__fila').count();
  expect(montadas).toBeGreaterThan(5);
  expect(montadas).toBeLessThan(40);

  // La petición lleva inmueble, desde y hasta (obligatorios) y el rango es el mes completo.
  const q = new URLSearchParams(api.peticiones.find((x) => x.ruta === '/calendario')!.query);
  expect(q.get('inmuebleId')).toBe(INMUEBLE_ID);
  expect(q.get('desde')).toMatch(/-01$/);
  expect(q.get('hasta')).toMatch(/-(28|29|30|31)$/);

  await capturarClaroOscuro(page, 'calendario');

  // U-001 empieza libre (u=0, d=0 -> Libre).
  await page.getByRole('button', { name: /^U-001, \d\d\/\d\d\/\d{4}: Libre$/ }).first().click();
  await expect(page.getByRole('button', { name: 'Crear reserva para este día' })).toBeVisible();
  await capturarClaroOscuro(page, 'calendario-detalle');
  await page.getByRole('button', { name: 'Crear reserva para este día' }).click();
  await expect(page).toHaveURL(/\/reservas\?nueva=1&unidadId=bbbbbbbb-0000-0000-0000-000000000001&fecha=\d{4}-\d\d-\d\d$/);
});

test('calendario: navegar de mes pide el rango del mes siguiente y una celda de contrato enlaza a su ficha', async ({ page, api }) => {
  conSesion(api);
  responderSelectores(api);
  api.responder('GET', '/calendario', (p) => ({ body: calendarioDe(new URLSearchParams(p.query).get('desde')!.slice(0, 7), 3) }));
  await page.goto('/calendario');
  await expect(page.getByText(/^3 unidades/)).toBeVisible();
  const antes = api.peticiones.filter((x) => x.ruta === '/calendario').length;
  await page.getByRole('button', { name: 'Mes siguiente' }).click();
  await expect.poll(() => api.peticiones.filter((x) => x.ruta === '/calendario').length).toBeGreaterThan(antes);

  // U-002 (u=1): los primeros 6 días son Contrato.
  await page.getByRole('button', { name: /^U-002, 01\/\d\d\/\d{4}: Contrato$/ }).click();
  await page.getByRole('button', { name: 'Ver contrato' }).click();
  await expect(page).toHaveURL(/\/contratos\/eeeeeeee-/);
});

test('calendario: un rango inválido (400) se enseña con el mensaje del servidor', async ({ page, api }) => {
  conSesion(api);
  responderSelectores(api);
  api.responder('GET', '/calendario', problema(400, 'API-001', 'Datos no válidos', 'El rango pedido son 400 días y el máximo son 366.'));
  await page.goto('/calendario');
  await expect(page.getByText('El rango pedido son 400 días y el máximo son 366.')).toBeVisible();
});

// ---------------------------------------------------------------- cotizaciones

test('cotizaciones: listado, alta con líneas y vista previa del total half-up, y cuerpo con importes como texto', async ({ page, api }) => {
  conSesion(api);
  responderSelectores(api);
  api.responder('GET', '/cotizaciones', { body: pagina([cotizacionResumen(), cotizacionResumen({ id: 'otra', estado: 'Aceptada', clienteNombre: 'Pedro Gil' })]) });
  api.responder('POST', '/cotizaciones', { status: 200, body: cotizacionDetalle() });
  api.responder('GET', `/cotizaciones/${COTIZACION_ID}`, { body: cotizacionDetalle() });

  await page.goto('/cotizaciones');
  await expect(page.getByRole('heading', { name: 'Cotizaciones', level: 1 })).toBeVisible();
  await expect(page.getByText('Ana Martínez')).toBeVisible();
  await expect(page.getByText(/1\.500,01/).first()).toBeVisible();
  await capturarClaroOscuro(page, 'cotizaciones-lista');

  await page.getByRole('button', { name: 'Nueva cotización' }).click();
  await page.getByLabel('Cliente *').click();
  await page.getByRole('option', { name: /Ana Martínez/ }).click();
  await page.getByLabel('Descripción 1').fill('Renta mensual');
  await page.getByLabel('Precio unitario 1').fill('0,005');
  await page.getByRole('button', { name: 'Añadir línea' }).click();
  await page.getByLabel('Descripción 2').fill('Otro');
  await page.getByLabel('Cantidad 2').fill('3');
  await page.getByLabel('Precio unitario 2').fill('0.335');
  // 1 x 0,005 -> 0,01 ; 3 x 0,335 = 1,005 -> 1,01 ; total 1,02 (half-up por línea).
  await expect(page.getByText(/^1,02/)).toBeVisible();
  await capturarClaroOscuro(page, 'cotizaciones-form');

  await page.getByRole('button', { name: 'Guardar borrador' }).click();
  await expect(page).toHaveURL(new RegExp(`/cotizaciones/${COTIZACION_ID}$`));
  expect(api.ultimoCuerpo('POST', '/cotizaciones')).toEqual({
    clienteId: CLIENTE_ID,
    prospectoId: null,
    unidadId: null,
    moneda: 'USD',
    lineas: [
      { descripcion: 'Renta mensual', cantidad: '1', precioUnitario: '0.005' },
      { descripcion: 'Otro', cantidad: '3', precioUnitario: '0.335' },
    ],
  });
});

test('cotizaciones: el borrador pasa a enviada con cotizacion.enviar; la aceptada ofrece crear reserva o contrato', async ({ page, api }) => {
  conSesion(api);
  let estado = 'Borrador';
  api.responder('GET', `/cotizaciones/${COTIZACION_ID}`, () => ({ body: cotizacionDetalle({ estado }) }));
  api.responder('POST', `/cotizaciones/${COTIZACION_ID}/estado`, (p) => {
    estado = (p.cuerpo as { estado: string }).estado;
    return { body: cotizacionResumen({ estado }) };
  });
  api.responder('GET', '/cotizaciones', { body: pagina([]) });

  await page.goto(`/cotizaciones/${COTIZACION_ID}`);
  await expect(page.getByText('Una cotización enviada ya no se edita')).toHaveCount(0);
  await capturarClaroOscuro(page, 'cotizacion-detalle');
  await page.getByRole('button', { name: 'Marcar como enviada' }).click();
  await expect(page.getByText('Una cotización enviada ya no se edita')).toBeVisible();
  expect(api.ultimoCuerpo('POST', `/cotizaciones/${COTIZACION_ID}/estado`)).toEqual({ estado: 'Enviada' });
  await expect(page.getByRole('button', { name: 'Editar' })).toHaveCount(0);

  await page.getByRole('button', { name: 'Marcar como aceptada' }).click();
  await expect(page.getByRole('button', { name: 'Crear reserva' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Crear contrato' })).toBeVisible();
});

test('cotizaciones: un conflicto 409 al cambiar de estado se enseña con el detalle del servidor', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', `/cotizaciones/${COTIZACION_ID}`, { body: cotizacionDetalle() });
  api.responder('POST', `/cotizaciones/${COTIZACION_ID}/estado`, problema(409, 'API-003', 'No se puede guardar el cambio', 'Una cotización en estado Aceptada no puede pasar a Enviada.'));
  await page.goto(`/cotizaciones/${COTIZACION_ID}`);
  await page.getByRole('button', { name: 'Marcar como enviada' }).click();
  await expect(page.getByText('Una cotización en estado Aceptada no puede pasar a Enviada.')).toBeVisible();
});

// ---------------------------------------------------------------- reservas

test('reservas: corta con hora orientativa libre y anticipo como nota; el cuerpo no lleva ningún cobro', async ({ page, api }) => {
  conSesion(api);
  responderSelectores(api);
  api.responder('GET', '/reservas', { body: pagina([reservaResumen(), reservaResumen({ id: 'r2', tipo: 'Mensual', modalidad: 'Mensual', estado: 'EnConflicto', salidaPrevista: null, dias: 0 })]) });
  api.responder('POST', '/reservas', { status: 201, body: reservaDetalle() });
  api.responder('GET', `/reservas/${RESERVA_ID}`, { body: reservaDetalle() });

  await page.goto('/reservas');
  await expect(page.getByRole('heading', { name: 'Reservas', level: 1 })).toBeVisible();
  await expect(page.getByText('En conflicto')).toBeVisible();
  await capturarClaroOscuro(page, 'reservas-lista');

  await page.getByRole('button', { name: 'Nueva reserva' }).click();
  await page.getByRole('button', { name: 'Corta' }).click();
  await page.getByLabel('Unidad *').click();
  await page.getByRole('option', { name: /A-101/ }).click();
  await page.getByLabel('Cliente *').click();
  await page.getByRole('option', { name: /Ana Martínez/ }).click();
  await page.getByRole('button', { name: 'Medio día' }).click();
  await page.locator('#dia-0-fecha').fill('03/11/2026');
  await page.getByLabel('Hora orientativa (opcional) 1').fill('de 9 a 14 aprox.');
  await page.getByLabel('Importe de la reserva').fill('120');
  await page.getByLabel('Anticipo anotado').fill('30');
  await page.getByLabel('Nota del anticipo').fill('Se devuelve si cancela con 48 h');
  await expect(page.getByText('El anticipo es una nota: no crea ningún cobro')).toBeVisible();
  await page.getByText('Tipo de reserva').click(); // cierra el calendario desplegable antes de capturar
  await capturarClaroOscuro(page, 'reservas-form');

  await page.getByRole('button', { name: 'Guardar reserva' }).click();
  await expect(page).toHaveURL(new RegExp(`/reservas/${RESERVA_ID}$`));
  const cuerpo = api.ultimoCuerpo('POST', '/reservas') as Record<string, unknown>;
  expect(cuerpo).toMatchObject({
    unidadId: UNIDAD_ID,
    clienteId: CLIENTE_ID,
    prospectoId: null,
    tipo: 'Corta',
    modalidad: 'MedioDia',
    entradaPrevista: '2026-11-03',
    salidaPrevista: '2026-11-03',
    moneda: 'USD',
    importe: '120',
    anticipoImporte: '30',
    anticipoNota: 'Se devuelve si cancela con 48 h',
    dias: [{ fecha: '2026-11-03', horaOrientativa: 'de 9 a 14 aprox.' }],
  });
  expect(Object.keys(cuerpo).filter((k) => /cobro|factura/i.test(k))).toEqual([]);
});

test('reservas: una unidad ocupada (409 DIS-001) se enseña como bloqueo claro y el formulario sigue abierto', async ({ page, api }) => {
  conSesion(api);
  responderSelectores(api);
  api.responder('GET', '/reservas', { body: pagina([]) });
  api.responder('POST', '/reservas', problema(409, 'DIS-001', 'Unidad no disponible', 'La unidad no está disponible para esas fechas: la tienen un contrato, otra reserva o ya está completo el día (2 medios días o 1 día completo).'));

  await page.goto(`/reservas?nueva=1&unidadId=${UNIDAD_ID}&fecha=2026-11-03`);
  await page.getByLabel('Cliente *').click();
  await page.getByRole('option', { name: /Ana Martínez/ }).click();
  await page.getByLabel('Día de entrada *').fill('03/11/2026');
  await page.getByRole('button', { name: 'Guardar reserva' }).click();
  await expect(page.getByText('Unidad no disponible', { exact: true })).toBeVisible();
  await expect(page.getByText(/la tienen un contrato, otra reserva/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Guardar reserva' })).toBeVisible();
});

test('reservas: con factura vigente, cancelar da 409 explicado; en conflicto, solo quien tiene contrato.estado puede cancelar', async ({ page, api }) => {
  conSesion(api);
  responderSelectores(api);
  api.responder('GET', `/reservas/${RESERVA_ID}`, { body: reservaDetalle() });
  api.responder('DELETE', `/reservas/${RESERVA_ID}`, problema(409, 'API-003', 'No se puede guardar el cambio', 'La reserva tiene una factura vigente: anule la factura antes de cancelarla.'));
  await page.goto(`/reservas/${RESERVA_ID}`);
  await expect(page.getByText('Se devuelve si cancela con 48 h')).toBeVisible();
  await expect(page.getByText('de 9 a 14 aprox.')).toBeVisible();
  await capturarClaroOscuro(page, 'reserva-detalle');
  await page.getByRole('button', { name: 'Cancelar reserva' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Cancelar reserva' }).click();
  await expect(page.getByText('La reserva tiene una factura vigente', { exact: false }).first()).toBeVisible();
  await expect(page.getByText('hay que anular antes la factura', { exact: false })).toBeVisible();
});

test('reservas: el empleado no ve cancelar una reserva en conflicto ni facturar', async ({ page, api }) => {
  conSesion(api, EMPLEADO_OPERACION);
  api.responder('GET', `/reservas/${RESERVA_ID}`, { body: reservaDetalle({ estado: 'EnConflicto' }) });
  await page.goto(`/reservas/${RESERVA_ID}`);
  await expect(page.getByText('Reserva en conflicto')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Cancelar reserva' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Facturar reserva' })).toHaveCount(0);
});

test('reservas: facturar una corta con serie (admin) emite y avisa de que el anticipo es una nota', async ({ page, api }) => {
  conSesion(api);
  responderSelectores(api);
  api.responder('GET', `/reservas/${RESERVA_ID}`, { body: reservaDetalle() });
  api.responder('GET', '/config/series', { body: pagina([{ id: 'se1', numero: 1, prefijo: 'A', moneda: 'USD', activa: true }]) });
  api.responder('POST', `/reservas/${RESERVA_ID}/factura`, {
    body: { facturaId: 'fa1', reservaId: RESERVA_ID, numero: 412, total: 120, moneda: 'USD', vencimiento: '2026-11-03', anticipoAviso: 'La reserva tiene un anticipo anotado de 30,00 USD: es una nota, no un cobro.' },
  });
  await page.goto(`/reservas/${RESERVA_ID}`);
  await page.getByRole('button', { name: 'Facturar reserva' }).click();
  await page.getByRole('dialog').getByLabel('Serie *').click();
  await page.getByRole('option', { name: /Serie 1/ }).click();
  await page.getByRole('button', { name: 'Emitir factura' }).click();
  await expect(page.getByText('es una nota, no un cobro.')).toBeVisible();
  expect(api.ultimoCuerpo('POST', `/reservas/${RESERVA_ID}/factura`)).toEqual({ serieId: 'se1' });
});

// ---------------------------------------------------------------- contratos

test('contratos: el listado entiende el estado numérico y la ficha enseña el Stepper del flujo', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', '/contratos', { body: pagina([contratoResumen(), contratoResumen({ id: 'c2', estado: 5, unidad: 'B-202', fechaBajaProgramada: '2026-12-31' })]) });
  api.responder('GET', `/contratos/${CONTRATO_ID}`, { body: contratoDetalle('Activo') });
  await page.goto('/contratos');
  await expect(page.getByRole('heading', { name: 'Contratos', level: 1 })).toBeVisible();
  await expect(page.getByText('Activo', { exact: true }).first()).toBeVisible();
  await expect(page.getByText('Baja programada', { exact: true }).first()).toBeVisible();
  await expect(page.getByText(/1\.250,50/).first()).toBeVisible();
  await capturarClaroOscuro(page, 'contratos-lista');

  await page.getByRole('row', { name: /A-101/ }).click();
  await expect(page).toHaveURL(new RegExp(`/contratos/${CONTRATO_ID}$`));
  await expect(page.getByText('Solicitud de aprobación', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Registrar cambio' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Dar de baja' })).toBeVisible();
  await capturarClaroOscuro(page, 'contrato-ficha');
});

test('contratos: aprobar y rechazar solo con contrato.estado; el empleado ve el contrato pero no los botones', async ({ page, api }) => {
  conSesion(api, EMPLEADO_OPERACION);
  api.responder('GET', `/contratos/${CONTRATO_ID}`, { body: contratoDetalle('PendienteAprobacion') });
  await page.goto(`/contratos/${CONTRATO_ID}`);
  await expect(page.getByRole('heading', { level: 1 })).toContainText('A-101');
  await expect(page.getByRole('button', { name: 'Aprobar' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Rechazar' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Activar contrato' })).toHaveCount(0);
});

test('contratos: el gerente aprueba y rechaza con comentario obligatorio', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', `/contratos/${CONTRATO_ID}`, { body: contratoDetalle('PendienteAprobacion') });
  api.responder('POST', `/contratos/${CONTRATO_ID}/rechazo`, { body: contratoResumen({ estado: 1 }) });
  api.responder('POST', `/contratos/${CONTRATO_ID}/aprobacion`, { body: contratoResumen({ estado: 3 }) });
  await page.goto(`/contratos/${CONTRATO_ID}`);
  await page.getByRole('button', { name: 'Rechazar' }).click();
  const dialogo = page.getByRole('dialog');
  await expect(dialogo.getByRole('button', { name: 'Rechazar contrato' })).toBeDisabled();
  await dialogo.getByLabel(/Motivo/).fill('Falta la cláusula de mascotas');
  await dialogo.getByRole('button', { name: 'Rechazar contrato' }).click();
  await expect.poll(() => api.llamadas('POST', `/contratos/${CONTRATO_ID}/rechazo`)).toBe(1);
  expect(api.ultimoCuerpo('POST', `/contratos/${CONTRATO_ID}/rechazo`)).toEqual({ comentario: 'Falta la cláusula de mascotas' });

  await page.getByRole('button', { name: 'Aprobar', exact: true }).click();
  await expect.poll(() => api.llamadas('POST', `/contratos/${CONTRATO_ID}/aprobacion`)).toBe(1);
});

test('contratos: la baja se programa en cualquier fecha desde hoy; finalizar no está disponible hasta que llegue la fecha', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', `/contratos/${CONTRATO_ID}`, { body: contratoDetalle('Activo') });
  api.responder('POST', `/contratos/${CONTRATO_ID}/baja`, { body: contratoResumen({ estado: 5 }) });
  await page.goto(`/contratos/${CONTRATO_ID}`);
  await page.getByRole('button', { name: 'Dar de baja' }).click();
  const dialogo = page.getByRole('dialog');
  await expect(dialogo.getByText(/preaviso recomendado es de 30 días/)).toBeVisible();
  await dialogo.getByLabel('Fecha efectiva de fin *').fill('15/01/2020');
  await dialogo.getByRole('button', { name: 'Programar baja' }).click();
  // El selector de fecha rechaza lo anterior a hoy (min): el texto vuelve atrás y la baja no se envía sin fecha.
  await expect(dialogo.getByText('Indica la fecha efectiva de fin.')).toBeVisible();
  expect(api.llamadas('POST', `/contratos/${CONTRATO_ID}/baja`)).toBe(0);
  await dialogo.getByLabel('Fecha efectiva de fin *').fill('31/12/2030');
  await dialogo.getByRole('button', { name: 'Programar baja' }).click();
  await expect.poll(() => api.llamadas('POST', `/contratos/${CONTRATO_ID}/baja`)).toBe(1);
  expect(api.ultimoCuerpo('POST', `/contratos/${CONTRATO_ID}/baja`)).toEqual({ fechaFin: '2030-12-31', comentario: null });
});

test('contratos: finalizar con la baja en el futuro está deshabilitado y lo explica', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', `/contratos/${CONTRATO_ID}`, { body: contratoDetalle('BajaProgramada', { fechaBajaProgramada: '2030-12-31' }) });
  await page.goto(`/contratos/${CONTRATO_ID}`);
  await expect(page.getByRole('button', { name: 'Finalizar' })).toBeDisabled();
  await expect(page.getByText('Disponible el 31/12/2030, cuando llegue la fecha de baja.')).toBeVisible();
});

test('contratos: liquidar con fianza o facturas pendientes da 409 y explica que se resuelve a mano', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', `/contratos/${CONTRATO_ID}`, { body: contratoDetalle('Finalizado', { fechaFinReal: '2026-10-01' }) });
  api.responder('POST', `/contratos/${CONTRATO_ID}/liquidacion`, problema(409, 'API-003', 'No se puede guardar el cambio', 'No se puede liquidar el contrato: la liquidación cierra la fianza y los saldos, y falta 1 fianza(s) cobrada(s) sin devolver, descontar ni retener y 2 factura(s) del contrato con importe pendiente de cobro.'));
  await page.goto(`/contratos/${CONTRATO_ID}`);
  await page.getByRole('button', { name: 'Liquidar' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Liquidar' }).click();
  await expect(page.getByText('No se puede liquidar todavía')).toBeVisible();
  await expect(page.getByText(/falta 1 fianza\(s\) cobrada\(s\) sin devolver/)).toBeVisible();
  await expect(page.getByText(/Esto se resuelve a mano/)).toBeVisible();
  await capturarClaroOscuro(page, 'contrato-liquidacion-bloqueada');
});

test('contratos: incrementos con fecha, cuerpo con porcentaje o importe y cancelación', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', `/contratos/${CONTRATO_ID}`, { body: contratoDetalle('Activo') });
  api.responder('GET', `/contratos/${CONTRATO_ID}/incrementos`, { body: incrementos });
  api.responder('POST', `/contratos/${CONTRATO_ID}/incrementos`, { status: 201, body: incrementos[0] });
  await page.goto(`/contratos/${CONTRATO_ID}`);
  await page.getByRole('tab', { name: /Incrementos/ }).click();
  await expect(page.getByText('5 %')).toBeVisible();
  await expect(page.getByText('Aplicado')).toBeVisible();
  await page.getByRole('button', { name: 'Nuevo incremento' }).click();
  await page.getByLabel(/^Porcentaje \(un 5 % es 5\)/).fill('4,5');
  await page.getByLabel('Entra en vigor el *').fill('01/03/2027');
  await page.getByRole('button', { name: 'Guardar incremento' }).click();
  await expect.poll(() => api.llamadas('POST', `/contratos/${CONTRATO_ID}/incrementos`)).toBe(1);
  expect(api.ultimoCuerpo('POST', `/contratos/${CONTRATO_ID}/incrementos`)).toEqual({ tipo: 'Porcentaje', porcentaje: '4.5', importe: null, fechaVigencia: '2027-03-01', comentario: null });
});

test('contratos: inventario congelado y revisión de salida firmada entera, con nota en lo que falta', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', `/contratos/${CONTRATO_ID}`, { body: contratoDetalle('Finalizado', { fechaFinReal: '2026-10-01' }) });
  api.responder('GET', `/contratos/${CONTRATO_ID}/inventario`, { body: inventario });
  api.responder('GET', `/contratos/${CONTRATO_ID}/revision-inventario`, problema(404, 'SEG-009', 'No encontrado', 'El contrato no tiene revisión de inventario firmada todavía.'));
  api.responder('POST', `/contratos/${CONTRATO_ID}/revision-inventario`, { status: 201, body: { id: 'rv', contratoId: CONTRATO_ID, firmadaAt: '2026-10-02T15:00:00Z', firmadaPorId: null, importePropuesto: 45.1, observacion: null, lineas: [] } });
  await page.goto(`/contratos/${CONTRATO_ID}`);
  await page.getByRole('tab', { name: /Inventario y salida/ }).click();
  await expect(page.getByText('Lámpara de pie').first()).toBeVisible();
  await capturarClaroOscuro(page, 'contrato-inventario');

  await page.getByRole('button', { name: 'Firmar revisión' }).click();
  await expect(page.getByText('Revisa los 2 elementos que quedan sin estado.')).toBeVisible();
  await page.getByRole('button', { name: 'OK' }).first().click();
  await page.getByRole('button', { name: 'Faltante' }).last().click();
  await page.getByPlaceholder('0,00').fill('45,10');
  await page.getByRole('button', { name: 'Firmar revisión' }).click();
  await expect(page.getByText('Escribe la nota de los elementos con incidencia.')).toBeVisible();
  await page.getByLabel(/Nota de «Lámpara de pie»/).fill('No está en la unidad');
  await page.getByRole('button', { name: 'Firmar revisión' }).click();
  await expect.poll(() => api.llamadas('POST', `/contratos/${CONTRATO_ID}/revision-inventario`)).toBe(1);
  expect(api.ultimoCuerpo('POST', `/contratos/${CONTRATO_ID}/revision-inventario`)).toEqual({
    lineas: [
      { contratoInventarioItemId: 'it1', estado: 'Presente', nota: null },
      { contratoInventarioItemId: 'it2', estado: 'Ausente', nota: 'No está en la unidad' },
    ],
    importePropuesto: '45.10',
    observacion: null,
  });
});

test('contratos: nuevo contrato desde una reserva copia los datos y manda reservaId', async ({ page, api }) => {
  conSesion(api);
  responderSelectores(api);
  api.responder('GET', `/reservas/${RESERVA_ID}`, { body: reservaDetalle({ tipo: 'Mensual', modalidad: 'Mensual', salidaPrevista: null, entradaPrevista: '2026-12-01' }) });
  api.responder('GET', '/plantillas-contrato', { body: pagina([plantilla]) });
  api.responder('POST', '/contratos', { status: 201, body: contratoDetalle('Borrador') });
  api.responder('GET', `/contratos/${CONTRATO_ID}`, { body: contratoDetalle('Borrador') });

  await page.goto(`/contratos/nuevo?reservaId=${RESERVA_ID}`);
  await expect(page.getByText(/Datos copiados de la reserva mensual/)).toBeVisible();
  await page.getByLabel(/^Renta/).fill('1250,50');
  await page.getByLabel('Fianza *').fill('2501');
  await page.getByLabel('Plantilla de contrato *').click();
  await page.getByRole('option', { name: /Alquiler mensual estándar/ }).click();
  await capturarClaroOscuro(page, 'contrato-formulario');
  await page.getByRole('button', { name: 'Guardar borrador' }).click();
  await expect(page).toHaveURL(new RegExp(`/contratos/${CONTRATO_ID}$`));
  expect(api.ultimoCuerpo('POST', '/contratos')).toMatchObject({
    clienteId: CLIENTE_ID,
    unidadId: UNIDAD_ID,
    reservaId: RESERVA_ID,
    facturadoraId: FACTURADORA_ID,
    fechaInicio: '2026-12-01',
    renta: '1250.50',
    fianza: '2501',
    moneda: 'USD',
    plantillaContratoId: 'ALQ-MENSUAL',
    reglaRenovacion: 'Anual',
    renovacionMeses: 12,
  });
});

test('plantillas y anexos: listado, edición con secciones y desactivación solo con contrato.estado', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', '/plantillas-contrato', { body: pagina([plantilla]) });
  api.responder('GET', '/plantillas-anexo', { body: pagina([anexo]) });
  api.responder('POST', '/plantillas-contrato/p1/desactivacion', { body: { ...plantilla, activa: false } });
  await page.goto('/contratos/plantillas');
  await expect(page.getByRole('heading', { name: 'Plantillas y anexos', level: 1 })).toBeVisible();
  await expect(page.getByText('Alquiler mensual estándar')).toBeVisible();
  await capturarClaroOscuro(page, 'plantillas');
  await page.getByRole('tab', { name: 'Anexos' }).click();
  await expect(page.getByText('Normas de convivencia')).toBeVisible();
  await page.getByRole('tab', { name: 'Plantillas de contrato' }).click();
  await page.getByRole('button', { name: 'Desactivar' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Desactivar' }).click();
  await expect.poll(() => api.llamadas('POST', '/plantillas-contrato/p1/desactivacion')).toBe(1);
});

test('plantillas: el empleado puede editar (contrato.borrador) pero no desactivar (contrato.estado)', async ({ page, api }) => {
  conSesion(api, EMPLEADO_OPERACION);
  api.responder('GET', '/plantillas-contrato', { body: pagina([plantilla]) });
  await page.goto('/contratos/plantillas');
  await expect(page.getByRole('button', { name: 'Editar' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Desactivar' })).toHaveCount(0);
});

test('sin cliente.ver el calendario, las reservas y los contratos dan sin permiso', async ({ page, api }) => {
  conSesion(api, identidad({ rol: 'empleado', permisosEfectivos: ['sesion.ver', 'sesion.password'] }));
  for (const ruta of ['/calendario', '/reservas', '/contratos']) {
    await page.goto(ruta);
    await expect(page.getByRole('heading', { name: 'Sin permiso', level: 1 })).toBeVisible();
  }
});
