import { useState } from 'react';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { ConfirmarDestructivo } from '../../../componentes/ConfirmarDestructivo';
import { AgentBadge } from '../../../design-system/components/ai/AgentBadge';
import { ApprovalCard } from '../../../design-system/components/ai/ApprovalCard';
import { Badge } from '../../../design-system/components/core/Badge';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { DataTable } from '../../../design-system/components/data/DataTable';
import { Money } from '../../../design-system/components/data/Money';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { EmptyState } from '../../../design-system/components/feedback/EmptyState';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { Pagination } from '../../../design-system/components/navigation/Pagination';
import { Tabs } from '../../../design-system/components/navigation/Tabs';
import { formatearFecha, formatearMarca } from '../../../dominio/fechas';
import { textos } from '../../../i18n/textos';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { aErrorApi } from '../../../api/errores';
import {
  useDecisionContrato,
  useDecisionPropuesta,
  useAlertas,
  useCerrarAlerta,
  usePropuestasIa,
  useSolicitudesContrato,
} from '../api';
import {
  accionDePropuesta,
  avisoSolicitudCambiada,
  cambioDeRenta,
  caducidadDePropuesta,
  esSolicitudCambiada,
  nombreTipoAlerta,
  type Alerta,
  type EstadoAlertas,
  type PropuestaIa,
  type SolicitudContrato,
} from '../modelo';
import { textosBandeja as t } from '../textos';

type Pestana = 'contratos' | 'propuestas' | 'alertas';

/**
 * Bandeja de aprobaciones y alertas (F2-10, F6-03, §3.16). Aprobar y rechazar llaman a las rutas que
 * reciben el SolicitudId; un 409 es «la solicitud cambió»: se recarga la lista y se avisa.
 * Los permisos se comprueban por pestaña con la acción de su endpoint.
 */
export default function Bandeja() {
  const identidad = useSesion((e) => e.identidad);
  const verContratos = puede(identidad, ACCIONES.contratoEstado);
  const verPropuestas = puede(identidad, ACCIONES.iaProponer);
  const decidirPropuestas = puede(identidad, ACCIONES.mensajeriaMasivo);

  const [pestana, setPestana] = useState<Pestana>('contratos');
  const [paginaContratos, setPaginaContratos] = useState(1);
  const [paginaPropuestas, setPaginaPropuestas] = useState(1);
  const contratos = useSolicitudesContrato(paginaContratos, verContratos);
  const propuestas = usePropuestasIa(paginaPropuestas, verPropuestas);

  const pestanas = [
    ...(verContratos ? [{ label: t.pestanas.contratos, value: 'contratos', count: contratos.data?.total }] : []),
    ...(verPropuestas ? [{ label: t.pestanas.propuestas, value: 'propuestas', count: propuestas.data?.total }] : []),
    { label: t.pestanas.alertas, value: 'alertas' },
  ];
  const activa = pestanas.some((p) => p.value === pestana) ? pestana : (pestanas[0]!.value as Pestana);

  return (
    <>
      <PageHeader
        eyebrow={textos.navegacion.grupos.operacion}
        title={t.titulo}
        description={t.descripcion}
        breadcrumbs={[{ label: textos.navegacion.grupos.operacion }, { label: textos.navegacion.modulos.bandeja.etiqueta }]}
      />
      <Tabs tabs={pestanas} value={activa} onChange={(v) => setPestana(v as Pestana)} />
      <div style={{ height: 'var(--space-9)' }} />
      {activa === 'contratos' && (
        <PestanaContratos consulta={contratos} pagina={paginaContratos} onPagina={setPaginaContratos} />
      )}
      {activa === 'propuestas' && (
        <PestanaPropuestas
          consulta={propuestas}
          pagina={paginaPropuestas}
          onPagina={setPaginaPropuestas}
          puedeDecidir={decidirPropuestas}
        />
      )}
      {activa === 'alertas' && <PestanaAlertas />}
    </>
  );
}

