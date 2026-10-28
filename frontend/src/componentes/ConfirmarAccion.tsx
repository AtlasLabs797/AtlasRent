import { useEffect, useState, type JSX, type ReactNode } from 'react';
import { mensajeDeError } from '../api/errores';
import { Button } from '../design-system/components/core/Button';
import { Alert } from '../design-system/components/feedback/Alert';
import { Dialog } from '../design-system/components/feedback/Dialog';

export interface ConfirmarAccionProps {
  open: boolean;
  /** Qué se hace, en infinitivo: "Desactivar usuario". Es el título y el texto del botón. */
  accion: string;
  /** Qué pasa al hacerlo (DESIGN §5). */
  consecuencia: ReactNode;
  /** Si lanza, el error de la API se pinta en el diálogo y no se cierra. */
  onConfirmar: () => Promise<void>;
  onCerrar: () => void;
  peligrosa?: boolean;
}

/**
 * Confirmación sin motivo, para acciones cuyo endpoint no recibe uno (desactivar, revocar, dar de
 * baja). Si la API pidiera motivo, se usa `ConfirmarDestructivo`: pedir un motivo que no se guarda
 * en ningún sitio sería engañar a quien lo escribe.
 */
export function ConfirmarAccion({ open, accion, consecuencia, onConfirmar, onCerrar, peligrosa = true }: ConfirmarAccionProps): JSX.Element | null {
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (open) setError(null);
  }, [open]);

  async function confirmar() {
    setEnviando(true);
    setError(null);
    try {
      await onConfirmar();
      onCerrar();
    } catch (e) {
      setError(mensajeDeError(e, 'No se ha podido aplicar el cambio.'));
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Dialog
      open={open}
      title={accion}
      onClose={enviando ? undefined : onCerrar}
      footer={
        <>
          <Button variant="ghost" onClick={onCerrar} disabled={enviando}>
            Cancelar
          </Button>
          <Button variant={peligrosa ? 'danger' : 'primary'} onClick={() => void confirmar()} disabled={enviando}>
            {enviando ? 'Aplicando…' : accion}
          </Button>
        </>
      }
    >
      <div style={{ display: 'grid', gap: 'var(--space-7)' }}>
        <p style={{ margin: 0, color: 'var(--text-muted)' }}>{consecuencia}</p>
        {error && (
          <Alert tone="danger" title="No se ha podido aplicar el cambio">
            {error}
          </Alert>
        )}
      </div>
    </Dialog>
  );
}
