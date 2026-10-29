import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  claveParaIntento,
  consultaCuentas,
  cuerpoCobro,
  evaluarSeleccion,
  filtrosDesdeParametros,
  FILTROS_VACIOS,
  huellaCuerpo,
  insigniaDeFila,
  numeroDocumento,
  parametrosDeFiltros,
  validarFormulario,
  type FormularioCobro,
} from '../src/modulos/finanzas/logica.ts';
import type { CuentaFila } from '../src/modulos/finanzas/tipos.ts';

function fila(o: Partial<CuentaFila> = {}): CuentaFila {
  return {
    tipo: 'Factura', id: 'f1', serieId: 's', serieNumero: 1, numero: 412, comprobanteTipo: null, comprobanteNumero: null,
    fecha: '2026-10-01', clienteId: 'c1', cliente: 'Cliente', unidadId: null, unidad: null, contratoId: 'k1', concepto: 'Renta',
    moneda: 'USD', total: '1000.00', retencion: '0.00', cobrado: '0.00', pendiente: '1000.00', saldoADevolver: null, vencimiento: '2026-10-05',
    diasVencida: 0, vencida: false, estado: 'Pendiente', modificada: false, tieneReservaConAnticipo: false, estadoEnvio: null,
    modificadaTrasEnvio: false, cobrable: true, ...o,
  };
}

const FORM: FormularioCobro = {
  fechaEfectiva: '2026-10-28', monedaRecibida: 'DOP', importeRecibido: '59500', formaPagoId: 'fp', cuentaReceptoraId: 'cr',
  referencia: '', usarSaldoAFavor: '', tipoCambioAplicado: '59,50',
};

test('filtros: ida y vuelta por la URL, solo lo no predeterminado', () => {
  const f = { ...FILTROS_VACIOS, moneda: 'DOP' as const, vencidas: true, incluirFianzas: false, page: 3, clienteId: 'abc' };
  const p = parametrosDeFiltros(f);
  assert.equal(p.toString(), 'clienteId=abc&moneda=DOP&vencidas=true&incluirFianzas=false&page=3');
  assert.deepEqual(filtrosDesdeParametros(p), f);
  assert.equal(parametrosDeFiltros(FILTROS_VACIOS).toString(), '');
});

test('consulta: Anulada es estado de factura, el resto estado de cobro', () => {
  assert.equal(consultaCuentas({ ...FILTROS_VACIOS, estado: 'Anulada' }, 25).estado, 'Anulada');
  const q = consultaCuentas({ ...FILTROS_VACIOS, estado: 'Parcial' }, 25);
  assert.equal(q.estadoCobro, 'Parcial');
  assert.equal(q.estado, undefined);
  assert.equal(q.incluirFianzas, true);
});

test('selección: mismo cliente y contrato, todo cobrable', () => {
  assert.equal(evaluarSeleccion([]).ok, false);
  const ok = evaluarSeleccion([fila(), fila({ id: 'f2' })]);
  assert.equal(ok.ok, true);
  assert.deepEqual(evaluarSeleccion([fila(), fila({ id: 'f2', clienteId: 'c2' })]), { ok: false, motivo: 'variosClientes' });
  assert.deepEqual(evaluarSeleccion([fila(), fila({ id: 'f2', contratoId: 'k2' })]), { ok: false, motivo: 'variosContratos' });
  assert.deepEqual(evaluarSeleccion([fila({ contratoId: null })]), { ok: false, motivo: 'sinContrato' });
  assert.deepEqual(evaluarSeleccion([fila({ cobrable: false })]), { ok: false, motivo: 'noCobrable' });
});

test('formulario: cambio obligatorio y positivo solo si hace falta', () => {
  assert.deepEqual(validarFormulario(FORM, true), {});
  assert.equal(validarFormulario({ ...FORM, tipoCambioAplicado: '' }, true).tipoCambioAplicado, 'obligatorio');
  assert.equal(validarFormulario({ ...FORM, tipoCambioAplicado: '0' }, true).tipoCambioAplicado, 'positivo');
  assert.equal(validarFormulario({ ...FORM, tipoCambioAplicado: '' }, false).tipoCambioAplicado, undefined);
  assert.equal(validarFormulario({ ...FORM, importeRecibido: '1.250,00' }, true).importeRecibido, 'invalido');
  assert.equal(validarFormulario({ ...FORM, importeRecibido: '0' }, true).importeRecibido, 'positivo');
  assert.equal(validarFormulario({ ...FORM, formaPagoId: '' }, true).formaPagoId, 'obligatorio');
});

