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
  saldoFavorUsado: '0.00', excedente: '0.00', totalAsignado: '59500.00', importeDevuelto: '0.00', estado: 'Registrado', anuladoAt: null, anuladoPor: null, motivoAnulacion: null,
  createdAt: '2026-10-28T14:00:00Z',
  asignaciones: [{ id: 'a1', facturaId: FACTURA_ID, fianzaId: null, facturaNumero: 412, facturaVencimiento: '2026-10-05', facturaMoneda: 'USD', importe: '1000.00', importeRecibido: '59500.00', tipoCambioValor: '59.500000', vigente: true, createdAt: '2026-10-28T14:00:00Z' }],
};

export const FACTURA = {
  id: FACTURA_ID, clienteId: CLIENTE_ID, clienteNombre: 'Inversiones Caribe SRL', unidadId: null, unidadNombre: 'Apto 3B', contratoId: CONTRATO_ID,
  serieId: SERIE_ID, serieNumero: 1, numero: 412, estado: 'Emitida', tipoSerie: 'Normal', moneda: 'USD', fechaEmision: '2026-10-01', fechaVencimiento: '2026-10-05',
  concepto: 'Renta octubre', subtotal: '1000.00', itbis: '0.00', total: '1000.00', retencion: '0.00', netoACobrar: '1000.00', tipoCambioValor: '59.500000', tipoCambioFecha: '2026-10-01',
  tipoCambioId: null, createdAt: '2026-10-01T12:00:00Z', updatedAt: null, cobrado: '0.00', ajustado: '0.00', pendiente: '1000.00', estadoCobro: 'Pendiente', vencida: true,
  retencionSinComprobante: false, referenciaEfactura: null, comprobanteFiscal: null, comprobanteFiscalId: null, modificadaTrasEnvio: false, comprobanteRetencionId: null,
  lineas: [{ id: 'l1', posicion: 1, tipo: 'Renta', concepto: 'Renta octubre', cantidad: '1.00', precioUnitario: '1000.00', importe: '1000.00', tipoItbisId: null, tipoItbisNombre: null, porcentajeItbis: null, itbisImporte: '0.00', tipoRetencionId: null, tipoRetencionNombre: null, porcentajeRetencion: null, baseRetencion: null }],
};

export const RETENCION_ID = '77777777-0000-0000-0000-000000000001';

export const COMPROBANTE_RETENCION = {
  id: RETENCION_ID, facturaId: FACTURA_ID, tipo: 'Retención ISR 10 %', base: 'Subtotal', porcentaje: '10.00', importe: '100.00', estado: 'Pendiente',
  numero: null, fechaRecepcion: null, recibidoAt: null, recibidoPor: null, createdAt: '2026-10-01T12:00:00Z',
};

const pagina = (data: unknown[]) => ({ body: { data, total: data.length, page: 1, pageSize: 200, totalPages: 1 } });

/** Respuestas comunes de las pantallas de finanzas: filtros (clientes, inmuebles) y catálogos mínimos de /catalogos/* (sin permiso de configuración). */
export function catalogos(api: ApiFalsa): void {
  api.responder('GET', '/clientes', pagina([{ id: CLIENTE_ID, numero: 1873, nombre: 'Inversiones Caribe SRL' }]));
  api.responder('GET', `/clientes/${CLIENTE_ID}`, { body: { id: CLIENTE_ID, numero: 1873, nombre: 'Inversiones Caribe SRL' } });
  api.responder('GET', '/inmuebles', pagina([{ id: 'inm1', nombre: 'Torre Marina' }]));
  api.responder('GET', '/inmuebles/inm1/unidades', pagina([{ id: 'u1', nombre: 'Apto 3B' }]));
  api.responder('GET', '/catalogos/subempresas', pagina([
    { id: 'sub1', nombre: 'Atlas Propiedades SRL', propietaria: true, facturadora: true },
    { id: 'sub2', nombre: 'Solo Propietaria SRL', propietaria: true, facturadora: false },
    { id: 'sub3', nombre: 'Solo Facturadora SRL', propietaria: false, facturadora: true },
  ]));
  api.responder('GET', '/catalogos/tipos-itbis', pagina([{ id: 'itbis1', nombre: 'ITBIS general', porcentaje: '18.0000' }]));
  api.responder('GET', '/catalogos/tipos-retencion', pagina([{ id: 'ret1', nombre: 'Retención ISR', porcentaje: '10.0000', base: 'Subtotal', esDefecto: true }]));
  api.responder('GET', '/catalogos/formas-pago', pagina([{ id: FORMA_ID, nombre: 'Transferencia' }]));
  api.responder('GET', '/catalogos/cuentas-receptoras', pagina([{ id: CUENTA_ID, nombre: 'Cuenta DOP', banco: 'Banco', moneda: 'DOP' }, { id: 'c2', nombre: 'Cuenta USD', banco: 'Banco', moneda: 'USD' }]));
  api.responder('GET', '/catalogos/series', pagina([{ id: SERIE_ID, numero: 1, prefijo: 'A', moneda: 'USD', tipo: 'Normal', generaComprobante: false }]));
  api.responder('GET', '/catalogos/parametros-cobro', { body: { toleranciaCambioUsd: '5.00', toleranciaCambioDop: null } });
}
