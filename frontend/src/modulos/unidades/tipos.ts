/** Contratos de `InmuebleDtos.cs`. Enums como string; el dinero se envía como string decimal. */
export type TipoInmueble = 'Edificio' | 'Villa' | 'Local' | 'Terreno' | 'Otro';
export type TipoUnidad = 'Oficina' | 'Sala' | 'Apartamento' | 'Local' | 'Bodega' | 'Terreno' | 'Otro';
export type EstadoUnidad = 'Disponible' | 'Mantenimiento' | 'FueraDeServicio';
export type ModalidadAlquiler = 'Mensual' | 'MedioDia' | 'DiaCompleto';
export type Moneda = 'USD' | 'DOP';
export type CategoriaDocumento = 'Compraventa' | 'Titulo' | 'Otros';

export interface Pagina<T> {
  data: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface InmuebleResumen {
  id: string;
  nombre: string;
  direccion: string | null;
  tipo: TipoInmueble;
  propietariaId: string | null;
  plantillaContratoPorDefecto: string | null;
  renovacionDefectoMeses: number | null;
  activo: boolean;
  createdAt: string;
}

export interface Tarifa {
  modalidad: ModalidadAlquiler;
  moneda: Moneda;
  /** String decimal (AGENTS §6); para mostrarlo se pasa por `formatearImporte`. */
  importe: string;
}

export interface Unidad {
  id: string;
  inmuebleId: string;
  nombre: string;
  tipo: TipoUnidad;
  propietariaId: string | null;
  facturadoraPorDefectoId: string | null;
  estadoManual: EstadoUnidad;
  modalidades: ModalidadAlquiler[];
  tarifas: Tarifa[];
  metrosCuadrados: string | null;
  capacidad: number | null;
  habitaciones: number | null;
  banos: string | null;
  medidor: string | null;
  notas: string | null;
  activo: boolean;
  createdAt: string;
}

export interface InmuebleFicha extends Omit<InmuebleResumen, never> {
  notas: string | null;
  unidades: Unidad[];
}

export interface CuerpoInmueble {
  nombre: string;
  direccion: string | null;
  tipo: TipoInmueble;
  propietariaId: string | null;
  notas: string | null;
  plantillaContratoPorDefecto: string | null;
  renovacionDefectoMeses: number | null;
}

export interface CuerpoTarifa {
  modalidad: ModalidadAlquiler;
  moneda: Moneda;
  /** String decimal con 2 decimales: nunca un number. */
  importe: string;
}

export interface CuerpoUnidad {
  nombre: string;
  tipo: TipoUnidad;
  propietariaId: string | null;
  facturadoraPorDefectoId: string | null;
  estadoManual: EstadoUnidad;
  modalidades: ModalidadAlquiler[];
  tarifas: CuerpoTarifa[];
  metrosCuadrados: number | null;
  capacidad: number | null;
  habitaciones: number | null;
  banos: number | null;
  medidor: string | null;
  notas: string | null;
}

export interface DocumentoInmueble {
  id: string;
  inmuebleId: string;
  categoria: CategoriaDocumento;
  version: number;
  nombreOriginal: string;
  tamanoBytes: number;
  tipoContenido: string;
  createdAt: string;
}
