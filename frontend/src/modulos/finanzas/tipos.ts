import type { Importe } from './decimal.ts';

/**
 * Contratos de la API de finanzas (backend: DTOs de Cuenta, Factura, FacturaPeriodicas, Cobro, Fianza,
 * TipoCambio, Configuracion). El `decimal` viaja como número JSON (`Importe`) y se envía como texto
 * decimal; los enums viajan como su nombre.
 */
export type Moneda = 'USD' | 'DOP';
export const MONEDAS: readonly Moneda[] = ['USD', 'DOP'];

export type TipoFilaCuenta = 'Factura' | 'Proforma' | 'Fianza';
export type EstadoFactura = 'Borrador' | 'Emitida' | 'Anulada' | 'Convertida';
export type EstadoCobroFactura = 'Pendiente' | 'Parcial' | 'Pagada' | 'Anulada';
export type EstadoCobro = 'Registrado' | 'Anulado';
export type EstadoFianza = 'Pendiente' | 'Parcial' | 'Retenida' | 'EnLiquidacion' | 'Devuelta' | 'RetenidaParcial';
export type TipoSerie = 'Normal' | 'Proforma' | 'Fiscal' | 'Historica';

export interface Pagina<T> {
  data: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface CuentaFila {
  tipo: TipoFilaCuenta;
  id: string;
  serieId: string | null;
  serieNumero: number | null;
  numero: number | null;
  comprobanteTipo: string | null;
  comprobanteNumero: number | null;
  fecha: string | null;
  clienteId: string;
  cliente: string | null;
  unidadId: string | null;
  unidad: string | null;
  contratoId: string | null;
  concepto: string | null;
  moneda: Moneda;
  total: Importe | null;
  retencion: Importe | null;
  cobrado: Importe | null;
  pendiente: Importe | null;
  saldoADevolver: Importe | null;
  vencimiento: string | null;
  diasVencida: number | null;
  vencida: boolean | null;
  estado: string;
  modificada: boolean | null;
  tieneReservaConAnticipo: boolean;
  estadoEnvio: string | null;
  modificadaTrasEnvio: boolean | null;
  cobrable: boolean;
}

export interface TotalesCuenta {
  moneda: Moneda;
  total: Importe;
  retencion: Importe;
  cobrado: Importe;
  pendiente: Importe;
}

export interface CuentasRespuesta extends Pagina<CuentaFila> {
  /** `null` para quien no tiene `informe.ver`: nunca se calcula en el cliente. */
  totales: TotalesCuenta[] | null;
  filtro: Record<string, unknown>;
}

export interface FacturaResumen {
  id: string;
  clienteId: string;
  clienteNombre: string | null;
  unidadId: string | null;
  unidadNombre: string | null;
  contratoId: string | null;
  serieId: string;
  serieNumero: number;
  numero: number | null;
  estado: EstadoFactura;
  tipoSerie: TipoSerie;
  moneda: Moneda;
  fechaEmision: string | null;
  fechaVencimiento: string;
  concepto: string;
  subtotal: Importe;
  itbis: Importe;
  total: Importe;
  retencion: Importe;
  netoACobrar: Importe;
  lineas: number;
  createdAt: string;
  cobrado: Importe;
  ajustado: Importe;
  pendiente: Importe;
  estadoCobro: EstadoCobroFactura;
  vencida: boolean;
  retencionSinComprobante: boolean;
  referenciaEfactura: string | null;
}

export interface FacturaLinea {
  id: string;
  posicion: number;
  tipo: string;
  concepto: string;
  cantidad: Importe;
  precioUnitario: Importe;
  importe: Importe;
  tipoItbisId: string | null;
  tipoItbisNombre: string | null;
  porcentajeItbis: Importe | null;
  itbisImporte: Importe;
  tipoRetencionId: string | null;
  tipoRetencionNombre: string | null;
  porcentajeRetencion: Importe | null;
  baseRetencion: string | null;
}

export interface Factura extends Omit<FacturaResumen, 'lineas'> {
  tipoCambioValor: Importe | null;
  tipoCambioFecha: string | null;
  updatedAt: string | null;
  lineas: FacturaLinea[];
}

export interface CobroAfectadoPorFactura {
  cobroId: string;
  fechaEfectiva: string;
  moneda: Moneda;
  importeRecibido: Importe;
  importeDeEstaFactura: Importe;
  importeQueSigueVigente: Importe;
  cambioDelSaldoAFavor: Importe;
  seAnulaEntero: boolean;
}

export interface FacturaAnulacionSimulacion {
  facturaId: string;
  numero: number | null;
  estado: EstadoFactura;
  netoACobrar: Importe;
  comprobanteFiscalSeAnula: boolean;
  numeroSeConserva: boolean;
  cobrosQueSeAnulan: CobroAfectadoPorFactura[];
  cobrosQueSeToCan: CobroAfectadoPorFactura[];
}

export interface FacturaDuplicadaParecida {
  facturaId: string;
  numero: number | null;
  estado: EstadoFactura;
  total: Importe;
  concepto: string;
}

export interface FacturaDuplicacionRespuesta {
  factura: FacturaResumen;
  similares: FacturaDuplicadaParecida[];
}

export interface FacturaItemSimulado {
  contratoId: string;
  clienteId: string;
  moneda: Moneda;
  periodoInicio: string;
  periodoFin: string;
  diasFacturables: number;
  diasDelMes: number;
  importe: Importe;
  prorrateado: boolean;
  vencimiento: string;
  yaExiste: boolean;
  facturaExistenteNumero: number | null;
  motivoOmision: string | null;
  seEmite: boolean;
}

export interface SimulacionPeriodicaRespuesta {
  periodo: string;
  periodoInicio: string;
  periodoFin: string;
  serieId: string;
  serieNumero: number;
  importeTotal: Importe;
  items: FacturaItemSimulado[];
}

export interface GeneracionPeriodicaRespuesta {
  periodo: string;
  serieNumero: number;
  creadas: number;
  omitidas: number;
  importeEmitido: Importe;
  facturas: Array<{ facturaId: string; contratoId: string; numero: number | null; importe: Importe; vencimiento: string }>;
  omitidos: Array<{ contratoId: string; motivo: string; facturaExistenteNumero: number | null }>;
  fallidas: number;
  fallidos: Array<{ contratoId: string; motivo: string }>;
}

export interface CobroResumen {
  id: string;
  clienteId: string;
  clienteNombre: string | null;
  contratoId: string | null;
  fechaEfectiva: string;
  monedaRecibida: Moneda;
  importeRecibido: Importe;
  formaPagoNombre: string | null;
  cuentaReceptoraNombre: string | null;
  tipoCambioValor: Importe;
  tipoCambioFecha: string;
  saldoFavorUsado: Importe;
  excedente: Importe;
  estado: EstadoCobro;
  createdAt: string;
}

export interface CobroAsignacion {
  id: string;
  facturaId: string | null;
  fianzaId: string | null;
  facturaNumero: number | null;
  facturaVencimiento: string | null;
  facturaMoneda: Moneda | null;
  importe: Importe;
  importeRecibido: Importe | null;
  tipoCambioValor: Importe | null;
  vigente: boolean;
}

export interface Cobro extends CobroResumen {
  referencia: string | null;
  totalAsignado: Importe;
  anuladoAt: string | null;
  motivoAnulacion: string | null;
  asignaciones: CobroAsignacion[];
}

export interface CobroAsignacionSimulada {
  facturaId: string | null;
  fianzaId: string | null;
  numero: number | null;
  fechaVencimiento: string | null;
  monedaDestino: Moneda;
  importe: Importe;
  importeRecibido: Importe | null;
  tipoCambioValor: Importe | null;
  pendienteAntes: Importe;
}

export interface CobroSimulacion {
  clienteId: string;
  contratoId: string | null;
  monedaRecibida: Moneda;
  importeRecibido: Importe;
  saldoFavorUsado: Importe;
  saldoAFavorDisponible: Importe;
  totalAsignado: Importe;
  excedente: Importe;
  saldoAFavorSeActualiza: boolean;
  asignaciones: CobroAsignacionSimulada[];
  deudaEnOtraMoneda: Array<{ moneda: Moneda; pendiente: Importe; facturas: number }>;
}

/** Cuerpo de `POST /cobros` y `POST /cobros/simular`; los importes van como texto decimal. */
export interface CobroPeticion {
  clienteId: string;
  contratoId?: string;
  fechaEfectiva: string;
  monedaRecibida: Moneda;
  importeRecibido: string;
  formaPagoId: string;
  referencia?: string;
  cuentaReceptoraId: string;
  usarSaldoAFavor?: string;
  asignaciones?: Array<{ facturaId?: string; fianzaId?: string; importe: string }>;
  facturasSeleccionadas?: string[];
  tipoCambioAplicado?: string;
}

export interface FianzaResumen {
  id: string;
  contratoId: string;
  importeExigido: Importe;
  moneda: Moneda;
  numeroFianzas: number;
  cobrado: Importe;
  devuelto: Importe;
  descontado: Importe;
  saldo: Importe;
  estado: EstadoFianza;
  createdAt: string;
}

export interface FianzaDevolucion {
  id: string;
  importe: Importe;
  importeDescontado: Importe;
  motivo: string | null;
  fecha: string;
}

export interface Fianza {
  fianza: FianzaResumen;
  devoluciones: FianzaDevolucion[];
}

export interface FormaPago {
  id: string;
  nombre: string;
  activa: boolean;
}

export interface CuentaReceptora {
  id: string;
  nombre: string;
  banco: string | null;
  moneda: Moneda;
  activa: boolean;
}

export interface Serie {
  id: string;
  numero: number;
  prefijo: string | null;
  moneda: Moneda | null;
  tipo: TipoSerie;
  activa: boolean;
}

export interface TipoCambioActual {
  fecha: string;
  moneda: Moneda;
  valor: Importe;
}
