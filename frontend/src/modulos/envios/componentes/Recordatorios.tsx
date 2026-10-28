import { useState } from 'react';
import { aErrorApi } from '../../../api/errores';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Badge } from '../../../design-system/components/core/Badge';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { DataTable } from '../../../design-system/components/data/DataTable';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { Dialog } from '../../../design-system/components/feedback/Dialog';
import { EmptyState } from '../../../design-system/components/feedback/EmptyState';
import { Pagination } from '../../../design-system/components/navigation/Pagination';
import { formatearFecha } from '../../../dominio/fechas';
import { useEnviarRecordatorio, useModoRecordatorios, usePausarRecordatorios, useRecordatorios } from '../api';
import { etiquetaModo, type EstadoRecordatorio, type RecordatorioFila } from '../modelo';
import { textosEnvios as t } from '../textos';

const ESTADOS: EstadoRecordatorio[] = ['Pendiente', 'Enviado', 'Omitido'];
const TONO: Record<EstadoRecordatorio, 'neutral' | 'success' | 'warning'> = {
  Pendiente: 'neutral',
  Enviado: 'success',
  Omitido: 'warning',
};

/**
 * Avisos de pago (F4-05, D12). El modo (apagado/manual/automático) lo guarda Configuración y solo lo
 * lee `configuracion.editar`; sin esa acción no se pide ni se inventa. En manual cada aviso se
 * confirma aquí, y el saldo del mensaje se recalcula en la API al confirmar.
 */
export function Recordatorios({ puedeVerModo, puedePausar }: { puedeVerModo: boolean; puedePausar: boolean }) {
  const r = t.recordatorios;
  const [pagina, setPagina] = useState(1);
  const [estado, setEstado] = useState<EstadoRecordatorio | ''>('');
  const consulta = useRecordatorios(pagina, estado);
  const modo = useModoRecordatorios(puedeVerModo);
  const enviar = useEnviarRecordatorio();
  const pausar = usePausarRecordatorios();
  const [aviso, setAviso] = useState<{ tono: 'success' | 'warning' | 'danger'; titulo: string; cuerpo: string } | null>(null);
  const [pausaDe, setPausaDe] = useState<RecordatorioFila | null>(null);

  function confirmar(fila: RecordatorioFila) {
    setAviso(null);
    enviar.mutate(fila.id, {
      onSuccess: (x) =>
        // 200 con Omitido (ya pagó, o cliente en pausa) es el resultado pedido, no un error.
        setAviso(
          x.estado === 'Omitido'
            ? { tono: 'warning', titulo: r.omitido, cuerpo: x.motivoOmision ?? r.omitidoSinMotivo }
            : { tono: 'success', titulo: r.enviado, cuerpo: '' },
        ),
      onError: (e) => setAviso({ tono: 'danger', titulo: r.errorEnviar, cuerpo: aErrorApi(e).detail ?? aErrorApi(e).title }),
    });
  }

  function cambiarPausa(pausados: boolean) {
    if (!pausaDe) return;
    pausar.mutate(
      { clienteId: pausaDe.clienteId, pausados },
      {
        onSuccess: () => {
          setAviso({ tono: 'success', titulo: pausados ? r.pausada : r.reanudada, cuerpo: pausaDe.cliente });
          setPausaDe(null);
        },
      },
    );
  }

  const nota =
    modo.data === 'Manual' ? r.notaManual : modo.data === 'Automatico' ? r.notaAutomatico : modo.data === 'Apagado' ? r.notaApagado : null;

  return (
    <>
      <div style={{ display: 'grid', gap: 'var(--space-6)', marginBottom: 'var(--space-9)' }}>
        {puedeVerModo ? (
          modo.data && (
            <Alert tone="info" title={`${r.modo}: ${etiquetaModo(modo.data)}`}>
              {nota}
            </Alert>
          )
        ) : (
          <Alert tone="info" title={r.modoDesconocido}>
            {r.notaManual}
          </Alert>
        )}
        {aviso && (
          <Alert tone={aviso.tono} title={aviso.titulo}>
            {aviso.cuerpo}
          </Alert>
        )}
      </div>
      <Card
        title={r.titulo}
        padded={false}
        actions={
          <div className="ar-control" style={{ minWidth: 180 }}>
            <select
              aria-label={r.estado}
              value={estado}
              onChange={(ev) => {
                setEstado(ev.target.value as EstadoRecordatorio | '');
                setPagina(1);
              }}
            >
              <option value="">{r.todosEstados}</option>
              {ESTADOS.map((s) => (
                <option key={s} value={s}>
                  {r.estados[s]}
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
                <EmptyState icon="bell" title={r.vacioTitulo}>
                  {r.vacioCuerpo}
                </EmptyState>
              }
              columns={[
                { key: 'fecha', label: r.columnas.fecha, render: (x: RecordatorioFila) => <span className="mono">{formatearFecha(x.fecha)}</span> },
                { key: 'cliente', label: r.columnas.cliente },
                { key: 'unidad', label: r.columnas.unidad, render: (x: RecordatorioFila) => x.unidad ?? '—' },
                {
                  key: 'estado',
                  label: r.columnas.estado,
                  render: (x: RecordatorioFila) => (
                    <div>
                      <Badge tone={TONO[x.estado]} dot>
                        {r.estados[x.estado]}
                      </Badge>
                      {x.motivoOmision && <div style={{ color: 'var(--text-muted)', marginTop: 4 }}>{x.motivoOmision}</div>}
                    </div>
                  ),
                },
                {
                  key: 'acciones',
                  label: r.columnas.acciones,
                  align: 'right',
                  render: (x: RecordatorioFila) => (
                    <div style={{ display: 'inline-flex', gap: 'var(--space-4)' }}>
                      {puedePausar && (
                        <Button variant="ghost" size="sm" icon="pause" onClick={() => setPausaDe(x)}>
                          {r.pausa}
                        </Button>
                      )}
                      {x.estado === 'Pendiente' && (
                        <Button variant="secondary" size="sm" icon="send" disabled={enviar.isPending} onClick={() => confirmar(x)}>
                          {r.enviar}
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
      <Dialog
        open={pausaDe !== null}
        title={r.pausaTitulo.replace('{cliente}', pausaDe?.cliente ?? '')}
        description={r.pausaDescripcion}
        onClose={() => setPausaDe(null)}
        footer={
          <>
            <Button variant="ghost" onClick={() => setPausaDe(null)}>
              {r.cerrar}
            </Button>
            <Button variant="secondary" disabled={pausar.isPending} onClick={() => cambiarPausa(false)}>
              {r.reanudar}
            </Button>
            <Button disabled={pausar.isPending} onClick={() => cambiarPausa(true)}>
              {r.pausar}
            </Button>
          </>
        }
      >
        {pausar.isError && <ErrorAlerta error={pausar.error} titulo={r.errorPausa} />}
      </Dialog>
    </>
  );
}
