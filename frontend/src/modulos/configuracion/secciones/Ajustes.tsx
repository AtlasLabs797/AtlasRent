import { useEffect, useState, type JSX } from 'react';
import { useForm } from 'react-hook-form';
import { aplicarErroresApi } from '../../../api/formularios';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { Skeleton } from '../../../design-system/components/feedback/Skeleton';
import { Input } from '../../../design-system/components/forms/Input';
import { Select } from '../../../design-system/components/forms/Select';
import { Switch } from '../../../design-system/components/forms/Switch';
import { useAjustes, useGuardarAjustes, type Ajustes as AjustesApi } from '../api';
import { CAMPOS_AJUSTES as CAMPOS, cuerpoAjustes } from '../catalogos';
import { validarCampo, valoresIniciales, type Valores } from '../esquema';
import { textosConfiguracion as textos } from '../textos';

const t = textos.ajustes;

export function Ajustes(): JSX.Element {
  const ajustes = useAjustes();
  if (ajustes.isPending) return <Skeleton lines={5} height={24} />;
  if (ajustes.isError) return <ErrorAlerta error={ajustes.error} titulo={textos.comun.errorLista} />;
  return <FormularioAjustes inicial={ajustes.data} />;
}

function FormularioAjustes({ inicial }: { inicial: AjustesApi }): JSX.Element {
  const guardar = useGuardarAjustes();
  const [general, setGeneral] = useState<string | null>(null);
  const [hecho, setHecho] = useState(false);
  const { register, handleSubmit, setError, setValue, watch, reset, formState } = useForm<Valores>({ defaultValues: valoresIniciales(CAMPOS, inicial) });
  const { errors, isSubmitting } = formState;

  useEffect(() => reset(valoresIniciales(CAMPOS, inicial)), [inicial, reset]);

  async function enviar(valores: Valores) {
    setGeneral(null);
    setHecho(false);
    let hay = false;
    for (const c of CAMPOS) {
      const m = validarCampo(c, valores[c.clave], textos.validacion);
      if (m) {
        setError(c.clave, { message: m });
        hay = true;
      }
    }
    if (hay) return;
    try {
      await guardar.mutateAsync(cuerpoAjustes(valores));
      setHecho(true);
    } catch (e) {
      setGeneral(aplicarErroresApi(e, setError, CAMPOS.map((c) => c.clave), textos.comun.errorGuardar));
    }
  }

  const campo = (clave: string) => CAMPOS.find((c) => c.clave === clave)!;
  const decimal = (clave: string) => {
    const c = campo(clave);
    return <Input label={c.etiqueta} inputMode="decimal" className="mono" suffix={c.tipo === 'decimal' ? c.sufijo : undefined} error={errors[clave]?.message} autoComplete="off" {...register(clave)} />;
  };
  const entero = (clave: string) => {
    const c = campo(clave);
    return <Input label={c.etiqueta} hint={c.hint} inputMode="numeric" className="mono" error={errors[clave]?.message} autoComplete="off" {...register(clave)} />;
  };
  const modo = campo('modoRecordatorios');

  return (
    <form onSubmit={handleSubmit(enviar)} noValidate style={{ display: 'grid', gap: 'var(--space-9)', maxWidth: 720 }}>
      <Card title={t.tolerancias}>
        <p style={{ margin: '0 0 var(--space-7)', color: 'var(--text-muted)' }}>{t.toleranciasHint}</p>
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--space-7)' }}>
          {decimal('toleranciaCambioUsd')}
          {decimal('toleranciaCambioDop')}
        </div>
      </Card>
      <Card title={t.recordatorios}>
        <div style={{ display: 'grid', gap: 'var(--space-7)' }}>
          <Select
            label={modo.etiqueta}
            hint={modo.tipo === 'select' ? modo.hint : undefined}
            options={modo.tipo === 'select' ? [...modo.opciones] : []}
            value={String(watch('modoRecordatorios'))}
            onChange={(ev) => setValue('modoRecordatorios', ev.target.value)}
          />
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--space-7)' }}>
            {entero('diasPrimerRecordatorio')}
            {entero('diasEntreRecordatorios')}
          </div>
        </div>
      </Card>
      <Card title={t.seguridad}>
        <Switch checked={watch('mfaGerente') === true} label={t.mfaGerente} onChange={(v) => setValue('mfaGerente', v)} />
        <span className="ar-hint" style={{ display: 'block', marginTop: 'var(--space-4)' }}>
          {t.mfaGerenteHint}
        </span>
      </Card>
      {general && (
        <Alert tone="danger" title={textos.comun.errorGuardar}>
          {general}
        </Alert>
      )}
      {hecho && <Alert tone="success" title={t.guardado} />}
      <div>
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? textos.comun.guardando : textos.comun.guardar}
        </Button>
      </div>
    </form>
  );
}
