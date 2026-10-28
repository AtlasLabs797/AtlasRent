import { useState, type JSX, type ReactNode } from 'react';
import { useForm } from 'react-hook-form';
import { aplicarErroresApi } from '../../../api/formularios';
import { Button } from '../../../design-system/components/core/Button';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { Drawer } from '../../../design-system/components/feedback/Drawer';
import { Input } from '../../../design-system/components/forms/Input';
import { Select } from '../../../design-system/components/forms/Select';
import { Switch } from '../../../design-system/components/forms/Switch';
import { Textarea } from '../../../design-system/components/forms/Textarea';
import { construirCuerpo, validarCampo, valoresIniciales, type Campo, type Fila, type Valores } from '../esquema';
import { textosConfiguracion as t } from '../textos';

export interface FormularioEsquemaProps {
  titulo: string;
  descripcion?: ReactNode;
  campos: readonly Campo[];
  /** Fila que se edita; `null` para un alta. */
  fila: Fila | null;
  /** Campos que no se pueden tocar en esta fila y el motivo. */
  bloqueo?: { campos: readonly string[]; motivo: string } | null;
  /** Sin permiso de escritura: todo en solo lectura y sin botón de guardar. */
  soloLectura?: boolean;
  /** Valores por defecto de un alta (sobre los vacíos). */
  porDefecto?: Valores;
  /** Validación que cruza campos: devuelve mensajes por clave. */
  validarExtra?: (valores: Valores) => Record<string, string>;
  onGuardar: (cuerpo: Record<string, unknown>) => Promise<unknown>;
  onCerrar: () => void;
  /** Contenido extra en el pie (p. ej. "Dar de baja"). */
  pie?: ReactNode;
}

/** Drawer con un formulario generado desde un esquema (`esquema.ts`). El 400/409 de la API se enseña en el propio drawer. */
export function FormularioEsquema({ titulo, descripcion, campos, fila, bloqueo, soloLectura, porDefecto, validarExtra, onGuardar, onCerrar, pie }: FormularioEsquemaProps): JSX.Element {
  const iniciales: Valores = { ...valoresIniciales(campos, fila), ...(fila ? {} : porDefecto) };
  const { register, handleSubmit, setError, setValue, watch, formState } = useForm<Valores>({ defaultValues: iniciales });
  const { errors, isSubmitting } = formState;
  const [general, setGeneral] = useState<string | null>(null);

  const fijos = (c: Campo): boolean => soloLectura === true || (fila !== null && c.soloAlta === true) || (bloqueo?.campos.includes(c.clave) ?? false);

  async function enviar(valores: Valores) {
    setGeneral(null);
    const finales: Valores = { ...valores };
    for (const c of campos) if (fijos(c)) finales[c.clave] = iniciales[c.clave] ?? '';
    let hay = false;
    for (const c of campos) {
      const mensaje = validarCampo(c, finales[c.clave], t.validacion);
      if (mensaje) {
        setError(c.clave, { type: 'validacion', message: mensaje });
        hay = true;
      }
    }
    if (!hay && validarExtra) {
      for (const [clave, mensaje] of Object.entries(validarExtra(finales))) {
        setError(clave, { type: 'validacion', message: mensaje });
        hay = true;
      }
    }
    if (hay) return;
    try {
      await onGuardar(construirCuerpo(campos, finales, fila));
      onCerrar();
    } catch (e) {
      setGeneral(aplicarErroresApi(e, setError, campos.map((c) => c.clave), t.comun.errorGuardar));
    }
  }

  function campo(c: Campo): JSX.Element {
    const error = errors[c.clave]?.message;
    const deshabilitado = fijos(c);
    if (c.tipo === 'switch') {
      return (
        <div key={c.clave} style={{ display: 'grid', gap: 4 }}>
          <Switch checked={watch(c.clave) === true} disabled={deshabilitado} label={c.etiqueta} onChange={(v) => setValue(c.clave, v)} />
          {c.hint && <span className="ar-hint">{c.hint}</span>}
        </div>
      );
    }
    if (c.tipo === 'select') {
      const opciones = [...(c.vacio !== undefined ? [{ value: '', label: c.vacio }] : []), ...c.opciones];
      return (
        <Select
          key={c.clave}
          label={c.etiqueta}
          hint={c.hint}
          error={error}
          disabled={deshabilitado}
          options={opciones}
          value={String(watch(c.clave) ?? '')}
          onChange={(ev) => setValue(c.clave, ev.target.value)}
        />
      );
    }
    const requerido = c.requerido === true;
    if (c.tipo === 'texto' && c.multilinea) {
      return <Textarea key={c.clave} label={c.etiqueta} hint={c.hint} error={error} required={requerido} disabled={deshabilitado} rows={4} {...register(c.clave)} />;
    }
    return (
      <Input
        key={c.clave}
        label={c.etiqueta}
        hint={c.hint}
        error={error}
        required={requerido}
        disabled={deshabilitado}
        type={c.tipo === 'fecha' ? 'date' : 'text'}
        inputMode={c.tipo === 'entero' ? 'numeric' : c.tipo === 'decimal' ? 'decimal' : undefined}
        suffix={c.tipo === 'decimal' ? c.sufijo : undefined}
        className={c.tipo === 'texto' && c.mono ? 'mono' : undefined}
        autoComplete="off"
        {...register(c.clave)}
      />
    );
  }

  return (
    <Drawer
      title={titulo}
      description={descripcion}
      onClose={isSubmitting ? undefined : onCerrar}
      footer={
        <>
          {pie}
          <span style={{ flex: 1 }} />
          <Button variant="ghost" onClick={onCerrar} disabled={isSubmitting}>
            {soloLectura ? t.comun.cerrar : t.comun.cancelar}
          </Button>
          {!soloLectura && (
            <Button type="submit" form="form-esquema" disabled={isSubmitting}>
              {isSubmitting ? t.comun.guardando : t.comun.guardar}
            </Button>
          )}
        </>
      }
    >
      <form id="form-esquema" onSubmit={handleSubmit(enviar)} noValidate style={{ display: 'grid', gap: 'var(--space-7)' }}>
        {bloqueo && <Alert tone="info">{bloqueo.motivo}</Alert>}
        {campos.map(campo)}
        {general && (
          <Alert tone="danger" title={t.comun.errorGuardar}>
            {general}
          </Alert>
        )}
      </form>
    </Drawer>
  );
}
