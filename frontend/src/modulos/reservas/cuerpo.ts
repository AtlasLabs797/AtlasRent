import { importeValido } from '../operacion/importes.ts';
import type { EstadoReservaApi } from '../operacion/etiquetas.ts';
import type { ModalidadApi, MonedaApi } from '../operacion/tipos.ts';

export type TipoReservaApi = 'Mensual' | 'Corta';

export interface DiaFormulario {
  fecha: string;
  /** Texto libre orientativo ("de 9 a 19 aprox."). No decide la capacidad (D11). */
  horaOrientativa: string;
}

export interface FormularioReserva {
  unidadId: string | null;
  destinatarioTipo: 'cliente' | 'prospecto';
  destinatarioId: string | null;
  tipo: TipoReservaApi;
  /** Mensual: día de entrada. Corta: se ignora, la entrada es el primer día. */
  entradaPrevista: string;
  /** Solo corta: medio día o día completo. */
  modalidadCorta: 'MedioDia' | 'DiaCompleto';
  moneda: MonedaApi;
  importe: string;
  anticipoImporte: string;
  anticipoNota: string;
  observaciones: string;
  dias: DiaFormulario[];
}

export const FORMULARIO_VACIO: FormularioReserva = {
  unidadId: null,
  destinatarioTipo: 'cliente',
  destinatarioId: null,
  tipo: 'Mensual',
  entradaPrevista: '',
  modalidadCorta: 'DiaCompleto',
  moneda: 'USD',
  importe: '',
  anticipoImporte: '',
  anticipoNota: '',
  observaciones: '',
  dias: [],
};

/** Primer día de la lista (ISO ordena como texto). */
export function primerDia(dias: DiaFormulario[]): string | null {
  const fechas = dias.map((d) => d.fecha).filter(Boolean).sort();
  return fechas[0] ?? null;
}

/**
 * Cuerpo de `POST/PUT /reservas`. La mensual bloquea un rango abierto: sin salida y sin días. La corta
 * entra y sale el mismo día (la API lo exige) y lleva sus días con la hora orientativa opcional; el
 * tipo de cada día lo pone la API según la modalidad.
 */
export function cuerpoReserva(f: FormularioReserva) {
  const corta = f.tipo === 'Corta';
  const entrada = corta ? (primerDia(f.dias) ?? '') : f.entradaPrevista;
  const anticipo = importeValido(f.anticipoImporte);
  return {
    unidadId: f.unidadId,
    clienteId: f.destinatarioTipo === 'cliente' ? f.destinatarioId : null,
    prospectoId: f.destinatarioTipo === 'prospecto' ? f.destinatarioId : null,
    tipo: f.tipo,
    entradaPrevista: entrada,
    salidaPrevista: corta ? entrada : null,
    modalidad: (corta ? f.modalidadCorta : 'Mensual') as ModalidadApi,
    moneda: f.moneda,
    importe: importeValido(f.importe) ?? '0',
    anticipoImporte: anticipo,
    anticipoNota: f.anticipoNota.trim() === '' ? null : f.anticipoNota.trim(),
    observaciones: f.observaciones.trim() === '' ? null : f.observaciones.trim(),
    dias: corta
      ? f.dias.map((d) => ({ fecha: d.fecha, horaOrientativa: d.horaOrientativa.trim() === '' ? null : d.horaOrientativa.trim() }))
      : [],
  };
}

export interface ErroresReserva {
  unidad?: string;
  destinatario?: string;
  entrada?: string;
  dias?: string;
  importe?: string;
  anticipo?: string;
  hora: Record<number, string>;
}

export function validarReserva(f: FormularioReserva): ErroresReserva {
  const e: ErroresReserva = { hora: {} };
  if (!f.unidadId) e.unidad = 'Elige la unidad.';
  if (!f.destinatarioId) e.destinatario = 'Elige un cliente o un prospecto.';
  if (f.tipo === 'Mensual' && !f.entradaPrevista) e.entrada = 'Indica el día de entrada.';
  if (f.tipo === 'Corta') {
    const fechas = f.dias.map((d) => d.fecha);
    if (fechas.length === 0 || fechas.some((x) => !x)) e.dias = 'Indica al menos un día.';
    else if (new Set(fechas).size !== fechas.length) e.dias = 'Hay un día repetido: si van dos personas, son dos reservas.';
  }
  if (f.importe.trim() !== '' && importeValido(f.importe) === null) e.importe = 'Escribe un importe válido, con hasta 2 decimales.';
  if (f.anticipoImporte.trim() !== '' && importeValido(f.anticipoImporte) === null) e.anticipo = 'Escribe un importe válido, con hasta 2 decimales.';
  f.dias.forEach((d, i) => {
    if (d.horaOrientativa.length > 60) e.hora[i] = 'Máximo 60 caracteres.';
  });
  return e;
}

export function hayErroresReserva(e: ErroresReserva): boolean {
  return Boolean(e.unidad || e.destinatario || e.entrada || e.dias || e.importe || e.anticipo || Object.keys(e.hora).length > 0);
}

/** Editar exige reserva Activa; cancelar admite también EnConflicto (ServicioReservas). */
export function reservaEditable(estado: EstadoReservaApi): boolean {
  return estado === 'Activa';
}

export function reservaCancelable(estado: EstadoReservaApi): boolean {
  return estado === 'Activa' || estado === 'EnConflicto';
}

/** La mensual se factura por contrato; solo la corta activa genera su propia factura. */
export function reservaFacturable(tipo: TipoReservaApi, estado: EstadoReservaApi, tieneCliente: boolean): boolean {
  return tipo === 'Corta' && estado === 'Activa' && tieneCliente;
}

/** Solo una reserva mensual viva (activa o en conflicto) se convierte en contrato. */
export function reservaConvertible(tipo: TipoReservaApi, estado: EstadoReservaApi): boolean {
  return tipo === 'Mensual' && (estado === 'Activa' || estado === 'EnConflicto');
}

export type BloqueoReserva = 'unidadNoDisponible' | 'facturaVigente' | 'unidadNoAlquilable' | 'otro';

/**
 * Clasifica un 409 para enseñarlo con el título que toca. El backend manda `DIS-001` cuando la unidad
 * está ocupada, y mensajes de negocio para la factura vigente y la unidad no alquilable.
 */
export function clasificarBloqueo(codigoReferencia: string | undefined, detalle: string | undefined): BloqueoReserva {
  if (codigoReferencia === 'DIS-001') return 'unidadNoDisponible';
  const d = (detalle ?? '').toLowerCase();
  if (d.includes('factura vigente')) return 'facturaVigente';
  if (d.includes('mantenimiento o fuera de servicio') || d.includes('está inactiva')) return 'unidadNoAlquilable';
  return 'otro';
}
