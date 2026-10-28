import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CAMPOS_NUMERADOR, CAMPOS_PLANTILLA, ITBIS, RETENCIONES, SERIES, cuerpoAjustes } from '../src/modulos/configuracion/catalogos.ts';
import {
  ACCIONES_AGENTE,
  cuerpoTipoCambio,
  cuerpoToken,
  estadoNumerador,
  estadoToken,
  formatearPorcentaje,
  formatearTasa,
  hoyNegocio,
  validarNumerador,
} from '../src/modulos/configuracion/config.ts';
import {
  compararDecimales,
  construirCuerpo,
  normalizarDecimalEntrada,
  validarCampo,
  valoresIniciales,
} from '../src/modulos/configuracion/esquema.ts';
import { textosConfiguracion } from '../src/modulos/configuracion/textos.ts';

const v = textosConfiguracion.validacion;

test('decimales de entrada: punto o coma, límite de decimales y sin notación rara', () => {
  assert.equal(normalizarDecimalEntrada('62,35', 6), '62.35');
  assert.equal(normalizarDecimalEntrada(' 60.123456 ', 6), '60.123456');
  assert.equal(normalizarDecimalEntrada('60.1234567', 6), null);
  assert.equal(normalizarDecimalEntrada('-1', 6), null);
  assert.equal(normalizarDecimalEntrada('1e3', 6), null);
  assert.equal(normalizarDecimalEntrada('', 6), null);
  assert.equal(compararDecimales('18.00', '100'), -1);
  assert.equal(compararDecimales('100.01', '100'), 1);
  assert.equal(compararDecimales('0.10', '0.1'), 0);
});

test('tipo de cambio: el valor viaja como string con 6 decimales máximo, nunca como número, y solo DOP', () => {
  assert.deepEqual(cuerpoTipoCambio('', '62,5'), { moneda: 'DOP', valor: '62.5' });
  assert.deepEqual(cuerpoTipoCambio('2026-10-27', '60.123456'), { fecha: '2026-10-27', moneda: 'DOP', valor: '60.123456' });
  assert.throws(() => cuerpoTipoCambio('', '0'));
  assert.throws(() => cuerpoTipoCambio('', '0.000000'));
  assert.throws(() => cuerpoTipoCambio('', '60.1234567'));
  assert.throws(() => cuerpoTipoCambio('', 'abc'));
});

test('formato de tasas y porcentajes sin coma flotante', () => {
  assert.equal(formatearTasa(62.5), '62,500000');
  assert.equal(formatearTasa('1060.35'), '1.060,350000');
  assert.equal(formatearPorcentaje(18), '18,00 %');
  assert.equal(formatearPorcentaje('0.5'), '0,50 %');
});

test('hoy se calcula en Santo Domingo (UTC-4), no en UTC', () => {
  assert.equal(hoyNegocio(new Date('2026-10-28T02:00:00Z')), '2026-10-27');
  assert.equal(hoyNegocio(new Date('2026-10-28T04:00:00Z')), '2026-10-28');
});

test('estado del numerador: vencido, agotado, por agotar y vigente; el día de vencimiento todavía vale', () => {
  const base = { restantes: 100, umbralAlerta: 10, fechaVencimiento: '2026-12-31' };
  assert.equal(estadoNumerador(base, '2026-10-28'), 'vigente');
  assert.equal(estadoNumerador({ ...base, restantes: 10 }, '2026-10-28'), 'porAgotar');
  assert.equal(estadoNumerador({ ...base, restantes: 0 }, '2026-10-28'), 'agotado');
  assert.equal(estadoNumerador(base, '2026-12-31'), 'vigente');
  assert.equal(estadoNumerador(base, '2027-01-01'), 'vencido');
  assert.equal(estadoNumerador({ ...base, restantes: 0 }, '2027-01-01'), 'vencido');
});

test('rango del numerador: hasta >= desde y siguiente entre desde y hasta + 1', () => {
  assert.deepEqual(validarNumerador({ rangoDesde: 1, rangoHasta: 100, siguienteNumero: 1 }), {});
  assert.deepEqual(validarNumerador({ rangoDesde: 1, rangoHasta: 100, siguienteNumero: 101 }), {});
  assert.deepEqual(validarNumerador({ rangoDesde: 1, rangoHasta: 100, siguienteNumero: 102 }), { siguienteNumero: 'siguiente' });
  assert.deepEqual(validarNumerador({ rangoDesde: 50, rangoHasta: 10, siguienteNumero: 50 }).rangoHasta, 'rango');
});

test('cuerpo de ITBIS: porcentaje como string; edición conserva el estado', () => {
  const valores = { nombre: ' ITBIS 18% ', porcentaje: '18,00', activo: true };
  assert.deepEqual(construirCuerpo(ITBIS.campos, valores), { nombre: 'ITBIS 18%', porcentaje: '18.00', activo: true });
  assert.equal(validarCampo(ITBIS.campos[1]!, '100.01', v), v.rango.replace('{min}', '0').replace('{max}', '100'));
  assert.equal(validarCampo(ITBIS.campos[1]!, '18.123', v), v.decimal.replace('{n}', '2'));
  assert.equal(validarCampo(ITBIS.campos[0]!, '  ', v), v.obligatorio);
});

