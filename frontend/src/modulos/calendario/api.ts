import { useQuery } from '@tanstack/react-query';
import { clienteHttp } from '../../api/cliente';
import type { RespuestaCalendario } from './modelo.ts';

/** `GET /calendario`: `desde` y `hasta` son obligatorios y van incluidos; máximo 366 días. */
export function useCalendario(inmuebleId: string | null, desde: string, hasta: string) {
  return useQuery({
    queryKey: ['calendario', inmuebleId, desde, hasta],
    enabled: inmuebleId !== null,
    queryFn: async () => (await clienteHttp.get<RespuestaCalendario>('/calendario', { params: { inmuebleId, desde, hasta } })).data,
  });
}
