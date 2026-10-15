import { create } from 'zustand';
import { clienteHttp } from '../api/cliente';

/**
 * Identidad de la sesión, tal y como la devuelve `GET /api/v1/auth/yo`. `permisosEfectivos` son los
 * identificadores de acción del backend (techo del rol recortado por los permisos por usuario).
 */
export interface Identidad {
  id: string;
  nombreAcceso: string;
  nombre: string;
  email: string | null;
  rol: string;
  debeCambiarPassword: boolean;
  mfaActivado: boolean;
  mfaPendiente: boolean;
  accesoEmergencia: boolean;
  ultimoAcceso: string | null;
  permisosEfectivos: string[];
}

/** Los tres estados de la interfaz al cargar: cargando, sin sesión y con sesión. */
export type EstadoSesion = 'cargando' | 'sin-sesion' | 'con-sesion';

interface Estado {
  estado: EstadoSesion;
  identidad: Identidad | null;
  /** Pide la identidad y deja el store en el estado que corresponda. Nunca lanza. */
  cargar: () => Promise<void>;
  /**
   * Entra con usuario y contraseña. Devuelve `'con-sesion'` si abrió sesión y
   * `'credenciales-invalidas'` si el usuario o la contraseña no son correctos (o la API no responde).
   * Lanza `RequiereSegundoFactor` si el backend exige el segundo factor: con usuario y contraseña no
   * hay sesión.
   */
  entrar: (nombreAcceso: string, contrasena: string) => Promise<'con-sesion' | 'credenciales-invalidas'>;
  salir: () => Promise<void>;
}

interface RespuestaDesafio {
  requiereMfa: boolean;
  requiereConfiguracion: boolean;
}

/** El backend exige segundo factor: con usuario y contraseña no se puede abrir sesión. */
export class RequiereSegundoFactor extends Error {
  requiereConfiguracion: boolean;

  constructor(requiereConfiguracion: boolean) {
    super('La cuenta tiene un segundo factor pendiente.');
    this.requiereConfiguracion = requiereConfiguracion;
  }
}

export const useSesion = create<Estado>((set) => ({
  estado: 'cargando',
  identidad: null,

  cargar: async () => {
    try {
      const { data } = await clienteHttp.get<Identidad>('/auth/yo');
      set({ estado: 'con-sesion', identidad: data });
    } catch {
      // 401 es lo normal al abrir la app sin sesión; cualquier otro fallo (API caída) también deja
      // la pantalla de acceso, que ya explica lo que pasa. La UI no muestra un error de red.
      set({ estado: 'sin-sesion', identidad: null });
    }
  },

  entrar: async (nombreAcceso, contrasena) => {
    try {
      const { data } = await clienteHttp.post<Identidad | RespuestaDesafio>('/auth/login', {
        nombreAcceso,
        contrasena,
      });

      if ('requiereMfa' in data && data.requiereMfa) {
        throw new RequiereSegundoFactor(data.requiereConfiguracion);
      }

      set({ estado: 'con-sesion', identidad: data as Identidad });
      return 'con-sesion' as const;
    } catch (e) {
      if (e instanceof RequiereSegundoFactor) throw e;
      set({ estado: 'sin-sesion', identidad: null });
      return 'credenciales-invalidas' as const;
    }
  },

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

/** Permisos efectivos de una identidad. Vacíos si no hay sesión. */
export function permisosDe(identidad: Identidad | null): string[] {
  return identidad?.permisosEfectivos ?? [];
}

/** Si una identidad tiene una acción del backend. */
export function puede(identidad: Identidad | null, accion: string): boolean {
  return permisosDe(identidad).includes(accion);
}