import { useEffect, useState, type JSX } from 'react';
import { useForm } from 'react-hook-form';
import { aplicarErroresApi } from '../../api/formularios';
import { Button } from '../../design-system/components/core/Button';
import { Alert } from '../../design-system/components/feedback/Alert';
import { Drawer } from '../../design-system/components/feedback/Drawer';
import { Input } from '../../design-system/components/forms/Input';
import { Select } from '../../design-system/components/forms/Select';
import { Textarea } from '../../design-system/components/forms/Textarea';
import { useGuardarInmueble } from './api';
import { cuerpoInmueble, mesesValidos, TIPOS_INMUEBLE, valoresInmueble, type ValoresInmueble } from './logica';
import { textosUnidades } from './textos';
import type { InmuebleFicha, InmuebleResumen, TipoInmueble } from './tipos';

const t = textosUnidades.formularioInmueble;
const CAMPOS = ['nombre', 'direccion', 'tipo', 'notas', 'plantillaContratoPorDefecto', 'renovacionDefectoMeses'] as const;

interface Props {
  open: boolean;
  onClose: () => void;
  ficha?: InmuebleFicha | undefined;
  onGuardado: (inmueble: InmuebleResumen) => void;
}

export function FormularioInmueble({ open, onClose, ficha, onGuardado }: Props): JSX.Element {
  const guardar = useGuardarInmueble();
  const [general, setGeneral] = useState<string | null>(null);
  const { register, handleSubmit, reset, setError, watch, setValue, formState } = useForm<ValoresInmueble>({ defaultValues: valoresInmueble(ficha) });
  const { errors, isSubmitting } = formState;

  useEffect(() => {
    if (!open) return;
    reset(valoresInmueble(ficha));
    setGeneral(null);
  }, [open, ficha, reset]);

  async function enviar(v: ValoresInmueble) {
    setGeneral(null);
    try {
      onGuardado(await guardar.mutateAsync({ ...(ficha ? { id: ficha.id } : {}), cuerpo: cuerpoInmueble(v, ficha) }));
    } catch (e) {
      setGeneral(aplicarErroresApi(e, setError, CAMPOS, t.errorGeneral));
    }
  }

  return (
    <Drawer
      open={open}
      onClose={isSubmitting ? undefined : onClose}
      title={ficha ? t.tituloEdicion : t.tituloAlta}
      footer={
        <>
          <Button variant="ghost" onClick={onClose} disabled={isSubmitting}>
            {t.cancelar}
          </Button>
          <Button type="submit" form="formulario-inmueble" disabled={isSubmitting}>
            {isSubmitting ? t.guardando : t.guardar}
          </Button>
        </>
      }
    >
      <form id="formulario-inmueble" onSubmit={handleSubmit(enviar)} noValidate style={{ display: 'grid', gap: 'var(--space-7)' }}>
        {general && (
          <Alert tone="danger" title={t.errorGeneral}>
            {general}
          </Alert>
        )}
        <Input label={t.nombre} required autoComplete="off" error={errors.nombre?.message} {...register('nombre', { validate: (v) => v.trim() !== '' || t.errores.obligatorio, maxLength: { value: 200, message: t.errores.largo } })} />
        <Select label={t.tipo} value={watch('tipo')} options={TIPOS_INMUEBLE.map((v) => ({ value: v, label: textosUnidades.tiposInmueble[v] }))} onChange={(e) => setValue('tipo', e.target.value as TipoInmueble)} />
        <Input label={t.direccion} autoComplete="off" error={errors.direccion?.message} {...register('direccion', { maxLength: { value: 300, message: t.errores.largo } })} />
        <Input label={t.plantilla} autoComplete="off" error={errors.plantillaContratoPorDefecto?.message} {...register('plantillaContratoPorDefecto', { maxLength: { value: 120, message: t.errores.largo } })} />
        <Input label={t.renovacion} inputMode="numeric" autoComplete="off" error={errors.renovacionDefectoMeses?.message} {...register('renovacionDefectoMeses', { validate: (v) => mesesValidos(v) || t.errores.meses })} />
        <Textarea label={t.notas} rows={3} error={errors.notas?.message} {...register('notas', { maxLength: { value: 1000, message: t.errores.largo } })} />
      </form>
    </Drawer>
  );
}
