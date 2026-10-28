import { create } from 'zustand';
import type { Identidad } from '../api/auth';
import { clienteHttp, registrarManejadoresSesion } from '../api/cliente';

export type { Identidad } from '../api/auth';

/** Los tres estados de la interfaz al cargar: cargando, sin sesión y con sesión. */
export type EstadoSesion = 'cargando' | 'sin-sesion' | 'con-sesion';

interface Estado {
  estado: EstadoSesion;
  identidad: Identidad | null;
  /** Pide la identidad y deja el store en el estado que corresponda. Nunca lanza. */
  cargar: () => Promise<void>;
  /** Abre la sesión con la identidad que devolvió el login, el segundo factor o el acceso de emergencia. */
  iniciarSesion: (identidad: Identidad) => void;
  /** Tras cambiar la contraseña la API quita el cambio pendiente: la interfaz lo refleja sin volver a pedir `/auth/yo`. */
  passwordCambiada: () => void;
  /** El MFA quedó activado desde "Mi cuenta". */
  mfaActivado: () => void;
  salir: () => Promise<void>;
}

export const useSesion = create<Estado>((set) => ({
  estado: 'cargando',
  identidad: null,

  cargar: async () => {
    try {
      const { data } = await clienteHttp.get<Identidad>('/auth/yo');
      set({ estado: 'con-sesion', identidad: data });
    } catch {
      // Un 401 ya intentó el refresco en el cliente HTTP; si seguimos aquí no hay sesión. Cualquier
      // otro fallo (API caída) también deja la pantalla de acceso, que ya explica lo que pasa.
      set({ estado: 'sin-sesion', identidad: null });
    }
  },

  iniciarSesion: (identidad) => set({ estado: 'con-sesion', identidad }),

  passwordCambiada: () =>
    set((actual) => (actual.identidad ? { identidad: { ...actual.identidad, debeCambiarPassword: false } } : actual)),

  mfaActivado: () =>
    set((actual) =>
      actual.identidad ? { identidad: { ...actual.identidad, mfaActivado: true, mfaPendiente: false } } : actual,
    ),

  salir: async () => {
    try {
      await clienteHttp.post('/auth/logout');
    } catch {
      // Si el cierre falla, la sesión local se limpia igual: la interfaz no debe quedarse mostrando
      // datos de una sesión que el usuario ya cerró aquí.
    }
    set({ estado: 'sin-sesion', identidad: null });
  },
}));

// El cliente HTTP no importa el store (dependencia circular): se enganchan aquí.
registrarManejadoresSesion({
  sesionPerdida: () => useSesion.setState({ estado: 'sin-sesion', identidad: null }),
  identidadRefrescada: (identidad) => {
    if (identidad && typeof identidad === 'object' && 'permisosEfectivos' in identidad) {
      useSesion.setState({ estado: 'con-sesion', identidad: identidad as Identidad });
    }
  },
  passwordPendiente: () =>
    useSesion.setState((actual) =>
      actual.identidad ? { identidad: { ...actual.identidad, debeCambiarPassword: true } } : actual,
    ),
});

/** Permisos efectivos de una identidad. Vacíos si no hay sesión. */
export function permisosDe(identidad: Identidad | null): string[] {
  return identidad?.permisosEfectivos ?? [];
}

/** Si una identidad tiene una acción del backend. */
export function puede(identidad: Identidad | null, accion: string): boolean {
  return permisosDe(identidad).includes(accion);
}
