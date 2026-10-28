import type { ApiFalsa } from './apiFalsa';

/** Datos inventados con la forma EXACTA de los DTO del backend (importes como número JSON, enums como nombre). */
export const CLIENTE_ID = 'aaaaaaaa-0000-0000-0000-000000000001';
export const CONTRATO_ID = 'bbbbbbbb-0000-0000-0000-000000000001';
export const FACTURA_ID = 'cccccccc-0000-0000-0000-000000000412';
export const FACTURA2_ID = 'cccccccc-0000-0000-0000-000000000413';
export const COBRO_ID = 'dddddddd-0000-0000-0000-000000000001';
export const FIANZA_ID = 'eeeeeeee-0000-0000-0000-000000000001';
export const SERIE_ID = 'ffffffff-0000-0000-0000-000000000001';
export const FORMA_ID = '99999999-0000-0000-0000-000000000001';
export const CUENTA_ID = '88888888-0000-0000-0000-000000000001';

export function fila(sobrescribir: Record<string, unknown> = {}) {
  return {
    tipo: 'Factura', id: FACTURA_ID, serieId: SERIE_ID, serieNumero: 1, numero: 412, comprobanteTipo: null, comprobanteNumero: null,
    fecha: '2026-10-01', clienteId: CLIENTE_ID, cliente: 'Inversiones Caribe SRL', unidadId: null, unidad: 'Apto 3B',
    contratoId: CONTRATO_ID, concepto: 'Renta octubre', moneda: 'USD', total: '1000.00', retencion: '0.00', cobrado: '0.00', pendiente: '1000.00',
    saldoADevolver: null, vencimiento: '2026-10-05', diasVencida: 23, vencida: true, estado: 'Pendiente', modificada: false,
    tieneReservaConAnticipo: false, estadoEnvio: 'Enviado', modificadaTrasEnvio: false, cobrable: true, ...sobrescribir,
  };
}

export const FILAS = [
  fila(),
  fila({ id: FACTURA2_ID, numero: 413, concepto: 'Renta septiembre', fecha: '2026-09-01', vencimiento: '2026-09-05', diasVencida: 53, estado: 'Parcial', cobrado: '400.00', pendiente: '600.00' }),
  fila({ tipo: 'Fianza', id: FIANZA_ID, serieId: null, serieNumero: null, numero: null, moneda: 'DOP', total: '59500.00', retencion: null, cobrado: '59500.00', pendiente: '0.00', vencimiento: null, diasVencida: null, vencida: null, estado: 'Retenida', cobrable: false, saldoADevolver: '59500.00', concepto: 'Fianza de 1 fianza', estadoEnvio: null, modificadaTrasEnvio: null }),
];

export function cuentasRespuesta(conTotales: boolean) {
  return {
    data: FILAS, total: FILAS.length, page: 1, pageSize: 25, totalPages: 1,
    totales: conTotales ? [{ moneda: 'USD', total: '2000.00', retencion: '0.00', cobrado: '400.00', pendiente: '1600.00' }, { moneda: 'DOP', total: '59500.00', retencion: '0.00', cobrado: '59500.00', pendiente: '0.00' }] : null,
    filtro: {},
  };
}

export const SIMULACION = {
  clienteId: CLIENTE_ID, contratoId: CONTRATO_ID, fechaEfectiva: '2026-10-28', monedaRecibida: 'DOP', importeRecibido: '59500.00',
  saldoFavorUsado: '0.00', saldoAFavorDisponible: '0.00', disponible: '59500.00', totalAsignado: '59500.00', excedente: '0.00', saldoAFavorSeActualiza: false,
  asignaciones: [{ facturaId: FACTURA_ID, fianzaId: null, numero: 412, fechaVencimiento: '2026-10-05', monedaDestino: 'USD', importe: '1000.00', importeRecibido: '59500.00', tipoCambioValor: '59.500000', pendienteAntes: '1000.00' }],
  deudaEnOtraMoneda: [],
};

