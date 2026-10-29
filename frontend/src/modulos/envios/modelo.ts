import { textosEnvios } from './textos.ts';

/**
 * Contratos de envíos, envío masivo y recordatorios (F4-02, F4-03, F4-05; §3.14). Lógica pura, sin
 * React ni Axios. Enums como string (`JsonStringEnumConverter`), importes ausentes: ninguna de estas
 * respuestas lleva dinero.
 */

export type TipoDocumento = 'Factura' | 'Recibo' | 'EstadoCuenta' | 'Cotizacion' | 'Contrato';
export type EstadoEnvio = 'Pendiente' | 'Enviando' | 'Enviado' | 'Entregado' | 'Leido' | 'Fallido';
export type EstadoRecordatorio = 'Pendiente' | 'Enviado' | 'Omitido';
export type ModoRecordatorios = 'Apagado' | 'Manual' | 'Automatico';

/** `EnvioFila`. */
export interface EnvioFila {
  id: string;
  tipo: TipoDocumento;
  documentoId: string;
  documentoAnio: number | null;
  clienteId: string | null;
  destinatarioEmail: string;
  destinatarioNombre: string | null;
  asunto: string;
  idioma: string;
  estado: EstadoEnvio;
  nombreArchivo: string;
  documentoModificadoAt: string | null;
  intentos: number;
  ultimoError: string | null;
  enviadoAt: string | null;
  createdAt: string;
}

/** `FiltroCuentas`: lo que el lote usa para elegir documentos. */
export interface FiltroCuentas {
  anio: number | null;
  serieId: string | null;
  moneda: 'USD' | 'DOP' | null;
  estado: string | null;
  estadoCobro: 'Pendiente' | 'Parcial' | 'Pagada' | null;
  clienteId: string | null;
  unidadId: string | null;
  vencidas: boolean;
  modificadas: boolean;
  noEnviadas: boolean;
  envioFallido: boolean;
  modificadasTrasEnvio: boolean;
  incluirProformas: boolean;
  incluirFianzas: boolean;
  desde: string | null;
  hasta: string | null;
  inmuebleId: string | null;
  propietariaId: string | null;
  facturadoraId: string | null;
}

export interface OpcionesMasivo {
  vencidas: boolean;
  noEnviadas: boolean;
  envioFallido: boolean;
  modificadasTrasEnvio: boolean;
  moneda: 'USD' | 'DOP' | null;
  forzar: boolean;
}

export interface CuerpoMasivo {
  filtro: FiltroCuentas;
  forzar: boolean;
}

/** Cuerpo de `preparar` y `confirmar`: el MISMO en los dos, para que lo confirmado sea lo revisado. */
export function cuerpoMasivo(o: OpcionesMasivo): CuerpoMasivo {
  return {
    filtro: {
      anio: null,
      serieId: null,
      moneda: o.moneda,
      estado: null,
      estadoCobro: null,
      clienteId: null,
      unidadId: null,
      vencidas: o.vencidas,
      modificadas: false,
      noEnviadas: o.noEnviadas,
      envioFallido: o.envioFallido,
      modificadasTrasEnvio: o.modificadasTrasEnvio,
      incluirProformas: false,
      incluirFianzas: false,
      desde: null,
      hasta: null,
      inmuebleId: null,
      propietariaId: null,
      facturadoraId: null,
    },
    forzar: o.forzar,
  };
}

export interface DocumentoMasivo {
  documentoId: string;
  tipoDocumento: TipoDocumento;
  numero: string;
  clienteId: string;
  cliente: string | null;
  destinatarioEmail: string;
  destinatarioNombre: string | null;
  asunto: string;
  cuerpo: string;
  nombreArchivo: string;
  idioma: string;
}

export interface ExcluidoMasivo {
  documentoId: string;
  tipo: string;
  numero: string | null;
  cliente: string | null;
  motivo: string;
  detalle: string;
}

export interface ResumenMasivo {
  total: number;
  totalExcluidos: number;
  documentos: DocumentoMasivo[];
  excluidos: ExcluidoMasivo[];
  filtro: FiltroCuentas;
  idioma: string;
  aviso: string;
}

export interface LoteEnvio {
  loteId: string;
  total: number;
  excluidos: number;
  idioma: string;
  aviso: string;
}

export interface FalloMasivo {
  envioId: string;
  documentoId: string;
  tipoDocumento: TipoDocumento;
  cliente: string | null;
  destinatarioEmail: string;
  intentos: number;
  motivo: string | null;
}

export interface ProgresoLote {
  loteId: string;
  total: number;
  enviados: number;
  fallidos: number;
  pendientes: number;
  fallidosDetalle: FalloMasivo[];
}

export interface ReintentoLote {
  loteId: string;
  reintentados: number;
  pendientes: number;
  aviso: string;
}

/** `RecordatorioFila`. */
export interface RecordatorioFila {
  id: string;
  clienteId: string;
  cliente: string;
  unidadId: string | null;
  unidad: string | null;
  fecha: string;
  estado: EstadoRecordatorio;
  envioId: string | null;
  destinatario: string | null;
  motivoOmision: string | null;
  createdAt: string;
}

type TonoBadge = 'neutral' | 'info' | 'success' | 'danger' | 'warning';

/** Estado de un envío: etiqueta y tono. `Enviando` es un estado propio (envío en curso o atascado). */
export function estadoDeEnvio(estado: EstadoEnvio): { etiqueta: string; tono: TonoBadge } {
  const mapa: Record<EstadoEnvio, TonoBadge> = {
    Pendiente: 'neutral',
    Enviando: 'warning',
    Enviado: 'neutral',
    Entregado: 'info',
    Leido: 'success',
    Fallido: 'danger',
  };
  return { etiqueta: textosEnvios.estados[estado], tono: mapa[estado] };
}

export function etiquetaTipo(tipo: string): string {
  return (textosEnvios.tipos as Record<string, string>)[tipo] ?? tipo;
}

export function etiquetaMotivoExclusion(motivo: string): string {
  return (textosEnvios.masivo.motivos as Record<string, string>)[motivo] ?? motivo;
}

/** Porcentaje entero de lo ya resuelto (enviado o fallido) sobre el total del lote. */
export function porcentajeLote(p: Pick<ProgresoLote, 'total' | 'enviados' | 'fallidos'>): number {
  if (p.total <= 0) return 0;
  return Math.min(100, Math.floor(((p.enviados + p.fallidos) * 100) / p.total));
}

/** El lote ha terminado cuando no queda nada pendiente; con fallidos, se puede reintentar. */
export function loteTerminado(p: Pick<ProgresoLote, 'pendientes'>): boolean {
  return p.pendientes === 0;
}

/** Modo de recordatorios para enseñarlo; un valor desconocido se muestra tal cual. */
export function etiquetaModo(modo: string): string {
  return (textosEnvios.recordatorios.modos as Record<string, string>)[modo] ?? modo;
}
