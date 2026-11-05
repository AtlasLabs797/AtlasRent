import { useState, type JSX } from 'react';
import { useForm } from 'react-hook-form';
import {
  entrarConAccesoEmergencia,
  login,
  usarCodigoRecuperacion,
  verificarSegundoFactor,
  type Identidad,
} from '../api/auth';
import { aErrorApi } from '../api/errores';
import { AltaMfa } from '../componentes/AltaMfa';
import { ErrorAlerta } from '../componentes/ErrorAlerta';
import { PantallaCentrada } from '../componentes/PantallaCentrada';
import { Button } from '../design-system/components/core/Button';
import { Input } from '../design-system/components/forms/Input';
import { textos } from '../i18n/textos';
import { useSesion } from '../sesion/store';

const { acceso } = textos;

/**
 * Pasos de la pantalla de acceso. Tras un login correcto sin sesión todavía (falta el segundo
 * factor) la API deja un token provisional en una cookie HttpOnly y de ahí salen cuatro caminos:
 * alta del MFA (administrador sin él), código TOTP, código de recuperación y acceso de emergencia
 * (break-glass). Los tres últimos se cierran con una identidad y abren sesión.
 */
type Paso = 'credenciales' | 'alta' | 'totp' | 'recuperacion' | 'emergencia';

/** Texto de cada variante del paso de código. */
const VARIANTES = {
  totp: { ...acceso.segundoFactor, boton: acceso.segundoFactor.verificar, accion: verificarSegundoFactor },
  recuperacion: { ...acceso.recuperacion, boton: acceso.recuperacion.entrar, accion: usarCodigoRecuperacion },
  emergencia: { ...acceso.emergencia, boton: acceso.emergencia.entrar, accion: entrarConAccesoEmergencia },
} as const;

export default function Acceso(): JSX.Element {
  const [paso, setPaso] = useState<Paso>('credenciales');
  const [ultimoPasoAlta, setUltimoPasoAlta] = useState(false);

  if (paso === 'credenciales') {
    return (
      <Credenciales
        alDesafio={(requiereConfiguracion) => {
          setUltimoPasoAlta(false);
          setPaso(requiereConfiguracion ? 'alta' : 'totp');
        }}
      />
    );
  }

  if (paso === 'alta') {
    return (
      <PantallaCentrada titulo={acceso.altaMfa.titulo} ancho={460}>
        <AltaMfa
          alTerminar={() => {
            setUltimoPasoAlta(true);
            setPaso('totp');
          }}
        />
        <Button variant="ghost" onClick={() => setPaso('credenciales')}>
          {acceso.volver}
        </Button>
      </PantallaCentrada>
    );
  }

  return <PasoCodigo variante={paso} ultimoPasoAlta={ultimoPasoAlta} cambiar={setPaso} />;
}

interface ValoresCredenciales {
  nombreAcceso: string;
  contrasena: string;
}

function Credenciales({ alDesafio }: { alDesafio: (requiereConfiguracion: boolean) => void }): JSX.Element {
  const iniciarSesion = useSesion((estado) => estado.iniciarSesion);
  const [error, setError] = useState<unknown>(null);
  const { register, handleSubmit, formState } = useForm<ValoresCredenciales>({
    defaultValues: { nombreAcceso: '', contrasena: '' },
  });

  async function enviar({ nombreAcceso, contrasena }: ValoresCredenciales) {
    setError(null);
    try {
      const resultado = await login(nombreAcceso, contrasena);
      if (resultado.tipo === 'sesion') iniciarSesion(resultado.identidad);
      else alDesafio(resultado.desafio.requiereConfiguracion);
    } catch (e) {
      setError(e);
    }
  }

  // 401 = credenciales; el resto (429 por límite o IP bloqueada, 5xx, sin red) lleva el texto del backend.
  const estado = error ? aErrorApi(error).status : undefined;

  return (
    <PantallaCentrada titulo={acceso.titulo}>
      <form onSubmit={handleSubmit(enviar)} noValidate style={{ display: 'grid', gap: 'var(--space-7)' }}>
        <p style={{ margin: 0, color: 'var(--text-muted)' }}>{acceso.descripcion}</p>
        <Input
          label={acceso.nombreAcceso}
          autoComplete="username"
          required
          {...register('nombreAcceso', { required: true })}
        />
        <Input
          label={acceso.contrasena}
          type="password"
          autoComplete="current-password"
          required
          {...register('contrasena', { required: true })}
        />
        {error !== null && (
          <ErrorAlerta
            error={error}
            titulo={acceso.error}
            {...(estado === 401 ? { reserva: acceso.credencialesInvalidas } : {})}
          />
        )}
        <Button type="submit" disabled={formState.isSubmitting}>
          {formState.isSubmitting ? acceso.cambiando : acceso.entrar}
        </Button>
      </form>
    </PantallaCentrada>
  );
}

