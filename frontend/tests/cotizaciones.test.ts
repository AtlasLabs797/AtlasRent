import assert from 'node:assert/strict';
import { test } from 'node:test';
import { centimosDeLinea, vistaPrevia } from '../src/modulos/cotizaciones/calculo.ts';
import { cotizacionEditable, cuerpoCotizacion, hayErrores, puedeConvertir, transicionesCotizacion, validarCotizacion, type FormularioCotizacion } from '../src/modulos/cotizaciones/cuerpo.ts';

const base: FormularioCotizacion = {
  destinatarioTipo: 'cliente',
  destinatarioId: 'c1',
  unidadId: null,
  moneda: 'USD',
  lineas: [{ descripcion: 'Renta', cantidad: '1', precioUnitario: '1250.00' }],
};

test('vista previa: cada línea se redondea half-up a 2 decimales y el total es la suma de líneas redondeadas', () => {
  assert.equal(centimosDeLinea('3', '0.335'), 101n); // 1,005 -> 1,01 (half-up, no 1,00 de coma flotante)
  assert.equal(centimosDeLinea('1.5', '10.01'), 1502n); // 15,015 -> 15,02
  assert.equal(centimosDeLinea('2', '1250'), 250000n);
  // Tres líneas de 0,005 cada una: redondeadas suman 0,03; sin redondear por línea serían 0,02.
  const v = vistaPrevia([1, 2, 3].map(() => ({ descripcion: 'x', cantidad: '1', precioUnitario: '0.005' })));
  assert.deepEqual(v.importes, ['0.01', '0.01', '0.01']);
  assert.equal(v.total, '0.03');
});

test('vista previa: acepta coma decimal, rechaza basura y marca la línea incompleta', () => {
  assert.equal(centimosDeLinea('1', '10,5'), 1050n);
  assert.equal(centimosDeLinea('-1', '10'), null);
  assert.equal(centimosDeLinea('', '10'), null);
  const v = vistaPrevia([{ descripcion: 'a', cantidad: '1', precioUnitario: '10' }, { descripcion: 'b', cantidad: '1', precioUnitario: 'abc' }]);
  assert.equal(v.incompleta, true);
  assert.equal(v.importes[1], null);
  assert.equal(v.total, '10.00');
});

test('el cuerpo manda cliente o prospecto, nunca los dos, e importes como texto decimal', () => {
  assert.deepEqual(cuerpoCotizacion(base), {
    clienteId: 'c1', prospectoId: null, unidadId: null, moneda: 'USD',
    lineas: [{ descripcion: 'Renta', cantidad: '1', precioUnitario: '1250.00' }],
  });
  const p = cuerpoCotizacion({ ...base, destinatarioTipo: 'prospecto', destinatarioId: 'p1', lineas: [{ descripcion: ' A ', cantidad: '2', precioUnitario: '10,5' }] });
  assert.equal(p.clienteId, null);
  assert.equal(p.prospectoId, 'p1');
  assert.deepEqual(p.lineas, [{ descripcion: 'A', cantidad: '2', precioUnitario: '10.5' }]);
});

test('validación previa: destinatario, líneas y números', () => {
  assert.equal(hayErrores(validarCotizacion(base)), false);
  assert.ok(validarCotizacion({ ...base, destinatarioId: null }).destinatario);
  assert.ok(validarCotizacion({ ...base, lineas: [] }).lineas);
  assert.ok(validarCotizacion({ ...base, lineas: [{ descripcion: '', cantidad: '1', precioUnitario: '1' }] }).linea[0]);
  assert.ok(validarCotizacion({ ...base, lineas: [{ descripcion: 'x', cantidad: '1', precioUnitario: '-5' }] }).linea[0]);
});

test('D18: borrador -> enviada -> aceptada o rechazada; solo el borrador se edita; solo la aceptada se convierte', () => {
  assert.deepEqual(transicionesCotizacion('Borrador'), ['Enviada']);
  assert.deepEqual(transicionesCotizacion('Enviada'), ['Aceptada', 'Rechazada']);
  assert.deepEqual(transicionesCotizacion('Aceptada'), []);
  assert.deepEqual(transicionesCotizacion('Rechazada'), []);
  assert.equal(cotizacionEditable('Borrador'), true);
  assert.equal(cotizacionEditable('Enviada'), false);
  assert.equal(puedeConvertir('Aceptada'), true);
  assert.equal(puedeConvertir('Enviada'), false);
});
