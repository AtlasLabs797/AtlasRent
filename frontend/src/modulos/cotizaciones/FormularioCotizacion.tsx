import { useEffect, useState, type JSX } from 'react';
import { useFieldArray, useForm } from 'react-hook-form';
import { aplicarErroresApi } from '../../api/formularios';
import { Button } from '../../design-system/components/core/Button';
import { Money } from '../../design-system/components/data/Money';
import { Alert } from '../../design-system/components/feedback/Alert';
import { Drawer } from '../../design-system/components/feedback/Drawer';
import { Input } from '../../design-system/components/forms/Input';
import { SegmentedControl } from '../../design-system/components/navigation/SegmentedControl';
import { SelectorDestinatario, SelectorUnidad } from '../operacion/Selectores';
import { MONEDAS } from '../operacion/etiquetas';
import type { MonedaApi } from '../operacion/tipos';
import { useGuardarCotizacion, type CotizacionDetalle } from './api';
import { vistaPrevia } from './calculo';
import { LINEA_VACIA, cuerpoCotizacion, hayErrores, validarCotizacion, type FormularioCotizacion } from './cuerpo';
import { textosCotizaciones } from './textos';

const t = textosCotizaciones.formulario;

export function valoresIniciales(c?: CotizacionDetalle, base?: Partial<FormularioCotizacion>): FormularioCotizacion {
  if (c) {
    return {
      destinatarioTipo: c.prospectoId ? 'prospecto' : 'cliente',
      destinatarioId: c.clienteId ?? c.prospectoId,
      unidadId: c.unidadId,
      moneda: c.moneda,
      lineas: c.lineas.map((l) => ({ descripcion: l.descripcion, cantidad: String(l.cantidad), precioUnitario: String(l.precioUnitario) })),
    };
  }
  return { destinatarioTipo: 'cliente', destinatarioId: null, unidadId: null, moneda: 'USD', lineas: [{ ...LINEA_VACIA }], ...base };
}

