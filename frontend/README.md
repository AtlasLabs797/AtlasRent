# Atlas Rent — frontend

SPA de Atlas Rent (React 19 + TypeScript + Vite). Ver `docs/DOC_TECNICA.md` §2 y §3.1 y `docs/DESIGN.md` §1.

## Scripts

| Script | Qué hace |
|---|---|
| `npm run dev` | Servidor de desarrollo de Vite |
| `npm run lint` | ESLint 10 (configuración plana) |
| `npm run typecheck` | `tsc --noEmit` |
| `npm test` | Node.js Test Runner sobre `tests/**/*.test.ts` |
| `npm run build` | `vite build` → `dist/`. Para que llegue a la API hay que ejecutar después `dotnet publish backend/AtlasRent.Api`: el target `AtlasRentPublicarFrontend` copia `dist/` a `wwwroot/` en el paquete |
| `npm run preview` | Sirve `dist/` en local |
| `npm run e2e` | Playwright (solo Chromium) con la API simulada: hace `npm run build`, sirve `dist/` con `vite preview` en el puerto 4173 y lo cierra al terminar. Capturas claro/oscuro en `e2e/capturas/` (ignoradas por git). Primera vez: `npx playwright install chromium` |

## Estructura

```
src/
  main.tsx                 raíz: fuentes, CSS del design system, QueryClient, router
  App.tsx                  tres estados de sesión + rutas de los módulos registrados (React.lazy + Suspense)
  api/                     cliente.ts (Axios, CSRF, refresco 401 single-flight, SEG-010) · auth.ts · errores.ts (ProblemDetails -> ErrorApi) · formularios.ts (errores por campo en React Hook Form)
  sesion/store.ts          Zustand: estado de sesión e identidad
  dominio/                 dinero.ts (importes string, sin coma flotante) · fechas.ts (dd/mm/aaaa sin desfase de zona)
  modulos/                 un módulo por carpeta + registro.ts (ver "Cómo añadir un módulo")
  navegacion/menu.ts       orden del menú y módulos pendientes; ruta y acción salen del registro
  componentes/             Layout, PantallaCentrada, ErrorAlerta, TablaVirtual, ConfirmarDestructivo, AltaMfa, FormularioPassword
  paginas/                 Acceso · CambioPasswordForzado · Inicio · Informes · DesignSystem (+ muestras) · SinPermiso · NoEncontrada
  i18n/textos.ts           textos comunes; los de cada módulo, en `modulos/<m>/textos.ts`
  tema/                    lógica pura y store del tema claro/oscuro
  design-system/           copia de `Atlas Rent Design System/` (tokens + todos los componentes en .tsx)
e2e/                       Playwright: apiFalsa.ts (page.route sobre /api/v1/**), prueba.ts (fixture), fixtures.ts, capturas.ts, *.spec.ts
```

## Cómo añadir un módulo

Cada módulo de pantallas vive en `src/modulos/<modulo>/` y se registra con una línea; no se toca `App.tsx` ni `menu.ts`. Referencia real: `src/modulos/asistente/`.

1. `rutas.ts` (TypeScript sin JSX, para que lo cargue también el runner de Node): exporta `modulo: DefinicionModulo` con `clave`, `accion` (la MISMA que exige la API), `ruta` base y `rutas: [{ path, cargar: () => import('./paginas/X') }]`. Las rutas hijas (`clientes/:id`) heredan el permiso.
2. `api.ts`: hooks de TanStack Query sobre `clienteHttp` (`useQuery` para leer, `useMutation` para escribir). Los importes viajan como string decimal; nada de aritmética de dinero.
3. `textos.ts`: los textos del módulo (`export const textosX = {...}`). La etiqueta y el icono del menú van en `i18n/textos.ts` (`navegacion.modulos`).
4. `paginas/*.tsx`: export default; `PageHeader` + componentes del DS. Fechas con `formatearFecha`, importes con `<Money amount="1250.00" />` o `formatearImporte`.
5. Registro: añade `export { modulo as clientes } from './clientes/rutas.ts';` en `src/modulos/registro.ts`. Si la clave ya estaba en `PENDIENTES` de `navegacion/menu.ts` deja de ser pendiente sola; si es nueva, añade su grupo en `rutas.ts` (`grupo`) y su etiqueta en `i18n/textos.ts`.
6. Tests en `tests/` (módulos puros con Node Test Runner) y, si hay flujo, un `e2e/<modulo>.spec.ts` con `ApiFalsa` y `capturarClaroOscuro`.

Patrones listos (no los reescribas):
- **Formulario**: React Hook Form + `aplicarErroresApi(error, setError, ['campo'])` pinta el 400 de validación en su campo y devuelve el mensaje general para un `Alert`. Ejemplo: `componentes/FormularioPassword.tsx`.
- **Tabla**: `DataTable` del DS hasta ~200 filas; `componentes/TablaVirtual.tsx` (TanStack Virtual, mismas columnas) para facturas, cobros y auditoría.
- **Acción destructiva**: `componentes/ConfirmarDestructivo.tsx` pide motivo obligatorio y dice qué pasa; los registros con efecto económico se anulan, no se borran.
- **Errores de API**: `aErrorApi(e)` da `title`, `detail`, `codigoReferencia` y `errors`; `ErrorAlerta` los enseña sin trazas.

## Notas

- Los componentes del design system se cambian primero en `Atlas Rent Design System/` y después se copian aquí (DESIGN.md §1.7).
- `design-system/tokens/fonts.css` ya no importa Google Fonts: las fuentes Geist llegan por `@fontsource` desde `main.tsx`.
- Paquetes fuera de DOC_TECNICA §2: `@eslint/js` y `globals` (los necesita la configuración plana de ESL 10) y `@types/node` (los tests del Node.js Test Runner se tipan en `tsc --noEmit`).
- Pendiente: el chunk compartido con `Icon` (`Button-*.js`, ~636 kB / 167 kB gzip) supera el umbral de aviso de 500 kB de Vite porque `Icon` importa el catálogo completo de lucide (`icons`). Un mapa de imports explícitos con los iconos que se usan lo reduce a unos pocos kB. Los textos ya no son el problema: salieron del chunk único (`textos-*.js`, ~23 kB) y los de cada módulo viajan con su módulo.
- Las capturas de E2E (`npm run e2e`) son la revisión visual de claro y oscuro; no sustituyen una revisión humana de cada pantalla nueva.
- `Icon`: lucide-react 1.x quitó sufijos numéricos y renombró algunos iconos; `ALIAS` en `Icon.tsx` cubre `trash-2`, `building-2` y `file-signature`. Un nombre sin resolver cae en un círculo sin avisar: comprueba el icono en la muestra del DS.