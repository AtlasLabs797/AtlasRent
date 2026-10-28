import { ACCIONES } from '../../permisos.ts';
import type { DefinicionModulo } from '../tipos.ts';

/** Cotizaciones. Los GET de `CotizacionesController` exigen `factura.ver`; escribir, `factura.borrador`. */
export const modulo: DefinicionModulo = {
  clave: 'cotizaciones',
  accion: ACCIONES.facturaVer,
  ruta: '/cotizaciones',
  grupo: 'operacion',
  rutas: [
    { path: 'cotizaciones', cargar: () => import('./paginas/Cotizaciones') },
    { path: 'cotizaciones/:id', cargar: () => import('./paginas/CotizacionDetalle') },
  ],
};