/** Alta y edición de una cotización en borrador. El total mostrado es solo una vista previa. */
export function FormularioCotizacionDrawer({
  open,
  cotizacion,
  onCerrar,
  onGuardada,
}: {
  open: boolean;
  cotizacion?: CotizacionDetalle;
  onCerrar: () => void;
  onGuardada: (id: string) => void;
}): JSX.Element {
  const guardar = useGuardarCotizacion(cotizacion?.id);
  const [general, setGeneral] = useState<string | null>(null);
  const [intentado, setIntentado] = useState(false);
  const { register, control, handleSubmit, watch, setValue, reset, setError } = useForm<FormularioCotizacion>({ defaultValues: valoresIniciales(cotizacion) });
  const { fields, append, remove } = useFieldArray({ control, name: 'lineas' });

  useEffect(() => {
    if (!open) return;
    reset(valoresIniciales(cotizacion));
    setGeneral(null);
    setIntentado(false);
  }, [open, cotizacion, reset]);

  const valores = watch();
  const previa = vistaPrevia(valores.lineas ?? []);

  const errores = validarCotizacion(valores);

  async function enviar(f: FormularioCotizacion) {
    setIntentado(true);
    setGeneral(null);
    if (hayErrores(validarCotizacion(f))) return;
    try {
      const guardada = await guardar.mutateAsync(cuerpoCotizacion(f));
      onGuardada(guardada.id);
    } catch (e) {
      setGeneral(aplicarErroresApi(e, setError, ['moneda', 'unidadId'], t.error));
    }
  }

  return (
    <Drawer
      open={open}
      onClose={guardar.isPending ? undefined : onCerrar}
      width={720}
      title={cotizacion ? t.tituloEditar : t.tituloNueva}
      footer={
        <>
          <Button variant="ghost" onClick={onCerrar} disabled={guardar.isPending}>
            {t.cancelar}
          </Button>
          <Button type="submit" form="form-cotizacion" disabled={guardar.isPending}>
            {guardar.isPending ? t.guardando : t.guardar}
          </Button>
        </>
      }
    >
      <form id="form-cotizacion" onSubmit={handleSubmit(enviar)} noValidate style={{ display: 'grid', gap: 'var(--space-9)' }}>
        {general && (
          <Alert tone="danger" title={t.error}>
            {general}
          </Alert>
        )}

        <SelectorDestinatario
          valor={{ tipo: valores.destinatarioTipo, id: valores.destinatarioId }}
          onChange={(d) => {
            setValue('destinatarioTipo', d.tipo);
            setValue('destinatarioId', d.id);
          }}
          error={intentado ? errores.destinatario : undefined}
        />

        <SelectorUnidad
          etiqueta={t.unidad}
          requerida={false}
          valor={valores.unidadId}
          onChange={(id) => setValue('unidadId', id)}
        />

        <div className="ar-field">
          <span className="ar-label">{t.moneda}</span>
          <SegmentedControl
            options={MONEDAS.map((m) => ({ value: m, label: m }))}
            value={valores.moneda}
            onChange={(v) => setValue('moneda', v as MonedaApi)}
          />
          <span className="ar-hint">{t.monedaAyuda}</span>
        </div>

        <fieldset style={{ border: 0, padding: 0, margin: 0, display: 'grid', gap: 'var(--space-6)' }}>
          <legend style={{ font: 'var(--type-label)', padding: 0, marginBottom: 'var(--space-4)' }}>{t.lineas}</legend>
          {intentado && errores.lineas && <Alert tone="danger">{errores.lineas}</Alert>}
          {fields.map((campo, i) => (
            <div
              key={campo.id}
              style={{ display: 'grid', gridTemplateColumns: 'minmax(0,2fr) 90px 130px 120px 32px', gap: 'var(--space-4)', alignItems: 'start' }}
            >
              <Input
                id={`linea-${i}-descripcion`}
                aria-label={`${t.descripcion} ${i + 1}`}
                placeholder={t.descripcion}
                error={intentado ? errores.linea[i] : undefined}
                {...register(`lineas.${i}.descripcion`)}
              />
              <Input id={`linea-${i}-cantidad`} aria-label={`${t.cantidad} ${i + 1}`} placeholder={t.cantidad} inputMode="decimal" style={{ textAlign: 'right' }} {...register(`lineas.${i}.cantidad`)} />
              <Input id={`linea-${i}-precio`} aria-label={`${t.precio} ${i + 1}`} placeholder={t.precio} inputMode="decimal" suffix={valores.moneda} {...register(`lineas.${i}.precioUnitario`)} />
              <div style={{ textAlign: 'right', paddingTop: 12 }}>
                {previa.importes[i] ? <Money amount={previa.importes[i]!} currency={valores.moneda} /> : <span style={{ color: 'var(--text-subtle)' }}>—</span>}
              </div>
              <Button
                variant="ghost"
                size="sm"
                icon="x"
                aria-label={`${t.quitarLinea} ${i + 1}`}
                disabled={fields.length === 1}
                onClick={() => remove(i)}
                type="button"
                style={{ padding: 0 }}
              />
            </div>
          ))}
          <div>
            <Button variant="secondary" size="sm" icon="plus" type="button" onClick={() => append({ ...LINEA_VACIA })}>
              {t.anadirLinea}
            </Button>
          </div>
        </fieldset>

        <div style={{ display: 'grid', gap: 'var(--space-4)', justifyItems: 'end', borderTop: '1px solid var(--border-subtle)', paddingTop: 'var(--space-6)' }}>
          <span style={{ color: 'var(--text-muted)' }}>{t.totalPrevia}</span>
          <Money amount={previa.total} currency={valores.moneda} strong />
          <span style={{ font: 'var(--type-fine)', color: 'var(--text-subtle)', textAlign: 'right' }}>{t.previaAviso}</span>
        </div>
      </form>
    </Drawer>
  );
}
