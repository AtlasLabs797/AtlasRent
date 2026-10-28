import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  claveErrorSubida,
  construirTarifas,
  contarEstados,
  cuerpoInmueble,
  cuerpoUnidad,
  errorImporte,
  formatearTamano,
  importeATexto,
  mesesValidos,
  TAMANO_MAXIMO_BYTES,
  validarPdf,
  valoresInmueble,
  valoresUnidad,
} from '../src/modulos/unidades/logica.ts';
import type { Unidad } from '../src/modulos/unidades/tipos.ts';

const unidad: Unidad = {
  id: 'u1', inmuebleId: 'i1', nombre: 'Apto 2B', tipo: 'Apartamento', propietariaId: 'prop-1', facturadoraPorDefectoId: 'fact-1',
  estadoManual: 'Mantenimiento', modalidades: ['Mensual', 'DiaCompleto'],
  tarifas: [{ modalidad: 'Mensual', moneda: 'USD', importe: 1250.5 }, { modalidad: 'DiaCompleto', moneda: 'DOP', importe: 8000 }],
  metrosCuadrados: 82.5, capacidad: 4, habitaciones: 2, banos: 1.5, medidor: null, notas: null, activo: true, createdAt: '2026-10-01T00:00:00Z',
};

test('los importes de tarifa viajan como string decimal con 2 decimales, sin coma flotante', () => {
  assert.equal(importeATexto('1250,5'), '1250.50');
  assert.equal(importeATexto('0.1'), '0.10');
  assert.equal(importeATexto('9999999999999999.99'), '9999999999999999.99');
  assert.throws(() => importeATexto('12,345'));
  assert.throws(() => importeATexto('-5'));
  assert.equal(errorImporte(''), null);
  assert.equal(errorImporte('1.234'), 'formato');
  assert.equal(errorImporte('abc'), 'formato');
});

test('la ficha de una unidad se rellena con sus tarifas en string y el cuerpo las devuelve en string', () => {
  const v = valoresUnidad(unidad);
  assert.equal(v.tarifas['Mensual|USD'], '1250.50');
  const c = cuerpoUnidad(v, unidad);
  assert.deepEqual(c.tarifas, [
    { modalidad: 'Mensual', moneda: 'USD', importe: '1250.50' },
    { modalidad: 'DiaCompleto', moneda: 'DOP', importe: '8000.00' },
  ]);
  for (const t of c.tarifas) assert.equal(typeof t.importe, 'string');
});

test('un PUT conserva propietaria y facturadora (la pantalla no las edita) y el estado manual', () => {
  const c = cuerpoUnidad(valoresUnidad(unidad), unidad);
  assert.equal(c.propietariaId, 'prop-1');
  assert.equal(c.facturadoraPorDefectoId, 'fact-1');
  assert.equal(c.estadoManual, 'Mantenimiento');
  assert.equal(c.metrosCuadrados, 82.5);
  assert.equal(c.banos, 1.5);
  assert.equal(cuerpoUnidad(valoresUnidad(), null).propietariaId, null);
});

test('una tarifa de una modalidad desmarcada se descarta: la API rechazaría tarifas fuera de las modalidades', () => {
  const t = construirTarifas({ modalidades: ['Mensual'], tarifas: { 'Mensual|USD': '100', 'MedioDia|USD': '40' } });
  assert.deepEqual(t, [{ modalidad: 'Mensual', moneda: 'USD', importe: '100.00' }]);
});

test('cuerpoInmueble conserva la propietaria y convierte la renovación', () => {
  const c = cuerpoInmueble({ ...valoresInmueble(), nombre: ' Torre ', renovacionDefectoMeses: '12' }, { propietariaId: 'p9' });
  assert.equal(c.nombre, 'Torre');
  assert.equal(c.propietariaId, 'p9');
  assert.equal(c.renovacionDefectoMeses, 12);
  assert.equal(cuerpoInmueble(valoresInmueble(), null).renovacionDefectoMeses, null);
  assert.ok(mesesValidos('') && mesesValidos('600') && !mesesValidos('601') && !mesesValidos('0') && !mesesValidos('1.5'));
});

test('validarPdf: solo PDF, de 1 byte a 25 MB', () => {
  assert.equal(validarPdf({ name: 'titulo.pdf', type: 'application/pdf', size: 1000 }), null);
  assert.equal(validarPdf({ name: 'TITULO.PDF', type: '', size: 1000 }), null);
  assert.equal(validarPdf({ name: 'foto.png', type: 'image/png', size: 1000 }), 'noPdf');
  assert.equal(validarPdf({ name: 'x.pdf', type: 'text/html', size: 1000 }), 'noPdf');
  assert.equal(validarPdf({ name: 'x.pdf', type: 'application/pdf', size: 0 }), 'vacio');
  assert.equal(validarPdf({ name: 'x.pdf', type: 'application/pdf', size: TAMANO_MAXIMO_BYTES }), null);
  assert.equal(validarPdf({ name: 'x.pdf', type: 'application/pdf', size: TAMANO_MAXIMO_BYTES + 1 }), 'grande');
});

test('mensaje de subida: 413 y 415 claros, 400 con el detalle de la API', () => {
  assert.equal(claveErrorSubida(413, undefined), 'grande');
  assert.equal(claveErrorSubida(415, undefined), 'noPdf');
  assert.equal(claveErrorSubida(400, 'El archivo no es un PDF.'), 'detalle');
  assert.equal(claveErrorSubida(400, undefined), 'generico');
  assert.equal(claveErrorSubida(500, 'x'), 'generico');
  assert.equal(claveErrorSubida(undefined, undefined), 'generico');
});

test('formatearTamano y contarEstados', () => {
  assert.equal(formatearTamano(512), '512 B');
  assert.equal(formatearTamano(1536), '1,5 KB');
  assert.equal(formatearTamano(3 * 1024 * 1024), '3,0 MB');
  assert.deepEqual(
    contarEstados([
      { estadoManual: 'Disponible', activo: true },
      { estadoManual: 'Mantenimiento', activo: true },
      { estadoManual: 'FueraDeServicio', activo: true },
      { estadoManual: 'Disponible', activo: false },
    ]),
    { disponible: 1, mantenimiento: 1, fuera: 1 },
  );
});
