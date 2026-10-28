/**
 * Calendario de disponibilidad (§3.1). Contratos de `GET /calendario` y lógica pura de pintado.
 * "Ocupada" y "reservada" las calcula el backend por fechas: aquí solo se traduce a casillas.
 */
export type TipoDia = 'Libre' | 'Contrato' | 'ReservaMensual' | 'ReservaCorta' | 'Mantenimiento' | 'EstadoManual';
export type OrigenOcupacion = 'Contrato' | 'ReservaMensual' | 'ReservaCorta' | 'Mantenimiento';

export interface OcupacionDia {
  origen: OrigenOcupacion;
  origenId: string;
  tipoDia: 'MedioDia' | 'DiaCompleto' | null;
  finExclusivo: string | null;
}

export interface DiaCalendario {
  fecha: string;
  tipo: TipoDia;
  disponible: boolean;
  motivo: string;
  ocupaciones: OcupacionDia[];
}

export interface UnidadCalendario {
  unidadId: string;
  unidad: string;
  inmuebleId: string;
  inmueble: string;
  estadoManual: 'Disponible' | 'Mantenimiento' | 'FueraDeServicio';
  desde: string;
  hasta: string;
  dias: DiaCalendario[];
}

export interface RespuestaCalendario {
  unidades: UnidadCalendario[];
}

export interface PintadoDia {
  tipo: TipoDia;
  /** Solo hay un medio día reservado: la casilla se pinta a medias. */
  medioDia: boolean;
  letra: string;
}

const LETRAS: Record<TipoDia, string> = {
  Libre: '',
  Contrato: 'C',
  ReservaMensual: 'R',
  ReservaCorta: 'D',
  Mantenimiento: 'M',
  EstadoManual: '–',
};

/** Una reserva corta con un único medio día deja la mitad libre; con dos medios días o un día completo, no. */
export function pintarDia(dia: DiaCalendario): PintadoDia {
  const medios = dia.ocupaciones.filter((o) => o.tipoDia === 'MedioDia').length;
  const completos = dia.ocupaciones.filter((o) => o.tipoDia === 'DiaCompleto' || o.tipoDia === null).length;
  const medioDia = dia.tipo === 'ReservaCorta' && medios === 1 && completos === 0;
  const letra = dia.tipo === 'ReservaCorta' && medioDia ? '½' : LETRAS[dia.tipo];
  return { tipo: dia.tipo, medioDia, letra };
}

export function filtrarUnidades(unidades: UnidadCalendario[], texto: string): UnidadCalendario[] {
  const t = texto.trim().toLowerCase();
  return t === '' ? unidades : unidades.filter((u) => u.unidad.toLowerCase().includes(t));
}

/** Días libres de una unidad dentro del rango pedido. */
export function diasLibres(unidad: UnidadCalendario): number {
  return unidad.dias.filter((d) => d.disponible).length;
}

export const ETIQUETA_TIPO_DIA: Record<TipoDia, string> = {
  Libre: 'Libre',
  Contrato: 'Contrato',
  ReservaMensual: 'Reserva mensual',
  ReservaCorta: 'Reserva corta',
  Mantenimiento: 'Mantenimiento',
  EstadoManual: 'Fuera de servicio',
};

/** Ruta a la ficha del origen de una ocupación, si tiene pantalla. */
export function rutaDeOrigen(o: OcupacionDia): string | null {
  switch (o.origen) {
    case 'Contrato':
      return `/contratos/${o.origenId}`;
    case 'ReservaMensual':
    case 'ReservaCorta':
      return `/reservas/${o.origenId}`;
    default:
      return null;
  }
}
