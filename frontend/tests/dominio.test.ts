import assert from 'node:assert/strict';
import { test } from 'node:test';
import { formatearImporte, formatearNumero, normalizarDecimal } from '../src/dominio/dinero.ts';
import { formatearFecha, formatearMarca } from '../src/dominio/fechas.ts';

test('importes: punto de miles, coma decimal y moneda', () => {
  assert.equal(formatearImporte('1250', 'USD'), '1.250,00 USD');
  assert.equal(formatearImporte('1250.5', 'USD'), '1.250,50 USD');
  assert.equal(formatearImporte('0', 'DOP'), '0,00 DOP');
  assert.equal(formatearImporte('1234567.89', 'DOP'), '1.234.567,89 DOP');
  assert.equal(formatearImporte('999.999', 'DOP'), '1.000,00 DOP');
});

test('importes: negativos con signo menos y -0 no existe', () => {
  assert.equal(formatearNumero('-1250.5'), '−1.250,50');
  assert.equal(formatearNumero('-0.001'), '0,00');
});

test('importes: no hay coma flotante, 0.1 + 0.2 no se cuela y los decimales largos no se pierden', () => {
  assert.equal(normalizarDecimal('0.30000000000000004'), '0.30');
  assert.equal(normalizarDecimal('9007199254740993.01'), '9007199254740993.01');
});

test('importes: redondeo half-up por dígitos, incluidos los negativos (away from zero)', () => {
  assert.equal(normalizarDecimal('1.005'), '1.01');
  assert.equal(normalizarDecimal('1.004'), '1.00');
  assert.equal(normalizarDecimal('-1.005'), '-1.01');
  assert.equal(normalizarDecimal('2.675'), '2.68');
  assert.equal(normalizarDecimal('99.995'), '100.00');
});

test('importes: un texto que no es decimal lanza en vez de mostrar basura', () => {
  assert.throws(() => normalizarDecimal('1,250.00'));
  assert.throws(() => normalizarDecimal(''));
  assert.throws(() => normalizarDecimal('abc'));
});

test('fechas: yyyy-MM-dd a dd/mm/aaaa sin desfase de zona', () => {
  assert.equal(formatearFecha('2026-03-05'), '05/03/2026');
  assert.equal(formatearFecha('2026-01-01'), '01/01/2026');
  assert.equal(formatearFecha('2024-02-29'), '29/02/2024');
});

test('fechas: las imposibles lanzan', () => {
  assert.throws(() => formatearFecha('2026-02-30'));
  assert.throws(() => formatearFecha('2026-13-01'));
  assert.throws(() => formatearFecha('05/03/2026'));
});

test('marcas de tiempo: UTC a hora de Santo Domingo (UTC-4)', () => {
  assert.equal(formatearMarca('2026-03-05T02:30:00Z'), '04/03/2026 22:30');
  assert.equal(formatearMarca('2026-07-01T16:05:00Z'), '01/07/2026 12:05');
});
