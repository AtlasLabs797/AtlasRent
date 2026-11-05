import assert from 'node:assert/strict';
import { test } from 'node:test';
import { desplazarMes, diasDelMes, diasEntre, etiquetaMes, fechasDelMes, hoyNegocio, inicialDiaSemana, rangoDelMes, sumarDias } from '../src/modulos/operacion/fechas.ts';
import { diasLibres, filtrarUnidades, pintarDia, rutaDeOrigen, type DiaCalendario, type UnidadCalendario } from '../src/modulos/calendario/modelo.ts';

const dia = (sobre: Partial<DiaCalendario> = {}): DiaCalendario => ({ fecha: '2026-11-03', tipo: 'Libre', disponible: true, motivo: '', ocupaciones: [], ...sobre });

test('hoyNegocio usa America/Santo_Domingo, no UTC', () => {
  // 01:30 UTC del 28/10 son las 21:30 del 27/10 en Santo Domingo (UTC-4).
  assert.equal(hoyNegocio(new Date('2026-10-28T01:30:00Z')), '2026-10-27');
  assert.equal(hoyNegocio(new Date('2026-10-28T12:00:00Z')), '2026-10-28');
});

test('aritmética de fechas sin desfase', () => {
  assert.equal(sumarDias('2026-02-28', 1), '2026-03-01');
  assert.equal(sumarDias('2026-12-31', 1), '2027-01-01');
  assert.equal(diasEntre('2026-10-01', '2026-11-01'), 31);
  assert.equal(inicialDiaSemana('2026-11-02'), 'L');
});

test('rango del mes: los dos extremos incluidos y navegación entre años', () => {
  assert.deepEqual(rangoDelMes({ anio: 2026, mes: 2 }), { desde: '2026-02-01', hasta: '2026-02-28' });
  assert.equal(diasDelMes({ anio: 2028, mes: 2 }), 29);
  assert.deepEqual(desplazarMes({ anio: 2026, mes: 12 }, 1), { anio: 2027, mes: 1 });
  assert.deepEqual(desplazarMes({ anio: 2026, mes: 1 }, -1), { anio: 2025, mes: 12 });
  assert.equal(fechasDelMes({ anio: 2026, mes: 11 }).length, 30);
  assert.equal(etiquetaMes({ anio: 2026, mes: 11 }), 'noviembre 2026');
});

test('un solo medio día de reserva corta se pinta a medias; dos medios días, completo', () => {
  const medio = { origen: 'ReservaCorta', origenId: 'a', tipoDia: 'MedioDia', finExclusivo: null } as const;
  assert.equal(pintarDia(dia({ tipo: 'ReservaCorta', disponible: false, ocupaciones: [medio] })).medioDia, true);
  assert.equal(pintarDia(dia({ tipo: 'ReservaCorta', disponible: false, ocupaciones: [medio, { ...medio, origenId: 'b' }] })).medioDia, false);
  assert.equal(pintarDia(dia({ tipo: 'ReservaCorta', disponible: false, ocupaciones: [{ ...medio, tipoDia: 'DiaCompleto' }] })).medioDia, false);
  assert.equal(pintarDia(dia({ tipo: 'Contrato', disponible: false })).letra, 'C');
  assert.equal(pintarDia(dia()).letra, '');
});

test('filtrar unidades y contar días libres', () => {
  const u = (nombre: string, libre: boolean): UnidadCalendario => ({
    unidadId: nombre, unidad: nombre, inmuebleId: 'i', inmueble: 'I', estadoManual: 'Disponible', desde: '2026-11-01', hasta: '2026-11-02',
    dias: [dia({ disponible: libre }), dia({ disponible: true })],
  });
  const lista = [u('A-101', true), u('B-202', false)];
  assert.deepEqual(filtrarUnidades(lista, ' b-2 ').map((x) => x.unidad), ['B-202']);
  assert.equal(filtrarUnidades(lista, '').length, 2);
  assert.equal(diasLibres(lista[1]!), 1);
});

test('origen de la ocupación enlaza a su ficha; el mantenimiento no tiene', () => {
  assert.equal(rutaDeOrigen({ origen: 'Contrato', origenId: 'x', tipoDia: null, finExclusivo: null }), '/contratos/x');
  assert.equal(rutaDeOrigen({ origen: 'ReservaCorta', origenId: 'y', tipoDia: 'MedioDia', finExclusivo: null }), '/reservas/y');
  assert.equal(rutaDeOrigen({ origen: 'Mantenimiento', origenId: 'z', tipoDia: null, finExclusivo: null }), null);
});