function PestanaContratos({
  consulta,
  pagina,
  onPagina,
}: {
  consulta: ReturnType<typeof useSolicitudesContrato>;
  pagina: number;
  onPagina: (p: number) => void;
}) {
  const { aprobar, rechazar } = useDecisionContrato();
  const [aRechazar, setARechazar] = useState<SolicitudContrato | null>(null);
  const [aviso, setAviso] = useState<{ tono: 'success' | 'warning' | 'danger'; titulo: string; cuerpo: string } | null>(null);
  const c = t.contratos;

  function aprobarFila(s: SolicitudContrato) {
    setAviso(null);
    aprobar.mutate(s.solicitudId, {
      onSuccess: () => setAviso({ tono: 'success', titulo: c.aprobada, cuerpo: c.aprobadaCuerpo }),
      onError: (e) =>
        setAviso(
          esSolicitudCambiada(e)
            ? { tono: 'warning', titulo: c.solicitudCambiadaTitulo, cuerpo: avisoSolicitudCambiada(e) }
            : { tono: 'danger', titulo: c.errorAprobar, cuerpo: aErrorApi(e).detail ?? aErrorApi(e).title },
        ),
    });
  }

  async function rechazarFila(motivo: string) {
    if (!aRechazar) return;
    setAviso(null);
    try {
      await rechazar.mutateAsync({ solicitudId: aRechazar.solicitudId, comentario: motivo });
      setAviso({ tono: 'success', titulo: t.rechazo.hecho, cuerpo: '' });
    } catch (e) {
      if (esSolicitudCambiada(e)) {
        setARechazar(null);
        setAviso({ tono: 'warning', titulo: c.solicitudCambiadaTitulo, cuerpo: avisoSolicitudCambiada(e) });
        return;
      }
      throw e;
    }
  }

  return (
    <>
      {aviso && (
        <>
          <Alert tone={aviso.tono} title={aviso.titulo}>
            {aviso.cuerpo}
          </Alert>
          <div style={{ height: 'var(--space-6)' }} />
        </>
      )}
      <Card title={c.titulo} padded={false}>
        {consulta.isPending ? (
          <p style={{ margin: 'var(--space-9)', color: 'var(--text-muted)' }}>{t.cargando}</p>
        ) : consulta.isError ? (
          <div style={{ padding: 'var(--space-9)' }}>
            <ErrorAlerta error={consulta.error} titulo={t.errorCarga} />
          </div>
        ) : (
          <>
            <DataTable
              rowKey="solicitudId"
              rows={consulta.data.data}
              empty={
                <EmptyState icon="inbox" title={c.vacioTitulo}>
                  {c.vacioCuerpo}
                </EmptyState>
              }
              columns={[
                {
                  key: 'contrato',
                  label: c.columnas.contrato,
                  render: (s: SolicitudContrato) => (
                    <div>
                      <strong style={{ fontWeight: 600 }}>{s.cliente ?? c.sinDato}</strong>
                      <div style={{ color: 'var(--text-muted)' }}>{s.unidad ?? c.sinDato}</div>
                      <div className="mono" style={{ color: 'var(--text-subtle)' }}>
                        {c.vigencia
                          .replace('{inicio}', formatearFecha(s.fechaInicio))
                          .replace('{fin}', formatearFecha(s.fechaFinVigente))}
                      </div>
                    </div>
                  ),
                },
                {
                  key: 'solicitud',
                  label: c.columnas.solicitud,
                  render: (s: SolicitudContrato) => (
                    <div>
                      <span className="mono">{formatearMarca(s.solicitadoAt)}</span>
                      {s.solicitadoPor && (
                        <div style={{ color: 'var(--text-muted)' }}>{c.por.replace('{nombre}', s.solicitadoPor)}</div>
                      )}
                    </div>
                  ),
                },
                {
                  key: 'renta',
                  label: c.columnas.renta,
                  align: 'right',
                  render: (s: SolicitudContrato) => <CambioRenta s={s} />,
                },
                {
                  key: 'acciones',
                  label: c.columnas.acciones,
                  align: 'right',
                  render: (s: SolicitudContrato) => (
                    <div style={{ display: 'inline-flex', gap: 'var(--space-4)' }}>
                      <Button variant="ghost" size="sm" onClick={() => setARechazar(s)} disabled={aprobar.isPending}>
                        {c.rechazar}
                      </Button>
                      <Button variant="secondary" size="sm" icon="check" onClick={() => aprobarFila(s)} disabled={aprobar.isPending}>
                        {c.aprobar}
                      </Button>
                    </div>
                  ),
                },
              ]}
            />
            <Pagination page={pagina} pageSize={consulta.data.pageSize} total={consulta.data.total} onChange={onPagina} />
          </>
        )}
      </Card>
      <ConfirmarDestructivo
        open={aRechazar !== null}
        accion={t.rechazo.accion}
        consecuencia={t.rechazo.consecuenciaContrato}
        onConfirmar={rechazarFila}
        onCerrar={() => setARechazar(null)}
      />
    </>
  );
}

