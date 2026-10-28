import type { ComponentType } from 'react';
import type { Accion } from '../permisos.ts';

/**
 * Contrato de un módulo de pantallas. Es TypeScript sin JSX a propósito: lo cargan también los
 * tests del Node.js Test Runner (menú, rutas), que no pueden importar `.tsx`. Las páginas se cargan
 * con `import()` dinámico, así que ninguna se evalúa hasta que el enrutador la pide.
 */

/** Grupos de la barra lateral (`textos.navegacion.grupos`). */
export type GrupoMenuId = 'operacion' | 'finanzas' | 'control' | 'herramientas';

export interface RutaModulo {
  /** Ruta relativa al `Layout`: `'clientes'`, `'clientes/:id'`. Cadena vacía para el índice (`/`). */
  path: string;
  /** `() => import('./paginas/Clientes')`: la página tiene que exportar el componente por defecto. */
  cargar: () => Promise<{ default: ComponentType }>;
}

export interface DefinicionModulo {
  /**
   * Clave estable. Si el módulo sale en el menú, es una clave de `textos.navegacion.modulos`
   * (etiqueta e icono); si no sale (p. ej. "Mi cuenta"), basta con que sea única.
   */
  clave: string;
  /**
   * Acción del backend que habilita el módulo: la MISMA que exigen sus endpoints. Sirve para filtrar
   * el menú y para guardar la ruta (quien llega por URL sin ella ve "sin permiso", no un 403).
   */
  accion: Accion;
  /** Ruta base absoluta: `'/clientes'`. Cubre también las rutas hijas (`/clientes/:id`). */
  ruta: string;
  /** Grupo del menú si la clave no está ya en el esqueleto de `navegacion/menu.ts`. Sin grupo ni esqueleto, no sale en el menú. */
  grupo?: GrupoMenuId;
  rutas: RutaModulo[];
}
