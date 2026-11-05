import { useMemo } from 'react';
import { Outlet, useLocation, useNavigate } from 'react-router';
import { Avatar } from '../design-system/components/data/Avatar';
import { IconButton } from '../design-system/components/core/IconButton';
import { AppShell } from '../design-system/components/navigation/AppShell';
import { Menu } from '../design-system/components/navigation/Menu';
import { SegmentedControl } from '../design-system/components/navigation/SegmentedControl';
import type { SideNavItem, SideNavProps } from '../design-system/components/navigation/SideNav';
import type { TopBarProps } from '../design-system/components/navigation/TopBar';
import { textos } from '../i18n/textos';
import { entradasNavegables, entradaDeRuta, menuPermitido, puedeVerRuta } from '../navegacion/menu';
import { permisosDe, useSesion } from '../sesion/store';
import SinPermiso from '../paginas/SinPermiso';
import { useTema } from '../tema/store';
import type { PreferenciaTema } from '../tema/resolver';

/**
 * Marco de la aplicación: SideNav + TopBar con el control de tema (DESIGN.md §3.1), los datos
 * reales del usuario y el contenido.
 * <para>
 * El menú se filtra con los permisos que devuelve <c>/auth/yo</c>, y con el mismo identificador de
 * acción que exige el backend: si a alguien le falta el permiso, no ve la entrada y, si llega por
 * URL directa, la guarda le lleva a "sin permiso" en vez de a un 403.
 * </para>
 */
export function Layout() {
  const navegar = useNavigate();
  const ubicacion = useLocation();
  const { preferencia, setPreferencia } = useTema();
  const identidad = useSesion((estado) => estado.identidad);
  const salir = useSesion((estado) => estado.salir);

  const permisos = useMemo(() => permisosDe(identidad), [identidad]);
  // Lo que el usuario puede usar, y de eso solo lo que tiene pantalla: una entrada sin destino
  // sería un 404.
  const grupos = useMemo(
    () => menuPermitido(permisos).map((grupo) => ({ ...grupo, entradas: entradasNavegables(grupo) })),
    [permisos],
  );

  const nav: SideNavProps = useMemo(
    () => ({
      groups: grupos
        .filter((grupo) => grupo.entradas.length > 0)
        .map((grupo) => ({
          label: grupo.etiqueta,
          items: grupo.entradas.map((entrada) => ({
            key: entrada.clave,
            icon: entrada.icono,
            label: entrada.etiqueta,
          })) satisfies SideNavItem[],
        })),
      active: entradaDeRuta(ubicacion.pathname)?.clave,
      onNavigate: (clave) => {
        const entrada = grupos.flatMap((grupo) => grupo.entradas).find((item) => item.clave === clave);
        if (entrada?.ruta) navegar(entrada.ruta);
      },
    }),
    [grupos, ubicacion.pathname, navegar],
  );

  const topbar: TopBarProps = {
    searchPlaceholder: textos.barraSuperior.buscar,
    children: (
      <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
        {identidad && (
          <Menu
            align="right"
            trigger={
              <button
                type="button"
                aria-label={textos.barraSuperior.cuenta}
                className="ar-btn ar-btn--ghost ar-btn--md"
                style={{ gap: 8 }}
              >
                <Avatar name={identidad.nombre} size={26} />
                <span style={{ font: 'var(--type-label)', color: 'var(--text-strong)' }}>{identidad.nombre}</span>
              </button>
            }
            items={[
              { label: textos.cuenta.menu, value: 'cuenta', icon: 'user-round' },
              { separator: true },
              { label: textos.barraSuperior.cerrar, value: 'salir', icon: 'log-out' },
            ]}
            onSelect={(valor) => {
              if (valor === 'cuenta') navegar('/cuenta');
              else void salir();
            }}
          />
        )}
        <div role="group" aria-label={textos.tema.etiqueta}>
          <SegmentedControl
            options={textos.tema.opciones.map((opcion) => ({ value: opcion.valor, label: opcion.etiqueta }))}
            value={preferencia}
            onChange={(valor) => setPreferencia(valor as PreferenciaTema)}
          />
        </div>
        <IconButton icon="log-out" label={textos.barraSuperior.cerrar} onClick={() => void salir()} />
      </div>
    ),
  };

  const permitido = puedeVerRuta(permisos, ubicacion.pathname);

  return (
    <AppShell nav={nav} topbar={topbar}>
      {permitido ? <Outlet /> : <SinPermiso />}
    </AppShell>
  );
}