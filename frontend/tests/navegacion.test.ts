import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { MODULOS } from '../src/modulos/lista.ts';
import { ACCIONES } from '../src/permisos.ts';
import {
  ENTRADAS_MENU,
  GRUPOS_MENU,
  accionDeRuta,
  entradasNavegables,
  menuPermitido,
  nombreDelRol,
  puedeVerRuta,
} from '../src/navegacion/menu.ts';

const raiz = new URL('../', import.meta.url);
const leer = (ruta: string) => readFileSync(new URL(ruta, raiz), 'utf8');

/** Los techos de rol de `MatrizPermisos` (Acciones.cs): son los que devuelve `/auth/yo`. */
const EMPLEADO = [
  ACCIONES.clienteVer,
  ACCIONES.clienteEditar,
  ACCIONES.contratoBorrador,
  ACCIONES.facturaVer,
  ACCIONES.facturaBorrador,
  ACCIONES.cotizacionEnviar,
  ACCIONES.sesionVer,
  ACCIONES.sesionCerrar,
];

const ADMIN = [...Object.values(ACCIONES)];

const modulos = (permisos: string[]): string[] =>
  menuPermitido(permisos).flatMap((grupo) => grupo.entradas.map((e) => String(e.clave)));
const enrutables = (permisos: string[]): string[] =>
  menuPermitido(permisos).flatMap((grupo) => entradasNavegables(grupo).map((e) => String(e.clave)));

