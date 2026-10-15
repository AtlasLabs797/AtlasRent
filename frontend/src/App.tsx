import { Suspense, lazy } from 'react';
import { Route, Routes } from 'react-router';
import { Layout } from './componentes/Layout';
import { textos } from './i18n/textos';

const Inicio = lazy(() => import('./paginas/Inicio'));
const DesignSystem = lazy(() => import('./paginas/DesignSystem'));
const NoEncontrada = lazy(() => import('./paginas/NoEncontrada'));

export function App() {
  return (
    <Suspense fallback={<div className="ar-shell__content">{textos.app.cargando}</div>}>
      <Routes>
        <Route element={<Layout />}>
          <Route index element={<Inicio />} />
          <Route path="componentes" element={<DesignSystem />} />
          <Route path="*" element={<NoEncontrada />} />
        </Route>
      </Routes>
    </Suspense>
  );
}