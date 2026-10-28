import { useQueries, useQuery } from '@tanstack/react-query';
import { useMemo } from 'react';
import { clienteHttp } from '../../api/cliente';
import type { ClienteLista, InmuebleLista, Pagina, ProspectoLista, UnidadConInmueble, UnidadLista } from './tipos.ts';

/** Tamaño máximo de página de la API (`ConsultaLista.PageSizeMaximo`). */
export const PAGINA_MAXIMA = 200;

/**
 * Clientes para los selectores. `Combobox` del DS filtra en cliente, así que se traen hasta 200.
 * TODO(A#): si la cartera pasa de 200 clientes hace falta búsqueda remota en el selector.
 */
export function useClientes() {
  return useQuery({
    queryKey: ['operacion', 'clientes'],
    queryFn: async () => (await clienteHttp.get<Pagina<ClienteLista>>('/clientes', { params: { pageSize: PAGINA_MAXIMA } })).data,
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

/** Todas las unidades de todos los inmuebles (una petición por inmueble), con el nombre del inmueble. */
export function useUnidades(): { unidades: UnidadConInmueble[]; cargando: boolean; error: unknown } {
  const inmuebles = useInmuebles();
  const lista = inmuebles.data?.data ?? [];
  const consultas = useQueries({
    queries: lista.map((inmueble) => ({
      queryKey: ['operacion', 'unidades', inmueble.id],
      queryFn: async () =>
        (await clienteHttp.get<Pagina<UnidadLista>>(`/inmuebles/${inmueble.id}/unidades`, { params: { pageSize: PAGINA_MAXIMA } })).data,
      staleTime: 60_000,
    })),
  });
  // Clave estable: `consultas` cambia de identidad en cada render, sus marcas de actualización no.
  const version = consultas.map((c) => c.dataUpdatedAt).join(',') + '|' + inmuebles.dataUpdatedAt;
  const unidades = useMemo(
    () =>
      consultas.flatMap((c, i) =>
        (c.data?.data ?? []).map((u) => ({ ...u, inmueble: lista[i]?.nombre ?? '' })),
      ),
    [version],
  );
  return {
    unidades,
    cargando: inmuebles.isPending || consultas.some((c) => c.isPending),
    error: inmuebles.error ?? consultas.find((c) => c.error)?.error ?? null,
  };
}
