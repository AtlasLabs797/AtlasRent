import { ACCIONES } from '../../permisos.ts';
import type { DefinicionModulo } from '../tipos.ts';

/**
 * Clientes y prospectos (AUD-08; spec §3.2). Leer es `cliente.ver` y escribir `cliente.editar`; las
 * rutas piden la lectura y los botones de escritura se condicionan con la segunda.
 * `clientes/prospectos` es estática y gana a `clientes/:id` en el enrutador.
 */
export const modulo: DefinicionModulo = {
  clave: 'clientes',
  accion: ACCIONES.clienteVer,
  ruta: '/clientes',
  rutas: [
    { path: 'clientes', cargar: () => import('./paginas/Clientes') },
    { path: 'clientes/prospectos', cargar: () => import('./paginas/Prospectos') },
    { path: 'clientes/:id', cargar: () => import('./paginas/ClienteFicha') },
  ],
};