function CambioRenta({ s }: { s: SolicitudContrato }) {
  const r = cambioDeRenta(s);
  return (
    <div>
      {r.nuevo ? (
        <Badge tone="info">{t.contratos.contratoNuevo}</Badge>
      ) : r.cambia ? (
        <>
          <Money amount={r.actual!} currency={s.moneda} tone="muted" /> →{' '}
        </>
      ) : null}
      {(r.nuevo || r.cambia) && <Money amount={r.propuesta} currency={s.moneda} strong />}
      {!r.nuevo && !r.cambia && (
        <>
          <Money amount={r.propuesta} currency={s.moneda} />
          <div style={{ color: 'var(--text-muted)' }}>{t.contratos.sinCambioDeRenta}</div>
        </>
      )}
    </div>
  );
}

function PestanaPropuestas({
  consulta,
  pagina,
  onPagina,
  puedeDecidir,
}: {
  consulta: ReturnType<typeof usePropuestasIa>;
  pagina: number;
  onPagina: (p: number) => void;
  puedeDecidir: boolean;
}) {
  const { aprobar, rechazar } = useDecisionPropuesta();
  const [aRechazar, setARechazar] = useState<PropuestaIa | null>(null);
  const [aviso, setAviso] = useState<{ tono: 'success' | 'warning' | 'danger'; titulo: string; cuerpo: string } | null>(null);
  const p = t.propuestas;

  function aprobarPropuesta(propuesta: PropuestaIa) {
    setAviso(null);
    aprobar.mutate(propuesta.id, {
      onSuccess: (r) => {
        // Una propuesta que caduca no es un error: llega con resultado Caducada y su motivo.
        const motivo = caducidadDePropuesta(r);
        setAviso(
          motivo
            ? { tono: 'warning', titulo: p.caducadaTitulo, cuerpo: motivo }
            : { tono: 'success', titulo: p.aprobada, cuerpo: p.aprobadaCuerpo },
        );
      },
      onError: (e) =>
        setAviso({ tono: 'danger', titulo: p.errorDecidir, cuerpo: aErrorApi(e).detail ?? aErrorApi(e).title }),
    });
  }

  async function rechazarPropuesta(motivo: string) {
    if (!aRechazar) return;
    setAviso(null);
    await rechazar.mutateAsync({ id: aRechazar.id, comentario: motivo });
    setAviso({ tono: 'success', titulo: t.rechazo.hecho, cuerpo: '' });
  }

  if (consulta.isPending) return <p style={{ color: 'var(--text-muted)' }}>{t.cargando}</p>;
  if (consulta.isError) return <ErrorAlerta error={consulta.error} titulo={t.errorCarga} />;

  return (
    <>
      {!puedeDecidir && (
        <>
          <Alert tone="info" title={p.sinPermisoDecidir} />
          <div style={{ height: 'var(--space-6)' }} />
        </>
      )}
      {aviso && (
        <>
          <Alert tone={aviso.tono} title={aviso.titulo}>
            {aviso.cuerpo}
          </Alert>
          <div style={{ height: 'var(--space-6)' }} />
        </>
      )}
      {consulta.data.data.length === 0 ? (
        <EmptyState icon="sparkles" title={p.vacioTitulo}>
          {p.vacioCuerpo}
        </EmptyState>
      ) : (
        <div style={{ display: 'grid', gap: 'var(--space-6)' }}>
          {consulta.data.data.map((x) => (
            <div key={x.id} style={{ display: 'grid', gap: 'var(--space-4)' }}>
              <div>
                <AgentBadge label={p.agente} />
              </div>
              <ApprovalCard
                agent={p.agente}
                action={accionDePropuesta(x.tipo)}
                record={x.cliente}
                current={p.actualSaldo.replace('{pendiente}', x.pendiente)}
                proposed={p.propuesto}
                reason={
                  <>
                    {x.comentario ?? p.sinJustificacion}
                    <span style={{ display: 'block', color: 'var(--text-muted)' }}>
                      {p.evidencia.replace('{pendiente}', x.pendiente).replace('{huella}', x.huellaContenido.slice(0, 12))}
                    </span>
                  </>
                }
                origin={x.solicitadoPor ? p.origen.replace('{nombre}', x.solicitadoPor) : p.origenSinNombre}
                requestedAt={formatearMarca(x.solicitadoAt)}
                {...(puedeDecidir
                  ? { onApprove: () => aprobarPropuesta(x), onReject: () => setARechazar(x) }
                  : {})}
              />
            </div>
          ))}
        </div>
      )}
      <Pagination page={pagina} pageSize={consulta.data.pageSize} total={consulta.data.total} onChange={onPagina} />
      <ConfirmarDestructivo
        open={aRechazar !== null}
        accion={t.rechazo.accion}
        consecuencia={t.rechazo.consecuenciaPropuesta}
        onConfirmar={rechazarPropuesta}
        onCerrar={() => setARechazar(null)}
      />
    </>
  );
}