test('retención: la base es un enum por nombre y esDefecto un booleano', () => {
  const ini = valoresIniciales(RETENCIONES.campos, { nombre: 'Retención', porcentaje: 30, base: 'Itbis', esDefecto: true, activo: true });
  assert.equal(ini.base, 'Itbis');
  assert.equal(ini.porcentaje, '30');
  assert.deepEqual(construirCuerpo(RETENCIONES.campos, ini), { nombre: 'Retención', porcentaje: '30', base: 'Itbis', esDefecto: true, activo: true });
});

test('serie: moneda libre va como null, entero como número, y con números emitidos se bloquean moneda, prefijo y tipo', () => {
  const nueva = valoresIniciales(SERIES.campos, null);
  assert.equal(nueva.moneda, '');
  const cuerpo = construirCuerpo(SERIES.campos, { ...nueva, numero: '3', prefijo: 'B', tipo: 'Normal', condicionesPago: '' });
  assert.equal(cuerpo.numero, 3);
  assert.equal(cuerpo.moneda, null);
  assert.equal(cuerpo.condicionesPago, null);
  assert.equal(cuerpo.activa, false);

  const sinEmitir = { numero: 1, prefijo: 'A', siguiente: 1 };
  const emitida = { numero: 1, prefijo: 'A', siguiente: 42 };
  assert.equal(SERIES.bloqueo?.(sinEmitir), null);
  assert.deepEqual(SERIES.bloqueo?.(emitida)?.campos, ['moneda', 'prefijo', 'tipo']);
  assert.equal(validarCampo(SERIES.campos[0]!, '1000', v), v.rango.replace('{min}', '1').replace('{max}', '999'));
});

test('numerador: ningún valor fiscal por defecto en el código y fecha en ISO', () => {
  const ini = valoresIniciales(CAMPOS_NUMERADOR, null);
  assert.equal(ini.rangoDesde, '');
  assert.equal(ini.fechaVencimiento, '');
  const cuerpo = construirCuerpo(CAMPOS_NUMERADOR, { tipoPrefijo: 'B01', rangoDesde: '1', rangoHasta: '500', fechaVencimiento: '2026-12-31', siguienteNumero: '1', umbralAlerta: '0' });
  assert.deepEqual(cuerpo, { tipoPrefijo: 'B01', rangoDesde: 1, rangoHasta: 500, fechaVencimiento: '2026-12-31', siguienteNumero: 1, umbralAlerta: 0 });
  assert.equal(validarCampo(CAMPOS_NUMERADOR[3]!, '31/12/2026', v), v.fecha);
});

test('plantilla: tipo e idioma son la clave y al editar conservan el valor original', () => {
  const original = { tipo: 'Recibo', idioma: 'es', asunto: 'Recibo', cuerpo: 'Hola {cliente}', activa: true };
  const cuerpo = construirCuerpo(CAMPOS_PLANTILLA, { tipo: 'Factura', idioma: 'en', asunto: 'Nuevo', cuerpo: 'Texto', activa: false }, original);
  assert.equal(cuerpo.tipo, 'Recibo');
  assert.equal(cuerpo.idioma, 'es');
  assert.equal(cuerpo.asunto, 'Nuevo');
  assert.equal(cuerpo.activa, false);
});

test('ajustes: tolerancias como string o null, enteros como número, mfaGerente booleano', () => {
  const cuerpo = cuerpoAjustes({
    toleranciaCambioUsd: '1,50',
    toleranciaCambioDop: '',
    modoRecordatorios: 'Manual',
    diasPrimerRecordatorio: '3',
    diasEntreRecordatorios: '',
    mfaGerente: true,
  });
  assert.deepEqual(cuerpo, {
    toleranciaCambioUsd: '1.50',
    toleranciaCambioDop: null,
    modoRecordatorios: 'Manual',
    diasPrimerRecordatorio: 3,
    diasEntreRecordatorios: null,
    mfaGerente: true,
  });
});

test('token de agente: solo la lista blanca, validez y límite opcionales', () => {
  assert.deepEqual([...ACCIONES_AGENTE], ['ia.consultar', 'ia.proponer', 'cliente.ver', 'factura.ver', 'informe.ver']);
  const base = { nombre: ' Asistente ', usuarioId: 'u1', acciones: ['ia.consultar', 'cobro.registrar', 'agente.token'], diasValidez: '', limiteConsultasDia: '50' };
  assert.deepEqual(cuerpoToken(base), { nombre: 'Asistente', usuarioId: 'u1', acciones: ['ia.consultar'], limiteConsultasDia: 50 });
  assert.equal(cuerpoToken({ ...base, diasValidez: '30' }).diasValidez, 30);
  assert.throws(() => cuerpoToken({ ...base, acciones: ['cobro.registrar'] }));
  assert.throws(() => cuerpoToken({ ...base, nombre: ' ' }));
  assert.throws(() => cuerpoToken({ ...base, usuarioId: '' }));
  assert.throws(() => cuerpoToken({ ...base, diasValidez: '0' }));
});

test('estado del token: revocado manda sobre caducado', () => {
  const ahora = Date.parse('2026-10-28T12:00:00Z');
  assert.equal(estadoToken({ caduca: '2026-12-01T00:00:00Z', revocado: null }, ahora), 'activo');
  assert.equal(estadoToken({ caduca: '2026-10-01T00:00:00Z', revocado: null }, ahora), 'caducado');
  assert.equal(estadoToken({ caduca: '2026-10-01T00:00:00Z', revocado: '2026-09-01T00:00:00Z' }, ahora), 'revocado');
});
