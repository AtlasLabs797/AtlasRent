import { diasEntre } from '../operacion/fechas.ts';
import { FLUJO_CONTRATO, REGLAS_RENOVACION, type EstadoContratoApi, type ReglaRenovacionApi } from '../operacion/etiquetas.ts';
import { importeValido } from '../operacion/importes.ts';
import type { MonedaApi } from '../operacion/tipos.ts';

/** Lógica pura de contratos (§3.5): flujo, formularios, baja y revisión de salida. */

// ---------------------------------------------------------------- flujo

export interface PasoFlujo {
  label: string;
  meta?: string;
  status: 'done' | 'current' | 'todo';
}

const ETIQUETA_PASO: Record<EstadoContratoApi, string> = {
  Borrador: 'Borrador',
  PendienteAprobacion: 'Solicitud de aprobación',
  Aprobado: 'Aprobado',
  Activo: 'Activo',
  BajaProgramada: 'Baja programada',
  Finalizado: 'Finalizado',
  Liquidado: 'Liquidado',
};

/** Pasos del `Stepper`: los anteriores al estado actual, hechos; el actual, en curso. `metaActual` es la fecha de entrada al estado. */
export function pasosDelFlujo(estado: EstadoContratoApi, metaActual?: string): PasoFlujo[] {
  const actual = FLUJO_CONTRATO.indexOf(estado);
  return FLUJO_CONTRATO.map((e, i) => ({
    label: ETIQUETA_PASO[e],
    status: i < actual ? 'done' : i === actual ? 'current' : 'todo',
    ...(i === actual && metaActual ? { meta: metaActual } : {}),
  }));
}

/** Qué se puede hacer en cada estado. `contrato.estado` (aprobar, activar, baja…) lo comprueba la pantalla aparte. */
export interface AccionesContrato {
  editar: boolean;
  enviarAprobacion: boolean;
  decidirAprobacion: boolean;
  activar: boolean;
  cambiar: boolean;
  incrementar: boolean;
  darBaja: boolean;
  anularBaja: boolean;
  finalizar: boolean;
  liquidar: boolean;
  revisarSalida: boolean;
}

export function accionesDeEstado(estado: EstadoContratoApi): AccionesContrato {
  const vigente = estado === 'Activo' || estado === 'BajaProgramada';
  return {
    editar: estado === 'Borrador',
    enviarAprobacion: estado === 'Borrador',
    decidirAprobacion: estado === 'PendienteAprobacion',
    activar: estado === 'Aprobado',
    cambiar: estado === 'Activo',
    incrementar: estado !== 'Finalizado' && estado !== 'Liquidado',
    darBaja: estado === 'Activo',
    anularBaja: estado === 'BajaProgramada',
    finalizar: estado === 'BajaProgramada',
    liquidar: estado === 'Finalizado',
    revisarSalida: vigente || estado === 'Finalizado',
  };
}

/** Finalizar solo cuando la fecha de baja ya llegó (BUG-025); antes, la API responde 409. */
export function puedeFinalizar(fechaBaja: string | null, hoy: string): boolean {
  return fechaBaja !== null && fechaBaja <= hoy;
}

// ---------------------------------------------------------------- baja

/** La baja se programa en cualquier fecha, pero no antes de hoy ni del inicio del contrato. */
export function fechaMinimaBaja(inicioContrato: string, hoy: string): string {
  return inicioContrato > hoy ? inicioContrato : hoy;
}

export function validarBaja(fechaFin: string, inicioContrato: string, hoy: string): string | null {
  if (!fechaFin) return 'Indica la fecha efectiva de fin.';
  if (fechaFin < fechaMinimaBaja(inicioContrato, hoy)) return 'La baja no puede ser anterior a hoy ni al inicio del contrato.';
  return null;
}

/** Días de preaviso desde hoy: informativo (recomendado 30, sin penalización ni obligación). */
export function diasDePreaviso(fechaFin: string, hoy: string): number {
  return diasEntre(hoy, fechaFin);
}

// ---------------------------------------------------------------- formulario

export interface FormularioContrato {
  clienteId: string | null;
  unidadId: string | null;
  reservaId: string | null;
  facturadoraId: string | null;
  fechaInicio: string;
  duracionInicialMeses: string;
  renta: string;
  moneda: MonedaApi;
  diaPago: string;
  fianza: string;
  reglaRenovacion: ReglaRenovacionApi;
  renovacionMeses: string;
  plantillaContratoId: string;
  observaciones: string;
}