function PestanaAlertas() {
  const a = t.alertas;
  const [pagina, setPagina] = useState(1);
  const [tipo, setTipo] = useState('');
  const [estado, setEstado] = useState<EstadoAlertas>('abiertas');
  const consulta = useAlertas(pagina, tipo, estado);
  const cerrar = useCerrarAlerta();
  const [error, setError] = useState<unknown>(null);

  function cerrarFila(alerta: Alerta) {
    setError(null);
    cerrar.mutate(alerta.id, { onError: setError });
  }

  return (
    <>
      <Alert tone="info" title={a.aviso} />
      <div style={{ height: 'var(--space-6)' }} />
      {error !== null && (
        <>
          <ErrorAlerta error={error} titulo={a.errorCerrar} />
          <div style={{ height: 'var(--space-6)' }} />
        </>
      )}
      <Card
        title={a.titulo}
        padded={false}
        actions={
          <div style={{ display: 'flex', gap: 'var(--space-4)', flexWrap: 'wrap' }}>
            <div className="ar-control" style={{ minWidth: 180 }}>
              <select
                aria-label={a.filtroTipo}
                value={tipo}
                onChange={(ev) => {
                  setTipo(ev.target.value);
                  setPagina(1);
                }}
              >
                <option value="">{a.todosTipos}</option>
                {Object.entries(a.tipos).map(([valor, etiqueta]) => (
                  <option key={valor} value={valor}>
                    {etiqueta}
                  </option>
                ))}
              </select>
            </div>
            <div className="ar-control" style={{ minWidth: 140 }}>
              <select
                aria-label={a.filtroEstado}
                value={estado}
                onChange={(ev) => {
                  setEstado(ev.target.value as EstadoAlertas);
                  setPagina(1);
                }}
              >
                {(Object.keys(a.estados) as EstadoAlertas[]).map((e) => (
                  <option key={e} value={e}>
                    {a.estados[e]}
                  </option>
                ))}
              </select>
            </div>
          </div>
        }
      >
        {consulta.isPending ? (
          <p style={{ margin: 'var(--space-9)', color: 'var(--text-muted)' }}>{t.cargando}</p>
        ) : consulta.isError ? (
          <div style={{ padding: 'var(--space-9)' }}>
            <ErrorAlerta error={consulta.error} titulo={t.errorCarga} />
          </div>
        ) : (
          <>
            <DataTable
              rows={consulta.data.data}
              empty={
                <EmptyState icon="bell" title={a.vacioTitulo}>
                  {a.vacioCuerpo}
                </EmptyState>
              }
              columns={[
                {
                  key: 'alerta',
                  label: a.columnas.alerta,
                  render: (x: Alerta) => (
                    <div>
                      <Badge tone={x.resueltaAt ? 'neutral' : 'warning'}>{nombreTipoAlerta(x.tipo)}</Badge>
                      <div style={{ marginTop: 4 }}>{x.mensaje}</div>
                    </div>
                  ),
                },
                {
                  key: 'fecha',
                  label: a.columnas.fecha,
                  render: (x: Alerta) => (
                    <div>
                      <span className="mono">{formatearMarca(x.ocurridoAt)}</span>
                      {x.resueltaAt && (
                        <div style={{ color: 'var(--text-muted)' }}>
                          {a.cerrada.replace('{fecha}', formatearMarca(x.resueltaAt))}
                        </div>
                      )}
                    </div>
                  ),
                },
                {
                  key: 'repeticiones',
                  label: a.columnas.repeticiones,
                  align: 'right',
                  render: (x: Alerta) => <span className="mono">{x.repeticiones === 1 ? a.repeticionUna : a.repeticiones.replace('{n}', String(x.repeticiones))}</span>,
                },
                {
                  key: 'acciones',
                  label: a.columnas.acciones,
                  align: 'right',
                  render: (x: Alerta) =>
                    x.resueltaAt ? null : (
                      <Button variant="secondary" size="sm" icon="check" disabled={cerrar.isPending} onClick={() => cerrarFila(x)}>
                        {a.cerrar}
                      </Button>
                    ),
                },
              ]}
            />
            <Pagination page={pagina} pageSize={consulta.data.pageSize} total={consulta.data.total} onChange={setPagina} />
          </>
        )}
      </Card>
    </>
  );
}
