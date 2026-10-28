import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { clienteHttp } from '../../api/cliente';
import { paramsListaClientes, peticionBusquedaDocumento } from './logica.ts';
import type {
  ClienteFicha,
  ClienteResumen,
  Contacto,
  CuerpoCliente,
  CuerpoContacto,
  CuerpoProspecto,
  Pagina,
  ProspectoFicha,
  ProspectoResumen,
  TipoCliente,
} from './tipos.ts';

/** Filtros de la lista. `documento` solo vive en memoria: va en el cuerpo del POST, nunca en la URL. */
export interface FiltrosClientes {
  q: string;
  tipo: TipoCliente | '';
  incluirInactivos: boolean;
  page: number;
  pageSize: number;
  documento: string;
}

export function useListaClientes(f: FiltrosClientes) {
  return useQuery({
    queryKey: ['clientes', 'lista', f],
    placeholderData: keepPreviousData,
    queryFn: async () => {
      if (f.documento.trim() !== '') {
        const { ruta, cuerpo } = peticionBusquedaDocumento(f.documento, {
          tipo: f.tipo === '' ? undefined : f.tipo,
          incluirInactivos: f.incluirInactivos,
          page: f.page,
          pageSize: f.pageSize,
        });
        return (await clienteHttp.post<Pagina<ClienteResumen>>(ruta, cuerpo)).data;
      }
      return (await clienteHttp.get<Pagina<ClienteResumen>>('/clientes', { params: paramsListaClientes(f) })).data;
    },
  });
}

export function useCliente(id: string) {
  return useQuery({
    queryKey: ['clientes', 'ficha', id],
    queryFn: async () => (await clienteHttp.get<ClienteFicha>(`/clientes/${id}`)).data,
  });
}

export function useContactos(id: string) {
  return useQuery({
    queryKey: ['clientes', 'contactos', id],
    queryFn: async () => (await clienteHttp.get<Contacto[]>(`/clientes/${id}/contactos`)).data,
  });
}

/** Alta (sin `id`) o cambio (con `id`). */
export function useGuardarCliente() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, cuerpo }: { id?: string; cuerpo: CuerpoCliente }) =>
      id
        ? (await clienteHttp.put<ClienteResumen>(`/clientes/${id}`, cuerpo)).data
        : (await clienteHttp.post<ClienteResumen>('/clientes', cuerpo)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ['clientes'] }),
  });
}

export function useDesactivarCliente() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      await clienteHttp.delete(`/clientes/${id}`);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['clientes'] }),
  });
}

export function useGuardarContacto(clienteId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, cuerpo }: { id?: string; cuerpo: CuerpoContacto }) =>
      id
        ? (await clienteHttp.put<Contacto>(`/clientes/${clienteId}/contactos/${id}`, cuerpo)).data
        : (await clienteHttp.post<Contacto>(`/clientes/${clienteId}/contactos`, cuerpo)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ['clientes', 'contactos', clienteId] }),
  });
}

export function useEliminarContacto(clienteId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      await clienteHttp.delete(`/clientes/${clienteId}/contactos/${id}`);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['clientes', 'contactos', clienteId] }),
  });
}

export function useListaProspectos(f: { q: string; page: number; pageSize: number }) {
  return useQuery({
    queryKey: ['prospectos', 'lista', f],
    placeholderData: keepPreviousData,
    queryFn: async () =>
      (
        await clienteHttp.get<Pagina<ProspectoResumen>>('/prospectos', {
          params: { page: f.page, pageSize: f.pageSize, ...(f.q.trim() ? { q: f.q.trim() } : {}) },
        })
      ).data,
  });
}

export async function leerProspecto(id: string): Promise<ProspectoFicha> {
  return (await clienteHttp.get<ProspectoFicha>(`/prospectos/${id}`)).data;
}

export function useGuardarProspecto() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, cuerpo }: { id?: string; cuerpo: CuerpoProspecto }) =>
      id
        ? (await clienteHttp.put<ProspectoResumen>(`/prospectos/${id}`, cuerpo)).data
        : (await clienteHttp.post<ProspectoResumen>('/prospectos', cuerpo)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ['prospectos'] }),
  });
}

export function useDesactivarProspecto() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      await clienteHttp.delete(`/prospectos/${id}`);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['prospectos'] }),
  });
}

/** Descarga un PDF de la API (con la cookie de sesión) y lo guarda con el nombre dado. */
export async function descargarArchivo(ruta: string, nombre: string, params?: Record<string, string | number>): Promise<void> {
  const { data } = await clienteHttp.get<Blob>(ruta, { responseType: 'blob', ...(params ? { params } : {}) });
  const url = URL.createObjectURL(data);
  const a = document.createElement('a');
  a.href = url;
  a.download = nombre;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}
