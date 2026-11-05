import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { clienteHttp } from '../../api/cliente';
import type {
  CategoriaDocumento,
  CuerpoInmueble,
  CuerpoUnidad,
  DocumentoInmueble,
  InmuebleFicha,
  InmuebleResumen,
  Pagina,
  TipoInmueble,
  Unidad,
} from './tipos.ts';

export function useListaInmuebles(f: { q: string; tipo: TipoInmueble | ''; incluirInactivos: boolean; page: number; pageSize: number }) {
  return useQuery({
    queryKey: ['inmuebles', 'lista', f],
    placeholderData: keepPreviousData,
    queryFn: async () =>
      (
        await clienteHttp.get<Pagina<InmuebleResumen>>('/inmuebles', {
          params: {
            page: f.page,
            pageSize: f.pageSize,
            ...(f.q.trim() ? { q: f.q.trim() } : {}),
            ...(f.tipo ? { tipo: f.tipo } : {}),
            ...(f.incluirInactivos ? { incluirInactivos: true } : {}),
          },
        })
      ).data,
  });
}

export function useInmueble(id: string) {
  return useQuery({
    queryKey: ['inmuebles', 'ficha', id],
    queryFn: async () => (await clienteHttp.get<InmuebleFicha>(`/inmuebles/${id}`)).data,
  });
}

export function useGuardarInmueble() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, cuerpo }: { id?: string; cuerpo: CuerpoInmueble }) =>
      id
        ? (await clienteHttp.put<InmuebleResumen>(`/inmuebles/${id}`, cuerpo)).data
        : (await clienteHttp.post<InmuebleResumen>('/inmuebles', cuerpo)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ['inmuebles'] }),
  });
}

export function useDesactivarInmueble() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      await clienteHttp.delete(`/inmuebles/${id}`);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['inmuebles'] }),
  });
}

export function useReactivarInmueble() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      await clienteHttp.post(`/inmuebles/${id}/reactivar`);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['inmuebles'] }),
  });
}

/** Alta (con `inmuebleId`) o cambio (con `unidadId`) de una unidad. */
export function useGuardarUnidad(inmuebleId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ unidadId, cuerpo }: { unidadId?: string; cuerpo: CuerpoUnidad }) =>
      unidadId
        ? (await clienteHttp.put<Unidad>(`/inmuebles/unidades/${unidadId}`, cuerpo)).data
        : (await clienteHttp.post<Unidad>(`/inmuebles/${inmuebleId}/unidades`, cuerpo)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ['inmuebles', 'ficha', inmuebleId] }),
  });
}

/**
 * Baja lógica de una unidad (`DELETE /inmuebles/unidades/{id}`): 409 si tiene un contrato o una
 * reserva vivos. Reactivar: `POST /inmuebles/unidades/{id}/reactivar`.
 */
export function useCambiarActivaUnidad(inmuebleId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ unidadId, activar }: { unidadId: string; activar: boolean }) =>
      activar
        ? (await clienteHttp.post<Unidad>(`/inmuebles/unidades/${unidadId}/reactivar`)).data
        : (await clienteHttp.delete<Unidad>(`/inmuebles/unidades/${unidadId}`)).data,
    onSuccess: () =>
      Promise.all([
        qc.invalidateQueries({ queryKey: ['inmuebles', 'ficha', inmuebleId] }),
        qc.invalidateQueries({ queryKey: ['operacion', 'unidades'] }),
      ]),
  });
}

/** Solo se pide con `inmueble.documento`: el empleado recibiría un 403, así que ni se intenta. */
export function useDocumentos(inmuebleId: string, habilitado: boolean) {
  return useQuery({
    queryKey: ['inmuebles', 'documentos', inmuebleId],
    enabled: habilitado,
    queryFn: async () =>
      (await clienteHttp.get<Pagina<DocumentoInmueble>>(`/inmuebles/${inmuebleId}/documentos`, { params: { pageSize: 100 } })).data,
  });
}

export function useSubirDocumento(inmuebleId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ archivo, categoria }: { archivo: File; categoria: CategoriaDocumento }) => {
      const datos = new FormData();
      datos.append('archivo', archivo);
      datos.append('categoria', categoria);
      // multipart/form-data sin boundary: axios lo quita y deja que el navegador ponga el suyo.
      return (
        await clienteHttp.post<DocumentoInmueble>(`/inmuebles/${inmuebleId}/documentos`, datos, {
          headers: { 'Content-Type': 'multipart/form-data' },
        })
      ).data;
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['inmuebles', 'documentos', inmuebleId] }),
  });
}

/** Descarga el PDF por la API (con permiso) y lo guarda con su nombre original. */
export async function descargarDocumento(inmuebleId: string, doc: DocumentoInmueble): Promise<void> {
  const { data } = await clienteHttp.get<Blob>(`/inmuebles/${inmuebleId}/documentos/${doc.id}/archivo`, { responseType: 'blob' });
  const url = URL.createObjectURL(data);
  const a = document.createElement('a');
  a.href = url;
  a.download = doc.nombreOriginal;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}
