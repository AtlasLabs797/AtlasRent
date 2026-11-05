import { useEffect, useState, type JSX } from 'react';
import { useForm } from 'react-hook-form';
import { confirmarAltaMfa, iniciarAltaMfa, type ActivacionMfa } from '../api/auth';
import { aErrorApi } from '../api/errores';
import { aplicarErroresApi } from '../api/formularios';
import { Button } from '../design-system/components/core/Button';
import { Alert } from '../design-system/components/feedback/Alert';
import { Skeleton } from '../design-system/components/feedback/Skeleton';
import { Checkbox } from '../design-system/components/forms/Checkbox';
import { Input } from '../design-system/components/forms/Input';
import { textos } from '../i18n/textos';
import { Copiable } from './Copiable';
import { ErrorAlerta } from './ErrorAlerta';

const t = textos.acceso.altaMfa;

/** Secreto base32 de una URI otpauth (parámetro secret). Sin él se enseña solo el enlace. */
export function secretoDeUri(uri: string): string | null {
  const m = /[?&]secret=([A-Za-z2-7]+)/.exec(uri);
  return m?.[1] ?? null;
}

interface Valores {
  codigo: string;
}

/**
 * Alta del segundo factor. Sirve para el login con token provisional (administrador sin MFA) y para
 * "Mi cuenta" con sesión: el backend resuelve cuál de los dos es. Sin librería de QR (decisión del
 * responsable): se enseña la clave y el enlace otpauth como texto copiable.
 * `alTerminar` se llama cuando el usuario confirma que ha guardado los códigos de recuperación.
 */
export function AltaMfa({ alTerminar, textoContinuar = t.continuar }: { alTerminar: () => void; textoContinuar?: string }): JSX.Element {
  const [activacion, setActivacion] = useState<ActivacionMfa | null>(null);
  const [errorInicio, setErrorInicio] = useState<unknown>(null);
  const [codigos, setCodigos] = useState<string[] | null>(null);
  const [guardados, setGuardados] = useState(false);
  const [general, setGeneral] = useState<string | null>(null);
  const { register, handleSubmit, setError, formState } = useForm<Valores>({ defaultValues: { codigo: '' } });

  useEffect(() => {
    let vivo = true;
    // El secreto sale solo en esta respuesta. En desarrollo StrictMode monta dos veces: el segundo
    // alta sustituye al primero en el servidor y es el que se queda en pantalla.
    iniciarAltaMfa().then(
      (datos) => vivo && setActivacion(datos),
      (e: unknown) => vivo && setErrorInicio(e),
    );
    return () => {
      vivo = false;
    };
  }, []);

  async function confirmar({ codigo }: Valores) {
    setGeneral(null);
    try {
      setCodigos(await confirmarAltaMfa(codigo.trim()));
    } catch (e) {
      // 401 = código incorrecto: el texto del producto explica mejor que el de la API por qué mirar la hora.
      setGeneral(aErrorApi(e).status === 401 ? t.invalido : aplicarErroresApi(e, setError, ['codigo'], t.invalido));
    }
  }

  if (codigos) {
    return (
      <div style={{ display: 'grid', gap: 'var(--space-7)' }}>
        <div>
          <h2 style={{ margin: 0, font: 'var(--type-subhead)', color: 'var(--text-strong)' }}>{t.recuperacionTitulo}</h2>
          <p style={{ margin: 'var(--space-3) 0 0', color: 'var(--text-muted)' }}>{t.recuperacionDescripcion}</p>
        </div>
        <Copiable texto={codigos.join('\n')} etiqueta={t.copiarCodigos} multilinea />
        <Checkbox label={t.guardados} checked={guardados} onChange={(e) => setGuardados(e.target.checked)} />
        <Button disabled={!guardados} onClick={alTerminar}>
          {textoContinuar}
        </Button>
      </div>
    );
  }

  if (errorInicio) return <ErrorAlerta error={errorInicio} titulo={t.titulo} />;

  if (!activacion) {
    return (
      <div role="status" aria-label={t.cargando} style={{ display: 'grid', gap: 'var(--space-5)' }}>
        <Skeleton lines={3} />
      </div>
    );
  }

  const secreto = secretoDeUri(activacion.uri);
  return (
    <form onSubmit={handleSubmit(confirmar)} noValidate style={{ display: 'grid', gap: 'var(--space-7)' }}>
      <p style={{ margin: 0, color: 'var(--text-muted)' }}>{t.descripcion}</p>
      {secreto && (
        <div>
          <div className="ar-label">{t.clave}</div>
          <Copiable texto={secreto} etiqueta={t.clave} />
        </div>
      )}
      <div>
        <div className="ar-label">{t.enlace}</div>
        <Copiable texto={activacion.uri} etiqueta={t.enlace} />
        <p style={{ margin: 0, font: 'var(--type-fine)', color: 'var(--text-muted)' }}>{t.enlaceAyuda}</p>
      </div>
      <Input
        label={t.codigo}
        inputMode="numeric"
        autoComplete="one-time-code"
        maxLength={activacion.digitos}
        error={formState.errors.codigo?.message}
        {...register('codigo', {
          required: t.invalido,
          pattern: { value: new RegExp(`^\\d{${activacion.digitos}}$`), message: t.invalido },
        })}
      />
      {general && (
        <Alert tone="danger" title={textos.acceso.error}>
          {general}
        </Alert>
      )}
      <Button type="submit" disabled={formState.isSubmitting}>
        {formState.isSubmitting ? t.confirmando : t.confirmar}
      </Button>
    </form>
  );
}
