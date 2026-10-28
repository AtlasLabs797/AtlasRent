import { Suspense, lazy, useEffect, type ComponentType } from 'react';
import { Route, Routes } from 'react-router';
import { Layout } from './componentes/Layout';
import { textos } from './i18n/textos';
import { MODULOS } from './modulos/lista';
import { useSesion } from './sesion/store';

const Acceso = lazy(() => import('./paginas/Acceso'));
const CambioPasswordForzado = lazy(() => import('./paginas/CambioPasswordForzado'));
const NoEncontrada = lazy(() => import('./paginas/NoEncontrada'));

/** Una página lazy por ruta de cada módulo registrado (`src/modulos/registro.ts`). Se crean una vez, fuera del render. */
const RUTAS_MODULOS: Array<{ path: string; Pagina: ComponentType }> = MODULOS.flatMap((modulo) =>
  modulo.rutas.map((ruta) => ({ path: ruta.path, Pagina: lazy(ruta.cargar) })),
);

/**
 * Tres estados, y solo tres: cargando la sesión, sin sesión (pantalla de acceso) y con sesión
 * (el `Layout` con menú y las rutas de los módulos registrados). Con sesión pero con el cambio de contraseña
 * pendiente (`debeCambiarPassword`, SEG-010) solo se ve la pantalla de cambio: la API rechaza todo lo demás. El tema lo fija el script inicial (`public/tema-inicial.js`) antes del primer
 * pintado, así que cambiar de estado no cambia los colores y no hay parpadeo.
 */
export function App() {
  const estado = useSesion((sesion) => sesion.estado);
  const cargar = useSesion((sesion) => sesion.cargar);
  const debeCambiarPassword = useSesion((sesion) => sesion.identidad?.debeCambiarPassword === true);

  useEffect(() => {
    void cargar();
  }, [cargar]);

  if (estado === 'cargando') {
    return (
      <div
        style={{
          minHeight: '100vh',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          background: 'var(--surface-app)',
          color: 'var(--text-muted)',
        }}
      >
        {textos.app.cargandoSesion}
      </div>
    );
  }

  return (
    <Suspense fallback={<div className="ar-shell__content">{textos.app.cargando}</div>}>
      {estado === 'sin-sesion' ? (
        <Routes>
          <Route path="*" element={<Acceso />} />
        </Routes>
      ) : debeCambiarPassword ? (
        <Routes>
          <Route path="*" element={<CambioPasswordForzado />} />
        </Routes>
      ) : (
        <Routes>
          <Route element={<Layout />}>
            {/* Cada módulo exige en `Layout` la acción que declara; la API la vuelve a comprobar. */}
            {RUTAS_MODULOS.map(({ path, Pagina }) => (
              <Route key={path || '/'} index={path === ''} path={path === '' ? undefined : path} element={<Pagina />} />
            ))}
            <Route path="*" element={<NoEncontrada />} />
          </Route>
        </Routes>
      )}
    </Suspense>
  );
}
