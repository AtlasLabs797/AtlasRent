import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { clienteHttp } from '../../api/cliente';
import type { EstadoReservaApi } from '../operacion/etiquetas.ts';
import type { Importe, ModalidadApi, MonedaApi, Pagina } from '../operacion/tipos.ts';
import type { TipoReservaApi, cuerpoReserva } from './cuerpo.ts';

export interface ReservaResumen {
  id: string;
  unidadId: string;
  unidad: string;
  clienteId: string | null;
  prospectoId: string | null;
  cliente: string | null;
  tipo: TipoReservaApi;
  estado: EstadoReservaApi;
  modalidad: ModalidadApi;
  moneda: MonedaApi;
  importe: Importe;
  anticipoImporte: Importe | null;
  fechaReserva: string;
  entradaPrevista: string;
  salidaPrevista: string | null;
  entradaReal: string | null;
  salidaReal: string | null;
  activo: boolean;
  dias: number;
  tieneAnticipo: boolean;
  createdAt: string;
}

export interface ReservaDia {
  id: string;
  fecha: string;
  horaOrientativa: string | null;
  tipo: 'MedioDia' | 'DiaCompleto';
}

export interface ReservaDetalle extends Omit<ReservaResumen, 'dias' | 'tieneAnticipo'> {
  anticipoNota: string | null;
  observaciones: string | null;
  dias: ReservaDia[];
}

export interface FacturaReservaRespuesta {
  facturaId: string;
  reservaId: string;
  numero: number | null;
  total: Importe;
  moneda: MonedaApi;
  vencimiento: string;
  anticipoAviso: string | null;
}

export interface FiltrosReservas {
  page: number;
  q: string;
  estado: EstadoReservaApi | '';
  tipo: TipoReservaApi | '';
  incluirInactivas: boolean;
}

export const TAMANO_PAGINA = 25;

export function useReservas(f: FiltrosReservas) {
  return useQuery({
    queryKey: ['reservas', 'lista', f],
    placeholderData: keepPreviousData,
    queryFn: async () =>
      (
        await clienteHttp.get<Pagina<ReservaResumen>>('/reservas', {
          params: {
            page: f.page,
            pageSize: TAMANO_PAGINA,
            ...(f.q ? { q: f.q } : {}),
            ...(f.estado ? { estado: f.estado } : {}),
            ...(f.tipo ? { tipo: f.tipo } : {}),
            ...(f.incluirInactivas ? { incluirInactivas: true } : {}),
          },
        })
      ).data,
  });
}

export function useReserva(id: string | undefined) {
  return useQuery({
    queryKey: ['reservas', 'detalle', id],
    enabled: Boolean(id),
    queryFn: async () => (await clienteHttp.get<ReservaDetalle>(`/reservas/${id}`)).data,
  });
}

export function useGuardarReserva(id?: string) {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (cuerpo: ReturnType<typeof cuerpoReserva>) =>
      (id ? await clienteHttp.put<ReservaDetalle>(`/reservas/${id}`, cuerpo) : await clienteHttp.post<ReservaDetalle>('/reservas', cuerpo)).data,
    onSuccess: () => Promise.all([cliente.invalidateQueries({ queryKey: ['reservas'] }), cliente.invalidateQueries({ queryKey: ['calendario'] })]),
  });
}

export function useCancelarReserva(id: string) {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async () => (await clienteHttp.delete<ReservaDetalle>(`/reservas/${id}`)).data,
    onSuccess: () => Promise.all([cliente.invalidateQueries({ queryKey: ['reservas'] }), cliente.invalidateQueries({ queryKey: ['calendario'] })]),
  });
}

/** `POST /reservas/{id}/factura` exige `factura.emitir`. */
export function useFacturarReserva(id: string) {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (serieId: string) => (await clienteHttp.post<FacturaReservaRespuesta>(`/reservas/${id}/factura`, { serieId })).data,
    onSuccess: () => cliente.invalidateQueries({ queryKey: ['reservas'] }),
  });
}

export interface SerieLista {
  id: string;
  numero: number;
  prefijo: string | null;
  moneda: MonedaApi | null;
  activa: boolean;
}

/** Las series solo las lista `configuracion.editar`. TODO(A#): un gerente que emite no puede elegir serie. */
export function useSeries(habilitado: boolean) {
  return useQuery({
    queryKey: ['reservas', 'series'],
    enabled: habilitado,
    queryFn: async () => (await clienteHttp.get<Pagina<SerieLista>>('/config/series', { params: { pageSize: 200 } })).data,
  });
}
