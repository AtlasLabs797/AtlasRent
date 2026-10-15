import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
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

test('el menú no enlaza a rutas que no existen', () => {
  const app = leer('src/App.tsx');
  const rutasMontadas = new Set<string>(['/']);
  for (const coincidencia of app.matchAll(/<Route (?:index|path)="([^"]*)"/g)) {
    const valor = coincidencia[1];
    // '*' es la ruta comodín de página no encontrada: no es una pantalla del menú.
    if (valor.includes('*')) continue;
    rutasMontadas.add(valor === '' ? '/' : `/${valor}`);
  }
  assert.deepEqual([...rutasMontadas].sort(), ['/', '/componentes']);

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

test('un módulo sin pantalla no aparece en la barra lateral', () => {
  const conPantalla = enrutables(ADMIN);
  for (const clave of conPantalla) {
    assert.ok(
      ENTRADAS_MENU.find((entrada) => entrada.clave === clave)?.ruta !== null,
      `${clave} no tiene pantalla y no debe ser pulsable`,
    );
  }
  assert.ok(!conPantalla.includes('informes'), 'Informes todavía no tiene pantalla');
  assert.deepEqual(conPantalla, ['panel', 'componentes']);
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