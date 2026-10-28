import assert from 'node:assert/strict';
import { test } from 'node:test';
import { estadoContrato, resultadoSolicitud } from '../src/modulos/operacion/etiquetas.ts';
import {
  CONTRATO_VACIO,
  accionesDeEstado,
  cuerpoCambio,
  cuerpoContrato,
  cuerpoIncremento,
  cuerpoRevision,
  diasDePreaviso,
  estadoDeIncremento,
  fechaMinimaBaja,
  hayErroresContrato,
  mesesDeRegla,
  pasosDelFlujo,
  puedeFinalizar,
  sumaImportes,
  validarBaja,
  validarCambio,
  validarContrato,
  validarIncremento,
  validarRevision,
  type FormularioContrato,
  type ItemRevision,
} from '../src/modulos/contratos/modelo.ts';

const valido: FormularioContrato = {
  ...CONTRATO_VACIO,
  clienteId: 'c', unidadId: 'u', facturadoraId: 'f', fechaInicio: '2026-11-01', renta: '1250,00', fianza: '2500', plantillaContratoId: 'ALQ-MENSUAL',
};

test('el estado numérico del resumen y el texto de la ficha dan el mismo estado', () => {
  assert.equal(estadoContrato(4), 'Activo');
  assert.equal(estadoContrato('BajaProgramada'), 'BajaProgramada');
  assert.equal(estadoContrato(7), 'Liquidado');
  assert.equal(estadoContrato(99), 'Borrador');
  assert.equal(resultadoSolicitud(2), 'Aprobada');
  assert.equal(resultadoSolicitud('Rechazada'), 'Rechazada');
});

test('el Stepper marca hechos los pasos anteriores, el actual en curso y el resto pendientes', () => {
  const pasos = pasosDelFlujo('Activo', '01/11/2026');
  assert.equal(pasos.length, 7);
  assert.deepEqual(pasos.map((p) => p.status), ['done', 'done', 'done', 'current', 'todo', 'todo', 'todo']);
  assert.equal(pasos[3]!.meta, '01/11/2026');
  assert.equal(pasos[1]!.label, 'Solicitud de aprobación');
});

test('acciones por estado: cada paso del flujo ofrece solo lo que le toca', () => {
  assert.equal(accionesDeEstado('Borrador').enviarAprobacion, true);
  assert.equal(accionesDeEstado('Borrador').editar, true);
  assert.equal(accionesDeEstado('PendienteAprobacion').decidirAprobacion, true);
  assert.equal(accionesDeEstado('Aprobado').activar, true);
  assert.equal(accionesDeEstado('Activo').darBaja, true);
  assert.equal(accionesDeEstado('Activo').cambiar, true);
  assert.equal(accionesDeEstado('BajaProgramada').finalizar, true);
  assert.equal(accionesDeEstado('BajaProgramada').anularBaja, true);
  assert.equal(accionesDeEstado('Finalizado').liquidar, true);
  assert.equal(accionesDeEstado('Liquidado').liquidar, false);
  assert.equal(accionesDeEstado('Activo').editar, false);
});

test('baja en cualquier fecha, pero no anterior a hoy ni al inicio; finalizar solo cuando llegó la fecha', () => {
  assert.equal(fechaMinimaBaja('2026-01-01', '2026-10-28'), '2026-10-28');
  assert.equal(fechaMinimaBaja('2027-01-01', '2026-10-28'), '2027-01-01');
  assert.equal(validarBaja('2026-10-28', '2026-01-01', '2026-10-28'), null); // hoy vale
  assert.equal(validarBaja('2026-10-29', '2026-01-01', '2026-10-28'), null); // mañana vale: sin preaviso mínimo
  assert.ok(validarBaja('2026-10-27', '2026-01-01', '2026-10-28'));
  assert.ok(validarBaja('2026-12-31', '2027-01-01', '2026-10-28'));
  assert.ok(validarBaja('', '2026-01-01', '2026-10-28'));
  assert.equal(diasDePreaviso('2026-11-27', '2026-10-28'), 30);
  assert.equal(puedeFinalizar('2026-10-28', '2026-10-28'), true);
  assert.equal(puedeFinalizar('2026-10-29', '2026-10-28'), false);
  assert.equal(puedeFinalizar(null, '2026-10-28'), false);
});

