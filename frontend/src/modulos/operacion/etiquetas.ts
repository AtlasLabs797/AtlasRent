import type { ModalidadApi, MonedaApi } from './tipos.ts';

/** Tono del `Badge` del DS. */
export type Tono = 'neutral' | 'accent' | 'success' | 'warning' | 'danger' | 'info' | 'ai';

export interface EtiquetaEstado {
  texto: string;
  tono: Tono;
}

export const MONEDAS: readonly MonedaApi[] = ['USD', 'DOP'];

export const etiquetaModalidad: Record<ModalidadApi, string> = {
  Mensual: 'Mensual',
  MedioDia: 'Medio día',
  DiaCompleto: 'Día completo',
};

export const etiquetaTipoReserva = { Mensual: 'Mensual', Corta: 'Corta' } as const;

export const ESTADOS_RESERVA = {
  Activa: { texto: 'Activa', tono: 'success' },
  EnConflicto: { texto: 'En conflicto', tono: 'danger' },
  Convertida: { texto: 'Convertida', tono: 'info' },
  Completada: { texto: 'Completada', tono: 'neutral' },
  Cancelada: { texto: 'Cancelada', tono: 'neutral' },
} as const satisfies Record<string, EtiquetaEstado>;
export type EstadoReservaApi = keyof typeof ESTADOS_RESERVA;

export const ESTADOS_COTIZACION = {
  Borrador: { texto: 'Borrador', tono: 'neutral' },
  Enviada: { texto: 'Enviada', tono: 'info' },
  Aceptada: { texto: 'Aceptada', tono: 'success' },
  Rechazada: { texto: 'Rechazada', tono: 'danger' },
} as const satisfies Record<string, EtiquetaEstado>;
export type EstadoCotizacionApi = keyof typeof ESTADOS_COTIZACION;

/** Flujo del contrato (§3.5), en el orden del `Stepper`. */
export const FLUJO_CONTRATO = ['Borrador', 'PendienteAprobacion', 'Aprobado', 'Activo', 'BajaProgramada', 'Finalizado', 'Liquidado'] as const;
export type EstadoContratoApi = (typeof FLUJO_CONTRATO)[number];

export const ESTADOS_CONTRATO: Record<EstadoContratoApi, EtiquetaEstado> = {
  Borrador: { texto: 'Borrador', tono: 'neutral' },
  PendienteAprobacion: { texto: 'Pendiente de aprobación', tono: 'warning' },
  Aprobado: { texto: 'Aprobado', tono: 'success' },
  Activo: { texto: 'Activo', tono: 'success' },
  BajaProgramada: { texto: 'Baja programada', tono: 'danger' },
  Finalizado: { texto: 'Finalizado', tono: 'neutral' },
  Liquidado: { texto: 'Liquidado', tono: 'neutral' },
};

/** Estado del contrato tal como llega (texto); un valor desconocido cae en Borrador. */
export function estadoContrato(valor: string): EstadoContratoApi {
  return (FLUJO_CONTRATO as readonly string[]).includes(valor) ? (valor as EstadoContratoApi) : 'Borrador';
}

export type ResultadoSolicitudApi = 'Pendiente' | 'Aprobada' | 'Rechazada' | 'Caducada';
const RESULTADOS: ResultadoSolicitudApi[] = ['Pendiente', 'Aprobada', 'Rechazada', 'Caducada'];

export function resultadoSolicitud(valor: string): ResultadoSolicitudApi {
  return (RESULTADOS as string[]).includes(valor) ? (valor as ResultadoSolicitudApi) : 'Pendiente';
}

export const etiquetaResultadoSolicitud: Record<ResultadoSolicitudApi, EtiquetaEstado> = {
  Pendiente: { texto: 'Pendiente', tono: 'warning' },
  Aprobada: { texto: 'Aprobada', tono: 'success' },
  Rechazada: { texto: 'Rechazada', tono: 'danger' },
  Caducada: { texto: 'Caducada', tono: 'neutral' },
};

export const REGLAS_RENOVACION = {
  Mensual: { texto: 'Mensual', meses: 1 },
  Trimestral: { texto: 'Trimestral', meses: 3 },
  Semestral: { texto: 'Semestral', meses: 6 },
  Anual: { texto: 'Anual', meses: 12 },
  Personalizada: { texto: 'Personalizada', meses: 0 },
} as const;
export type ReglaRenovacionApi = keyof typeof REGLAS_RENOVACION;

export const etiquetaTipoAnexo = {
  Normas: 'Normas',
  Inventario: 'Inventario',
  Mantenimiento: 'Mantenimiento',
  Otros: 'Otros',
} as const;
export type TipoAnexoApi = keyof typeof etiquetaTipoAnexo;
