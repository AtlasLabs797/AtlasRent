import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  FORMULARIO_VACIO,
  clasificarBloqueo,
  cuerpoReserva,
  hayErroresReserva,
  reservaCancelable,
  reservaConvertible,
  reservaEditable,
  reservaFacturable,
  validarReserva,
  type FormularioReserva,
} from '../src/modulos/reservas/cuerpo.ts';

const mensual: FormularioReserva = { ...FORMULARIO_VACIO, unidadId: 'u1', destinatarioId: 'c1', entradaPrevista: '2026-11-01', importe: '1250,50' };
const corta: FormularioReserva = {
  ...FORMULARIO_VACIO,
  unidadId: 'u1',
  destinatarioId: 'c1',
  tipo: 'Corta',
  modalidadCorta: 'MedioDia',
  importe: '120',
  dias: [
    { fecha: '2026-11-10', horaOrientativa: ' de 9 a 14 aprox. ' },
    { fecha: '2026-11-03', horaOrientativa: '' },
  ],
};

test('mensual: rango abierto, sin salida ni días, modalidad Mensual', () => {
  const c = cuerpoReserva(mensual);
  assert.equal(c.tipo, 'Mensual');
  assert.equal(c.modalidad, 'Mensual');
  assert.equal(c.salidaPrevista, null);
  assert.deepEqual(c.dias, []);
  assert.equal(c.entradaPrevista, '2026-11-01');
  assert.equal(c.importe, '1250.50');
});

test('corta: entra y sale el primer día, lleva sus días y la hora es texto libre opcional', () => {
  const c = cuerpoReserva(corta);
  assert.equal(c.entradaPrevista, '2026-11-03');
  assert.equal(c.salidaPrevista, '2026-11-03');
  assert.equal(c.modalidad, 'MedioDia');
  assert.deepEqual(c.dias, [
    { fecha: '2026-11-10', horaOrientativa: 'de 9 a 14 aprox.' },
    { fecha: '2026-11-03', horaOrientativa: null },
  ]);
});

test('el anticipo viaja como importe + nota, sin ningún campo de cobro', () => {
  const c = cuerpoReserva({ ...mensual, anticipoImporte: '300', anticipoNota: ' a cuenta ' });
  assert.equal(c.anticipoImporte, '300');
  assert.equal(c.anticipoNota, 'a cuenta');
  assert.deepEqual(Object.keys(c).filter((k) => /cobro|factura/i.test(k)), []);
  assert.equal(cuerpoReserva(mensual).anticipoImporte, null);
});

test('validación: unidad, destinatario, entrada, días repetidos e importes', () => {
  assert.equal(hayErroresReserva(validarReserva(mensual)), false);
  assert.equal(hayErroresReserva(validarReserva(corta)), false);
  assert.ok(validarReserva({ ...mensual, unidadId: null }).unidad);
  assert.ok(validarReserva({ ...mensual, destinatarioId: null }).destinatario);
  assert.ok(validarReserva({ ...mensual, entradaPrevista: '' }).entrada);
  assert.ok(validarReserva({ ...corta, dias: [] }).dias);
  assert.ok(validarReserva({ ...corta, dias: [{ fecha: '2026-11-03', horaOrientativa: '' }, { fecha: '2026-11-03', horaOrientativa: '' }] }).dias);
  assert.ok(validarReserva({ ...mensual, importe: '12,345' }).importe);
  assert.ok(validarReserva({ ...mensual, anticipoImporte: 'abc' }).anticipo);
  assert.ok(validarReserva({ ...corta, dias: [{ fecha: '2026-11-03', horaOrientativa: 'x'.repeat(61) }] }).hora[0]);
});

test('estados: editar solo activa; cancelar activa o en conflicto; facturar solo corta activa con cliente; convertir solo mensual viva', () => {
  assert.equal(reservaEditable('Activa'), true);
  assert.equal(reservaEditable('EnConflicto'), false);
  assert.equal(reservaCancelable('EnConflicto'), true);
  assert.equal(reservaCancelable('Convertida'), false);
  assert.equal(reservaFacturable('Corta', 'Activa', true), true);
  assert.equal(reservaFacturable('Corta', 'Activa', false), false);
  assert.equal(reservaFacturable('Mensual', 'Activa', true), false);
  assert.equal(reservaConvertible('Mensual', 'EnConflicto'), true);
  assert.equal(reservaConvertible('Corta', 'Activa'), false);
});

test('los 409 se clasifican: unidad ocupada, factura vigente, unidad no alquilable', () => {
  assert.equal(clasificarBloqueo('DIS-001', 'La unidad no está disponible'), 'unidadNoDisponible');
  assert.equal(clasificarBloqueo('API-003', 'La reserva tiene una factura vigente: anule la factura antes de cancelarla.'), 'facturaVigente');
  assert.equal(clasificarBloqueo('API-003', 'La unidad está en mantenimiento o fuera de servicio y no se puede reservar.'), 'unidadNoAlquilable');
  assert.equal(clasificarBloqueo('API-003', 'Otra cosa'), 'otro');
});
