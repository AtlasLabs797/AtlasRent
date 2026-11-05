import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { clienteHttp } from '../../api/cliente';
import type { EstadoCotizacionApi } from '../operacion/etiquetas.ts';
import type { Importe, MonedaApi, Pagina } from '../operacion/tipos.ts';
import type { cuerpoCotizacion } from './cuerpo.ts';

export interface CotizacionResumen {
  id: string;
  clienteId: string | null;
  clienteNombre: string | null;
  prospectoId: string | null;
  prospectoNombre: string | null;
  unidadId: string | null;
  unidadNombre: string | null;
  estado: EstadoCotizacionApi;
  moneda: MonedaApi;
  subtotal: Importe;
  total: Importe;
  lineas: number;
  activo: boolean;
  createdAt: string;
}

export interface CotizacionLinea {
  posicion: number;
  descripcion: string;
  cantidad: Importe;
  precioUnitario: Importe;
  importe: Importe;
}

export interface CotizacionDetalle extends Omit<CotizacionResumen, 'lineas'> {
  lineas: CotizacionLinea[];
}

export interface TransferenciaCotizacion {
  cotizacionId: string;
  clienteId: string | null;
  clienteNombre: string | null;
  prospectoId: string | null;
  prospectoNombre: string | null;
  unidadId: string | null;
  unidadNombre: string | null;
  moneda: MonedaApi;
  total: Importe;
  lineas: CotizacionLinea[];
}

export interface FiltrosCotizaciones {
  page: number;
  q: string;
  estado: EstadoCotizacionApi | '';
}

export const TAMANO_PAGINA = 25;

export function useCotizaciones(f: FiltrosCotizaciones) {
  return useQuery({
    queryKey: ['cotizaciones', 'lista', f],
    placeholderData: keepPreviousData,
    queryFn: async () =>
      (
        await clienteHttp.get<Pagina<CotizacionResumen>>('/cotizaciones', {
          params: { page: f.page, pageSize: TAMANO_PAGINA, ...(f.q ? { q: f.q } : {}), ...(f.estado ? { estado: f.estado } : {}) },
        })
      ).data,
  });
}

export function useCotizacion(id: string | undefined) {
  return useQuery({
    queryKey: ['cotizaciones', 'detalle', id],
    enabled: Boolean(id),
    queryFn: async () => (await clienteHttp.get<CotizacionDetalle>(`/cotizaciones/${id}`)).data,
  });
}

/** Datos copiados de una cotización aceptada para rellenar el formulario de reserva o de contrato. */
export function useTransferencia(id: string | null, destino: 'reserva' | 'contrato') {
  return useQuery({
    queryKey: ['cotizaciones', 'transferencia', destino, id],
    enabled: Boolean(id),
    queryFn: async () => (await clienteHttp.get<TransferenciaCotizacion>(`/cotizaciones/${id}/para-${destino}`)).data,
  });
}

type Cuerpo = ReturnType<typeof cuerpoCotizacion>;

export function useGuardarCotizacion(id?: string) {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (cuerpo: Cuerpo) =>
      (id ? await clienteHttp.put<CotizacionDetalle>(`/cotizaciones/${id}`, cuerpo) : await clienteHttp.post<CotizacionDetalle>('/cotizaciones', cuerpo)).data,
    onSuccess: () => cliente.invalidateQueries({ queryKey: ['cotizaciones'] }),
  });
}

export function useCambiarEstadoCotizacion(id: string) {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (estado: EstadoCotizacionApi) => (await clienteHttp.post<CotizacionResumen>(`/cotizaciones/${id}/estado`, { estado })).data,
    onSuccess: () => cliente.invalidateQueries({ queryKey: ['cotizaciones'] }),
  });
}

export function useDesactivarCotizacion(id: string) {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async () => (await clienteHttp.delete(`/cotizaciones/${id}`)).data,
    onSuccess: () => cliente.invalidateQueries({ queryKey: ['cotizaciones'] }),
  });
}
