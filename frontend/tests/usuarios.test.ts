import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ACCIONES } from '../src/permisos.ts';
import { menuPermitido, puedeVerRuta } from '../src/navegacion/menu.ts';
import {
  cuerpoAlta,
  cuerpoPermiso,
  errorContrasena,
  gruposDelTecho,
  permisoBloqueado,
  recortadas,
} from '../src/modulos/usuarios/usuarios.ts';
import { cuerpoAprobacion, cuerpoIp, cuerpoSolicitudEmergencia, esGuid, esIpValida } from '../src/modulos/bloqueos/bloqueos.ts';

// Techo de ejemplo con la forma que devuelve la API (`techo` de GET /usuarios/{id}/permisos).
const TECHO_EMPLEADO: string[] = [
  ACCIONES.clienteVer, ACCIONES.clienteEditar, ACCIONES.contratoBorrador, ACCIONES.facturaVer, ACCIONES.facturaBorrador,
  ACCIONES.cotizacionEnviar, ACCIONES.sesionVer, ACCIONES.sesionCerrar, ACCIONES.sesionPassword,
];
const TECHO_GERENTE: string[] = [...TECHO_EMPLEADO, ACCIONES.facturaEmitir, ACCIONES.facturaAnular, ACCIONES.informeVer];
const acciones = (techo: string[]) => gruposDelTecho(techo).flatMap((g) => [...g.acciones]);

test('el editor solo ofrece el techo que manda la API: el empleado no ve acciones de gerente ni de admin', () => {
  const empleado = acciones(TECHO_EMPLEADO);
  assert.ok(empleado.includes(ACCIONES.facturaBorrador));
  assert.ok(!empleado.includes(ACCIONES.facturaEmitir));
  assert.ok(!empleado.includes(ACCIONES.informeVer));
  assert.ok(!empleado.includes(ACCIONES.configuracionEditar));
  for (const a of empleado) assert.ok(TECHO_EMPLEADO.includes(a));
});

test('las acciones de sesión no se ofrecen y un techo vacío no ofrece nada', () => {
  assert.ok(!acciones([...TECHO_EMPLEADO, ACCIONES.configuracionEditar]).includes(ACCIONES.sesionVer));
  assert.deepEqual(gruposDelTecho([]), []);
});

test('a un administrador no se le dejan quitar mfa.obligatorio ni el acceso de emergencia (SEC-025)', () => {
  assert.equal(permisoBloqueado('admin', ACCIONES.mfaObligatorio), true);
  assert.equal(permisoBloqueado('admin', ACCIONES.seguridadAccesoEmergencia), true);
  assert.equal(permisoBloqueado('admin', ACCIONES.facturaAnular), false);
  assert.equal(permisoBloqueado('gerente', ACCIONES.facturaAnular), false);
});

test('cuántos permisos tiene recortados un usuario respecto a su techo', () => {
  const todos = acciones(TECHO_GERENTE);
  assert.equal(recortadas(TECHO_GERENTE, todos), 0);
  assert.equal(recortadas(TECHO_GERENTE, todos.filter((a) => a !== ACCIONES.facturaAnular && a !== ACCIONES.informeVer)), 2);
  assert.deepEqual(cuerpoPermiso('factura.anular', false), { accion: 'factura.anular', permitido: false });
});

test('contraseña: de 12 a 72 caracteres', () => {
  assert.equal(errorContrasena('a'.repeat(11)), 'corta');
  assert.equal(errorContrasena('a'.repeat(12)), null);
  assert.equal(errorContrasena('a'.repeat(72)), null);
  assert.equal(errorContrasena('a'.repeat(73)), 'larga');
});

test('el cuerpo del alta recorta textos, omite el email vacío y no toca la contraseña', () => {
  const contrasena = '  doce caracteres  ';
  assert.deepEqual(cuerpoAlta({ nombreAcceso: ' lrosario ', nombre: ' Luis Rosario ', email: ' ', contrasena, rol: 'empleado' }), {
    nombreAcceso: 'lrosario',
    nombre: 'Luis Rosario',
    contrasena,
    rol: 'empleado',
  });
  assert.equal(cuerpoAlta({ nombreAcceso: 'a', nombre: 'A', email: 'a@b.do', contrasena, rol: 'gerente' }).email, 'a@b.do');
  assert.throws(() => cuerpoAlta({ nombreAcceso: 'a', nombre: 'A', email: 'sin-arroba', contrasena, rol: 'gerente' }));
  assert.throws(() => cuerpoAlta({ nombreAcceso: 'a', nombre: 'A', email: '', contrasena, rol: 'superadmin' }));
  assert.throws(() => cuerpoAlta({ nombreAcceso: '', nombre: 'A', email: '', contrasena, rol: 'admin' }));
});

test('usuarios solo se ve con configuracion.editar; gerente y empleado no tienen la entrada ni la ruta', () => {
  const gerente = [ACCIONES.sesionVer, ACCIONES.facturaVer, ACCIONES.informeVer];
  const claves = (p: string[]) => menuPermitido(p).flatMap((g) => g.entradas.map((e) => String(e.clave)));
  for (const k of ['usuarios', 'configuracion', 'bloqueos']) assert.ok(!claves(gerente).includes(k));
  assert.equal(puedeVerRuta(gerente, '/usuarios'), false);
  assert.equal(puedeVerRuta(gerente, '/configuracion'), false);
  assert.equal(puedeVerRuta(gerente, '/bloqueos'), false);
  const admin = Object.values(ACCIONES);
  assert.ok(claves(admin).includes('usuarios') && claves(admin).includes('configuracion') && claves(admin).includes('bloqueos'));
});

test('IP: acepta IPv4 e IPv6 razonables y rechaza basura', () => {
  assert.equal(esIpValida('203.0.113.25'), true);
  assert.equal(esIpValida(' 10.0.0.1 '), true);
  assert.equal(esIpValida('2001:db8::1'), true);
  assert.equal(esIpValida('256.1.1.1'), false);
  assert.equal(esIpValida('1.2.3'), false);
  assert.equal(esIpValida('hola'), false);
  assert.equal(esIpValida('1::2::3'), false);
  assert.deepEqual(cuerpoIp(' 10.0.0.1 '), { ip: '10.0.0.1' });
});

test('acceso de emergencia: motivo de 10 a 500 y solicitud con formato de guid', () => {
  const id = '22222222-2222-2222-2222-222222222222';
  assert.equal(esGuid(id), true);
  assert.equal(esGuid('22222222'), false);
  assert.deepEqual(cuerpoSolicitudEmergencia(id, '  perdió el móvil y los códigos '), { usuarioObjetivoId: id, motivo: 'perdió el móvil y los códigos' });
  assert.throws(() => cuerpoSolicitudEmergencia(id, 'corto'));
  assert.throws(() => cuerpoSolicitudEmergencia(id, 'x'.repeat(501)));
  assert.throws(() => cuerpoSolicitudEmergencia('nada', 'motivo suficiente largo'));
  assert.deepEqual(cuerpoAprobacion(id), { solicitudId: id });
  assert.throws(() => cuerpoAprobacion('nada'));
});
