import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useMemo } from 'react';
import { clienteHttp } from '../../api/cliente';
import type { ClienteLista, InmuebleLista, Pagina, ProspectoLista, UnidadConInmueble, UnidadLista } from './tipos.ts';

/** Tamaño máximo de página de la API (`ConsultaLista.PageSizeMaximo`). */
export const PAGINA_MAXIMA = 200;

/**
 * Clientes completos hasta 200, para quien filtra en local (el formulario de contratos). Los
 * selectores de cotizaciones y reservas buscan en remoto con `useBuscarClientes`.
 * TODO(A#): el formulario de contratos sigue con esta carga; si la cartera pasa de 200 clientes
 * necesita la búsqueda remota.
 */
export function useClientes() {
  return useQuery({
    queryKey: ['operacion', 'clientes'],
    queryFn: async () => (await clienteHttp.get<Pagina<ClienteLista>>('/clientes', { params: { pageSize: PAGINA_MAXIMA } })).data,
    staleTime: 60_000,
  });
}

/** Resultados que enseña el selector de clientes por búsqueda. */
export const RESULTADOS_BUSQUEDA = 20;

/** Búsqueda remota por nombre o número (`GET /clientes?q=`). Sin texto trae los primeros. */
export function useBuscarClientes(q: string) {
  const texto = q.trim();
  return useQuery({
    queryKey: ['operacion', 'clientes', 'buscar', texto],
    placeholderData: keepPreviousData,
    queryFn: async () =>
      (
        await clienteHttp.get<Pagina<ClienteLista>>('/clientes', {
          params: { pageSize: RESULTADOS_BUSQUEDA, ...(texto ? { q: texto } : {}) },
        })
      ).data,
    staleTime: 30_000,
  });
}

/** El cliente ya elegido (p. ej. al editar), que puede no estar entre los resultados de la búsqueda. */
export function useClientePorId(id: string | null) {
  return useQuery({
    queryKey: ['operacion', 'clientes', 'uno', id],
    enabled: id !== null,
    queryFn: async () => (await clienteHttp.get<ClienteLista>(`/clientes/${id}`)).data,
    staleTime: 60_000,
  });
}

export function useProspectos() {
  return useQuery({
    queryKey: ['operacion', 'prospectos'],
    queryFn: async () => (await clienteHttp.get<Pagina<ProspectoLista>>('/prospectos', { params: { pageSize: PAGINA_MAXIMA } })).data,
    staleTime: 60_000,
  });
}

export function useInmuebles() {
  return useQuery({
    queryKey: ['operacion', 'inmuebles'],
    queryFn: async () => (await clienteHttp.get<Pagina<InmuebleLista>>('/inmuebles', { params: { pageSize: PAGINA_MAXIMA } })).data,
    staleTime: 60_000,
  });
}

/** Unidades activas con el nombre de su inmueble: `GET /inmuebles/unidades` (listado global) y los inmuebles. */
export function useUnidades(): { unidades: UnidadConInmueble[]; cargando: boolean; error: unknown } {
  const inmuebles = useInmuebles();
  const global = useQuery({
    queryKey: ['operacion', 'unidades'],
    queryFn: async () =>
      (await clienteHttp.get<Pagina<UnidadLista>>('/inmuebles/unidades', { params: { activa: true, pageSize: PAGINA_MAXIMA } })).data,
    staleTime: 60_000,
  });
  const unidades = useMemo(() => {
    const nombres = new Map((inmuebles.data?.data ?? []).map((i) => [i.id, i.nombre]));
    return (global.data?.data ?? []).map((u) => ({ ...u, inmueble: nombres.get(u.inmuebleId) ?? '' }));
  }, [global.data, inmuebles.data]);
  return {
    unidades,
    cargando: inmuebles.isPending || global.isPending,
    error: inmuebles.error ?? global.error ?? null,
  };
}
