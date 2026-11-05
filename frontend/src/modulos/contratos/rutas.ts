import { ACCIONES } from '../../permisos.ts';
import type { DefinicionModulo } from '../tipos.ts';

/**
 * Contratos. Leer exige `cliente.ver` (ContratosController); crear y editar el borrador, `contrato.borrador`;
 * aprobar, activar, baja, liquidación e incrementos, `contrato.estado`. Las rutas estáticas (`nuevo`,
 * `plantillas`) van antes que `:id` y el enrutador las prefiere de todos modos.
 */
export const modulo: DefinicionModulo = {
  clave: 'contratos',
  accion: ACCIONES.clienteVer,
  ruta: '/contratos',
  rutas: [
    { path: 'contratos', cargar: () => import('./paginas/Contratos') },
    { path: 'contratos/nuevo', cargar: () => import('./paginas/ContratoFormulario') },
    { path: 'contratos/plantillas', cargar: () => import('./paginas/Plantillas') },
    { path: 'contratos/:id', cargar: () => import('./paginas/ContratoFicha') },
    { path: 'contratos/:id/editar', cargar: () => import('./paginas/ContratoFormulario') },
  ],
};
