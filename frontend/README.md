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

## Estructura

```
src/
  main.tsx                 raíz: fuentes, CSS del design system, QueryClient, router
  App.tsx                  rutas con React.lazy + Suspense
  api/cliente.ts           Axios: withCredentials + X-CSRF-Token
  tema/resolver.ts         lógica pura del tema (sin dependencias, testeable)
  tema/store.ts            store de Zustand + matchMedia
  i18n/textos.ts           textos de interfaz en español
  componentes/Layout.tsx   AppShell + SideNav + TopBar con el control de tema
  paginas/                 Inicio · DesignSystem (muestra) · NoEncontrada
  design-system/           copia de `Atlas Rent Design System/` (tokens + componentes en .tsx)
```

## Notas

- Los componentes del design system se cambian primero en `Atlas Rent Design System/` y después se copian aquí (DESIGN.md §1.7).
- `design-system/tokens/fonts.css` ya no importa Google Fonts: las fuentes Geist llegan por `@fontsource` desde `main.tsx`.
- Paquetes fuera de DOC_TECNICA §2: `@eslint/js` y `globals` (los necesita la configuración plana de ESL 10) y `@types/node` (los tests del Node.js Test Runner se tipan en `tsc --noEmit`).
- Pendiente: el chunk de textos centralizados (`textos-*.js`, ~634 kB / 173 kB gzip) y el entry (`index-*.js`, ~331 kB / 110 kB gzip) superan el umbral de aviso de 500 kB de Vite. Cuando exista la primera pantalla real conviene un mapa de imports explícitos en `Icon` y revisar si los textos deben ir en chunks por módulo.
- No se ha revisado la UI en un navegador: en esta tarea solo se verificaron lint, typecheck, tests y build.