// Extensión explícita: este módulo lo carga también el runner de Node en tests/temaStore.test.ts.
import { create } from 'zustand';
import { CLAVE_TEMA, leerPreferencia, resolverTema, type PreferenciaTema, type Tema } from './resolver.ts';

interface EstadoTema {
  preferencia: PreferenciaTema;
  tema: Tema;
  setPreferencia: (preferencia: PreferenciaTema) => void;
}

const consultaSistema = window.matchMedia('(prefers-color-scheme: dark)');

function aplicar(tema: Tema): void {
  document.documentElement.setAttribute('data-theme', tema);
}

export const useTema = create<EstadoTema>((set) => ({
  preferencia: leerPreferencia(localStorage),
  tema: resolverTema(leerPreferencia(localStorage), consultaSistema.matches),

  setPreferencia: (preferencia) => {
    localStorage.setItem(CLAVE_TEMA, preferencia);
    const tema = resolverTema(preferencia, consultaSistema.matches);
    aplicar(tema);
    set({ preferencia, tema });
  },
}));

// Mientras la preferencia sea "sistema", los cambios del sistema siguen a la app.
consultaSistema.addEventListener('change', () => {
  if (useTema.getState().preferencia !== 'sistema') return;
  const tema = resolverTema('sistema', consultaSistema.matches);
  aplicar(tema);
  useTema.setState({ tema });
});