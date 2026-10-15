import { useMemo } from 'react';
import { Outlet, useLocation, useNavigate } from 'react-router';
import { AppShell } from '../design-system/components/navigation/AppShell';
import { SegmentedControl } from '../design-system/components/navigation/SegmentedControl';
import type { SideNavItem, SideNavProps } from '../design-system/components/navigation/SideNav';
import type { TopBarProps } from '../design-system/components/navigation/TopBar';
import { textos } from '../i18n/textos';
import { useTema } from '../tema/store';
import type { PreferenciaTema } from '../tema/resolver';

const CLAVE_POR_RUTA: Record<string, string> = {
  '/': 'panel',
  '/componentes': 'componentes',
};

/** Marco de la aplicación: SideNav + TopBar con el control de tema (DESIGN.md §3.1) y el contenido. */
export function Layout() {
  const navegar = useNavigate();
  const ubicacion = useLocation();
  const { preferencia, setPreferencia } = useTema();

  const nav: SideNavProps = useMemo(
    () => ({
      groups: textos.navegacion.grupos.map((grupo) => ({
        label: grupo.etiqueta,
        items: grupo.items.map((item) => ({
          key: item.clave,
          icon: item.icono,
          label: item.etiqueta,
        })) satisfies SideNavItem[],
      })),
      active: CLAVE_POR_RUTA[ubicacion.pathname],
      onNavigate: (clave) => navegar('/' + clave),
      user: textos.navegacion.usuario,
    }),
    [ubicacion.pathname, navegar],
  );

  const topbar: TopBarProps = {
    searchPlaceholder: textos.barraSuperior.buscar,
    children: (
      <div role="group" aria-label={textos.tema.etiqueta}>
        <SegmentedControl
          options={textos.tema.opciones.map((opcion) => ({ value: opcion.valor, label: opcion.etiqueta }))}
          value={preferencia}
          onChange={(valor) => setPreferencia(valor as PreferenciaTema)}
        />
      </div>
    ),
  };

  return (
    <AppShell nav={nav} topbar={topbar}>
      <Outlet />
    </AppShell>
  );
}