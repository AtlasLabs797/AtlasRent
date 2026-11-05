import assert from 'node:assert/strict';
import { before, test } from 'node:test';
import { CLAVE_TEMA } from '../src/tema/resolver.ts';
import type { useTema as TipoUseTema } from '../src/tema/store.ts';

/**
 * El store de tema toca `window`, `localStorage` y `document`, que el runner de Node no tiene. Se
 * monta un DOM mínimo antes de importarlo (import dinámico): lo que se prueba es el comportamiento
 * del store, no el navegador. El DOM se monta una sola vez porque el módulo se cachea y su
 * `matchMedia` es el del primer `import`.
 */
type Oyente = () => void;

interface Dom {
  atributos: () => string | undefined;
  cambiaSistema: (oscuro: boolean) => void;
  valorGuardado: () => string | undefined;
}

let dom: Dom;
let useTema: typeof TipoUseTema;

function montarDom(oscuridadInicial: boolean) {
  const almacen = new Map<string, string>();
  const oyentes: Oyente[] = [];
  let oscuridad = oscuridadInicial;
  const atributos: Record<string, string> = {};

  const globals = globalThis as unknown as Record<string, unknown>;
  globals.localStorage = {
    getItem: (clave: string) => almacen.get(clave) ?? null,
    setItem: (clave: string, valor: string) => {
      almacen.set(clave, valor);
    },
  };
  globals.window = {
    matchMedia: () => ({
      get matches() {
        return oscuridad;
      },
      addEventListener: (_evento: string, oyente: Oyente) => oyentes.push(oyente),
    }),
  };
  globals.document = {
    documentElement: {
      setAttribute: (clave: string, valor: string) => {
        atributos[clave] = valor;
      },
    },
  };

  return {
    atributos: () => atributos['data-theme'],
    cambiaSistema: (oscuro: boolean) => {
      oscuridad = oscuro;
      for (const oyente of oyentes) oyente();
    },
    valorGuardado: () => almacen.get(CLAVE_TEMA),
  };
}

before(async () => {
  dom = montarDom(false);
  ({ useTema } = await import('../src/tema/store.ts'));
});

test('la preferencia se guarda, se recupera y se aplica en el documento', () => {
  // Sin preferencia guardada, la app arranca en "sistema" y con el sistema en claro.
  assert.equal(useTema.getState().preferencia, 'sistema');
  assert.equal(useTema.getState().tema, 'light');

  useTema.getState().setPreferencia('oscuro');
  assert.equal(dom.valorGuardado(), 'oscuro', 'la preferencia queda guardada en el navegador');
  assert.equal(useTema.getState().tema, 'dark');
  assert.equal(dom.atributos(), 'dark', 'data-theme se fija en el documento');

  useTema.getState().setPreferencia('claro');
  assert.equal(useTema.getState().tema, 'light');
  assert.equal(dom.atributos(), 'light');
});

test('con "sistema" la app sigue al sistema, y con una preferencia explícita no', () => {
  useTema.getState().setPreferencia('sistema');

  dom.cambiaSistema(true);
  assert.equal(useTema.getState().tema, 'dark', 'el sistema pasó a oscuro');
  assert.equal(dom.atributos(), 'dark');

  dom.cambiaSistema(false);
  assert.equal(useTema.getState().tema, 'light');

  // Con una preferencia explícita, lo que diga el sistema da igual.
  useTema.getState().setPreferencia('oscuro');
  dom.cambiaSistema(false);
  assert.equal(useTema.getState().tema, 'dark');
  assert.equal(dom.atributos(), 'dark');
});