export const CONTRATO_VACIO: FormularioContrato = {
  clienteId: null,
  unidadId: null,
  reservaId: null,
  facturadoraId: null,
  fechaInicio: '',
  duracionInicialMeses: '12',
  renta: '',
  moneda: 'USD',
  diaPago: '1',
  fianza: '0',
  reglaRenovacion: 'Anual',
  renovacionMeses: '12',
  plantillaContratoId: '',
  observaciones: '',
};

/** Al elegir una regla fija (mensual, trimestral…) los meses salen de la regla; la personalizada los pide. */
export function mesesDeRegla(regla: ReglaRenovacionApi, actual: string): string {
  const meses = REGLAS_RENOVACION[regla].meses;
  return meses > 0 ? String(meses) : actual;
}

function entero(texto: string): number | null {
  return /^\d+$/.test(texto.trim()) ? Number(texto.trim()) : null;
}

/** Cuerpo de `POST/PUT /contratos`. Importes como texto decimal. */
export function cuerpoContrato(f: FormularioContrato) {
  return {
    clienteId: f.clienteId,
    unidadId: f.unidadId,
    reservaId: f.reservaId,
    facturadoraId: f.facturadoraId,
    fechaInicio: f.fechaInicio || null,
    duracionInicialMeses: entero(f.duracionInicialMeses),
    renta: importeValido(f.renta),
    moneda: f.moneda,
    diaPago: entero(f.diaPago),
    fianza: importeValido(f.fianza),
    reglaRenovacion: f.reglaRenovacion,
    renovacionMeses: entero(f.renovacionMeses),
    plantillaContratoId: f.plantillaContratoId.trim() === '' ? null : f.plantillaContratoId.trim(),
    observaciones: f.observaciones.trim() === '' ? null : f.observaciones.trim(),
  };
}

export type ErroresContrato = Partial<Record<'cliente' | 'unidad' | 'facturadora' | 'fechaInicio' | 'duracion' | 'renta' | 'diaPago' | 'fianza' | 'renovacion' | 'plantilla', string>>;

export function validarContrato(f: FormularioContrato): ErroresContrato {
  const e: ErroresContrato = {};
  if (!f.clienteId) e.cliente = 'Elige el cliente.';
  if (!f.unidadId) e.unidad = 'Elige la unidad.';
  else if (!f.facturadoraId) e.facturadora = 'La unidad no tiene subempresa facturadora por defecto: configúrala en la unidad.';
  if (!f.fechaInicio) e.fechaInicio = 'Indica la fecha de inicio.';
  const duracion = entero(f.duracionInicialMeses);
  if (duracion === null || duracion < 1) e.duracion = 'Un mes o más.';
  const renta = importeValido(f.renta);
  if (renta === null || Number(renta) <= 0) e.renta = 'La renta debe ser mayor que cero, con hasta 2 decimales.';
  const dia = entero(f.diaPago);
  if (dia === null || dia < 1 || dia > 31) e.diaPago = 'Entre 1 y 31.';
  if (importeValido(f.fianza) === null) e.fianza = 'Escribe la fianza (0 si no hay), con hasta 2 decimales.';
  const meses = entero(f.renovacionMeses);
  const esperados = REGLAS_RENOVACION[f.reglaRenovacion].meses;
  if (meses === null || meses < 1) e.renovacion = 'Un mes o más.';
  else if (esperados > 0 && meses !== esperados) e.renovacion = `La renovación ${REGLAS_RENOVACION[f.reglaRenovacion].texto.toLowerCase()} dura ${esperados} meses.`;
  if (f.plantillaContratoId.trim() === '') e.plantilla = 'Elige la plantilla: es la que se congela al aprobar.';
  return e;
}

export function hayErroresContrato(e: ErroresContrato): boolean {
  return Object.keys(e).length > 0;
}

// ---------------------------------------------------------------- cambio en contrato vigente

export interface FormularioCambio {
  renta: string;
  diaPago: string;
  fianza: string;
  reglaRenovacion: ReglaRenovacionApi;
  renovacionMeses: string;
  plantillaContratoId: string;
  observaciones: string;
  motivo: string;
}

/** El contrato vigente no cambia de moneda; la envía tal cual. El motivo es obligatorio: va escrito en la versión. */
export function cuerpoCambio(f: FormularioCambio, moneda: MonedaApi) {
  return {
    renta: importeValido(f.renta),
    moneda,
    diaPago: entero(f.diaPago),
    fianza: importeValido(f.fianza),
    reglaRenovacion: f.reglaRenovacion,
    renovacionMeses: entero(f.renovacionMeses),
    plantillaContratoId: f.plantillaContratoId.trim(),
    observaciones: f.observaciones.trim() === '' ? null : f.observaciones.trim(),
    motivo: f.motivo.trim(),
  };
}

