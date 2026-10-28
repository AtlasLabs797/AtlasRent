import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { clienteHttp } from '../../api/cliente';
import { claveIdempotencia, type Pagina } from '../../api/pagina';
import { cuerpoRechazo, type PropuestaIa, type SolicitudContrato } from './modelo.ts';

/**
 * Bandeja de aprobaciones. Contratos: `contrato.estado`. Propuestas: listar `ia.proponer`, decidir
 * `mensajeria.masivo`. Las rutas de decisión reciben el SolicitudId (la fila), no el del contrato.
 */
export const CLAVES = {
  contratos: ['bandeja', 'contratos'] as const,
  propuestas: ['bandeja', 'propuestas'] as const,
};

export function useSolicitudesContrato(page: number, habilitado: boolean) {
  return useQuery({
    queryKey: [...CLAVES.contratos, page],
    enabled: habilitado,
    queryFn: async () => {
      const { data } = await clienteHttp.get<Pagina<SolicitudContrato>>('/contratos/solicitudes', {
        params: { page, pageSize: 25 },
      });
      return data;
    },
  });
}

export function usePropuestasIa(page: number, habilitado: boolean) {
  return useQuery({
    queryKey: [...CLAVES.propuestas, page],
    enabled: habilitado,
    queryFn: async () => {
      const { data } = await clienteHttp.get<Pagina<PropuestaIa>>('/ia/propuestas', {
        params: { page, pageSize: 25 },
      });
      return data;
    },
  });
}

export function useDecisionContrato() {
  const cliente = useQueryClient();
  const refrescar = () => cliente.invalidateQueries({ queryKey: CLAVES.contratos });
  const aprobar = useMutation({
    mutationFn: (solicitudId: string) =>
      clienteHttp.post(`/contratos/solicitudes/${solicitudId}/aprobacion`, undefined, { headers: claveIdempotencia() }),
    // También tras un 409: la lista ya no es la vigente.
    onSettled: refrescar,
  });
  const rechazar = useMutation({
    mutationFn: ({ solicitudId, comentario }: { solicitudId: string; comentario: string }) =>
      clienteHttp.post(`/contratos/solicitudes/${solicitudId}/rechazo`, cuerpoRechazo(comentario), {
        headers: claveIdempotencia(),
      }),
    onSettled: refrescar,
  });
  return { aprobar, rechazar };
}

export function useDecisionPropuesta() {
  const cliente = useQueryClient();
  const refrescar = () => cliente.invalidateQueries({ queryKey: CLAVES.propuestas });
  const aprobar = useMutation({
    mutationFn: async (id: string) => {
      const { data } = await clienteHttp.post<PropuestaIa>(`/ia/propuestas/${id}/aprobacion`, undefined, {
        headers: claveIdempotencia(),
      });
      return data;
    },
    onSettled: refrescar,
  });
  const rechazar = useMutation({
    mutationFn: ({ id, comentario }: { id: string; comentario: string }) =>
      clienteHttp.post(`/ia/propuestas/${id}/rechazo`, cuerpoRechazo(comentario), { headers: claveIdempotencia() }),
    onSettled: refrescar,
  });
  return { aprobar, rechazar };
}
