import type { JSX } from 'react';
import { clienteHttp } from '../../api/cliente';
import { Badge } from '../../design-system/components/core/Badge';
import { Skeleton } from '../../design-system/components/feedback/Skeleton';
import type { EtiquetaEstado } from './etiquetas.ts';

export function BadgeEstado({ estado }: { estado: EtiquetaEstado }): JSX.Element {
  return (
    <Badge tone={estado.tono} dot>
      {estado.texto}
    </Badge>
  );
}

export function CargandoFilas({ filas = 5 }: { filas?: number }): JSX.Element {
  return (
    <div style={{ display: 'grid', gap: 'var(--space-6)', padding: 'var(--space-9)' }} aria-busy="true">
      {Array.from({ length: filas }, (_, i) => (
        <Skeleton key={i} height={20} />
      ))}
    </div>
  );
}

/** Descarga un PDF con la sesión (cookies) y lo abre en una pestaña nueva. */
export async function abrirPdf(ruta: string): Promise<void> {
  const { data } = await clienteHttp.get<Blob>(ruta, { responseType: 'blob' });
  const url = URL.createObjectURL(new Blob([data], { type: 'application/pdf' }));
  window.open(url, '_blank', 'noopener');
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
