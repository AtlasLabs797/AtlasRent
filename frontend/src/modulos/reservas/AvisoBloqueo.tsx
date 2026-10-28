import type { JSX } from 'react';
import { aErrorApi } from '../../api/errores';
import { Alert } from '../../design-system/components/feedback/Alert';
import { clasificarBloqueo } from './cuerpo';
import { textosReservas } from './textos';

/**
 * Enseña un fallo de la API. Un 409 se clasifica (unidad ocupada, factura vigente, unidad no
 * alquilable) para dar un título claro; el detalle del servidor va debajo tal cual.
 */
export function AvisoBloqueo({ error, titulo }: { error: unknown; titulo?: string }): JSX.Element {
  const e = aErrorApi(error);
  const b = textosReservas.bloqueos;
  const tipo = e.status === 409 ? clasificarBloqueo(e.codigoReferencia, e.detail) : 'otro';
  const aviso =
    tipo === 'unidadNoDisponible'
      ? { titulo: b.unidadNoDisponible, ayuda: b.unidadNoDisponibleCuerpo }
      : tipo === 'facturaVigente'
        ? { titulo: b.facturaVigente, ayuda: b.facturaVigenteCuerpo }
        : tipo === 'unidadNoAlquilable'
          ? { titulo: b.unidadNoAlquilable, ayuda: b.unidadNoAlquilableCuerpo }
          : { titulo: titulo ?? b.otro, ayuda: undefined };
  return (
    <Alert tone={e.status === 409 ? 'warning' : 'danger'} title={aviso.titulo}>
      {e.detail ?? e.title}
      {aviso.ayuda && <span style={{ display: 'block', marginTop: 4 }}>{aviso.ayuda}</span>}
      {e.codigoReferencia && (
        <span className="mono" style={{ display: 'block', marginTop: 4, font: 'var(--type-fine)' }}>
          Código de referencia: {e.codigoReferencia}
        </span>
      )}
    </Alert>
  );
}
