import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';

const raiz = new URL('../', import.meta.url);
const leer = (ruta: string) => readFileSync(new URL(ruta, raiz), 'utf8');

test('el CSS del design system se importa una sola vez, en main.tsx', () => {
  const main = leer('src/main.tsx');
  const apariciones = main.match(/design-system\/styles\.css/g) ?? [];
  assert.equal(apariciones.length, 1, 'styles.css debe importarse exactamente una vez');
});

test('las fuentes vienen de @fontsource, sin Google Fonts', () => {
  const main = leer('src/main.tsx');
  assert.ok(main.includes('@fontsource/geist-sans/400.css'));
  assert.ok(main.includes('@fontsource/geist-mono/400.css'));

  const fuentes = leer('src/design-system/tokens/fonts.css');
  assert.ok(!fuentes.includes('http'), 'tokens/fonts.css no debe pedir nada por red');
  assert.ok(!leer('src/design-system/styles.css').includes('fonts.googleapis.com'));
});

test('ningún archivo fuente pide recursos de red en tiempo de ejecución', () => {
  const rutas = [
    'src/main.tsx',
    'src/App.tsx',
    'src/api/cliente.ts',
    'src/design-system/components/core/Icon.tsx',
    'src/design-system/styles.css',
  ];
  for (const ruta of rutas) {
    const contenido = leer(ruta);
    assert.ok(!/https?:\/\/(?!localhost)/.test(contenido), `${ruta} no debe apuntar a un CDN`);
  }
});

test('las páginas del design system usan las clases ar- del DS', () => {
  const pagina = leer('src/paginas/DesignSystem.tsx');
  for (const componente of ['Button', 'Badge', 'Card', 'Input', 'Alert', 'StatCard', 'DataTable', 'UnitCard']) {
    assert.ok(pagina.includes(`<${componente}`), `falta el componente ${componente} en la muestra`);
  }
  assert.ok(pagina.includes('ar-overline'), 'falta una clase del design system');
});