test('el menú no enlaza a rutas que no existen: las rutas salen del registro de módulos y App.tsx las monta', () => {
  const rutasMontadas = new Set<string>();
  for (const modulo of MODULOS) {
    for (const ruta of modulo.rutas) rutasMontadas.add(ruta.path === '' ? '/' : `/${ruta.path}`);
  }
  assert.deepEqual([...rutasMontadas].sort(), ['/', '/asistente', '/clientes', '/clientes/:id', '/clientes/prospectos', '/componentes', '/cuenta', '/informes', '/unidades', '/unidades/:id']);

  // App.tsx no declara rutas de módulo a mano: las monta del registro (si no, el menú y el
  // enrutador podrían divergir).
  const app = leer('src/App.tsx');
  assert.ok(app.includes('MODULOS'), 'App.tsx monta las rutas desde MODULOS');
  assert.ok(!/<Route (?:index|path)="[a-z]/.test(app), 'App.tsx no declara rutas de módulo a mano');

  for (const entrada of ENTRADAS_MENU) {
    if (entrada.ruta === null) {
      assert.ok(entrada.pendiente, `${entrada.clave} sin ruta debe marcar qué pantalla falta`);
      continue;
    }
    assert.ok(
      rutasMontadas.has(entrada.ruta),
      `${entrada.clave} apunta a ${entrada.ruta}, que el enrutador no monta`,
    );
  }
});

test('el registro de módulos es coherente: claves y rutas únicas, rutas hijas bajo la ruta base', () => {
  assert.equal(new Set(MODULOS.map((m) => m.clave)).size, MODULOS.length, 'claves únicas');
  assert.equal(new Set(MODULOS.map((m) => m.ruta)).size, MODULOS.length, 'rutas base únicas');
  for (const modulo of MODULOS) {
    assert.ok(modulo.rutas.length > 0, `${modulo.clave} sin rutas`);
    for (const ruta of modulo.rutas) {
      const absoluta = ruta.path === '' ? '/' : `/${ruta.path}`;
      assert.ok(
        absoluta === modulo.ruta || absoluta.startsWith(modulo.ruta === '/' ? '/' : `${modulo.ruta}/`),
        `${modulo.clave}: ${absoluta} no cuelga de ${modulo.ruta}`,
      );
    }
  }
  // Todo módulo con grupo declarado sale en el menú; uno sin grupo y fuera del esqueleto no.
  const enMenu = new Set(ENTRADAS_MENU.map((e) => e.clave as string));
  for (const modulo of MODULOS.filter((m) => m.grupo !== undefined)) {
    assert.ok(enMenu.has(modulo.clave), `${modulo.clave} declara grupo pero no sale en el menú`);
  }
  assert.ok(!enMenu.has('cuenta'), '"Mi cuenta" no sale en el menú lateral');
});

test('una ruta hija hereda el permiso del módulo y "Mi cuenta" lo tiene todo el mundo con sesión', () => {
  assert.equal(accionDeRuta('/informes/mensual'), ACCIONES.informeVer);
  assert.equal(puedeVerRuta(EMPLEADO, '/informes/mensual'), false);
  assert.equal(accionDeRuta('/informesfalsos'), null, 'el prefijo exige el separador');
  // sesion.password está en el techo de los tres roles (MatrizPermisos).
  assert.equal(accionDeRuta('/cuenta'), ACCIONES.sesionPassword);
  assert.equal(puedeVerRuta([ACCIONES.sesionPassword], '/cuenta'), true);
  assert.equal(puedeVerRuta([], '/cuenta'), false);
});

test('un módulo sin pantalla no aparece en la barra lateral', () => {
  const conPantalla = enrutables(ADMIN);
  for (const clave of conPantalla) {
    assert.ok(
      ENTRADAS_MENU.find((entrada) => entrada.clave === clave)?.ruta !== null,
      `${clave} no tiene pantalla y no debe ser pulsable`,
    );
  }
  assert.deepEqual(conPantalla, ['panel', 'clientes', 'unidades', 'informes', 'asistente', 'componentes']);
});

test('los permisos de la API filtran el menú: el empleado no ve Informes y el admin sí', () => {
  assert.ok(!modulos(EMPLEADO).includes('informes'), 'el empleado no tiene informe.ver');
  assert.ok(modulos(ADMIN).includes('informes'), 'el administrador sí tiene informe.ver');

  for (const modulo of ['configuracion', 'usuarios', 'auditoria', 'bloqueos', 'cobros', 'fianzas']) {
    assert.ok(!modulos(EMPLEADO).includes(modulo), `el empleado no debería ver ${modulo}`);
  }

  // El panel lo tiene todo el mundo con sesión.
  assert.ok(modulos(EMPLEADO).includes('panel'));
  assert.ok(modulos(ADMIN).includes('panel'));
});

test('cada módulo se filtra por la acción que exige el backend', () => {
  const soloInforme = [ACCIONES.sesionVer, ACCIONES.informeVer];
  assert.deepEqual(modulos(soloInforme), ['panel', 'informes']);

  // Quitar un permiso por usuario esconde su módulo aunque el rol lo trajera.
  const sinConfiguracion = ADMIN.filter((accion) => accion !== ACCIONES.configuracionEditar);
  const resultado = modulos(sinConfiguracion);
  assert.ok(!resultado.includes('configuracion'));
  assert.ok(!resultado.includes('usuarios'));
  assert.ok(!resultado.includes('componentes'));
  assert.ok(resultado.includes('informes'));
  assert.ok(enrutables(ADMIN).includes('componentes'));
  assert.ok(!enrutables(sinConfiguracion).includes('componentes'));
  assert.ok(enrutables(sinConfiguracion).includes('panel'));
});

test('una ruta prohibida da sin permiso y una ruta desconocida no se filtra', () => {
  assert.equal(puedeVerRuta(EMPLEADO, '/componentes'), false, 'configuracion.editar falta al empleado');
  assert.equal(puedeVerRuta(ADMIN, '/componentes'), true);

  // Informes pide informe.ver: el empleado no lo tiene y la ruta está guardada, no solo oculta.
  assert.equal(accionDeRuta('/informes'), ACCIONES.informeVer);
  assert.equal(puedeVerRuta(EMPLEADO, '/informes'), false);
  assert.equal(puedeVerRuta(ADMIN, '/informes'), true);

  // Una ruta que no pertenece a ningún módulo no es "sin permiso": no existe.
  assert.equal(puedeVerRuta(EMPLEADO, '/no-existe'), true);
  assert.equal(accionDeRuta('/no-existe'), null);
});

test('un menú sin permisos no muestra grupos vacíos', () => {
  assert.deepEqual(menuPermitido([]), []);
  assert.deepEqual(menuPermitido(['otra.cosa']), []);
});

test('el rol del backend se muestra con su nombre y no inventado', () => {
  assert.equal(nombreDelRol('admin'), 'Administrador');
  assert.equal(nombreDelRol('gerente'), 'Gerente');
  assert.equal(nombreDelRol('empleado'), 'Empleado');
  assert.equal(nombreDelRol('inventado'), 'inventado');
});

test('cada entrada tiene clave, icono y etiqueta del texto centralizado', () => {
  for (const entrada of ENTRADAS_MENU) {
    assert.ok(entrada.etiqueta.length > 0);
    assert.ok(entrada.icono.length > 0);
  }
  assert.equal(new Set(ENTRADAS_MENU.map((e) => e.clave)).size, ENTRADAS_MENU.length, 'las claves son únicas');
  assert.ok(GRUPOS_MENU.every((grupo) => grupo.entradas.length > 0));
});