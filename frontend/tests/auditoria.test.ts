import assert from 'node:assert/strict';
import { test } from 'node:test';
import { datosComoTexto, FILTROS_VACIOS, paramsAuditoria } from '../src/modulos/auditoria/modelo.ts';

test('auditoría: solo viajan los filtros rellenos y el usuario solo si es un GUID', () => {
  assert.deepEqual(paramsAuditoria(FILTROS_VACIOS), {});
  const p = paramsAuditoria({ desde: '2026-10-01', hasta: '', usuarioId: 'no-es-guid', entidad: ' factura ', accion: '' });
  assert.deepEqual(p, { desde: '2026-10-01', entidad: 'factura' });
  const g = '11111111-1111-1111-1111-111111111111';
  assert.equal(paramsAuditoria({ ...FILTROS_VACIOS, usuarioId: g }).usuarioId, g);
});

test('auditoría: los datos se formatean como texto y el HTML queda como texto', () => {
  assert.equal(datosComoTexto(null), '');
  assert.equal(datosComoTexto({ a: '<b>x</b>' }), '{\n  "a": "<b>x</b>"\n}');
});