export function validarCambio(f: FormularioCambio): { motivo?: string; renta?: string; general?: string } {
  const e: { motivo?: string; renta?: string } = {};
  if (f.motivo.trim() === '') e.motivo = 'El motivo es obligatorio: queda escrito en la versión nueva.';
  const renta = importeValido(f.renta);
  if (renta === null || Number(renta) <= 0) e.renta = 'La renta debe ser mayor que cero.';
  return e;
}

// ---------------------------------------------------------------- incrementos

export type TipoIncrementoApi = 'Porcentaje' | 'ImporteFijo';

export interface FormularioIncremento {
  tipo: TipoIncrementoApi;
  valor: string;
  fechaVigencia: string;
  comentario: string;
}

/** Porcentaje o importe, nunca los dos: el DTO rechaza el campo que sobra. */
export function cuerpoIncremento(f: FormularioIncremento) {
  const valor = importeValido(f.valor);
  return {
    tipo: f.tipo,
    porcentaje: f.tipo === 'Porcentaje' ? valor : null,
    importe: f.tipo === 'ImporteFijo' ? valor : null,
    fechaVigencia: f.fechaVigencia || null,
    comentario: f.comentario.trim() === '' ? null : f.comentario.trim(),
  };
}

export function validarIncremento(f: FormularioIncremento): { valor?: string; fecha?: string } {
  const e: { valor?: string; fecha?: string } = {};
  const v = importeValido(f.valor);
  if (v === null || Number(v) <= 0) e.valor = f.tipo === 'Porcentaje' ? 'Escribe el porcentaje (un 5 % es 5).' : 'Escribe el importe a sumar.';
  if (!f.fechaVigencia) e.fecha = 'Indica desde cuándo entra en vigor.';
  return e;
}

export type EstadoIncremento = 'Pendiente' | 'Aplicado' | 'Cancelado';

export function estadoDeIncremento(i: { aplicado: boolean; cancelado: boolean }): EstadoIncremento {
  return i.cancelado ? 'Cancelado' : i.aplicado ? 'Aplicado' : 'Pendiente';
}

// ---------------------------------------------------------------- revisión de salida

export type EstadoItemUi = 'ok' | 'faltante' | 'danado';
export type EstadoRevisionApi = 'Presente' | 'Ausente' | 'Danado';

export interface ItemRevision {
  id: string;
  name: string;
  qty: number;
  state: EstadoItemUi | undefined;
  amount: string;
  nota: string;
}

const A_API: Record<EstadoItemUi, EstadoRevisionApi> = { ok: 'Presente', faltante: 'Ausente', danado: 'Danado' };
/** Suma exacta de importes escritos a mano, en céntimos: solo para proponer el total del faltante. */
export function sumaImportes(importes: string[]): string {
  let total = 0n;
  for (const texto of importes) {
    const v = importeValido(texto);
    if (v === null) continue;
    const [entera = '0', dec = ''] = v.split('.');
    total += BigInt(entera) * 100n + BigInt(dec.padEnd(2, '0'));
  }
  const s = total.toString().padStart(3, '0');
  return `${s.slice(0, -2)}.${s.slice(-2)}`;
}

/** Cada elemento ausente o dañado necesita su nota (RevisionInventarioLineaPeticion): es lo que explica el faltante. */
export function validarRevision(items: ItemRevision[]): { sinRevisar: number; sinNota: string[] } {
  return {
    sinRevisar: items.filter((i) => i.state === undefined).length,
    sinNota: items.filter((i) => (i.state === 'faltante' || i.state === 'danado') && i.nota.trim() === '').map((i) => i.id),
  };
}

/** Cuerpo de `POST /contratos/{id}/revision-inventario`: se firma entera, con todas las líneas. */
export function cuerpoRevision(items: ItemRevision[], observacion: string) {
  const total = sumaImportes(items.filter((i) => i.state && i.state !== 'ok').map((i) => i.amount));
  return {
    lineas: items.map((i) => ({
      contratoInventarioItemId: i.id,
      estado: A_API[i.state ?? 'ok'],
      nota: i.nota.trim() === '' ? null : i.nota.trim(),
    })),
    importePropuesto: total === '0.00' ? null : total,
    observacion: observacion.trim() === '' ? null : observacion.trim(),
  };
}
