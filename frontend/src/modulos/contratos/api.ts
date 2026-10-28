import { keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { clienteHttp } from '../../api/cliente';
import type { EstadoContratoApi, ReglaRenovacionApi, ResultadoSolicitudApi, TipoAnexoApi } from '../operacion/etiquetas.ts';
import type { Importe, MonedaApi, Pagina } from '../operacion/tipos.ts';
import type { EstadoRevisionApi, TipoIncrementoApi, cuerpoCambio, cuerpoContrato, cuerpoIncremento, cuerpoRevision } from './modelo.ts';

export interface ContratoResumen {
  id: string;
  clienteId: string;
  cliente: string | null;
  unidadId: string;
  unidad: string | null;
  facturadoraId: string;
  fechaInicio: string;
  fechaFinVigente: string;
  renta: Importe;
  moneda: MonedaApi;
  estado: EstadoContratoApi;
  estadoActualDesde: string | null;
  fechaBajaProgramada: string | null;
  createdAt: string;
}

export interface VersionContrato {
  numero: number;
  cambio: string;
  rentaAnterior: Importe | null;
  rentaNueva: Importe | null;
  moneda: MonedaApi;
  alcance: string;
  createdAt: string;
}

export interface SolicitudAprobacion {
  id: string;
  resultado: ResultadoSolicitudApi;
  huellaContenido: string;
  solicitadoAt: string;
  resueltoPorId: string | null;
  resueltoAt: string | null;
  comentario: string | null;
  motivoCaducidad: string | null;
}

export interface ContratoDetalle {
  id: string;
  clienteId: string;
  cliente: string | null;
  unidadId: string;
  unidad: string | null;
  facturadoraId: string;
  facturadora: string | null;
  fechaInicio: string;
  duracionInicialMeses: number;
  fechaFinInicial: string;
  fechaFinVigente: string;
  renta: Importe;
  moneda: MonedaApi;
  diaPago: number;
  fianza: Importe;
  reglaRenovacion: ReglaRenovacionApi;
  renovacionMeses: number;
  plantillaContratoId: string;
  plantillaContratoCongeladaId: string | null;
  plantillaCongeladaAt: string | null;
  estado: EstadoContratoApi;
  estadoActualDesde: string | null;
  estadoActualComentario: string | null;
  fechaBajaProgramada: string | null;
  fechaFinReal: string | null;
  observaciones: string | null;
  versiones: VersionContrato[];
  solicitudes: SolicitudAprobacion[];
  createdAt: string;
}

export interface Incremento {
  id: string;
  tipo: TipoIncrementoApi;
  porcentaje: Importe | null;
  importe: Importe | null;
  fechaVigencia: string;
  aplicado: boolean;
  aplicadoAt: string | null;
  cancelado: boolean;
  canceladoAt: string | null;
  rentaAntes: Importe | null;
  rentaDespues: Importe | null;
}

export interface ItemInventario {
  id: string;
  inventarioBaseId: string | null;
  nombre: string;
  cantidad: Importe;
  notas: string | null;
  orden: number;
}

export interface RevisionLinea {
  id: string;
  contratoInventarioItemId: string;
  nombre: string;
  cantidad: Importe;
  estado: EstadoRevisionApi;
  nota: string | null;
}

export interface Revision {
  id: string;
  contratoId: string;
  firmadaAt: string;
  firmadaPorId: string | null;
  importePropuesto: Importe | null;
  observacion: string | null;
  lineas: RevisionLinea[];
}

export interface FiltrosContratos {
  page: number;
  q: string;
  estado: EstadoContratoApi | '';
}
export const TAMANO_PAGINA = 25;

export function useContratos(f: FiltrosContratos) {
  return useQuery({
    queryKey: ['contratos', 'lista', f],
    placeholderData: keepPreviousData,
    queryFn: async () =>
      (
        await clienteHttp.get<Pagina<ContratoResumen>>('/contratos', {
          params: { page: f.page, pageSize: TAMANO_PAGINA, ...(f.q ? { q: f.q } : {}), ...(f.estado ? { estado: f.estado } : {}) },
        })
      ).data,
  });
}

export function useContrato(id: string | undefined) {
  return useQuery({
    queryKey: ['contratos', 'detalle', id],
    enabled: Boolean(id),
    queryFn: async () => (await clienteHttp.get<ContratoDetalle>(`/contratos/${id}`)).data,
  });
}

export function useIncrementos(id: string) {
  return useQuery({
    queryKey: ['contratos', 'incrementos', id],
    queryFn: async () => (await clienteHttp.get<Incremento[]>(`/contratos/${id}/incrementos`)).data,
  });
}

export function useInventarioContrato(id: string) {
  return useQuery({
    queryKey: ['contratos', 'inventario', id],
    queryFn: async () => (await clienteHttp.get<ItemInventario[]>(`/contratos/${id}/inventario`)).data,
  });
}

/** 404 = todavía no hay revisión firmada: no es un error de pantalla. */
export function useRevision(id: string) {
  return useQuery({
    queryKey: ['contratos', 'revision', id],
    retry: false,
    queryFn: async () => {
      try {
        return (await clienteHttp.get<Revision>(`/contratos/${id}/revision-inventario`)).data;
      } catch (e) {
        if ((e as { response?: { status?: number } }).response?.status === 404) return null;
        throw e;
      }
    },
  });
}

function refrescar(cliente: QueryClient) {
  return Promise.all([cliente.invalidateQueries({ queryKey: ['contratos'] }), cliente.invalidateQueries({ queryKey: ['calendario'] })]);
}

export function useGuardarContrato(id?: string) {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (cuerpo: ReturnType<typeof cuerpoContrato>) =>
      (id ? await clienteHttp.put<ContratoDetalle>(`/contratos/${id}`, cuerpo) : await clienteHttp.post<ContratoDetalle>('/contratos', cuerpo)).data,
    onSuccess: () => refrescar(cliente),
  });
}

