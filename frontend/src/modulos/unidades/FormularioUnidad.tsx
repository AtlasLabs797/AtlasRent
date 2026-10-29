import { useEffect, useState, type JSX } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { aErrorApi } from '../../api/errores';
import { aplicarErroresApi } from '../../api/formularios';
import { Badge } from '../../design-system/components/core/Badge';
import { Button } from '../../design-system/components/core/Button';
import { Alert } from '../../design-system/components/feedback/Alert';
import { Dialog } from '../../design-system/components/feedback/Dialog';
import { Drawer } from '../../design-system/components/feedback/Drawer';
import { Checkbox } from '../../design-system/components/forms/Checkbox';
import { Input } from '../../design-system/components/forms/Input';
import { Select } from '../../design-system/components/forms/Select';
import { Textarea } from '../../design-system/components/forms/Textarea';
import { useCambiarActivaUnidad, useGuardarUnidad } from './api';
import {
  claveTarifa,
  cuerpoUnidad,
  decimalSimpleValido,
  enteroValido,
  errorImporte,
  ESTADOS_UNIDAD,
  MODALIDADES,
  MONEDAS,
  TIPOS_UNIDAD,
  valoresUnidad,
  type ValoresUnidad,
} from './logica';
import { textosUnidades } from './textos';
import type { EstadoUnidad, ModalidadAlquiler, TipoUnidad, Unidad } from './tipos';

const t = textosUnidades.formularioUnidad;
const u = textosUnidades.unidades;
const CAMPOS = ['nombre', 'tipo', 'estadoManual', 'modalidades', 'tarifas', 'metrosCuadrados', 'capacidad', 'habitaciones', 'banos', 'medidor', 'notas'] as const;

interface Props {
  inmuebleId: string;
  /** `null` cerrado, `'nueva'` alta, o la unidad que se edita. */
  unidad: Unidad | 'nueva' | null;
  onClose: () => void;
}

