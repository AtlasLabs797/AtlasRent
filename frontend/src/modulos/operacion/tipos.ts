/**
 * Contratos de la API que comparten calendario, cotizaciones, reservas y contratos. Los importes
 * llegan como número JSON (el backend serializa `decimal` así) y salen como string decimal: ninguna
 * aritmética de dinero en el frontend salvo la vista previa de `cotizaciones/calculo.ts`.
 */
export type Importe = number | string;
export type MonedaApi = 'USD' | 'DOP';
export type ModalidadApi = 'Mensual' | 'MedioDia' | 'DiaCompleto';

/** Envoltorio de las listas paginadas (`EnvoltorioAsync`). */
export interface Pagina<T> {
  data: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface ClienteLista {
  id: string;
  numero: number;
  nombre: string;
  tipo: string;
  email: string | null;
  telefono: string | null;
  activo: boolean;
}

export interface ProspectoLista {
  id: string;
  nombre: string;
  tipo: string;
  email: string | null;
  telefono: string | null;
  activo: boolean;
}

export interface InmuebleLista {
  id: string;
  nombre: string;
  direccion: string | null;
  tipo: string;
  plantillaContratoPorDefecto: string | null;
  renovacionDefectoMeses: number | null;
  activo: boolean;
}

export interface TarifaUnidad {
  modalidad: ModalidadApi;
  moneda: MonedaApi;
  importe: Importe;
}

export interface UnidadLista {
  id: string;
  inmuebleId: string;
  nombre: string;
  tipo: string;
  facturadoraPorDefectoId: string | null;
  estadoManual: 'Disponible' | 'Mantenimiento' | 'FueraDeServicio';
  modalidades: ModalidadApi[];
  tarifas: TarifaUnidad[];
  activo: boolean;
}

/** Unidad con el nombre de su inmueble: lo que necesitan los selectores. */
export interface UnidadConInmueble extends UnidadLista {
  inmueble: string;
}
