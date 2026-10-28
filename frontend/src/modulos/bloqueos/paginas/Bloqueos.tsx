import { useState, type JSX } from 'react';
import { useForm } from 'react-hook-form';
import { aErrorApi } from '../../../api/errores';
import { aplicarErroresApi } from '../../../api/formularios';
import { Copiable } from '../../../componentes/Copiable';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Badge } from '../../../design-system/components/core/Badge';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { DataTable } from '../../../design-system/components/data/DataTable';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { Skeleton } from '../../../design-system/components/feedback/Skeleton';
import { Input } from '../../../design-system/components/forms/Input';
import { Select } from '../../../design-system/components/forms/Select';
import { Textarea } from '../../../design-system/components/forms/Textarea';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { Tabs } from '../../../design-system/components/navigation/Tabs';
import { formatearMarca } from '../../../dominio/fechas';
import { textos } from '../../../i18n/textos';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { useUsuarios, type UsuarioResumen } from '../../usuarios/api';
import { textosUsuarios } from '../../usuarios/textos';
import { useAprobarEmergencia, useDesbloquearCuenta, useDesbloquearIp, useSolicitarEmergencia, type RespuestaEmergencia } from '../api';
import { MOTIVO_MAXIMO, MOTIVO_MINIMO, esGuid, esIpValida } from '../bloqueos';
import { textosBloqueos as t } from '../textos';

type Pestana = 'cuentas' | 'ips' | 'emergencia';

/** Desbloqueos (spec §2, acción 19) y acceso de emergencia. Cada pestaña se muestra solo con la acción que exige su endpoint. */
export default function Bloqueos(): JSX.Element {
  const identidad = useSesion((s) => s.identidad);
  const disponibles: Array<{ value: Pestana; label: string }> = [
    ...(puede(identidad, ACCIONES.cuentaDesbloquear) ? [{ value: 'cuentas' as const, label: t.pestanas.cuentas }] : []),
    ...(puede(identidad, ACCIONES.ipDesbloquear) ? [{ value: 'ips' as const, label: t.pestanas.ips }] : []),
    ...(puede(identidad, ACCIONES.seguridadAccesoEmergencia) ? [{ value: 'emergencia' as const, label: t.pestanas.emergencia }] : []),
  ];
  const [elegida, setElegida] = useState<Pestana | null>(null);
  const actual = disponibles.find((p) => p.value === elegida)?.value ?? disponibles[0]?.value;

  return (
    <>
      <PageHeader
        eyebrow={textos.navegacion.grupos.control}
        title={t.titulo}
        description={t.descripcion}
        breadcrumbs={[{ label: textos.navegacion.grupos.control }, { label: textos.navegacion.modulos.bloqueos.etiqueta }]}
      />
      <Tabs tabs={disponibles} value={actual} onChange={(v) => setElegida(v as Pestana)} style={{ marginBottom: 'var(--space-9)' }} />
      {actual === 'cuentas' && <PestanaCuentas />}
      {actual === 'ips' && <PestanaIps />}
      {actual === 'emergencia' && <PestanaEmergencia />}
    </>
  );
}

function PestanaCuentas(): JSX.Element {
  const usuarios = useUsuarios();
  const desbloquear = useDesbloquearCuenta();
  const [hecho, setHecho] = useState<UsuarioResumen | null>(null);
  const [enCurso, setEnCurso] = useState<string | null>(null);
  const c = t.cuentas;

  function lanzar(u: UsuarioResumen) {
    setHecho(null);
    setEnCurso(u.id);
    desbloquear.mutate(u.id, {
      onSuccess: () => setHecho(u),
      onSettled: () => setEnCurso(null),
    });
  }

  return (
    <div style={{ display: 'grid', gap: 'var(--space-7)' }}>
      <Alert tone="info">{c.aviso}</Alert>
      {hecho && (
        <Alert tone="success" title={c.desbloqueada}>
          {c.desbloqueadaCuerpo.replace('{usuario}', hecho.nombreAcceso)}
        </Alert>
      )}
      {desbloquear.isError && <ErrorAlerta error={desbloquear.error} titulo={c.error} />}
      {usuarios.isError && <ErrorAlerta error={usuarios.error} titulo={c.errorLista} />}
      <Card title={c.titulo} padded={false}>
        {usuarios.isPending ? (
          <div style={{ padding: 'var(--space-9)' }}>
            <Skeleton lines={4} height={20} />
          </div>
        ) : (
          <DataTable
            rows={usuarios.data ?? []}
            columns={[
              { key: 'nombreAcceso', label: c.columnas.usuario, render: (u: UsuarioResumen) => <span className="mono">{u.nombreAcceso}</span> },
              { key: 'nombre', label: c.columnas.nombre },
              { key: 'rol', label: c.columnas.rol, render: (u: UsuarioResumen) => <Badge tone="neutral">{textosUsuarios.roles[u.rol] ?? u.rol}</Badge> },
              {
                key: 'accion',
                label: c.columnas.accion,
                align: 'right',
                render: (u: UsuarioResumen) => (
                  <Button size="sm" variant="secondary" disabled={enCurso !== null} onClick={() => lanzar(u)}>
                    {c.desbloquear}
                  </Button>
                ),
              },
            ]}
          />
        )}
      </Card>
    </div>
  );
}