test('formulario de contrato: cuerpo con importes como texto y validación de la renovación', () => {
  const c = cuerpoContrato(valido);
  assert.equal(c.renta, '1250.00');
  assert.equal(c.fianza, '2500');
  assert.equal(c.diaPago, 1);
  assert.equal(c.reservaId, null);
  assert.equal(hayErroresContrato(validarContrato(valido)), false);
  assert.ok(validarContrato({ ...valido, clienteId: null }).cliente);
  assert.ok(validarContrato({ ...valido, facturadoraId: null }).facturadora);
  assert.ok(validarContrato({ ...valido, renta: '0' }).renta);
  assert.ok(validarContrato({ ...valido, diaPago: '32' }).diaPago);
  assert.ok(validarContrato({ ...valido, reglaRenovacion: 'Semestral', renovacionMeses: '12' }).renovacion);
  assert.equal(validarContrato({ ...valido, reglaRenovacion: 'Personalizada', renovacionMeses: '9' }).renovacion, undefined);
  assert.ok(validarContrato({ ...valido, plantillaContratoId: '' }).plantilla);
  assert.equal(mesesDeRegla('Trimestral', '12'), '3');
  assert.equal(mesesDeRegla('Personalizada', '9'), '9');
});

test('cambio en contrato vigente: el motivo es obligatorio y la moneda no cambia', () => {
  const f = { renta: '1300', diaPago: '5', fianza: '2500', reglaRenovacion: 'Anual' as const, renovacionMeses: '12', plantillaContratoId: 'ALQ', observaciones: '', motivo: '  ' };
  assert.ok(validarCambio(f).motivo);
  assert.equal(validarCambio({ ...f, motivo: 'Actualización anual' }).motivo, undefined);
  const c = cuerpoCambio({ ...f, motivo: ' Actualización ' }, 'DOP');
  assert.equal(c.moneda, 'DOP');
  assert.equal(c.motivo, 'Actualización');
});

test('incremento: porcentaje o importe fijo, nunca los dos, con su fecha', () => {
  assert.deepEqual(cuerpoIncremento({ tipo: 'Porcentaje', valor: '5', fechaVigencia: '2027-01-01', comentario: '' }), {
    tipo: 'Porcentaje', porcentaje: '5', importe: null, fechaVigencia: '2027-01-01', comentario: null,
  });
  const fijo = cuerpoIncremento({ tipo: 'ImporteFijo', valor: '50,5', fechaVigencia: '2027-01-01', comentario: 'x' });
  assert.equal(fijo.porcentaje, null);
  assert.equal(fijo.importe, '50.5');
  assert.ok(validarIncremento({ tipo: 'Porcentaje', valor: '', fechaVigencia: '' , comentario: '' }).valor);
  assert.ok(validarIncremento({ tipo: 'Porcentaje', valor: '5', fechaVigencia: '', comentario: '' }).fecha);
  assert.equal(estadoDeIncremento({ aplicado: true, cancelado: false }), 'Aplicado');
  assert.equal(estadoDeIncremento({ aplicado: false, cancelado: true }), 'Cancelado');
  assert.equal(estadoDeIncremento({ aplicado: false, cancelado: false }), 'Pendiente');
});

test('revisión de salida: se firma entera, ausente y dañado piden nota y el total sale exacto de los importes', () => {
  const items: ItemRevision[] = [
    { id: 'a', name: 'Sofá', qty: 1, state: 'ok', amount: '', nota: '' },
    { id: 'b', name: 'Lámpara', qty: 2, state: 'faltante', amount: '45,10', nota: 'No está' },
    { id: 'c', name: 'Mesa', qty: 1, state: 'danado', amount: '0.20', nota: '' },
  ];
  assert.deepEqual(validarRevision(items), { sinRevisar: 0, sinNota: ['c'] });
  assert.equal(validarRevision([{ ...items[0]!, state: undefined }]).sinRevisar, 1);
  items[2]!.nota = 'Rayada';
  const c = cuerpoRevision(items, '');
  assert.equal(c.lineas.length, 3);
  assert.deepEqual(c.lineas.map((l) => l.estado), ['Presente', 'Ausente', 'Danado']);
  assert.equal(c.lineas[0]!.nota, null);
  assert.equal(c.importePropuesto, '45.30'); // 45,10 + 0,20 sin coma flotante
  assert.equal(cuerpoRevision([items[0]!], 'ok').importePropuesto, null);
  assert.equal(sumaImportes(['0.10', '0.20']), '0.30');
});
