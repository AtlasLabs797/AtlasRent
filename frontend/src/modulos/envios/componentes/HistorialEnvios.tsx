import { useState } from 'react';
import { aErrorApi } from '../../../api/errores';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Badge } from '../../../design-system/components/core/Badge';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { DataTable } from '../../../design-system/components/data/DataTable';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { EmptyState } from '../../../design-system/components/feedback/EmptyState';
import { Pagination } from '../../../design-system/components/navigation/Pagination';
import { formatearMarca } from '../../../dominio/fechas';
import { descargarCopia, useEnvios, useReintentarEnvio } from '../api';
import { estadoDeEnvio, etiquetaTipo, type EnvioFila, type EstadoEnvio } from '../modelo';
import { textosEnvios as t } from '../textos';

const ESTADOS: EstadoEnvio[] = ['Pendiente', 'Enviando', 'Enviado', 'Entregado', 'Leido', 'Fallido'];

export function HistorialEnvios({ puedeReintentar }: { puedeReintentar: boolean }) {
  const h = t.historial;
  const [pagina, setPagina] = useState(1);
  const [estado, setEstado] = useState<EstadoEnvio | ''>('');
  const consulta = useEnvios(pagina, estado);
  const reintento = useReintentarEnvio();
  const [aviso, setAviso] = useState<{ tono: 'success' | 'danger'; titulo: string; cuerpo: string } | null>(null);

  async function descargar(e: EnvioFila) {
    setAviso(null);
    try {
      await descargarCopia(e);
    } catch {
      setAviso({ tono: 'danger', titulo: h.errorDescarga, cuerpo: '' });
    }
  }

  function reintentar(e: EnvioFila) {
    setAviso(null);
    reintento.mutate(e, {
      onSuccess: () => setAviso({ tono: 'success', titulo: h.reintentado, cuerpo: h.reintentadoCuerpo }),
      onError: (err) =>
        setAviso({ tono: 'danger', titulo: h.errorReintento, cuerpo: aErrorApi(err).detail ?? aErrorApi(err).title }),
    });
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
      <Card
        title={h.titulo}
        padded={false}
        actions={
          <div className="ar-control" style={{ minWidth: 180 }}>
            <select
              aria-label={h.estado}
              value={estado}
              onChange={(ev) => {
                setEstado(ev.target.value as EstadoEnvio | '');
                setPagina(1);
              }}
            >
              <option value="">{h.todosEstados}</option>
              {ESTADOS.map((s) => (
                <option key={s} value={s}>
                  {t.estados[s]}
                </option>
              ))}
            </select>
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
                <EmptyState icon="send" title={h.vacioTitulo}>
                  {h.vacioCuerpo}
                </EmptyState>
              }
              columns={[
                {
                  key: 'documento',
                  label: h.columnas.documento,
                  render: (e: EnvioFila) => (
                    <div>
                      <strong style={{ fontWeight: 600 }}>{etiquetaTipo(e.tipo)}</strong>
                      <div className="mono" style={{ color: 'var(--text-muted)' }}>
                        {e.nombreArchivo}
                      </div>
                      {e.documentoModificadoAt && (
                        <Badge tone="warning" style={{ marginTop: 4 }}>
                          {h.modificado}
                        </Badge>
                      )}
                    </div>
                  ),
                },
                {
                  key: 'destinatario',
                  label: h.columnas.destinatario,
                  render: (e: EnvioFila) => (
                    <div>
                      {e.destinatarioNombre ?? e.destinatarioEmail}
                      {e.destinatarioNombre && <div className="mono" style={{ color: 'var(--text-muted)' }}>{e.destinatarioEmail}</div>}
                    </div>
                  ),
                },
                {
                  key: 'estado',
                  label: h.columnas.estado,
                  render: (e: EnvioFila) => {
                    const s = estadoDeEnvio(e.estado);
                    return (
                      <div>
                        <Badge tone={s.tono} dot>
                          {s.etiqueta}
                        </Badge>
                        {e.estado === 'Fallido' && e.ultimoError && (
                          <div style={{ color: 'var(--danger)', marginTop: 4 }}>{e.ultimoError}</div>
                        )}
                      </div>
                    );
                  },
                },
                {
                  key: 'fecha',
                  label: h.columnas.fecha,
                  render: (e: EnvioFila) => <span className="mono">{formatearMarca(e.enviadoAt ?? e.createdAt)}</span>,
                },
                { key: 'intentos', label: h.columnas.intentos, align: 'right', render: (e: EnvioFila) => <span className="mono">{e.intentos}</span> },
                {
                  key: 'acciones',
                  label: h.columnas.acciones,
                  align: 'right',
                  render: (e: EnvioFila) => (
                    <div style={{ display: 'inline-flex', gap: 'var(--space-4)' }}>
                      <Button variant="ghost" size="sm" icon="download" aria-label={h.descargarEtiqueta} onClick={() => void descargar(e)}>
                        {h.descargar}
                      </Button>
                      {e.estado === 'Fallido' && puedeReintentar && (
                        <Button variant="secondary" size="sm" icon="rotate-cw" disabled={reintento.isPending} onClick={() => reintentar(e)}>
                          {h.reintentar}
                        </Button>
                      )}
                    </div>
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
