import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  clavesDeEntidad,
  claveDeFiltros,
  esFecha,
  ESTADOS,
  fechaCorta,
  FILTROS_VACIOS,
  filtrosDesdeParametros,
  MONEDAS,
  parametrosDeFiltros,
  type FiltrosInformes,
} from '../src/informes/filtros.ts';

const GUID = '3f1a0c2e-5b6d-4a7c-8e9f-0a1b2c3d4e5f';
const consulta = (texto: string) => filtrosDesdeParametros(new URLSearchParams(texto));

test('sin query string no hay filtros, y no se manda ningún parámetro a la API', () => {
  assert.deepEqual(consulta(''), FILTROS_VACIOS);
  assert.deepEqual(parametrosDeFiltros(FILTROS_VACIOS), {});
  assert.equal(claveDeFiltros(FILTROS_VACIOS), 'sin-filtros');
});

test('los filtros de la pantalla se leen de la URL y vuelven a ella', () => {
  const url = `desde=2026-01-01&hasta=2026-10-18&moneda=DOP&estado=Emitida&clienteId=${GUID}`;
  const filtros = consulta(url);

  assert.equal(filtros.desde, '2026-01-01');
  assert.equal(filtros.hasta, '2026-10-18');
  assert.equal(filtros.moneda, 'DOP');
  assert.equal(filtros.estado, 'Emitida');
  assert.equal(filtros.clienteId, GUID);

  // Ida y vuelta: la URL que sale vuelve a dar los mismos filtros.
  assert.deepEqual(consulta(new URLSearchParams(parametrosDeFiltros(filtros)).toString()), filtros);
});

test('una fecha imposible o mal escrita no llega a la API', () => {
  assert.equal(esFecha('2026-02-30'), false, 'febrero no tiene día 30');
  assert.equal(esFecha('2026-13-01'), false);
  assert.equal(esFecha('2026-2-01'), false, 'el input type=date siempre da dos dígitos');
  assert.equal(esFecha('2026-02-28'), true);

  const filtros = consulta('desde=2026-02-30&hasta=no-es-fecha');
  assert.equal(filtros.desde, null);
  assert.equal(filtros.hasta, null);
  assert.deepEqual(parametrosDeFiltros(filtros), {});
});

test('moneda y estado solo aceptan los valores del backend', () => {
  for (const moneda of MONEDAS) {
    assert.equal(consulta(`moneda=${moneda}`).moneda, moneda);
  }
  for (const estado of ESTADOS) {
    assert.equal(consulta(`estado=${estado}`).estado, estado);
  }
  assert.equal(consulta('moneda=eur').moneda, null, 'no hay euros en Atlas Rent');
  assert.equal(consulta('estado=Pagada').estado, null);
  assert.equal(consulta('estado=emitida').estado, null, 'los estados van por nombre exacto');
});

test('los identificadores de entidad se descartan si no son GUID', () => {
  assert.equal(consulta(`unidadId=${GUID}`).unidadId, GUID);
  assert.equal(consulta('unidadId=12').unidadId, null);
  assert.equal(consulta('clienteId=%3Cscript%3E').clienteId, null);
});

test('los identificadores que solo llegan por URL se distinguen de los de la pantalla', () => {
  const porUrl = consulta(`clienteId=${GUID}&serieId=${GUID}&estado=Anulada`);
  assert.deepEqual(clavesDeEntidad(porUrl), ['serieId', 'clienteId']);
  assert.deepEqual(clavesDeEntidad(FILTROS_VACIOS), []);
});

test('la clave distingue informes distintos y ordena los mismos filtros igual', () => {
  const a: FiltrosInformes = { ...FILTROS_VACIOS, desde: '2026-01-01', hasta: '2026-01-31' };
  const b: FiltrosInformes = { ...FILTROS_VACIOS, desde: '2026-02-01', hasta: '2026-02-28' };
  assert.notEqual(claveDeFiltros(a), claveDeFiltros(b));

  // El orden de las claves no depende de cómo se construyó el objeto: el `queryKey` de TanStack
  // Query compara por valor y dos objetos con el mismo contenido deben dar la misma clave.
  const desordenado = {
    hasta: '2026-01-31',
    clienteId: GUID,
    desde: '2026-01-01',
    estado: null,
    moneda: null,
    serieId: null,
    unidadId: null,
    contratoId: null,
    inmuebleId: null,
    propietariaId: null,
    facturadoraId: null,
  } satisfies FiltrosInformes;
  assert.equal(claveDeFiltros(desordenado), `desde=2026-01-01&hasta=2026-01-31&clienteId=${GUID}`);
});

test('las fechas se enseñan en dd/mm/aaaa', () => {
  assert.equal(fechaCorta('2026-10-18'), '18/10/2026');
  assert.equal(fechaCorta(null), '—');
  assert.equal(fechaCorta('2026-02-30'), '—');
});