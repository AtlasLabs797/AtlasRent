import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { clienteHttp } from '../../api/cliente';
import type { Fila } from './esquema.ts';

/** Contratos de `ConfigController`, `TiposCambioController`, `PlantillasMensajeController` y `AgenteController`. */
export interface Pagina<T> {
  data: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

/** Lista completa de un catálogo (hasta 200 filas, el máximo de la API). */
export function useCatalogo(ruta: string) {
  return useQuery({
    queryKey: ['config', ruta],
    queryFn: async () => (await clienteHttp.get<Pagina<Fila>>(ruta, { params: { pageSize: 200 } })).data.data,
  });
}

/** Alta (sin id) o edición (con id) de una fila de catálogo. */
export function useGuardarCatalogo(ruta: string) {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (p: { id?: string; cuerpo: Record<string, unknown> }) =>
      (p.id ? await clienteHttp.put<Fila>(`${ruta}/${p.id}`, p.cuerpo) : await clienteHttp.post<Fila>(ruta, p.cuerpo)).data,
    onSuccess: () => cliente.invalidateQueries({ queryKey: ['config', ruta] }),
  });
}

export function useDarDeBaja(ruta: string) {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      await clienteHttp.delete(`${ruta}/${id}`);
    },
    onSuccess: () => cliente.invalidateQueries({ queryKey: ['config', ruta] }),
  });
}

export interface NumeradorFiscal extends Fila {
  id: string;
  serieId: string;
  serieNumero: number;
  tipoPrefijo: string;
  rangoDesde: number;
  rangoHasta: number;
  fechaVencimiento: string;
  siguienteNumero: number;
  umbralAlerta: number;
  restantes: number;
}

export function useNumeradores() {
  return useQuery({
    queryKey: ['config', '/config/numeradores-fiscales'],
    queryFn: async () => (await clienteHttp.get<Pagina<NumeradorFiscal>>('/config/numeradores-fiscales', { params: { pageSize: 200 } })).data.data,
  });
}

export function useGuardarNumerador() {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (p: { serieId: string; cuerpo: Record<string, unknown> }) =>
      (await clienteHttp.put<NumeradorFiscal>(`/config/numeradores-fiscales/${p.serieId}`, p.cuerpo)).data,
    onSuccess: () => cliente.invalidateQueries({ queryKey: ['config', '/config/numeradores-fiscales'] }),
  });
}

export interface Ajustes {
  id: string;
  toleranciaCambioUsd: string | null;
  toleranciaCambioDop: string | null;
  modoRecordatorios: string;
  diasPrimerRecordatorio: number | null;
  diasEntreRecordatorios: number | null;
  mfaGerente: boolean;
}

export function useAjustes() {
  return useQuery({ queryKey: ['config', 'ajustes'], queryFn: async () => (await clienteHttp.get<Ajustes>('/config/ajustes')).data });
}

export function useGuardarAjustes() {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (cuerpo: Record<string, unknown>) => (await clienteHttp.put<Ajustes>('/config/ajustes', cuerpo)).data,
    onSuccess: (datos) => cliente.setQueryData(['config', 'ajustes'], datos),
  });
}

export interface TipoCambio {
  id: string;
  fecha: string;
  moneda: string;
  valor: string;
  fuente: string;
  registradoAt: string;
  registradoPor: string | null;
}

export function useTiposCambio(page: number, pageSize: number) {
  return useQuery({
    queryKey: ['tipos-cambio', page, pageSize],
    queryFn: async () =>
      (await clienteHttp.get<Pagina<TipoCambio>>('/tipos-cambio', { params: { page, pageSize, sortBy: 'fecha', sortDir: 'desc' } })).data,
  });
}

export interface TipoCambioActual {
  fecha: string;
  moneda: string;
  valor: string;
  id: string | null;
  fuente: string | null;
}

export function useTipoCambioActual() {
  return useQuery({
    queryKey: ['tipos-cambio', 'actual'],
    retry: false,
    queryFn: async () => (await clienteHttp.get<TipoCambioActual>('/tipos-cambio/actual', { params: { moneda: 'DOP' } })).data,
  });
}

export function useCargarTipoCambio() {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (cuerpo: Record<string, unknown>) => (await clienteHttp.post<TipoCambio>('/tipos-cambio', cuerpo)).data,
    onSuccess: () => cliente.invalidateQueries({ queryKey: ['tipos-cambio'] }),
  });
}

export function useGuardarPlantilla() {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (cuerpo: Record<string, unknown>) => (await clienteHttp.put<Fila>('/config/plantillas-mensaje', cuerpo)).data,
    onSuccess: () => cliente.invalidateQueries({ queryKey: ['config', '/config/plantillas-mensaje'] }),
  });
}

export interface TokenAgente {
  id: string;
  nombre: string;
  usuarioId: string;
  acciones: string[];
  creado: string;
  caduca: string | null;
  revocado: string | null;
  ultimoUso: string | null;
  limiteConsultasDia: number;
  /** Solo en el alta: el secreto, una única vez. */
  token?: string;
}

export function useTokensAgente() {
  return useQuery({
    queryKey: ['agente', 'tokens'],
    queryFn: async () => (await clienteHttp.get<{ data: TokenAgente[] }>('/agente/tokens')).data.data,
  });
}

export function useEmitirToken() {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (cuerpo: Record<string, unknown>) => (await clienteHttp.post<TokenAgente>('/agente/tokens', cuerpo)).data,
    onSuccess: () => cliente.invalidateQueries({ queryKey: ['agente', 'tokens'] }),
  });
}

export function useRevocarToken() {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      await clienteHttp.post(`/agente/tokens/${id}/revocar`);
    },
    onSuccess: () => cliente.invalidateQueries({ queryKey: ['agente', 'tokens'] }),
  });
}
