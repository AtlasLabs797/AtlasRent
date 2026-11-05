import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { clienteHttp } from '../../api/cliente';
import { claveIdempotencia, type Pagina } from '../../api/pagina';
import {
  type CuerpoMasivo,
  type EnvioFila,
  type EstadoEnvio,
  type EstadoRecordatorio,
  type LoteEnvio,
  type ModoRecordatorios,
  type ProgresoLote,
  type RecordatorioFila,
  type ReintentoLote,
  type ResumenMasivo,
} from './modelo.ts';

/** Historial de envíos: `GET /envios` (`factura.ver`). */
export function useEnvios(page: number, estado: EstadoEnvio | '') {
  return useQuery({
    queryKey: ['envios', 'historial', page, estado],
    queryFn: async () => {
      const { data } = await clienteHttp.get<Pagina<EnvioFila>>('/envios', {
        params: { page, pageSize: 25, ...(estado ? { estado } : {}) },
      });
      return data;
    },
  });
}

/**
 * Copia exacta del PDF que salió (`GET /envios/{id}/pdf`). Un error llega como JSON y con
 * `responseType: 'blob'` se guardaría como PDF: se comprueba el tipo antes de escribir nada.
 */
export async function descargarCopia(envio: Pick<EnvioFila, 'id' | 'nombreArchivo'>): Promise<void> {
  const r = await clienteHttp.get<Blob>(`/envios/${envio.id}/pdf`, { responseType: 'blob' });
  if (String(r.headers['content-type'] ?? '').includes('application/json')) throw new Error('respuesta no válida');
  const url = URL.createObjectURL(r.data);
  const a = document.createElement('a');
  a.href = url;
  a.download = envio.nombreArchivo;
  a.click();
  URL.revokeObjectURL(url);
}

/** Reintento de un envío fallido: `POST /envios/{id}/reintentar` (`cotizacion.enviar`), sin cuerpo. Un 409 es «ya no es reintentable». */
export function useReintentarEnvio() {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (envio: EnvioFila) => {
      const { data } = await clienteHttp.post<EnvioFila>(`/envios/${envio.id}/reintentar`, undefined, {
        headers: claveIdempotencia(),
      });
      return data;
    },
    onSettled: () => cliente.invalidateQueries({ queryKey: ['envios', 'historial'] }),
  });
}

/** Envío masivo (`mensajeria.masivo`): preparar → confirmar → progreso → reintentar. */
export function usePrepararMasivo() {
  return useMutation({
    mutationFn: async (cuerpo: CuerpoMasivo) => {
      const { data } = await clienteHttp.post<ResumenMasivo>('/envios/masivo/preparar', cuerpo, {
        headers: claveIdempotencia(),
      });
      return data;
    },
  });
}

export function useConfirmarMasivo() {
  return useMutation({
    mutationFn: async (cuerpo: CuerpoMasivo) => {
      const { data } = await clienteHttp.post<LoteEnvio>('/envios/masivo/confirmar', cuerpo, {
        headers: claveIdempotencia(),
      });
      return data;
    },
  });
}

/** Progreso del lote; consulta cada 3 s hasta que no queda nada pendiente. */
export function useProgresoLote(loteId: string | null) {
  return useQuery({
    queryKey: ['envios', 'lote', loteId],
    enabled: loteId !== null,
    refetchInterval: (q) => (q.state.data && q.state.data.pendientes === 0 ? false : 3000),
    queryFn: async () => {
      const { data } = await clienteHttp.get<ProgresoLote>(`/envios/masivo/${loteId}`);
      return data;
    },
  });
}

export function useReintentarLote(loteId: string | null) {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      const { data } = await clienteHttp.post<ReintentoLote>(`/envios/masivo/${loteId}/reintentar`, undefined, {
        headers: claveIdempotencia(),
      });
      return data;
    },
    onSettled: () => cliente.invalidateQueries({ queryKey: ['envios', 'lote', loteId] }),
  });
}

/** Avisos de pago (`mensajeria.masivo`). */
export function useRecordatorios(page: number, estado: EstadoRecordatorio | '') {
  return useQuery({
    queryKey: ['envios', 'recordatorios', page, estado],
    queryFn: async () => {
      const { data } = await clienteHttp.get<Pagina<RecordatorioFila>>('/recordatorios', {
        params: { page, pageSize: 25, ...(estado ? { estado } : {}) },
      });
      return data;
    },
  });
}

/** Confirma un aviso preparado (modo manual). Puede volver `Omitido`: no es un error. */
export function useEnviarRecordatorio() {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      const { data } = await clienteHttp.post<RecordatorioFila>(`/recordatorios/${id}/enviar`, undefined, {
        headers: claveIdempotencia(),
      });
      return data;
    },
    onSettled: () => cliente.invalidateQueries({ queryKey: ['envios', 'recordatorios'] }),
  });
}

/** Pausa o levanta los avisos de un cliente (`cliente.editar`). */
export function usePausarRecordatorios() {
  return useMutation({
    mutationFn: ({ clienteId, pausados }: { clienteId: string; pausados: boolean }) =>
      clienteHttp.put(`/clientes/${clienteId}/recordatorios-pausados`, { pausados }, { headers: claveIdempotencia() }),
  });
}

/** Modo de recordatorios: `GET /recordatorios/modo` (`mensajeria.masivo`, la misma acción que la pestaña). */
export function useModoRecordatorios() {
  return useQuery({
    queryKey: ['envios', 'recordatorios', 'modo'],
    queryFn: async () => {
      const { data } = await clienteHttp.get<{ modo: ModoRecordatorios }>('/recordatorios/modo');
      return data.modo;
    },
  });
}
