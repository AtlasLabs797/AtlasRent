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
import { useUsuarios } from '../../usuarios/api';
import {
  ESTADOS_SOLICITUD,
  useAprobarEmergencia,
  useCuentasBloqueadas,
  useDesbloquearCuenta,
  useDesbloquearIp,
  useIpsBloqueadas,
  useListaBlanca,
  useSolicitarEmergencia,
  useSolicitudesEmergencia,
  type CuentaBloqueada,
  type EstadoSolicitud,
  type IpBloqueada,
  type RespuestaEmergencia,
  type SolicitudEmergencia,
} from '../api';
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
  const cuentas = useCuentasBloqueadas();
  const desbloquear = useDesbloquearCuenta();
  const [hecho, setHecho] = useState<CuentaBloqueada | null>(null);
  const [enCurso, setEnCurso] = useState<string | null>(null);
  const c = t.cuentas;

  function lanzar(u: CuentaBloqueada) {
    setHecho(null);
    setEnCurso(u.usuarioId);
    desbloquear.mutate(u.usuarioId, {
      onSuccess: () => setHecho(u),
      onSettled: () => setEnCurso(null),
    });
  }

  return (
    <div style={{ display: 'grid', gap: 'var(--space-7)' }}>
      {hecho && (
        <Alert tone="success" title={c.desbloqueada}>
          {c.desbloqueadaCuerpo.replace('{usuario}', hecho.nombreAcceso)}
        </Alert>
      )}
      {desbloquear.isError && <ErrorAlerta error={desbloquear.error} titulo={c.error} />}
      {cuentas.isError && <ErrorAlerta error={cuentas.error} titulo={c.errorLista} />}
      <Card title={c.titulo} padded={false}>
        {cuentas.isPending ? (
          <div style={{ padding: 'var(--space-9)' }}>
            <Skeleton lines={4} height={20} />
          </div>
        ) : (
          <DataTable
            rows={cuentas.data ?? []}
            rowKey="usuarioId"
            empty={c.vacio}
            columns={[
              { key: 'nombreAcceso', label: c.columnas.usuario, render: (u: CuentaBloqueada) => <span className="mono">{u.nombreAcceso}</span> },
              { key: 'nombre', label: c.columnas.nombre },
              { key: 'desde', label: c.columnas.desde, render: (u: CuentaBloqueada) => <span className="mono">{formatearMarca(u.desde)}</span> },
              { key: 'hasta', label: c.columnas.hasta, render: (u: CuentaBloqueada) => <span className="mono">{formatearMarca(u.hasta)}</span> },
              {
                key: 'accion',
                label: c.columnas.accion,
                align: 'right',
                render: (u: CuentaBloqueada) => (
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
  return (
    <div style={{ display: 'grid', gap: 'var(--space-9)' }}>
      <ListaIps />
      <DesbloqueoManualIp />
      <ListaBlanca />
    </div>
  );
}

function ListaIps(): JSX.Element {
  const ips = useIpsBloqueadas();
  const desbloquear = useDesbloquearIp();
  const [hecho, setHecho] = useState<string | null>(null);
  const [enCurso, setEnCurso] = useState<string | null>(null);
  const i = t.ips;

  function lanzar(ip: string) {
    setHecho(null);
    setEnCurso(ip);
    desbloquear.mutate(ip, { onSuccess: () => setHecho(ip), onSettled: () => setEnCurso(null) });
  }

  return (
    <div style={{ display: 'grid', gap: 'var(--space-7)' }}>
      {hecho && (
        <Alert tone="success" title={i.hecho}>
          {i.hechoCuerpo.replace('{ip}', hecho)}
        </Alert>
      )}
      {desbloquear.isError && <ErrorAlerta error={desbloquear.error} titulo={i.error} />}
      {ips.isError && <ErrorAlerta error={ips.error} titulo={i.errorLista} />}
      <Card title={i.titulo} padded={false}>
        {ips.isPending ? (
          <div style={{ padding: 'var(--space-9)' }}>
            <Skeleton lines={4} height={20} />
          </div>
        ) : (
          <DataTable
            rows={ips.data ?? []}
            rowKey="ip"
            empty={i.vacio}
            columns={[
              { key: 'ip', label: i.columnas.ip, render: (b: IpBloqueada) => <span className="mono">{b.ip}</span> },
              { key: 'nivel', label: i.columnas.nivel },
              { key: 'fallos', label: i.columnas.fallos },
              { key: 'desde', label: i.columnas.desde, render: (b: IpBloqueada) => <span className="mono">{formatearMarca(b.desde)}</span> },
              {
                key: 'hasta',
                label: i.columnas.hasta,
                render: (b: IpBloqueada) => (b.hasta ? <span className="mono">{formatearMarca(b.hasta)}</span> : <Badge tone="warning">{i.indefinido}</Badge>),
              },
              {
                key: 'accion',
                label: i.columnas.accion,
                align: 'right',
                render: (b: IpBloqueada) => (
                  <Button size="sm" variant="secondary" disabled={enCurso !== null} onClick={() => lanzar(b.ip)}>
                    {i.desbloquearFila}
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

function ListaBlanca(): JSX.Element {
  const lista = useListaBlanca();
  const l = t.ips.listaBlanca;
  return (
    <Card title={l.titulo}>
      <div style={{ display: 'grid', gap: 'var(--space-6)' }}>
        <p style={{ margin: 0, color: 'var(--text-muted)' }}>{l.descripcion}</p>
        {lista.isError && <ErrorAlerta error={lista.error} titulo={l.error} />}
        {lista.isPending ? (
          <Skeleton lines={2} height={20} />
        ) : (lista.data ?? []).length === 0 ? (
          <p style={{ margin: 0, color: 'var(--text-muted)' }}>{l.vacio}</p>
        ) : (
          <div style={{ display: 'flex', gap: 'var(--space-4)', flexWrap: 'wrap' }}>
            {(lista.data ?? []).map((ip) => (
              <Badge key={ip} tone="neutral">
                <span className="mono">{ip}</span>
              </Badge>
            ))}
          </div>
        )}
      </div>
    </Card>
  );
}

function DesbloqueoManualIp(): JSX.Element {
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
    <Card title={i.manual}>
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
    <div style={{ display: 'grid', gap: 'var(--space-9)', gridTemplateColumns: 'minmax(0, 1fr)' }}>
      <Alert tone="warning">{t.emergencia.intro}</Alert>
      <div style={{ display: 'grid', gap: 'var(--space-9)', gridTemplateColumns: 'repeat(auto-fit, minmax(380px, 1fr))', alignItems: 'start' }}>
        <SolicitarEmergencia />
        <AprobarEmergencia />
      </div>
      <Solicitudes />
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
        {resultado && <ResultadoAprobacion resultado={resultado} />}
        <div>
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? a.enviando : a.enviar}
          </Button>
        </div>
      </form>
    </Card>
  );
}

/** Lo que devuelve la aprobación: el código de un solo uso (si esta aprobación lo genera) o el aviso de que falta otra. */
function ResultadoAprobacion({ resultado }: { resultado: RespuestaEmergencia }): JSX.Element {
  const a = t.emergencia.aprobar;
  return resultado.codigoAcceso ? (
    <Alert tone="success" title={a.codigoTitulo}>
      {a.codigoCuerpo}
      <div style={{ marginTop: 'var(--space-4)' }}>
        <Copiable texto={resultado.codigoAcceso} etiqueta={a.codigo} />
      </div>
      {resultado.caduca && <div className="mono">{a.caduca.replace('{fecha}', formatearMarca(resultado.caduca))}</div>}
    </Alert>
  ) : (
    <Alert tone="info" title={a.sinCodigo}>
      {a.sinCodigoCuerpo}
    </Alert>
  );
}

const TONO_ESTADO: Record<string, 'warning' | 'success' | 'neutral' | 'info'> = {
  pendiente: 'warning',
  aprobada: 'success',
  caducada: 'neutral',
  usada: 'info',
};

function Solicitudes(): JSX.Element {
  const yo = useSesion((s) => s.identidad);
  const [estado, setEstado] = useState<EstadoSolicitud>('pendiente');
  const solicitudes = useSolicitudesEmergencia(estado);
  const aprobar = useAprobarEmergencia();
  const [resultado, setResultado] = useState<RespuestaEmergencia | null>(null);
  const [enCurso, setEnCurso] = useState<string | null>(null);
  const s = t.emergencia.solicitudes;

  function lanzar(id: string) {
    setResultado(null);
    setEnCurso(id);
    aprobar.mutate(id, { onSuccess: setResultado, onSettled: () => setEnCurso(null) });
  }

  return (
    <div style={{ display: 'grid', gap: 'var(--space-7)', gridTemplateColumns: 'minmax(0, 1fr)' }}>
      {resultado && <ResultadoAprobacion resultado={resultado} />}
      {aprobar.isError && <ErrorAlerta error={aprobar.error} titulo={t.emergencia.aprobar.error} />}
      {solicitudes.isError && <ErrorAlerta error={solicitudes.error} titulo={s.error} />}
      <Card title={s.titulo} padded={false}>
        <div style={{ padding: 'var(--space-7) var(--space-9) 0', maxWidth: 280 }}>
          <Select
            label={s.filtro}
            value={estado}
            options={ESTADOS_SOLICITUD.map((e) => ({ value: e, label: s.estados[e] }))}
            onChange={(ev) => setEstado(ev.target.value as EstadoSolicitud)}
          />
        </div>
        {solicitudes.isPending ? (
          <div style={{ padding: 'var(--space-9)' }}>
            <Skeleton lines={3} height={20} />
          </div>
        ) : (
          <DataTable
            rows={solicitudes.data ?? []}
            empty={s.vacio}
            columns={[
              { key: 'usuarioNombreAcceso', label: s.columnas.para, render: (r: SolicitudEmergencia) => <span className="mono">{r.usuarioNombreAcceso}</span> },
              { key: 'solicitanteNombreAcceso', label: s.columnas.solicitante, render: (r: SolicitudEmergencia) => <span className="mono">{r.solicitanteNombreAcceso}</span> },
              { key: 'motivo', label: s.columnas.motivo },
              { key: 'creadoAt', label: s.columnas.creada, render: (r: SolicitudEmergencia) => <span className="mono">{formatearMarca(r.creadoAt)}</span> },
              {
                key: 'estado',
                label: s.columnas.estado,
                render: (r: SolicitudEmergencia) => (
                  <Badge tone={TONO_ESTADO[r.estado] ?? 'neutral'}>{s.estadoBadge[r.estado as keyof typeof s.estadoBadge] ?? r.estado}</Badge>
                ),
              },
              {
                key: 'accion',
                label: s.columnas.accion,
                align: 'right',
                render: (r: SolicitudEmergencia) =>
                  r.estado === 'pendiente' ? (
                    <Button
                      size="sm"
                      variant="secondary"
                      disabled={enCurso !== null || r.solicitanteId === yo?.id}
                      title={r.solicitanteId === yo?.id ? s.propia : undefined}
                      onClick={() => lanzar(r.id)}
                    >
                      {s.aprobar}
                    </Button>
                  ) : null,
              },
            ]}
          />
        )}
      </Card>
    </div>
  );
}
