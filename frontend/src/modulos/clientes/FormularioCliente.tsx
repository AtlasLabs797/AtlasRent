import { useEffect, useState, type JSX } from 'react';
import { useForm } from 'react-hook-form';
import { aplicarErroresApi } from '../../api/formularios';
import { Button } from '../../design-system/components/core/Button';
import { Alert } from '../../design-system/components/feedback/Alert';
import { Drawer } from '../../design-system/components/feedback/Drawer';
import { Checkbox } from '../../design-system/components/forms/Checkbox';
import { Input } from '../../design-system/components/forms/Input';
import { Select } from '../../design-system/components/forms/Select';
import { Textarea } from '../../design-system/components/forms/Textarea';
import { ACCIONES } from '../../permisos';
import { puede, useSesion } from '../../sesion/store';
import { useGuardarCliente } from './api';
import { cuerpoCliente, emailPlausible, numeroValido, TIPOS, valoresCliente, type ValoresCliente } from './logica';
import { textosClientes } from './textos';
import type { ClienteFicha, ClienteResumen } from './tipos';

const t = textosClientes.formulario;
const CAMPOS = ['nombre', 'tipo', 'documento', 'email', 'telefono', 'direccion', 'notas', 'numero'] as const;

interface Props {
  open: boolean;
  onClose: () => void;
  /** Cliente que se edita; sin él es un alta. */
  ficha?: ClienteFicha | undefined;
  /** Valores de partida de un alta (p. ej. copiados de un prospecto). */
  inicial?: ValoresCliente | undefined;
  onGuardado: (cliente: ClienteResumen) => void;
}

/** Alta y edición de cliente en un panel lateral. El número solo se ofrece a quien puede fijarlo. */
export function FormularioCliente({ open, onClose, ficha, inicial, onGuardado }: Props): JSX.Element {
  const alta = ficha === undefined;
  const identidad = useSesion((s) => s.identidad);
  const puedeFijarNumero = alta && puede(identidad, ACCIONES.configuracionEditar);
  const guardar = useGuardarCliente();
  const [general, setGeneral] = useState<string | null>(null);
  const { register, handleSubmit, setError, reset, watch, setValue, formState } = useForm<ValoresCliente>({
    defaultValues: ficha ? valoresCliente(ficha) : (inicial ?? valoresCliente()),
  });
  const { errors, isSubmitting } = formState;

  useEffect(() => {
    if (!open) return;
    reset(ficha ? valoresCliente(ficha) : (inicial ?? valoresCliente()));
    setGeneral(null);
  }, [open, ficha, inicial, reset]);

  async function enviar(v: ValoresCliente) {
    setGeneral(null);
    try {
      const guardado = await guardar.mutateAsync({
        ...(ficha ? { id: ficha.id } : {}),
        cuerpo: cuerpoCliente(v, { alta, puedeFijarNumero }),
      });
      onGuardado(guardado);
    } catch (e) {
      setGeneral(aplicarErroresApi(e, setError, CAMPOS, t.errorGeneral));
    }
  }

  const largo = (max: number) => ({ value: max, message: t.errores.largo });

  return (
    <Drawer
      open={open}
      onClose={isSubmitting ? undefined : onClose}
      title={alta ? t.tituloAlta : t.tituloEdicion}
      description={alta ? t.descripcionAlta : undefined}
      footer={
        <>
          <Button variant="ghost" onClick={onClose} disabled={isSubmitting}>
            {t.cancelar}
          </Button>
          <Button type="submit" form="formulario-cliente" disabled={isSubmitting}>
            {isSubmitting ? t.guardando : t.guardar}
          </Button>
        </>
      }
    >
      <form id="formulario-cliente" onSubmit={handleSubmit(enviar)} noValidate style={{ display: 'grid', gap: 'var(--space-7)' }}>
        {general && (
          <Alert tone="danger" title={t.errorGeneral}>
            {general}
          </Alert>
        )}
        <Input
          label={t.nombre}
          required
          autoComplete="off"
          error={errors.nombre?.message}
          {...register('nombre', {
            validate: (v) => v.trim() !== '' || t.errores.obligatorio,
            maxLength: largo(200),
          })}
        />
        <Select
          label={t.tipo}
          value={watch('tipo')}
          options={TIPOS.map((valor) => ({ value: valor, label: textosClientes.tipos[valor] }))}
          onChange={(e) => setValue('tipo', e.target.value as ValoresCliente['tipo'])}
        />
        <Input
          label={t.documento}
          hint={t.documentoPista}
          autoComplete="off"
          error={errors.documento?.message}
          {...register('documento', { maxLength: largo(64) })}
        />
        <Input
          label={t.email}
          type="email"
          autoComplete="off"
          error={errors.email?.message}
          {...register('email', { validate: (v) => emailPlausible(v) || t.errores.email, maxLength: largo(200) })}
        />
        <Input label={t.telefono} type="tel" autoComplete="off" error={errors.telefono?.message} {...register('telefono', { maxLength: largo(40) })} />
        <Input label={t.direccion} autoComplete="off" error={errors.direccion?.message} {...register('direccion', { maxLength: largo(300) })} />
        <Textarea label={t.notas} rows={3} error={errors.notas?.message} {...register('notas', { maxLength: largo(1000) })} />
        <div style={{ display: 'grid', gap: 'var(--space-5)' }}>
          <Checkbox label={textosClientes.marcas.agenteRetencion} {...register('agenteRetencion')} />
          <Checkbox label={textosClientes.marcas.enGestionCobro} {...register('enGestionCobro')} />
          <Checkbox label={textosClientes.marcas.recordatoriosPausados} {...register('recordatoriosPausados')} />
        </div>
        {puedeFijarNumero && (
          <Input
            label={t.numero}
            hint={t.numeroPista}
            inputMode="numeric"
            autoComplete="off"
            error={errors.numero?.message}
            {...register('numero', { validate: (v) => v.trim() === '' || numeroValido(v) || t.errores.numero })}
          />
        )}
      </form>
    </Drawer>
  );
}
