import { useState, type JSX } from 'react';
import { useForm } from 'react-hook-form';
import { cambiarPassword } from '../api/auth';
import { aplicarErroresApi } from '../api/formularios';
import { Button } from '../design-system/components/core/Button';
import { Alert } from '../design-system/components/feedback/Alert';
import { Input } from '../design-system/components/forms/Input';
import { textos } from '../i18n/textos';
import { useSesion } from '../sesion/store';

const t = textos.cuenta.password;
// Mismas reglas que el backend (ServicioContrasenas): la API vuelve a validarlas y añade HaveIBeenPwned.
const MINIMO = 12;
const MAXIMO = 72;

interface Valores {
  contrasenaActual: string;
  contrasenaNueva: string;
  repetir: string;
}

/**
 * Cambio de la contraseña propia (POST /auth/password): el mismo formulario en la pantalla forzada
 * (SEG-010) y en "Mi cuenta". Los errores del 400 de validación se pintan en su campo y el 400 SEG-011
 * (contraseña actual incorrecta, filtrada) en un aviso general.
 */
export function FormularioPassword({ alCambiar }: { alCambiar?: () => void }): JSX.Element {
  const passwordCambiada = useSesion((estado) => estado.passwordCambiada);
  const [general, setGeneral] = useState<string | null>(null);
  const [hecho, setHecho] = useState<{ comprobada: boolean } | null>(null);
  const { register, handleSubmit, setError, reset, getValues, formState } = useForm<Valores>({
    defaultValues: { contrasenaActual: '', contrasenaNueva: '', repetir: '' },
  });
  const { errors, isSubmitting } = formState;

  async function enviar(valores: Valores) {
    setGeneral(null);
    setHecho(null);
    try {
      const comprobada = await cambiarPassword(valores.contrasenaActual, valores.contrasenaNueva);
      reset();
      setHecho({ comprobada });
      passwordCambiada();
      alCambiar?.();
    } catch (e) {
      setGeneral(aplicarErroresApi(e, setError, ['contrasenaActual', 'contrasenaNueva'], t.error));
    }
  }

  return (
    <form onSubmit={handleSubmit(enviar)} noValidate style={{ display: 'grid', gap: 'var(--space-7)' }}>
      <Input
        label={t.actual}
        type="password"
        autoComplete="current-password"
        error={errors.contrasenaActual?.message}
        {...register('contrasenaActual', { required: t.errores.obligatorio })}
      />
      <Input
        label={t.nueva}
        type="password"
        autoComplete="new-password"
        hint={t.descripcion}
        error={errors.contrasenaNueva?.message}
        {...register('contrasenaNueva', {
          required: t.errores.obligatorio,
          minLength: { value: MINIMO, message: t.errores.longitud },
          maxLength: { value: MAXIMO, message: t.errores.longitud },
          validate: (v) => v !== getValues('contrasenaActual') || t.errores.igual,
        })}
      />
      <Input
        label={t.repetir}
        type="password"
        autoComplete="new-password"
        error={errors.repetir?.message}
        {...register('repetir', {
          required: t.errores.obligatorio,
          validate: (v) => v === getValues('contrasenaNueva') || t.errores.noCoincide,
        })}
      />
      {general && (
        <Alert tone="danger" title={t.error}>
          {general}
        </Alert>
      )}
      {hecho && (
        <Alert tone="success" title={t.hecho}>
          {hecho.comprobada ? t.hechoCuerpo : `${t.hechoCuerpo} ${t.noComprobada}`}
        </Alert>
      )}
      <Button type="submit" disabled={isSubmitting}>
        {isSubmitting ? t.guardando : t.guardar}
      </Button>
    </form>
  );
}