function PestanaIps(): JSX.Element {
  const desbloquear = useDesbloquearIp();
  const [hecho, setHecho] = useState<string | null>(null);
  const [general, setGeneral] = useState<{ titulo: string; texto: string } | null>(null);
  const { register, handleSubmit, reset, setError, formState } = useForm<{ ip: string }>({ defaultValues: { ip: '' } });
  const { errors, isSubmitting } = formState;
  const i = t.ips;

  async function enviar({ ip }: { ip: string }) {
    setHecho(null);
    setGeneral(null);
    try {
      await desbloquear.mutateAsync(ip);
      setHecho(ip.trim());
      reset();
    } catch (e) {
      const api = aErrorApi(e);
      if (api.status === 404) setGeneral({ titulo: i.noBloqueada, texto: api.detail ?? i.noBloqueada });
      else {
        const texto = aplicarErroresApi(e, setError, ['ip'], i.error);
        if (texto) setGeneral({ titulo: i.error, texto });
      }
    }
  }

  return (
    <Card title={i.titulo}>
      <form onSubmit={handleSubmit(enviar)} noValidate style={{ display: 'grid', gap: 'var(--space-7)', maxWidth: 480 }}>
        <p style={{ margin: 0, color: 'var(--text-muted)' }}>{i.descripcion}</p>
        <Input
          label={i.etiqueta}
          placeholder={i.pista}
          className="mono"
          error={errors.ip?.message}
          {...register('ip', { validate: (v) => esIpValida(v) || i.invalida })}
        />
        {general && (
          <Alert tone={general.titulo === i.noBloqueada ? 'warning' : 'danger'} title={general.titulo}>
            {general.texto}
          </Alert>
        )}
        {hecho && (
          <Alert tone="success" title={i.hecho}>
            {i.hechoCuerpo.replace('{ip}', hecho)}
          </Alert>
        )}
        <div>
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? i.desbloqueando : i.desbloquear}
          </Button>
        </div>
      </form>
    </Card>
  );
}

function PestanaEmergencia(): JSX.Element {
  return (
    <div style={{ display: 'grid', gap: 'var(--space-9)' }}>
      <Alert tone="warning">{t.emergencia.intro}</Alert>
      <div style={{ display: 'grid', gap: 'var(--space-9)', gridTemplateColumns: 'repeat(auto-fit, minmax(380px, 1fr))', alignItems: 'start' }}>
        <SolicitarEmergencia />
        <AprobarEmergencia />
      </div>
    </div>
  );
}

