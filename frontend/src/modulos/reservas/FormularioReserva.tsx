import { useEffect, useMemo, useState, type JSX } from 'react';
import { useFieldArray, useForm } from 'react-hook-form';
import { Button } from '../../design-system/components/core/Button';
import { Alert } from '../../design-system/components/feedback/Alert';
import { Drawer } from '../../design-system/components/feedback/Drawer';
import { DatePicker } from '../../design-system/components/forms/DatePicker';
import { Input } from '../../design-system/components/forms/Input';
import { Textarea } from '../../design-system/components/forms/Textarea';
import { SegmentedControl } from '../../design-system/components/navigation/SegmentedControl';
import { formatearImporte } from '../../dominio/dinero';
import { SelectorDestinatario, SelectorUnidad } from '../operacion/Selectores';
import { MONEDAS } from '../operacion/etiquetas';
import type { MonedaApi, UnidadConInmueble } from '../operacion/tipos';
import { useUnidades } from '../operacion/api';
import { useGuardarReserva, type ReservaDetalle } from './api';
import { AvisoBloqueo } from './AvisoBloqueo';
import { FORMULARIO_VACIO, cuerpoReserva, hayErroresReserva, validarReserva, type FormularioReserva } from './cuerpo';
import { textosReservas } from './textos';

const t = textosReservas.formulario;

export function valoresDeReserva(r?: ReservaDetalle, base?: Partial<FormularioReserva>): FormularioReserva {
  if (!r) return { ...FORMULARIO_VACIO, ...base };
  const corta = r.tipo === 'Corta';
  return {
    unidadId: r.unidadId,
    destinatarioTipo: r.prospectoId ? 'prospecto' : 'cliente',
    destinatarioId: r.clienteId ?? r.prospectoId,
    tipo: r.tipo,
    entradaPrevista: r.entradaPrevista,
    modalidadCorta: r.modalidad === 'MedioDia' ? 'MedioDia' : 'DiaCompleto',
    moneda: r.moneda,
    importe: String(r.importe),
    anticipoImporte: r.anticipoImporte == null ? '' : String(r.anticipoImporte),
    anticipoNota: r.anticipoNota ?? '',
    observaciones: r.observaciones ?? '',
    dias: corta ? r.dias.map((d) => ({ fecha: d.fecha, horaOrientativa: d.horaOrientativa ?? '' })) : [],
  };
}