/** Ficha editable de una unidad: estado manual, modalidades, tarifas (como texto decimal) y opcionales. */
export function FormularioUnidad({ inmuebleId, unidad, onClose }: Props): JSX.Element {
  const existente = unidad !== null && unidad !== 'nueva' ? unidad : undefined;
  const guardar = useGuardarUnidad(inmuebleId);
  const cambiarActiva = useCambiarActivaUnidad(inmuebleId);
  const [confirmando, setConfirmando] = useState(false);
  const [general, setGeneral] = useState<string | null>(null);
  const { register, handleSubmit, reset, setError, watch, setValue, control, formState } = useForm<ValoresUnidad>({ defaultValues: valoresUnidad(existente) });
  const { errors, isSubmitting } = formState;
  const modalidades = watch('modalidades');

  useEffect(() => {
    if (unidad === null) return;
    reset(valoresUnidad(existente));
    setGeneral(null);
  }, [unidad, existente, reset]);

  async function enviar(v: ValoresUnidad) {
    setGeneral(null);
    try {
      await guardar.mutateAsync({ ...(existente ? { unidadId: existente.id } : {}), cuerpo: cuerpoUnidad(v, existente) });
      onClose();
    } catch (e) {
      setGeneral(aplicarErroresApi(e, setError, CAMPOS, t.errorGeneral));
    }
  }

  /** Desactivar (409 si hay contrato o reserva vivos: la API da el motivo) o reactivar. */
  async function cambiarEstado(activar: boolean) {
    if (!existente) return;
    setGeneral(null);
    try {
      await cambiarActiva.mutateAsync({ unidadId: existente.id, activar });
      setConfirmando(false);
      onClose();
    } catch (e) {
      setConfirmando(false);
      setGeneral(aErrorApi(e).detail ?? (activar ? u.reactivarError : u.desactivarError));
    }
  }

  function alternarModalidad(m: ModalidadAlquiler, marcada: boolean) {
    const actuales = watch('modalidades');
    setValue('modalidades', marcada ? [...actuales, m] : actuales.filter((x) => x !== m), { shouldValidate: true });
  }

  const largo = (max: number) => ({ value: max, message: t.largo });
  const decimal = (v: string) => decimalSimpleValido(v) || t.numeroError;
  const entero = (v: string) => enteroValido(v) || t.numeroError;

  return (
    <>
    <Drawer
      open={unidad !== null}
      onClose={isSubmitting ? undefined : onClose}
      title={existente ? existente.nombre : t.tituloAlta}
      description={existente ? textosUnidades.unidades.detalle : undefined}
      footer={
        <>
          {existente?.activo && (
            <Button variant="ghost" onClick={() => setConfirmando(true)} disabled={isSubmitting} style={{ marginRight: 'auto' }}>
              {u.desactivar}
            </Button>
          )}
          {existente && !existente.activo && (
            <Button variant="secondary" onClick={() => void cambiarEstado(true)} disabled={isSubmitting || cambiarActiva.isPending} style={{ marginRight: 'auto' }}>
              {u.reactivar}
            </Button>
          )}
          <Button variant="ghost" onClick={onClose} disabled={isSubmitting}>
            {t.cancelar}
          </Button>
          <Button type="submit" form="formulario-unidad" disabled={isSubmitting}>
            {isSubmitting ? t.guardando : t.guardar}
          </Button>
        </>
      }
    >
      <form id="formulario-unidad" onSubmit={handleSubmit(enviar)} noValidate style={{ display: 'grid', gap: 'var(--space-7)' }}>
        {general && (
          <Alert tone="danger" title={t.errorGeneral}>
            {general}
          </Alert>
        )}
        {existente && !existente.activo && <Alert tone="warning">{textosUnidades.unidades.inactivaAviso}</Alert>}
        {existente && (
          <div>
            {existente.activo ? <Badge status="activo" /> : <Badge tone="neutral">{textosUnidades.inactivo}</Badge>}
          </div>
        )}
        <Input label={t.nombre} required autoComplete="off" error={errors.nombre?.message} {...register('nombre', { validate: (v) => v.trim() !== '' || t.obligatorio, maxLength: largo(120) })} />
        <Select label={t.tipo} value={watch('tipo')} options={TIPOS_UNIDAD.map((v) => ({ value: v, label: textosUnidades.tiposUnidad[v] }))} onChange={(e) => setValue('tipo', e.target.value as TipoUnidad)} />
        <Select
          label={t.estado}
          hint={t.estadoPista}
          value={watch('estadoManual')}
          options={ESTADOS_UNIDAD.map((v) => ({ value: v, label: textosUnidades.estados[v] }))}
          onChange={(e) => setValue('estadoManual', e.target.value as EstadoUnidad)}
        />

        <fieldset style={{ border: 0, padding: 0, margin: 0, display: 'grid', gap: 'var(--space-5)' }}>
          <legend className="ar-label">{t.modalidades}</legend>
          <Controller
            control={control}
            name="modalidades"
            rules={{ validate: (v) => v.length > 0 || t.modalidadesError }}
            render={() => (
              <>
                {MODALIDADES.map((m) => (
                  <Checkbox key={m} label={textosUnidades.modalidades[m]} checked={modalidades.includes(m)} onChange={(e) => alternarModalidad(m, e.target.checked)} />
                ))}
              </>
            )}
          />
          {errors.modalidades?.message && <span className="ar-hint ar-hint--error">{errors.modalidades.message}</span>}
        </fieldset>

        {modalidades.length > 0 && (
          <fieldset style={{ border: 0, padding: 0, margin: 0, display: 'grid', gap: 'var(--space-5)' }}>
            <legend className="ar-label">{t.tarifas}</legend>
            <span className="ar-hint">{t.tarifasPista}</span>
            {MODALIDADES.filter((m) => modalidades.includes(m)).map((m) => (
              <div key={m} style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 'var(--space-5)' }}>
                {MONEDAS.map((moneda) => {
                  const nombre = `tarifas.${claveTarifa(m, moneda)}` as `tarifas.${string}`;
                  return (
                    <Input
                      key={moneda}
                      label={`${textosUnidades.modalidades[m]} · ${moneda}`}
                      inputMode="decimal"
                      autoComplete="off"
                      suffix={moneda}
                      error={errors.tarifas?.[claveTarifa(m, moneda)]?.message}
                      {...register(nombre as 'tarifas', { validate: (v) => errorImporte(String(v ?? '')) === null || t.importeError })}
                    />
                  );
                })}
              </div>
            ))}
          </fieldset>
        )}

        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 'var(--space-5)' }}>
          <Input label={t.metros} inputMode="decimal" autoComplete="off" error={errors.metrosCuadrados?.message} {...register('metrosCuadrados', { validate: decimal })} />
          <Input label={t.capacidad} inputMode="numeric" autoComplete="off" error={errors.capacidad?.message} {...register('capacidad', { validate: entero })} />
          <Input label={t.habitaciones} inputMode="numeric" autoComplete="off" error={errors.habitaciones?.message} {...register('habitaciones', { validate: entero })} />
          <Input label={t.banos} hint={t.banosPista} inputMode="decimal" autoComplete="off" error={errors.banos?.message} {...register('banos', { validate: decimal })} />
        </div>
        <Input label={t.medidor} autoComplete="off" error={errors.medidor?.message} {...register('medidor', { maxLength: largo(120) })} />
        <Textarea label={t.notas} rows={3} error={errors.notas?.message} {...register('notas', { maxLength: largo(1000) })} />
      </form>
    </Drawer>
    <Dialog
      open={confirmando}
      title={u.desactivarTitulo}
      onClose={() => setConfirmando(false)}
      footer={
        <>
          <Button variant="ghost" onClick={() => setConfirmando(false)}>
            {t.cancelar}
          </Button>
          <Button variant="danger" disabled={cambiarActiva.isPending} onClick={() => void cambiarEstado(false)}>
            {u.desactivarConfirmar}
          </Button>
        </>
      }
    >
      <p style={{ margin: 0, color: 'var(--text-muted)' }}>{u.desactivarCuerpo}</p>
    </Dialog>
    </>
  );
}
