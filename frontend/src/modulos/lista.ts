import * as registro from './registro.ts';
import type { DefinicionModulo } from './tipos.ts';

/** Todos los módulos registrados en `registro.ts`. */
export const MODULOS: readonly DefinicionModulo[] = Object.values(registro);
