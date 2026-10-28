/**
 * REGISTRO DE MÓDULOS. Añadir un módulo de pantallas es añadir UNA línea aquí:
 *
 *   export { modulo as clientes } from './clientes/rutas.ts';
 *
 * `App.tsx` monta sus rutas y `navegacion/menu.ts` su entrada de menú desde `MODULOS`
 * (`./lista.ts`). No hay que tocar nada más. Orden alfabético para que dos agentes añadiendo
 * módulos a la vez no choquen en la misma línea.
 */
export { modulo as asistente } from './asistente/rutas.ts';
export { modulo as clientes } from './clientes/rutas.ts';
export { modulo as bandeja } from './bandeja/rutas.ts';
export { modulo as bloqueos } from './bloqueos/rutas.ts';
export { modulo as componentes } from './componentes/rutas.ts';
export { modulo as configuracion } from './configuracion/rutas.ts';
export { modulo as cuenta } from './cuenta/rutas.ts';
export { modulo as envios } from './envios/rutas.ts';
export { modulo as informes } from './informes/rutas.ts';
export { modulo as panel } from './panel/rutas.ts';
export { modulo as unidades } from './unidades/rutas.ts';
export { modulo as usuarios } from './usuarios/rutas.ts';
