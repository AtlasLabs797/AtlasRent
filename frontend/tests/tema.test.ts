import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { CLAVE_TEMA, leerPreferencia, resolverTema } from '../src/tema/resolver.ts';

test('una preferencia explícita manda sobre el sistema', () => {
  assert.equal(resolverTema('claro', true), 'light');
  assert.equal(resolverTema('claro', false), 'light');
  assert.equal(resolverTema('oscuro', false), 'dark');
  assert.equal(resolverTema('oscuro', true), 'dark');
});

test('"sistema" sigue a matchMedia', () => {
  assert.equal(resolverTema('sistema', true), 'dark');
  assert.equal(resolverTema('sistema', false), 'light');
});

test('una preferencia corrupta o ausente cae en sistema', () => {
  const almacen = { getItem: () => 'azul' };
  assert.equal(leerPreferencia(almacen), 'sistema');
  assert.equal(leerPreferencia({ getItem: () => null }), 'sistema');
  assert.equal(leerPreferencia({ getItem: () => 'oscuro' }), 'oscuro');
});

test('el script en línea de index.html comparte clave y valores con el store', () => {
  const html = readFileSync(new URL('../index.html', import.meta.url), 'utf8');
  assert.ok(html.includes(CLAVE_TEMA), 'el script en línea debe leer la misma clave que resolver.ts');
  assert.ok(html.includes("'oscuro'"), 'debe reconocer la preferencia oscura');
  assert.ok(html.includes("'sistema'"), 'debe reconocer la preferencia sistema');
  assert.ok(html.includes("matchMedia('(prefers-color-scheme: dark)')"), 'debe resolver sistema con matchMedia');
  assert.ok(html.includes('data-theme'), 'debe fijar data-theme antes del primer pintado');
  assert.ok(html.indexOf('data-theme') < html.indexOf('/src/main.tsx'), 'el tema se fija antes de cargar la app');
});