function SolicitarEmergencia(): JSX.Element {
  const yo = useSesion((s) => s.identidad);
  const usuarios = useUsuarios();
  const solicitar = useSolicitarEmergencia();
  const [creada, setCreada] = useState<RespuestaEmergencia | null>(null);
  const [general, setGeneral] = useState<string | null>(null);
  const { register, handleSubmit, setValue, watch, setError, formState } = useForm<{ usuarioObjetivoId: string; motivo: string }>({
    defaultValues: { usuarioObjetivoId: '', motivo: '' },
  });
  const { errors, isSubmitting } = formState;
  const s = t.emergencia.solicitar;
  const admins = (usuarios.data ?? []).filter((u) => u.rol === 'admin' && u.activo && u.id !== yo?.id);

  async function enviar(v: { usuarioObjetivoId: string; motivo: string }) {
    setGeneral(null);
    setCreada(null);
    if (!esGuid(v.usuarioObjetivoId)) {
      setError('usuarioObjetivoId', { message: s.objetivoObligatorio });
      return;
    }
    try {
      setCreada(await solicitar.mutateAsync(v));
    } catch (e) {
      setGeneral(aplicarErroresApi(e, setError, ['usuarioObjetivoId', 'motivo'], s.error));
    }
  }

  return (
    <Card title={s.titulo}>
      <form onSubmit={handleSubmit(enviar)} noValidate style={{ display: 'grid', gap: 'var(--space-7)' }}>
        {usuarios.isPending ? (
          <Skeleton height={44} />
        ) : admins.length === 0 ? (
          <Alert tone="info">{s.sinAdmins}</Alert>
        ) : (
          <Select
            label={s.objetivo}
            placeholder={s.objetivoPlaceholder}
            value={watch('usuarioObjetivoId')}
            error={errors.usuarioObjetivoId?.message}
            options={admins.map((a) => ({ value: a.id, label: `${a.nombre} · ${a.nombreAcceso}` }))}
            onChange={(ev) => setValue('usuarioObjetivoId', ev.target.value, { shouldValidate: true })}
          />
        )}
        <Textarea
          label={s.motivo}
          required
          rows={3}
          hint={s.motivoHint}
          error={errors.motivo?.message}
          {...register('motivo', {
            validate: (v) => (v.trim().length < MOTIVO_MINIMO ? s.motivoCorto : v.trim().length > MOTIVO_MAXIMO ? s.motivoLargo : true),
          })}
        />
        {general && (
          <Alert tone="danger" title={s.error}>
            {general}
          </Alert>
        )}
        {creada && (
          <Alert tone="success" title={s.hecho}>
            {s.hechoCuerpo}
            <div style={{ marginTop: 'var(--space-4)' }}>
              <Copiable texto={creada.id} etiqueta={s.identificador} />
            </div>
          </Alert>
        )}
        <div>
          <Button type="submit" disabled={isSubmitting || admins.length === 0}>
            {isSubmitting ? s.enviando : s.enviar}
          </Button>
        </div>
      </form>
    </Card>
  );
}

function AprobarEmergencia(): JSX.Element {
  const aprobar = useAprobarEmergencia();
  const [resultado, setResultado] = useState<RespuestaEmergencia | null>(null);
  const [general, setGeneral] = useState<string | null>(null);
  const { register, handleSubmit, reset, setError, formState } = useForm<{ solicitudId: string }>({ defaultValues: { solicitudId: '' } });
  const { errors, isSubmitting } = formState;
  const a = t.emergencia.aprobar;

  async function enviar({ solicitudId }: { solicitudId: string }) {
    setGeneral(null);
    setResultado(null);
    try {
      setResultado(await aprobar.mutateAsync(solicitudId));
      reset();
    } catch (e) {
      setGeneral(aplicarErroresApi(e, setError, ['solicitudId'], a.error));
    }
  }

  return (
    <Card title={a.titulo}>
      <form onSubmit={handleSubmit(enviar)} noValidate style={{ display: 'grid', gap: 'var(--space-7)' }}>
        <p style={{ margin: 0, color: 'var(--text-muted)' }}>{a.descripcion}</p>
        <Input
          label={a.etiqueta}
          className="mono"
          autoComplete="off"
          error={errors.solicitudId?.message}
          {...register('solicitudId', { validate: (v) => esGuid(v) || a.invalido })}
        />
        {general && (
          <Alert tone="danger" title={a.error}>
            {general}
          </Alert>
        )}
        {resultado?.codigoAcceso ? (
          <Alert tone="success" title={a.codigoTitulo}>
            {a.codigoCuerpo}
            <div style={{ marginTop: 'var(--space-4)' }}>
              <Copiable texto={resultado.codigoAcceso} etiqueta={a.codigo} />
            </div>
            {resultado.caduca && <div className="mono">{a.caduca.replace('{fecha}', formatearMarca(resultado.caduca))}</div>}
          </Alert>
        ) : resultado ? (
          <Alert tone="info" title={a.sinCodigo}>
            {a.sinCodigoCuerpo}
          </Alert>
        ) : null}
        <div>
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? a.enviando : a.enviar}
          </Button>
        </div>
      </form>
    </Card>
  );
}