test('cuerpo: importes como texto decimal, cambio con seis decimales y facturas marcadas', () => {
  const sel = { clienteId: 'c1', contratoId: 'k1', filas: [fila()] };
  const c = cuerpoCobro(FORM, sel, 'seleccionadas', null, true);
  assert.equal(c.importeRecibido, '59500.00');
  assert.equal(c.tipoCambioAplicado, '59.500000');
  assert.equal(c.usarSaldoAFavor, '0.00');
  assert.deepEqual(c.facturasSeleccionadas, ['f1']);
  assert.equal(c.asignaciones, undefined);
  assert.equal(cuerpoCobro(FORM, sel, 'fifo', null, true).facturasSeleccionadas, undefined);
  assert.equal(cuerpoCobro(FORM, sel, 'fifo', null, false).tipoCambioAplicado, undefined);
});

test('cuerpo: con líneas simuladas van como asignaciones explícitas y se descartan las de cero', () => {
  const sel = { clienteId: 'c1', contratoId: 'k1', filas: [fila()] };
  const c = cuerpoCobro(FORM, sel, 'seleccionadas', [
    { facturaId: 'f1', fianzaId: null, importe: '1000.00' },
    { facturaId: null, fianzaId: 'z1', importe: '250.50' },
    { facturaId: 'f2', fianzaId: null, importe: '0.00' },
  ], true);
  assert.deepEqual(c.asignaciones, [{ facturaId: 'f1', importe: '1000.00' }, { fianzaId: 'z1', importe: '250.50' }]);
  assert.equal(c.facturasSeleccionadas, undefined);
});

test('cuerpo: con fianzas seleccionadas las asignaciones salen explícitas por su pendiente', () => {
  const sel = { clienteId: 'c1', contratoId: 'k1', filas: [fila(), fila({ tipo: 'Fianza', id: 'z1', pendiente: '500.00' })] };
  const c = cuerpoCobro(FORM, sel, 'seleccionadas', null, true);
  assert.deepEqual(c.asignaciones, [{ facturaId: 'f1', importe: '1000.00' }, { fianzaId: 'z1', importe: '500.00' }]);
});

test('idempotencia: mismo cuerpo reutiliza clave; cuerpo distinto estrena', () => {
  const sel = { clienteId: 'c1', contratoId: 'k1', filas: [fila()] };
  const h1 = huellaCuerpo(cuerpoCobro(FORM, sel, 'seleccionadas', null, true));
  const h2 = huellaCuerpo(cuerpoCobro({ ...FORM, importeRecibido: '60000' }, sel, 'seleccionadas', null, true));
  let n = 0;
  const gen = () => `k${++n}`;
  const a = claveParaIntento(null, h1, gen);
  assert.equal(claveParaIntento(a, h1, gen).clave, a.clave);
  assert.notEqual(claveParaIntento(a, h2, gen).clave, a.clave);
});

test('presentación: número de documento e insignias', () => {
  assert.equal(numeroDocumento(1, 412), '1-00412');
  assert.equal(numeroDocumento(null, null), '—');
  assert.equal(insigniaDeFila('Factura', 'Pagada').status, 'pagado');
  assert.equal(insigniaDeFila('Fianza', 'EnLiquidacion').status, 'devolucion');
  assert.equal(insigniaDeFila('Proforma', 'Emitida').label, 'Proforma');
});

test('fianza: el motivo es obligatorio con descuento o si la devolución no deja la fianza a cero', async () => {
  const { motivoObligatorio } = await import('../src/modulos/finanzas/logica.ts');
  assert.equal(motivoObligatorio('1000.00', '0', '1000.00'), false);
  assert.equal(motivoObligatorio('800.00', '0', '1000.00'), true);
  assert.equal(motivoObligatorio('900.00', '100.00', '1000.00'), true);
  assert.equal(motivoObligatorio('700.00', '300.00', '1000.00'), true);
});

test('contratoId viaja en la URL y en la consulta de /cuentas', () => {
  const f = filtrosDesdeParametros(new URLSearchParams('contratoId=abc'));
  assert.equal(f.contratoId, 'abc');
  assert.equal(parametrosDeFiltros(f).get('contratoId'), 'abc');
  assert.equal(consultaCuentas(f, 25).contratoId, 'abc');
  assert.equal('contratoId' in consultaCuentas(FILTROS_VACIOS, 25), false);
});
