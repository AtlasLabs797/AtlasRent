import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  cuerpoCliente,
  cuerpoContacto,
  cuerpoProspecto,
  emailPlausible,
  enlacesDeCliente,
  numeroValido,
  paramsListaClientes,
  peticionBusquedaDocumento,
  valoresCliente,
  valoresClienteDesdeProspecto,
} from '../src/modulos/clientes/logica.ts';
import { menuPermitido } from '../src/navegacion/menu.ts';
import { ACCIONES } from '../src/permisos.ts';

const v = { ...valoresCliente(), nombre: '  Ana Pérez  ', documento: ' 001-0000000-1 ', email: '', telefono: '809-555-0100' };

test('cuerpoCliente recorta, convierte vacíos en null y no manda número a quien no puede fijarlo', () => {
  const c = cuerpoCliente({ ...v, numero: '77' }, { alta: true, puedeFijarNumero: false });
  assert.equal(c.nombre, 'Ana Pérez');
  assert.equal(c.documento, '001-0000000-1');
  assert.equal(c.email, null);
  assert.equal(c.direccion, null);
  assert.ok(!('numero' in c));
});

test('el número solo viaja en el alta de quien puede fijarlo (configuracion.editar)', () => {
  assert.equal(cuerpoCliente({ ...v, numero: '77' }, { alta: true, puedeFijarNumero: true }).numero, 77);
  assert.ok(!('numero' in cuerpoCliente({ ...v, numero: '77' }, { alta: false, puedeFijarNumero: true })));
  assert.ok(!('numero' in cuerpoCliente({ ...v, numero: '' }, { alta: true, puedeFijarNumero: true })));
});

test('numeroValido sigue [Range(1, 99999999)]', () => {
  for (const ok of ['1', '99999999', ' 12 ']) assert.ok(numeroValido(ok), ok);
  for (const mal of ['0', '100000000', '-1', '1.5', 'abc', '']) assert.ok(!numeroValido(mal), mal);
});

test('la búsqueda por documento lleva el documento en el cuerpo y nunca en la ruta', () => {
  const { ruta, cuerpo } = peticionBusquedaDocumento(' 001-0000000-1 ', { incluirInactivos: false, page: 1, pageSize: 25 });
  assert.equal(ruta, '/clientes/busqueda');
  assert.ok(!ruta.includes('001'));
  assert.equal(cuerpo.documento, '001-0000000-1');
  assert.ok(!('tipo' in cuerpo));
});

test('la lista GET nunca incluye un parámetro de documento y omite filtros vacíos', () => {
  const p = paramsListaClientes({ q: ' ana ', tipo: '', incluirInactivos: false, page: 2, pageSize: 25 });
  assert.deepEqual(p, { page: 2, pageSize: 25, q: 'ana' });
  assert.ok(!('documento' in p));
  assert.deepEqual(paramsListaClientes({ q: '', tipo: 'Empresa', incluirInactivos: true, page: 1, pageSize: 25 }), {
    page: 1,
    pageSize: 25,
    tipo: 'Empresa',
    incluirInactivos: true,
  });
});

test('contactos y prospectos: vacíos a null y nombre recortado', () => {
  const c = cuerpoContacto({ nombre: ' Luis ', relacion: '', email: 'a@b.co', telefono: ' ', esPrincipal: true });
  assert.deepEqual(c, { nombre: 'Luis', relacion: null, email: 'a@b.co', telefono: null, esPrincipal: true });
  const p = cuerpoProspecto({ nombre: ' X ', tipo: 'Empresa', documento: '', email: '', telefono: '', notas: ' hola ' });
  assert.deepEqual(p, { nombre: 'X', tipo: 'Empresa', documento: null, email: null, telefono: null, notas: 'hola' });
});

test('emailPlausible acepta vacío y rechaza lo que no tiene forma de email', () => {
  assert.ok(emailPlausible(''));
  assert.ok(emailPlausible('a@b.co'));
  assert.ok(!emailPlausible('a@b'));
  assert.ok(!emailPlausible('a b@c.co'));
});

test('convertir un prospecto copia solo sus datos compartidos y deja las marcas y el número en blanco', () => {
  const val = valoresClienteDesdeProspecto({
    id: 'p', nombre: 'Pro', tipo: 'Empresa', documento: '1-01-00000-1', email: null, telefono: '809', notas: null,
    activo: true, createdAt: '2026-10-01T00:00:00Z',
  });
  assert.equal(val.nombre, 'Pro');
  assert.equal(val.tipo, 'Empresa');
  assert.equal(val.email, '');
  assert.equal(val.numero, '');
  assert.equal(val.agenteRetencion, false);
});

test('los enlaces de la ficha usan rutas relativas con clienteId y solo los módulos disponibles', () => {
  const todos = enlacesDeCliente('abc 1', () => true);
  assert.deepEqual(todos.map((e) => e.destino), ['/contratos?clienteId=abc%201', '/reservas?clienteId=abc%201', '/cuentas?clienteId=abc%201']);
  assert.deepEqual(enlacesDeCliente('x', (b) => b === '/contratos').map((e) => e.clave), ['contratos']);
  assert.deepEqual(enlacesDeCliente('x', () => false), []);
});

test('el menú: clientes y unidades salen con cliente.ver, que el empleado tiene', () => {
  const empleado = [ACCIONES.clienteVer, ACCIONES.clienteEditar];
  const claves = menuPermitido(empleado).flatMap((g) => g.entradas.filter((e) => e.ruta !== null).map((e) => String(e.clave)));
  assert.ok(claves.includes('clientes'));
  assert.ok(claves.includes('unidades'));
});
