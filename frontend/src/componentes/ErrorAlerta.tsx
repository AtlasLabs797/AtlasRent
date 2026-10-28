import type { JSX } from 'react';
import { aErrorApi } from '../api/errores';
import { Alert } from '../design-system/components/feedback/Alert';
import { textos } from '../i18n/textos';

/**
 * Aviso de un fallo de la API: el `detail` del backend si lo hay, el texto de reserva si no hubo
 * respuesta, y el código de referencia (lo que el soporte busca en el log). No enseña trazas.
 * `reserva` sustituye al mensaje cuando la pantalla sabe explicar mejor el caso (p. ej. un 401 de login).
 */
export function ErrorAlerta({ error, titulo, reserva }: { error: unknown; titulo: string; reserva?: string }): JSX.Element {
  const e = aErrorApi(error);
  const mensaje = e.status === undefined ? textos.acceso.sinConexion : (reserva ?? e.detail ?? e.title);
  return (
    <Alert tone="danger" title={titulo}>
      {mensaje}
      {e.codigoReferencia && (
        <span style={{ display: 'block', marginTop: 4, font: 'var(--type-fine)' }} className="mono">
          {textos.acceso.codigoReferencia.replace('{codigo}', e.codigoReferencia)}
        </span>
      )}
    </Alert>
  );
}
