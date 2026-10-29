import { ACCIONES } from '../src/permisos';
import { problema, type ApiFalsa } from './apiFalsa';

/** Identidades de muestra con la forma de `GET /auth/yo`. Datos inventados. */
export function identidad(sobrescribir: Record<string, unknown> = {}) {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    nombreAcceso: 'admin',
    nombre: 'Marta Peña',
    email: null,
    rol: 'admin',
    debeCambiarPassword: false,
    mfaActivado: true,
    mfaPendiente: false,
    accesoEmergencia: false,
    ultimoAcceso: '2026-10-27T14:30:00Z',
    permisosEfectivos: Object.values(ACCIONES),
    ...sobrescribir,
  };
}

export const EMPLEADO = identidad({
  nombreAcceso: 'empleado',
  nombre: 'Luis Rosario',
  rol: 'empleado',
  permisosEfectivos: [ACCIONES.clienteVer, ACCIONES.sesionVer, ACCIONES.sesionCerrar, ACCIONES.sesionPassword],
});

export const DESAFIO_MFA = { requiereMfa: true, requiereConfiguracion: false, nombreAcceso: 'admin', caduca: '2026-10-28T12:05:00Z' };
export const DESAFIO_ALTA_MFA = { ...DESAFIO_MFA, requiereConfiguracion: true };

/** Sin sesión: `/auth/yo` da 401 y el refresco también (lo que ve quien abre la app sin entrar). */
export function sinSesion(api: ApiFalsa): void {
  api.responder('GET', '/auth/yo', problema(401, 'AUT-002', 'No autenticado', 'Falta la cookie de sesión o no es válida.'));
  api.responder('POST', '/auth/refresh', problema(401, 'AUT-004', 'No autenticado', 'Falta el token de refresco.'));
}

/** Con sesión de la identidad dada. */
export function conSesion(api: ApiFalsa, quien = identidad()): void {
  api.responder('GET', '/auth/yo', { body: quien });
  api.responder('POST', '/auth/logout', { status: 204 });
}

const IMPORTE_CERO = { usd: '0.00', dop: '0.00', consolidadoUsd: '0.00' };

/** `DashboardRespuesta` mínimo con la forma de `GET /informes/dashboard`. */
export const DASHBOARD = {
  fecha: '2026-10-28',
  unidades: { disponibles: 40, reservadas: 5, ocupadas: 100, mantenimiento: 5 },
  contratosActivos: 98,
  resumen: {
    desde: null,
    hasta: null,
    fechaCorte: '2026-10-28',
    proformado: IMPORTE_CERO,
    facturado: { usd: '9000.00', dop: '0.00', consolidadoUsd: '9000.00' },
    cobrado: { facturas: IMPORTE_CERO, fianzas: IMPORTE_CERO, saldoAFavor: IMPORTE_CERO, total: { usd: '7000.00', dop: '0.00', consolidadoUsd: '7000.00' } },
    retenido: { conComprobante: IMPORTE_CERO, sinComprobante: IMPORTE_CERO, total: IMPORTE_CERO },
    pendiente: { facturas: IMPORTE_CERO, fianzas: IMPORTE_CERO, total: { usd: '2000.00', dop: '0.00', consolidadoUsd: '2000.00' } },
    vencido: { usd: '800.00', dop: '0.00', consolidadoUsd: '800.00' },
    fianzasRetenidas: IMPORTE_CERO,
    diferenciasCambio: IMPORTE_CERO,
    serieMensual: [
      { mes: '2026-09', facturadoUsd: '8500.00', cobradoUsd: '6900.00' },
      { mes: '2026-10', facturadoUsd: '9000.00', cobradoUsd: '7000.00' },
    ],
    documentosSinTipoCambioCongelado: 0,
    saldoAFavorFueraDePeriodo: false,
  },
};

const PAGINA_VACIA = { data: [], total: 0, page: 1, pageSize: 25, totalPages: 1 };

/** `AlertaFila` con la forma de `GET /alertas`. Datos inventados. */
export const ALERTA = (id: string, tipo: string, mensaje: string, extra: Record<string, unknown> = {}) => ({
  id,
  tipo,
  mensaje,
  ocurridoAt: '2026-10-27T15:00:00Z',
  repeticiones: 1,
  resueltaAt: null,
  ...extra,
});

export const ALERTAS_PANEL = {
  data: [ALERTA('a1', 'seguridad', 'Entrada desde una IP nueva'), ALERTA('a2', 'factura', 'Factura periódica fallida', { repeticiones: 3 })],
  total: 2,
  page: 1,
  pageSize: 5,
  totalPages: 1,
};

/** Respuestas del panel de inicio para quien tiene todos los permisos. */
export function panelAdmin(api: ApiFalsa): void {
  api.responder('GET', '/informes/dashboard', { body: DASHBOARD });
  api.responder('GET', '/contratos/solicitudes', { body: PAGINA_VACIA });
  api.responder('GET', '/ia/propuestas', { body: PAGINA_VACIA });
  api.responder('GET', '/alertas', { body: ALERTAS_PANEL });
}
