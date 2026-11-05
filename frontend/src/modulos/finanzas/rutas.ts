import { ACCIONES } from '../../permisos.ts';
import type { DefinicionModulo } from '../tipos.ts';

/**
 * Finanzas: cuatro entradas de menú (facturas, cobros, fianzas y la vista única de cuentas). Todas
 * leen con `factura.ver`, que es lo que exigen sus GET; las acciones de escritura de cada pantalla
 * se condicionan con su propia acción (`cobro.registrar`, `factura.emitir`, `factura.anular`, `fianza.mover`).
 */
export const facturas: DefinicionModulo = {
  clave: 'facturas',
  accion: ACCIONES.facturaVer,
  ruta: '/facturas',
  rutas: [
    { path: 'facturas', cargar: () => import('./paginas/Facturas') },
    { path: 'facturas/periodicas', cargar: () => import('./paginas/FacturasPeriodicas') },
    { path: 'facturas/nueva', cargar: () => import('./paginas/FacturaBorrador') },
    { path: 'facturas/:id/editar', cargar: () => import('./paginas/FacturaBorrador') },
    { path: 'facturas/:id', cargar: () => import('./paginas/FacturaFicha') },
  ],
};

export const cobros: DefinicionModulo = {
  clave: 'cobros',
  accion: ACCIONES.facturaVer,
  ruta: '/cobros',
  rutas: [
    { path: 'cobros', cargar: () => import('./paginas/Cobros') },
    { path: 'cobros/:id', cargar: () => import('./paginas/CobroFicha') },
  ],
};

export const fianzas: DefinicionModulo = {
  clave: 'fianzas',
  accion: ACCIONES.facturaVer,
  ruta: '/fianzas',
  rutas: [
    { path: 'fianzas', cargar: () => import('./paginas/Fianzas') },
    { path: 'fianzas/:id', cargar: () => import('./paginas/FianzaFicha') },
  ],
};

export const cuentas: DefinicionModulo = {
  clave: 'cuentas',
  accion: ACCIONES.facturaVer,
  ruta: '/cuentas',
  rutas: [{ path: 'cuentas', cargar: () => import('./paginas/Cuentas') }],
};
