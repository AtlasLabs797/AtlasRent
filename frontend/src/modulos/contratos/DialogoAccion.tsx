import { useEffect, useState, type JSX, type ReactNode } from 'react';
import { Button } from '../../design-system/components/core/Button';
import { Alert } from '../../design-system/components/feedback/Alert';
import { Dialog } from '../../design-system/components/feedback/Dialog';
import { AvisoBloqueo } from '../reservas/AvisoBloqueo';

/**
 * Diálogo de una acción del flujo del contrato: dice qué pasa, muestra el error de la API (409 con su
 * detalle, sin trazas) y se cierra solo si la acción sale bien. `ayudaError` explica cómo se resuelve un
 * bloqueo (p. ej. la liquidación pendiente de fianza o facturas).
 */
export function DialogoAccion({
  abierto,
  titulo,
  descripcion,
  confirmar,
  tono = 'primary',
  deshabilitado,
  ayudaError,
  tituloError,
  onConfirmar,
  onCerrar,
  children,
}: {
  abierto: boolean;
  titulo: string;
  descripcion?: ReactNode;
  confirmar: string;
  tono?: 'primary' | 'danger';
  deshabilitado?: boolean;
  ayudaError?: string;
  tituloError?: string;
  onConfirmar: () => Promise<unknown>;
  onCerrar: () => void;
  children?: ReactNode;
}): JSX.Element {
  const [pendiente, setPendiente] = useState(false);
  const [fallo, setFallo] = useState<unknown>(null);

  useEffect(() => {
    if (abierto) setFallo(null);
  }, [abierto]);

  async function aceptar() {
    setPendiente(true);
    setFallo(null);
    try {
      await onConfirmar();
      onCerrar();
    } catch (e) {
      setFallo(e);
    } finally {
      setPendiente(false);
    }
  }

  return (
    <Dialog
      open={abierto}
      title={titulo}
      description={descripcion}
      onClose={pendiente ? undefined : onCerrar}
      footer={
        <>
          <Button variant="ghost" onClick={onCerrar} disabled={pendiente}>
            Cancelar
          </Button>
          <Button variant={tono} onClick={() => void aceptar()} disabled={pendiente || deshabilitado}>
            {pendiente ? 'Procesando…' : confirmar}
          </Button>
        </>
      }
    >
      <div style={{ display: 'grid', gap: 'var(--space-6)' }}>
        {fallo !== null && (
          <>
            <AvisoBloqueo error={fallo} titulo={tituloError} />
            {ayudaError && <Alert tone="info">{ayudaError}</Alert>}
          </>
        )}
        {children}
      </div>
    </Dialog>
  );
}