function PasoCodigo({
  variante,
  ultimoPasoAlta,
  cambiar,
}: {
  variante: 'totp' | 'recuperacion' | 'emergencia';
  ultimoPasoAlta: boolean;
  cambiar: (paso: Paso) => void;
}): JSX.Element {
  const iniciarSesion = useSesion((estado) => estado.iniciarSesion);
  const [error, setError] = useState<unknown>(null);
  const { register, handleSubmit, formState, reset } = useForm<{ codigo: string }>({ defaultValues: { codigo: '' } });
  const v = VARIANTES[variante];

  async function enviar({ codigo }: { codigo: string }) {
    setError(null);
    try {
      const identidad: Identidad = await v.accion(codigo.trim());
      iniciarSesion(identidad);
    } catch (e) {
      setError(e);
      reset();
    }
  }

  function ir(paso: Paso) {
    setError(null);
    reset();
    cambiar(paso);
  }

  const estado = error ? aErrorApi(error).status : undefined;

  return (
    <PantallaCentrada titulo={v.titulo}>
      <form onSubmit={handleSubmit(enviar)} noValidate style={{ display: 'grid', gap: 'var(--space-7)' }}>
        <p style={{ margin: 0, color: 'var(--text-muted)' }}>
          {variante === 'totp' && ultimoPasoAlta ? acceso.altaMfa.ultimoPaso : v.descripcion}
        </p>
        <Input
          label={v.codigo}
          placeholder={v.pista}
          autoComplete="one-time-code"
          inputMode={variante === 'totp' ? 'numeric' : 'text'}
          autoFocus
          required
          {...register('codigo', { required: true })}
        />
        {error !== null && (
          <ErrorAlerta
            error={error}
            titulo={acceso.error}
            {...(estado === 401 ? { reserva: acceso.segundoFactor.invalido } : {})}
          />
        )}
        <Button type="submit" disabled={formState.isSubmitting}>
          {formState.isSubmitting ? acceso.segundoFactor.verificando : v.boton}
        </Button>
        <div style={{ display: 'grid', gap: 'var(--space-3)', justifyItems: 'start' }}>
          {variante !== 'totp' && (
            <Button variant="ghost" size="sm" onClick={() => ir('totp')}>
              {acceso.segundoFactor.usarAplicacion}
            </Button>
          )}
          {variante !== 'recuperacion' && (
            <Button variant="ghost" size="sm" onClick={() => ir('recuperacion')}>
              {acceso.segundoFactor.usarRecuperacion}
            </Button>
          )}
          {variante !== 'emergencia' && (
            <Button variant="ghost" size="sm" onClick={() => ir('emergencia')}>
              {acceso.segundoFactor.usarEmergencia}
            </Button>
          )}
          <Button variant="ghost" size="sm" onClick={() => ir('credenciales')}>
            {acceso.volver}
          </Button>
        </div>
      </form>
    </PantallaCentrada>
  );
}