export const COBRO = {
  id: COBRO_ID, clienteId: CLIENTE_ID, clienteNombre: 'Inversiones Caribe SRL', contratoId: CONTRATO_ID, fechaEfectiva: '2026-10-28',
  monedaRecibida: 'DOP', importeRecibido: '59500.00', formaPagoId: FORMA_ID, formaPagoNombre: 'Transferencia', referencia: 'TRF-0001',
  cuentaReceptoraId: CUENTA_ID, cuentaReceptoraNombre: 'Cuenta DOP', tipoCambioValor: '59.500000', tipoCambioFecha: '2026-10-28', tipoCambioId: null,
  saldoFavorUsado: '0.00', excedente: '0.00', totalAsignado: '59500.00', estado: 'Registrado', anuladoAt: null, anuladoPor: null, motivoAnulacion: null,
  createdAt: '2026-10-28T14:00:00Z',
  asignaciones: [{ id: 'a1', facturaId: FACTURA_ID, fianzaId: null, facturaNumero: 412, facturaVencimiento: '2026-10-05', facturaMoneda: 'USD', importe: '1000.00', importeRecibido: '59500.00', tipoCambioValor: '59.500000', vigente: true, createdAt: '2026-10-28T14:00:00Z' }],
};

export const FACTURA = {
  id: FACTURA_ID, clienteId: CLIENTE_ID, clienteNombre: 'Inversiones Caribe SRL', unidadId: null, unidadNombre: 'Apto 3B', contratoId: CONTRATO_ID,
  serieId: SERIE_ID, serieNumero: 1, numero: 412, estado: 'Emitida', tipoSerie: 'Normal', moneda: 'USD', fechaEmision: '2026-10-01', fechaVencimiento: '2026-10-05',
  concepto: 'Renta octubre', subtotal: '1000.00', itbis: '0.00', total: '1000.00', retencion: '0.00', netoACobrar: '1000.00', tipoCambioValor: '59.500000', tipoCambioFecha: '2026-10-01',
  tipoCambioId: null, createdAt: '2026-10-01T12:00:00Z', updatedAt: null, cobrado: '0.00', ajustado: '0.00', pendiente: '1000.00', estadoCobro: 'Pendiente', vencida: true,
  retencionSinComprobante: false, referenciaEfactura: null,
  lineas: [{ id: 'l1', posicion: 1, tipo: 'Renta', concepto: 'Renta octubre', cantidad: '1.00', precioUnitario: '1000.00', importe: '1000.00', tipoItbisId: null, tipoItbisNombre: null, porcentajeItbis: null, itbisImporte: '0.00', tipoRetencionId: null, tipoRetencionNombre: null, porcentajeRetencion: null, baseRetencion: null }],
};

/** Respuestas comunes de las pantallas de finanzas: filtros (clientes, inmuebles) y, para el administrador, catálogos. */
export function catalogos(api: ApiFalsa, admin: boolean): void {
  api.responder('GET', '/clientes', { body: { data: [{ id: CLIENTE_ID, numero: 1873, nombre: 'Inversiones Caribe SRL' }], total: 1, page: 1, pageSize: 200, totalPages: 1 } });
  api.responder('GET', '/inmuebles', { body: { data: [{ id: 'inm1', nombre: 'Torre Marina' }], total: 1, page: 1, pageSize: 200, totalPages: 1 } });
  if (admin) {
    api.responder('GET', '/config/subempresas', { body: { data: [], total: 0, page: 1, pageSize: 200, totalPages: 0 } });
    api.responder('GET', '/config/formas-pago', { body: { data: [{ id: FORMA_ID, nombre: 'Transferencia', activa: true }], total: 1, page: 1, pageSize: 200, totalPages: 1 } });
    api.responder('GET', '/config/cuentas', { body: { data: [{ id: CUENTA_ID, nombre: 'Cuenta DOP', banco: 'Banco', moneda: 'DOP', activa: true }, { id: 'c2', nombre: 'Cuenta USD', banco: 'Banco', moneda: 'USD', activa: true }], total: 2, page: 1, pageSize: 200, totalPages: 1 } });
    api.responder('GET', '/config/series', { body: { data: [{ id: SERIE_ID, numero: 1, prefijo: 'A', moneda: 'USD', tipo: 'Normal', activa: true }], total: 1, page: 1, pageSize: 200, totalPages: 1 } });
  }
}
