import { Suspense, lazy, useEffect } from 'react';
import { Route, Routes } from 'react-router';
import { Layout } from './componentes/Layout';
import { textos } from './i18n/textos';
import { useSesion } from './sesion/store';

const Acceso = lazy(() => import('./paginas/Acceso'));
const Inicio = lazy(() => import('./paginas/Inicio'));
const Informes = lazy(() => import('./paginas/Informes'));
const Asistente = lazy(() => import('./paginas/Asistente'));
const DesignSystem = lazy(() => import('./paginas/DesignSystem'));
const NoEncontrada = lazy(() => import('./paginas/NoEncontrada'));

/**
 * Tres estados, y solo tres: cargando la sesión, sin sesión (pantalla de acceso) y con sesión
 * (el `Layout` con menú y rutas). El tema lo fija el script en línea de `index.html` antes del primer
 * pintado, así que cambiar de estado no cambia los colores y no hay parpadeo.
 */
export function App() {
  const estado = useSesion((sesion) => sesion.estado);
  const cargar = useSesion((sesion) => sesion.cargar);

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
      ) : (
        <Routes>
          <Route element={<Layout />}>
            <Route index element={<Inicio />} />
            {/* Informes exige `informe.ver`; el `Layout` filtra la ruta con la misma acción que
                exige la API, así que el empleado recibe "sin permiso" y no un 403. */}
            <Route path="informes" element={<Informes />} />
            {/* El asistente exige `ia.consultar`, la misma acción que declara el endpoint: el
                `Layout` filtra la ruta con ella y quien no la tiene recibe "sin permiso". */}
            <Route path="asistente" element={<Asistente />} />
            <Route path="componentes" element={<DesignSystem />} />
            <Route path="*" element={<NoEncontrada />} />
          </Route>
        </Routes>
      )}
    </Suspense>
  );
}