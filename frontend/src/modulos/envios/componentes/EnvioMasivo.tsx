import { useState } from 'react';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { DataTable } from '../../../design-system/components/data/DataTable';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { EmptyState } from '../../../design-system/components/feedback/EmptyState';
import { Switch } from '../../../design-system/components/forms/Switch';
import { SegmentedControl } from '../../../design-system/components/navigation/SegmentedControl';
import { Stepper } from '../../../design-system/components/navigation/Stepper';
import { MessagePreview } from '../../../design-system/components/documents/MessagePreview';
import { useConfirmarMasivo, usePrepararMasivo, useProgresoLote, useReintentarLote } from '../api';
import {
  cuerpoMasivo,
  etiquetaMotivoExclusion,
  etiquetaTipo,
  loteTerminado,
  porcentajeLote,
  type FalloMasivo,
  type OpcionesMasivo,
  type ResumenMasivo,
} from '../modelo';
import { textosEnvios as t } from '../textos';

const VISTAS_PREVIAS = 3;
const INICIAL: OpcionesMasivo = {
  vencidas: false,
  noEnviadas: true,
  envioFallido: false,
  modificadasTrasEnvio: false,
  moneda: null,
  forzar: false,
};

/**
 * Envío masivo (§3.14): filtro → revisión (preparar no manda nada) → confirmar (encola) → progreso
 * → reintentar solo los fallidos. Preparar y confirmar mandan el MISMO cuerpo.
 */
