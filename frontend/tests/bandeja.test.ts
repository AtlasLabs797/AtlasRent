import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ErrorApi } from '../src/api/errores.ts';
import { ACCIONES } from '../src/permisos.ts';
import { menuPermitido, puedeVerRuta } from '../src/navegacion/menu.ts';
import {
  accionDePropuesta,
  cambioDeRenta,
  caducidadDePropuesta,
  cuerpoRechazo,
  esSolicitudCambiada,
  nombreTipoAlerta,
} from '../src/modulos/bandeja/modelo.ts';
import {
  cuerpoMasivo,
  estadoDeEnvio,
  etiquetaMotivoExclusion,
  loteTerminado,
  porcentajeLote,
} from '../src/modulos/envios/modelo.ts';
import { accesosRapidos, comparacionMensual, mesLegible } from '../src/modulos/panel/modelo.ts';

const claves = (p: string[]) => menuPermitido(p).flatMap((g) => g.entradas.map((e) => String(e.clave)));

test('bandeja y envíos se filtran con la acción de su endpoint', () => {
  const empleado = [ACCIONES.sesionVer, ACCIONES.facturaVer];
  assert.ok(!claves(empleado).includes('bandeja'));
  assert.equal(puedeVerRuta(empleado, '/bandeja'), false);
  assert.ok(claves(empleado).includes('envios'));
  assert.ok(claves([ACCIONES.clienteVer]).includes('bandeja'));
});

test('un 409 es solicitud cambiada y el rechazo exige comentario recortado', () => {
  assert.equal(esSolicitudCambiada(new ErrorApi({ status: 409, title: 'x' })), true);
  assert.equal(esSolicitudCambiada(new ErrorApi({ status: 500, title: 'x' })), false);
  assert.deepEqual(cuerpoRechazo('  no  '), { comentario: 'no' });
  assert.throws(() => cuerpoRechazo('   '));
});

test('sin renta vigente el contrato es nuevo; con cambio se enseñan las dos rentas', () => {
  assert.equal(cambioDeRenta({ rentaVigente: null, rentaPropuesta: '900.00', hayCambioDeRenta: true }).nuevo, true);
  const c = cambioDeRenta({ rentaVigente: '800.00', rentaPropuesta: '900.00', hayCambioDeRenta: true });
  assert.deepEqual([c.actual, c.propuesta, c.cambia, c.nuevo], ['800.00', '900.00', true, false]);
});

test('una propuesta caducada da su motivo y una aprobada no', () => {
  assert.equal(caducidadDePropuesta({ resultado: 'Caducada', motivoCaducidad: 'El saldo cambió' }), 'El saldo cambió');
  assert.ok(caducidadDePropuesta({ resultado: 'Caducada', motivoCaducidad: null }));
  assert.equal(caducidadDePropuesta({ resultado: 'Aprobada', motivoCaducidad: null }), null);
  assert.equal(accionDePropuesta('recordatorio_saldo'), 'Recordatorio de saldo');
  assert.equal(accionDePropuesta('otro'), 'otro');
});

test('envíos: Enviando es un estado propio, el lote avanza y el reintento no fuerza', () => {
  assert.equal(estadoDeEnvio('Enviando').etiqueta, 'Enviando');
  assert.equal(estadoDeEnvio('Fallido').tono, 'danger');
  assert.equal(porcentajeLote({ total: 4, enviados: 2, fallidos: 1 }), 75);
  assert.equal(porcentajeLote({ total: 0, enviados: 0, fallidos: 0 }), 0);
  assert.equal(loteTerminado({ pendientes: 0 }), true);
  assert.equal(etiquetaMotivoExclusion('YaEnviado'), 'Ya enviado a este destinatario');
  const c = cuerpoMasivo({ vencidas: true, noEnviadas: false, envioFallido: false, modificadasTrasEnvio: false, moneda: 'USD', forzar: false });
  assert.equal(c.filtro.vencidas, true);
  assert.equal(c.filtro.moneda, 'USD');
  assert.equal(c.forzar, false);
});

test('panel: comparación con el mes anterior y accesos según permisos', () => {
  assert.equal(mesLegible('2026-10'), '10/2026');
  const c = comparacionMensual([{ mes: '2026-09', facturadoUsd: '100.00', cobradoUsd: '50.00' }, { mes: '2026-10', facturadoUsd: '120.00', cobradoUsd: '60.00' }], 'facturadoUsd');
  assert.deepEqual([c.mes, c.actual, c.mesAnterior, c.anterior], ['10/2026', '120.00', '09/2026', '100.00']);
  assert.equal(comparacionMensual([], 'cobradoUsd').actual, null);
  assert.deepEqual(accesosRapidos([ACCIONES.sesionVer]), []);
  assert.deepEqual(accesosRapidos([ACCIONES.facturaVer, ACCIONES.informeVer]).map((a) => a.clave), ['envios', 'informes']);
});

test('alertas: tipos traducidos y un tipo desconocido se enseña tal cual', () => {
  assert.equal(nombreTipoAlerta('tipo_cambio'), 'Tipo de cambio');
  assert.equal(nombreTipoAlerta('nuevo_tipo'), 'nuevo_tipo');
});
