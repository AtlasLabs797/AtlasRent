import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  aDecimal,
  compararImportes,
  entradaADecimal,
  equivalenteEnMonedaFactura,
  equivalenteRecibido,
  restarImportes,
  sumarImportes,
} from '../src/modulos/finanzas/decimal.ts';

test('vista previa de conversión: 59.500 DOP a 59,50 son 1.000,00 USD', () => {
  assert.equal(equivalenteEnMonedaFactura('59500.00', '59.50'), '1000.00');
  assert.equal(equivalenteEnMonedaFactura('59500', '59.5'), '1000.00');
});

test('vista previa de conversión: half-up y sin coma flotante', () => {
  // 100 / 3 = 33.333... -> 33.33 ; 100.01 / 3 = 33.3366... -> 33.34
  assert.equal(equivalenteEnMonedaFactura('100.00', '3'), '33.33');
  assert.equal(equivalenteEnMonedaFactura('100.01', '3'), '33.34');
  // 1.00 / 0.8 = 1.25 exacto; 0.01 / 4 = 0.0025 -> 0.00 ; 0.02 / 4 = 0.005 -> 0.01 (mitad hacia arriba)
  assert.equal(equivalenteEnMonedaFactura('0.02', '4'), '0.01');
  assert.equal(equivalenteEnMonedaFactura('0.01', '4'), '0.00');
  assert.equal(equivalenteEnMonedaFactura('1.00', '0.8'), '1.25');
  // seis decimales de cambio
  assert.equal(equivalenteEnMonedaFactura('61234.56', '61.234560'), '1000.00');
});

test('vista previa de conversión: cambio cero o negativo no convierte', () => {
  assert.equal(equivalenteEnMonedaFactura('100', '0'), null);
  assert.equal(equivalenteEnMonedaFactura('100', '-1'), null);
});

test('equivalente recibido: 1.000 USD a 59,50 son 59.500,00 DOP, con half-up', () => {
  assert.equal(equivalenteRecibido('1000.00', '59.50'), '59500.00');
  assert.equal(equivalenteRecibido('0.01', '59.5'), '0.60');
});

test('sumas y restas exactas (0.1 + 0.2 no se cuela)', () => {
  assert.equal(sumarImportes(['0.10', '0.20']), '0.30');
  assert.equal(sumarImportes(['0.1', '0.2', '0.0000001']), '0.30');
  assert.equal(restarImportes('1000.00', '999.99'), '0.01');
  assert.equal(restarImportes('1.00', '2.50'), '-1.50');
  assert.equal(compararImportes('10.00', '9.99'), 1);
  assert.equal(compararImportes('10', '10.00'), 0);
});

test('entrada tecleada: coma o punto decimal, sin separador de miles ni más decimales de los permitidos', () => {
  assert.equal(entradaADecimal('1250,5'), '1250.50');
  assert.equal(entradaADecimal(' 1250.50 '), '1250.50');
  assert.equal(entradaADecimal('1.250,00'), null);
  assert.equal(entradaADecimal('1.250'), null);
  assert.equal(entradaADecimal('10.005'), null);
  assert.equal(entradaADecimal('-5'), null);
  assert.equal(entradaADecimal('1e3'), null);
  assert.equal(entradaADecimal(''), null);
  assert.equal(entradaADecimal('59,5', 6), '59.500000');
});

test('aDecimal normaliza lo que manda la API', () => {
  assert.equal(aDecimal('1250.5'), '1250.50');
  assert.equal(aDecimal('0'), '0.00');
});