export function EnvioMasivo() {
  const m = t.masivo;
  const [opciones, setOpciones] = useState<OpcionesMasivo>(INICIAL);
  const [resumen, setResumen] = useState<ResumenMasivo | null>(null);
  const [loteId, setLoteId] = useState<string | null>(null);
  const preparar = usePrepararMasivo();
  const confirmar = useConfirmarMasivo();
  const progreso = useProgresoLote(loteId);
  const reintentar = useReintentarLote(loteId);

  const paso = loteId ? (progreso.data && loteTerminado(progreso.data) ? 3 : 2) : resumen ? 1 : 0;

  function nuevo() {
    setResumen(null);
    setLoteId(null);
    preparar.reset();
    confirmar.reset();
    reintentar.reset();
  }

  return (
    <div style={{ display: 'grid', gap: 'var(--space-9)' }}>
      <Stepper
        steps={m.pasos.map((label, i) => ({ label, status: i < paso ? 'done' : i === paso ? 'current' : 'todo' }))}
      />

      {paso === 0 && (
        <Card title={m.titulo}>
          <p style={{ margin: '0 0 var(--space-9)', color: 'var(--text-muted)' }}>{m.descripcionFiltro}</p>
          <div style={{ display: 'grid', gap: 'var(--space-6)', maxWidth: 520 }}>
            <Switch label={m.vencidas} checked={opciones.vencidas} onChange={(v) => setOpciones({ ...opciones, vencidas: v })} />
            <Switch label={m.noEnviadas} checked={opciones.noEnviadas} onChange={(v) => setOpciones({ ...opciones, noEnviadas: v })} />
            <Switch label={m.envioFallido} checked={opciones.envioFallido} onChange={(v) => setOpciones({ ...opciones, envioFallido: v })} />
            <Switch
              label={m.modificadasTrasEnvio}
              checked={opciones.modificadasTrasEnvio}
              onChange={(v) => setOpciones({ ...opciones, modificadasTrasEnvio: v })}
            />
            <div className="ar-field">
              <span className="ar-label" id="masivo-moneda">
                {m.moneda}
              </span>
              <div role="group" aria-labelledby="masivo-moneda">
                <SegmentedControl
                  options={[
                    { value: '', label: m.todasMonedas },
                    { value: 'USD', label: 'USD' },
                    { value: 'DOP', label: 'DOP' },
                  ]}
                  value={opciones.moneda ?? ''}
                  onChange={(v) => setOpciones({ ...opciones, moneda: v === '' ? null : (v as 'USD' | 'DOP') })}
                />
              </div>
            </div>
            <Switch label={m.forzar} checked={opciones.forzar} onChange={(v) => setOpciones({ ...opciones, forzar: v })} />
          </div>
          {preparar.isError && (
            <div style={{ marginTop: 'var(--space-9)' }}>
              <ErrorAlerta error={preparar.error} titulo={m.errorPreparar} />
            </div>
          )}
          <div style={{ marginTop: 'var(--space-9)' }}>
            <Button
              icon="list-checks"
              disabled={preparar.isPending}
              onClick={() => preparar.mutate(cuerpoMasivo(opciones), { onSuccess: setResumen })}
            >
              {preparar.isPending ? m.preparando : m.preparar}
            </Button>
          </div>
        </Card>
      )}

      {paso === 1 && resumen && (
        <>
          <Card title={m.resumenTitulo}>
            <p style={{ margin: 0 }}>
              {m.resumen.replace('{total}', String(resumen.total)).replace('{excluidos}', String(resumen.totalExcluidos))}
            </p>
            {resumen.aviso && <p style={{ margin: 'var(--space-4) 0 0', color: 'var(--text-muted)' }}>{resumen.aviso}</p>}
            {resumen.total === 0 && (
              <EmptyState icon="mail-x" title={m.sinDocumentos}>
                {m.sinDocumentosCuerpo}
              </EmptyState>
            )}
          </Card>

          {resumen.documentos.length > 0 && (
            <Card title={m.vistaPreviaTitulo}>
              <div style={{ display: 'grid', gap: 'var(--space-6)' }}>
                {resumen.documentos.slice(0, VISTAS_PREVIAS).map((d) => (
                  <MessagePreview
                    key={d.documentoId}
                    to={d.destinatarioNombre ? `${d.destinatarioNombre} · ${d.destinatarioEmail}` : d.destinatarioEmail}
                    channel="Email"
                    template={`${etiquetaTipo(d.tipoDocumento)} ${d.numero}`}
                    text={`${d.asunto}\n\n${d.cuerpo}`}
                    attachments={[{ name: d.nombreArchivo }]}
                    status="programado"
                  />
                ))}
              </div>
              <p style={{ margin: 'var(--space-6) 0 0', color: 'var(--text-subtle)' }}>
                {m.vistaPreviaNota
                  .replace('{n}', String(Math.min(VISTAS_PREVIAS, resumen.documentos.length)))
                  .replace('{total}', String(resumen.total))}
              </p>
            </Card>
          )}

          {resumen.excluidos.length > 0 && (
            <Card title={`${m.excluidosTitulo} (${resumen.totalExcluidos})`} padded={false}>
              <DataTable
                rowKey="documentoId"
                rows={resumen.excluidos}
                columns={[
                  {
                    key: 'documento',
                    label: m.columnasExcluidos.documento,
                    render: (x: ResumenMasivo['excluidos'][number]) => (
                      <span className="mono">
                        {etiquetaTipo(x.tipo)} {x.numero ?? ''}
                      </span>
                    ),
                  },
                  { key: 'cliente', label: m.columnasExcluidos.cliente, render: (x: ResumenMasivo['excluidos'][number]) => x.cliente ?? '—' },
                  {
                    key: 'motivo',
                    label: m.columnasExcluidos.motivo,
                    render: (x: ResumenMasivo['excluidos'][number]) => (
                      <span title={x.detalle}>{etiquetaMotivoExclusion(x.motivo)}</span>
                    ),
                  },
                ]}
              />
            </Card>
          )}

          {confirmar.isError && <ErrorAlerta error={confirmar.error} titulo={m.errorConfirmar} />}
          <Alert tone="info" title={m.confirmadoAviso} />
          <div style={{ display: 'flex', gap: 'var(--space-6)' }}>
            <Button variant="ghost" onClick={nuevo} disabled={confirmar.isPending}>
              {m.volver}
            </Button>
            <Button
              icon="send"
              disabled={resumen.total === 0 || confirmar.isPending}
              onClick={() => confirmar.mutate(cuerpoMasivo(opciones), { onSuccess: (l) => setLoteId(l.loteId) })}
            >
              {confirmar.isPending ? m.confirmando : m.confirmar}
            </Button>
          </div>
        </>
      )}

      {paso >= 2 && (
        <Card title={m.progresoTitulo}>
          {progreso.isError && <ErrorAlerta error={progreso.error} titulo={m.errorProgreso} />}
          {progreso.data && (
            <>
              <div
                role="progressbar"
                aria-valuenow={porcentajeLote(progreso.data)}
                aria-valuemin={0}
                aria-valuemax={100}
                style={{ height: 8, borderRadius: 999, background: 'var(--surface-sunken)', overflow: 'hidden' }}
              >
                <div style={{ width: `${porcentajeLote(progreso.data)}%`, height: '100%', background: 'var(--accent)' }} />
              </div>
              <p className="mono" style={{ margin: 'var(--space-6) 0 0' }}>
                {m.progreso
                  .replace('{hechos}', String(progreso.data.enviados + progreso.data.fallidos))
                  .replace('{total}', String(progreso.data.total))
                  .replace('{enviados}', String(progreso.data.enviados))
                  .replace('{fallidos}', String(progreso.data.fallidos))
                  .replace('{pendientes}', String(progreso.data.pendientes))}
              </p>
              {loteTerminado(progreso.data) && (
                <Alert tone={progreso.data.fallidos > 0 ? 'warning' : 'success'} title={progreso.data.fallidos > 0 ? m.terminadoConFallos : m.terminado} />
              )}
              {progreso.data.fallidosDetalle.length > 0 && (
                <div style={{ marginTop: 'var(--space-9)' }}>
                  <div className="ar-overline">{m.fallidosTitulo}</div>
                  <DataTable
                    rowKey="envioId"
                    rows={progreso.data.fallidosDetalle}
                    columns={[
                      {
                        key: 'documento',
                        label: m.columnasFallidos.documento,
                        render: (f: FalloMasivo) => etiquetaTipo(f.tipoDocumento) + (f.cliente ? ` · ${f.cliente}` : ''),
                      },
                      { key: 'destinatarioEmail', label: m.columnasFallidos.destinatario },
                      { key: 'intentos', label: m.columnasFallidos.intentos, align: 'right', render: (f: FalloMasivo) => <span className="mono">{f.intentos}</span> },
                      { key: 'motivo', label: m.columnasFallidos.motivo, render: (f: FalloMasivo) => f.motivo ?? '—' },
                    ]}
                  />
                </div>
              )}
            </>
          )}
          {reintentar.isError && <ErrorAlerta error={reintentar.error} titulo={m.errorReintento} />}
          {reintentar.isSuccess && (
            <Alert tone="info" title={m.reintentoHecho.replace('{n}', String(reintentar.data.reintentados))}>
              {reintentar.data.aviso}
            </Alert>
          )}
          <div style={{ display: 'flex', gap: 'var(--space-6)', marginTop: 'var(--space-9)' }}>
            {progreso.data && progreso.data.fallidos > 0 && loteTerminado(progreso.data) && (
              <Button variant="secondary" icon="rotate-cw" disabled={reintentar.isPending} onClick={() => reintentar.mutate()}>
                {reintentar.isPending ? m.reintentando : m.reintentarFallidos}
              </Button>
            )}
            {progreso.data && loteTerminado(progreso.data) && (
              <Button variant="ghost" onClick={nuevo}>
                {m.nuevo}
              </Button>
            )}
          </div>
        </Card>
      )}
    </div>
  );
}