/** Alta y edición de una reserva mensual o corta. El anticipo es una nota, nunca un cobro. */
export function FormularioReservaDrawer({
  open,
  reserva,
  inicial,
  aviso,
  onCerrar,
  onGuardada,
}: {
  open: boolean;
  reserva?: ReservaDetalle;
  inicial?: Partial<FormularioReserva>;
  /** Texto informativo encima del formulario (p. ej. "datos copiados de una cotización"). */
  aviso?: string;
  onCerrar: () => void;
  onGuardada: (id: string) => void;
}): JSX.Element {
  const guardar = useGuardarReserva(reserva?.id);
  const [fallo, setFallo] = useState<unknown>(null);
  const [intentado, setIntentado] = useState(false);
  const { control, handleSubmit, watch, setValue, reset, register } = useForm<FormularioReserva>({ defaultValues: valoresDeReserva(reserva, inicial) });
  const { fields, append, remove } = useFieldArray({ control, name: 'dias' });
  const { unidades } = useUnidades();

  useEffect(() => {
    if (!open) return;
    reset(valoresDeReserva(reserva, inicial));
    setFallo(null);
    setIntentado(false);
  }, [open, reserva, inicial, reset]);

  const v = watch();
  const errores = validarReserva(v);
  const corta = v.tipo === 'Corta';
  const modalidad = corta ? v.modalidadCorta : 'Mensual';
  const unidad: UnidadConInmueble | undefined = useMemo(() => unidades.find((u) => u.id === v.unidadId), [unidades, v.unidadId]);
  const tarifa = unidad?.tarifas.find((x) => x.modalidad === modalidad && x.moneda === v.moneda);

  async function enviar(f: FormularioReserva) {
    setIntentado(true);
    setFallo(null);
    if (hayErroresReserva(validarReserva(f))) return;
    try {
      const guardada = await guardar.mutateAsync(cuerpoReserva(f));
      onGuardada(guardada.id);
    } catch (e) {
      setFallo(e);
    }
  }

  const ver = <T,>(mensaje: T | undefined) => (intentado ? mensaje : undefined);

  return (
    <Drawer
      open={open}
      onClose={guardar.isPending ? undefined : onCerrar}
      width={640}
      title={reserva ? t.tituloEditar : t.tituloNueva}
      footer={
        <>
          <Button variant="ghost" onClick={onCerrar} disabled={guardar.isPending}>
            {t.cancelar}
          </Button>
          <Button type="submit" form="form-reserva" disabled={guardar.isPending}>
            {guardar.isPending ? t.guardando : t.guardar}
          </Button>
        </>
      }
    >
      <form id="form-reserva" onSubmit={handleSubmit(enviar)} noValidate style={{ display: 'grid', gap: 'var(--space-9)' }}>
        {fallo !== null && <AvisoBloqueo error={fallo} titulo={t.error} />}
        {aviso && <Alert tone="info">{aviso}</Alert>}

        <SelectorUnidad valor={v.unidadId} onChange={(id) => setValue('unidadId', id)} error={ver(errores.unidad)} />
        <SelectorDestinatario
          valor={{ tipo: v.destinatarioTipo, id: v.destinatarioId }}
          onChange={(d) => {
            setValue('destinatarioTipo', d.tipo);
            setValue('destinatarioId', d.id);
          }}
          error={ver(errores.destinatario)}
        />

        <div className="ar-field">
          <span className="ar-label">{t.tipo}</span>
          <SegmentedControl
            options={[
              { value: 'Mensual', label: t.mensual },
              { value: 'Corta', label: t.corta },
            ]}
            value={v.tipo}
            onChange={(x) => {
              if (reserva) return; // el tipo no cambia al editar
              setValue('tipo', x as FormularioReserva['tipo']);
              if (x === 'Corta' && v.dias.length === 0) append({ fecha: '', horaOrientativa: '' });
            }}
          />
          <span className="ar-hint">{corta ? t.cortaAyuda : t.mensualAyuda}</span>
        </div>

        {!corta ? (
          <DatePicker label={t.entrada} required value={v.entradaPrevista} onChange={(e) => setValue('entradaPrevista', e.target.value)} error={ver(errores.entrada)} />
        ) : (
          <>
            <div className="ar-field">
              <span className="ar-label">{t.modalidad}</span>
              <SegmentedControl
                options={[
                  { value: 'MedioDia', label: t.medioDia },
                  { value: 'DiaCompleto', label: t.diaCompleto },
                ]}
                value={v.modalidadCorta}
                onChange={(x) => setValue('modalidadCorta', x as FormularioReserva['modalidadCorta'])}
              />
              <span className="ar-hint">{t.modalidadAyuda}</span>
            </div>
            <fieldset style={{ border: 0, padding: 0, margin: 0, display: 'grid', gap: 'var(--space-6)' }}>
              <legend style={{ font: 'var(--type-label)', padding: 0, marginBottom: 'var(--space-4)' }}>{t.dias}</legend>
              {intentado && errores.dias && <Alert tone="danger">{errores.dias}</Alert>}
              {fields.map((campo, i) => (
                <div key={campo.id} style={{ display: 'grid', gridTemplateColumns: '170px minmax(0,1fr) 32px', gap: 'var(--space-4)', alignItems: 'start' }}>
                  <DatePicker
                    id={`dia-${i}-fecha`}
                    placeholder="dd/mm/aaaa"
                    value={v.dias[i]?.fecha ?? ''}
                    onChange={(e) => setValue(`dias.${i}.fecha`, e.target.value)}
                  />
                  <Input
                    id={`dia-${i}-hora`}
                    aria-label={`${t.hora} ${i + 1}`}
                    placeholder={t.horaPista}
                    maxLength={60}
                    error={ver(errores.hora[i])}
                    {...register(`dias.${i}.horaOrientativa`)}
                  />
                  <Button variant="ghost" size="sm" icon="x" type="button" aria-label={`${t.quitarDia} ${i + 1}`} disabled={fields.length === 1} onClick={() => remove(i)} style={{ padding: 0 }} />
                </div>
              ))}
              <span className="ar-hint">
                {t.hora}. {t.horaAyuda}
              </span>
              <div>
                <Button variant="secondary" size="sm" icon="plus" type="button" onClick={() => append({ fecha: '', horaOrientativa: '' })}>
                  {t.anadirDia}
                </Button>
              </div>
            </fieldset>
          </>
        )}

        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--space-6)' }}>
          <div className="ar-field">
            <span className="ar-label">{t.moneda}</span>
            <SegmentedControl options={MONEDAS.map((m) => ({ value: m, label: m }))} value={v.moneda} onChange={(x) => setValue('moneda', x as MonedaApi)} />
          </div>
          <Input
            id="reserva-importe"
            label={t.importe}
            inputMode="decimal"
            suffix={v.moneda}
            hint={tarifa ? t.tarifaUnidad.replace('{tarifa}', formatearImporte(tarifa.importe, tarifa.moneda)) : undefined}
            error={ver(errores.importe)}
            {...register('importe')}
          />
        </div>

        <Alert tone="info">{textosReservas.anticipoNota}</Alert>
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--space-6)' }}>
          <Input id="reserva-anticipo" label={t.anticipo} inputMode="decimal" suffix={v.moneda} error={ver(errores.anticipo)} {...register('anticipoImporte')} />
          <Input id="reserva-anticipo-nota" label={t.anticipoNotaCampo} placeholder={t.anticipoNotaPista} maxLength={300} {...register('anticipoNota')} />
        </div>

        <Textarea id="reserva-observaciones" label={t.observaciones} rows={3} maxLength={1000} {...register('observaciones')} />
      </form>
    </Drawer>
  );
}
