import { useQuery } from '@tanstack/react-query';
import { clienteHttp } from '../../api/cliente';
import type { Pagina } from '../../api/pagina';
import { paramsAuditoria, type AuditoriaFila, type FiltrosAuditoria } from './modelo.ts';

/** `GET /auditoria` (`auditoria.ver`): solo lectura. */
export function useAuditoria(page: number, filtros: FiltrosAuditoria) {
  return useQuery({
    queryKey: ['auditoria', page, filtros],
    queryFn: async () => {
      const { data } = await clienteHttp.get<Pagina<AuditoriaFila>>('/auditoria', {
        params: { page, pageSize: 50, ...paramsAuditoria(filtros) },
      });
      return data;
    },
  });
}
