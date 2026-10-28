import { useEffect, useState, type JSX, type ReactNode } from 'react';
import { useForm } from 'react-hook-form';
import { aplicarErroresApi } from '../api/formularios';
import { Button } from '../design-system/components/core/Button';
import { Alert } from '../design-system/components/feedback/Alert';
import { Dialog } from '../design-system/components/feedback/Dialog';
import { Textarea } from '../design-system/components/forms/Textarea';
import { textos } from '../i18n/textos';

export interface ConfirmarDestructivoProps {
  open: boolean;
  /** Qué se hace, en infinitivo: "Anular factura". Es el título y el texto del botón. */
  accion: string;
  /** Qué pasa al hacerlo (DESIGN §5): "Los cobros vinculados se revierten." */
  consecuencia: ReactNode;
  /** Se llama con el motivo ya recortado. Si lanza, el error de la API se pinta en el diálogo y no se cierra. */
  onConfirmar: (motivo: string) => Promise<void>;
  onCerrar: () => void;
  /** Longitud mínima del motivo; por defecto basta con que no esté vacío. Que sea la que exija la API. */
  minimoMotivo?: number;
}

interface Valores {
  motivo: string;
}

/**
 * Confirmación de una acción destructiva (DESIGN §5): dice qué pasa y pide un motivo obligatorio.
 * Los registros con efecto económico no se borran, se anulan (AGENTS §6), y el motivo va a la
 * auditoría: por eso el botón no se habilita sin él. Escape cierra, salvo mientras se envía.
 */
export function ConfirmarDestructivo({ open, accion, consecuencia, onConfirmar, onCerrar, minimoMotivo = 1 }: ConfirmarDestructivoProps): JSX.Element | null {
  const t = textos.confirmarDestructivo;
  const [general, setGeneral] = useState<string | null>(null);
  const { register, handleSubmit, setError, reset, watch, formState } = useForm<Valores>({ defaultValues: { motivo: '' } });
  const { isSubmitting, errors } = formState;
  const motivo = watch('motivo');

  useEffect(() => {
    if (!open) return;
    reset();
    setGeneral(null);
  }, [open, reset]);

  useEffect(() => {
    if (!open || isSubmitting) return;
    const tecla = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onCerrar();
    };
    document.addEventListener('keydown', tecla);
    return () => document.removeEventListener('keydown', tecla);
  }, [open, isSubmitting, onCerrar]);

  async function enviar({ motivo: valor }: Valores) {
    setGeneral(null);
    try {
      await onConfirmar(valor.trim());
      onCerrar();
    } catch (e) {
      setGeneral(aplicarErroresApi(e, setError, ['motivo'], t.error));
    }
  }

  const valido = motivo.trim().length >= minimoMotivo;

  return (
    <Dialog
      open={open}
      title={accion}
      onClose={isSubmitting ? undefined : onCerrar}
      footer={
        <>
          <Button variant="ghost" onClick={onCerrar} disabled={isSubmitting}>
            {t.cancelar}
          </Button>
          <Button variant="danger" type="submit" form="confirmar-destructivo" disabled={!valido || isSubmitting}>
            {isSubmitting ? t.enviando : accion}
          </Button>
        </>
      }
    >
      <form id="confirmar-destructivo" onSubmit={handleSubmit(enviar)} noValidate style={{ display: 'grid', gap: 'var(--space-7)' }}>
        <p style={{ margin: 0, color: 'var(--text-muted)' }}>{consecuencia}</p>
        <Textarea
          label={t.motivo}
          required
          rows={3}
          autoFocus
          error={errors.motivo?.message}
          {...register('motivo', {
            validate: (v) => v.trim().length >= minimoMotivo || (minimoMotivo > 1 ? t.motivoCorto.replace('{n}', String(minimoMotivo)) : t.motivoObligatorio),
          })}
        />
        {general && (
          <Alert tone="danger" title={t.errorTitulo}>
            {general}
          </Alert>
        )}
      </form>
    </Dialog>
  );
}
