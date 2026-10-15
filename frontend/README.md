# Atlas Rent — frontend

SPA de Atlas Rent (React 19 + TypeScript + Vite). Ver `docs/DOC_TECNICA.md` §2 y §3.1 y `docs/DESIGN.md` §1.

## Scripts

| Script | Qué hace |
|---|---|
| `npm run dev` | Servidor de desarrollo de Vite |
| `npm run lint` | ESLint 10 (configuración plana) |
| `npm run typecheck` | `tsc --noEmit` |
| `npm test` | Node.js Test Runner sobre `tests/**/*.test.ts` |
| `npm run build` | `vite build` → `dist/`, que la API sirve como estático desde `wwwroot/` |
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
- Pendiente: `Icon` resuelve el nombre del icono en el mapa completo de `lucide-react`, así que el chunk principal pesa 931 kB (261 kB gzip). Cuando exista la primera pantalla real conviene un mapa de imports explícitos.
- No se ha revisado la UI en un navegador: en esta tarea solo se verificaron lint, typecheck, tests y build.