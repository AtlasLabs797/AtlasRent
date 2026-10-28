import { ACCIONES } from '../../permisos.ts';
import type { DefinicionModulo } from '../tipos.ts';

/** Reservas. Leer exige `cliente.ver`; crear, editar y cancelar, `cliente.editar`; facturar, `factura.emitir`. */
export const modulo: DefinicionModulo = {
  clave: 'reservas',
  accion: ACCIONES.clienteVer,
  ruta: '/reservas',
  grupo: 'operacion',
  rutas: [
    { path: 'reservas', cargar: () => import('./paginas/Reservas') },
    { path: 'reservas/:id', cargar: () => import('./paginas/ReservaDetalle') },
  ],
};