/** Transiciones del flujo: cada una es un POST a su ruta (solicitud, aprobacion, rechazo, activacion, baja, anular-baja, finalizacion, liquidacion). */
export type TransicionContrato = 'solicitud' | 'aprobacion' | 'rechazo' | 'activacion' | 'baja' | 'anular-baja' | 'finalizacion' | 'liquidacion';

export function useTransicion(id: string, ruta: TransicionContrato) {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (cuerpo?: Record<string, unknown>) => (await clienteHttp.post<ContratoResumen>(`/contratos/${id}/${ruta}`, cuerpo ?? {})).data,
    onSuccess: () => refrescar(cliente),
  });
}

export function useCambioContrato(id: string) {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (cuerpo: ReturnType<typeof cuerpoCambio>) => (await clienteHttp.post<ContratoResumen>(`/contratos/${id}/cambios`, cuerpo)).data,
    onSuccess: () => refrescar(cliente),
  });
}

export function useGuardarIncremento(id: string, incrementoId?: string) {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (cuerpo: ReturnType<typeof cuerpoIncremento>) =>
      (incrementoId
        ? await clienteHttp.put<Incremento>(`/contratos/${id}/incrementos/${incrementoId}`, cuerpo)
        : await clienteHttp.post<Incremento>(`/contratos/${id}/incrementos`, cuerpo)
      ).data,
    onSuccess: () => cliente.invalidateQueries({ queryKey: ['contratos', 'incrementos', id] }),
  });
}

export function useCancelarIncremento(id: string) {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (incrementoId: string) => (await clienteHttp.post<Incremento>(`/contratos/${id}/incrementos/${incrementoId}/cancelacion`, {})).data,
    onSuccess: () => cliente.invalidateQueries({ queryKey: ['contratos', 'incrementos', id] }),
  });
}

export function useFirmarRevision(id: string) {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (cuerpo: ReturnType<typeof cuerpoRevision>) => (await clienteHttp.post<Revision>(`/contratos/${id}/revision-inventario`, cuerpo)).data,
    onSuccess: () => cliente.invalidateQueries({ queryKey: ['contratos', 'revision', id] }),
  });
}

// ---------------------------------------------------------------- plantillas y anexos

export interface Seccion {
  titulo: string;
  texto: string;
}

export interface PlantillaContrato {
  id: string;
  codigo: string;
  nombre: string;
  secciones: Seccion[];
  anexosTipos: TipoAnexoApi[];
  activa: boolean;
  createdAt: string;
  updatedAt: string | null;
}

export interface PlantillaAnexo {
  id: string;
  tipo: TipoAnexoApi;
  nombre: string;
  secciones: Seccion[];
  activa: boolean;
  createdAt: string;
  updatedAt: string | null;
}

export function usePlantillas(habilitado = true) {
  return useQuery({
    queryKey: ['contratos', 'plantillas'],
    enabled: habilitado,
    queryFn: async () => (await clienteHttp.get<Pagina<PlantillaContrato>>('/plantillas-contrato', { params: { pageSize: 200 } })).data,
  });
}

export function useAnexos(habilitado = true) {
  return useQuery({
    queryKey: ['contratos', 'anexos'],
    enabled: habilitado,
    queryFn: async () => (await clienteHttp.get<Pagina<PlantillaAnexo>>('/plantillas-anexo', { params: { pageSize: 200, todos: true } })).data,
  });
}

export interface CuerpoPlantilla {
  codigo?: string;
  tipo?: TipoAnexoApi;
  nombre: string;
  secciones: Seccion[];
  anexosTipos?: TipoAnexoApi[];
}

export function useGuardarPlantilla(clase: 'contrato' | 'anexo', id?: string) {
  const cliente = useQueryClient();
  const base = clase === 'contrato' ? '/plantillas-contrato' : '/plantillas-anexo';
  return useMutation({
    mutationFn: async (cuerpo: CuerpoPlantilla) =>
      (id ? await clienteHttp.put(`${base}/${id}`, cuerpo) : await clienteHttp.post(`${base}`, cuerpo)).data,
    onSuccess: () => cliente.invalidateQueries({ queryKey: ['contratos', clase === 'contrato' ? 'plantillas' : 'anexos'] }),
  });
}

export function useDesactivarPlantilla(clase: 'contrato' | 'anexo') {
  const cliente = useQueryClient();
  const base = clase === 'contrato' ? '/plantillas-contrato' : '/plantillas-anexo';
  return useMutation({
    mutationFn: async (id: string) => (await clienteHttp.post(`${base}/${id}/desactivacion`, {})).data,
    onSuccess: () => cliente.invalidateQueries({ queryKey: ['contratos', clase === 'contrato' ? 'plantillas' : 'anexos'] }),
  });
}
