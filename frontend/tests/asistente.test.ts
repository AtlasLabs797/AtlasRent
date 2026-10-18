import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ACCIONES } from '../src/permisos.ts';
import { entradasNavegables, menuPermitido, puedeVerRuta } from '../src/navegacion/menu.ts';
import { ALCANCES, avisoDeError, cuerpoConsulta } from '../src/ia/consulta.ts';

const modulos = (permisos: string[]): string[] =>
  menuPermitido(permisos).flatMap((grupo) => grupo.entradas.map((e) => String(e.clave)));

test('el asistente solo se ve con ia.consultar y la ruta se filtra con la misma acción', () => {
  assert.equal(ACCIONES.iaConsultar, 'ia.consultar');

  const conPermiso = [ACCIONES.sesionVer, ACCIONES.iaConsultar];
  assert.ok(modulos(conPermiso).includes('asistente'), 'con ia.consultar el módulo se muestra');

  // Sin la acción desaparece del menú y la ruta directa da "sin permiso", no un 403.
  assert.ok(!modulos([ACCIONES.sesionVer]).includes('asistente'));
  assert.equal(puedeVerRuta([ACCIONES.sesionVer], '/asistente'), false);
  assert.equal(puedeVerRuta(conPermiso, '/asistente'), true);

  const entrada = menuPermitido(conPermiso)
    .flatMap((grupo) => grupo.entradas)
    .find((e) => e.clave === 'asistente');
  assert.equal(entrada?.ruta, '/asistente');
  assert.ok(entradasNavegables({ etiqueta: 'Herramientas', entradas: [entrada!] }).length === 1);
});

test('la entrada de menú del asistente usa una etiqueta y un icono del catálogo', () => {
  const entrada = menuPermitido([ACCIONES.iaConsultar])
    .flatMap((grupo) => grupo.entradas)
    .find((e) => e.clave === 'asistente');
  assert.equal(entrada?.etiqueta, 'Asistente');
  assert.equal(entrada?.icono, 'sparkles');
});

test('el cuerpo de la petición recorta la pregunta y solo deja los alcances del contrato', () => {
  assert.deepEqual(cuerpoConsulta('  ¿Cuántos contratos vencen este mes?  ', 'contratos'), {
    pregunta: '¿Cuántos contratos vencen este mes?',
    alcance: 'contratos',
  });

  for (const alcance of ALCANCES) {
    assert.equal(cuerpoConsulta('test', alcance).alcance, alcance);
  }
  assert.deepEqual([...ALCANCES], ['clientes', 'cuentas', 'facturas', 'contratos', 'informes']);
});

test('una pregunta vacía o un alcance que no existe no llegan a la API', () => {
  assert.throws(() => cuerpoConsulta('', 'clientes'));
  assert.throws(() => cuerpoConsulta('   \n  ', 'clientes'));
  assert.throws(() => cuerpoConsulta('test', 'unidades'));
  assert.throws(() => cuerpoConsulta('test', 'CONTRATOS'), 'los alcances van en minúsculas exactas');
});

test('401 y 403 avisan sin reintento: la sesión hay que renovarla y el permiso hay que pedirlo', () => {
  const caducada = avisoDeError({ response: { status: 401, data: {} } });
  const sinPermiso = avisoDeError({ response: { status: 403, data: {} } });

  assert.equal(caducada.reintentable, false);
  assert.equal(sinPermiso.reintentable, false);
  assert.ok(caducada.titulo.length > 0 && caducada.mensaje.length > 0);
  assert.ok(sinPermiso.titulo.length > 0 && sinPermiso.mensaje.length > 0);
});

test('429 y 503 sí se reintentan: las dos se acaban solas', () => {
  assert.equal(avisoDeError({ response: { status: 429, data: {} } }).reintentable, true);
  assert.equal(avisoDeError({ response: { status: 503, data: {} } }).reintentable, true);
});

test('el texto que manda la API es el que se enseña, con el título de la pantalla', () => {
  const aviso = avisoDeError({
    response: {
      status: 429,
      data: { title: 'Demasiadas consultas', detail: 'Se alcanzó el límite del día. Vuelve mañana.' },
    },
  });

  assert.equal(aviso.mensaje, 'Se alcanzó el límite del día. Vuelve mañana.');
  assert.equal(aviso.titulo, 'Se ha alcanzado el límite de consultas');
});

test('un fallo sin respuesta de la API es un aviso genérico reintentable', () => {
  const sinRed = avisoDeError(new Error('Network Error'));
  const cuerpoVacio = avisoDeError({ response: { status: 500, data: null } });

  assert.equal(sinRed.tono, 'danger');
  assert.equal(sinRed.reintentable, true);
  assert.ok(sinRed.mensaje.length > 0);
  assert.equal(cuerpoVacio.reintentable, true);
});