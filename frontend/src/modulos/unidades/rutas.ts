import { ACCIONES } from '../../permisos.ts';
import type { DefinicionModulo } from '../tipos.ts';

/**
 * Inmuebles y unidades (AUD-08; spec §3.1). Verlos es `cliente.ver` y mantenerlos `cliente.editar`
 * (Acciones.cs). Los documentos de propiedad piden `inmueble.documento`, que el empleado no tiene:
 * pestaña y llamadas se condicionan con esa acción dentro de la ficha.
 */
export const modulo: DefinicionModulo = {
  clave: 'unidades',
  accion: ACCIONES.clienteVer,
  ruta: '/unidades',
  rutas: [
    { path: 'unidades', cargar: () => import('./paginas/Inmuebles') },
    { path: 'unidades/:id', cargar: () => import('./paginas/InmuebleFicha') },
  ],
};
