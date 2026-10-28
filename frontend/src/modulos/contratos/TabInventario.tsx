import { useEffect, useState, type JSX } from 'react';
import { ErrorAlerta } from '../../componentes/ErrorAlerta';
import { Badge } from '../../design-system/components/core/Badge';
import { Button } from '../../design-system/components/core/Button';
import { Card } from '../../design-system/components/core/Card';
import { DataTable } from '../../design-system/components/data/DataTable';
import { InventoryChecklist, type InventoryItem } from '../../design-system/components/data/InventoryChecklist';
import { Money } from '../../design-system/components/data/Money';
import { Alert } from '../../design-system/components/feedback/Alert';
import { EmptyState } from '../../design-system/components/feedback/EmptyState';
import { Input } from '../../design-system/components/forms/Input';
import { Textarea } from '../../design-system/components/forms/Textarea';
import { formatearMarca } from '../../dominio/fechas';
import { CargandoFilas } from '../operacion/Comunes';
import type { MonedaApi } from '../operacion/tipos';
import { AvisoBloqueo } from '../reservas/AvisoBloqueo';
import { useFirmarRevision, useInventarioContrato, useRevision, type ItemInventario, type RevisionLinea } from './api';
import { cuerpoRevision, validarRevision, type ItemRevision } from './modelo';
import { textosContratos } from './textos';

const t = textosContratos.inventario;

/**
 * Inventario congelado del contrato y revisión de salida. La revisión se firma entera (todas las líneas),
 * una sola vez; ausente y dañado piden nota. Firmarla exige `contrato.estado`. No crea ningún cobro.
 */
export function TabInventario({ contratoId, moneda, puedeFirmar }: { contratoId: string; moneda: MonedaApi; puedeFirmar: boolean }): JSX.Element {
  const inventario = useInventarioContrato(contratoId);
  const revision = useRevision(contratoId);
  const items = inventario.data ?? [];

  if (inventario.isPending || revision.isPending) return <CargandoFilas filas={4} />;
  if (inventario.isError) return <ErrorAlerta error={inventario.error} titulo={t.titulo} />;

  return (
    <div style={{ display: 'grid', gap: 'var(--space-9)' }}>
      <Card title={t.titulo}>
        {items.length === 0 ? (
          <EmptyState icon="package" title={t.vacio}>
            {t.vacioCuerpo}
          </EmptyState>
        ) : (
          <>
            <p style={{ margin: '0 0 var(--space-6)', color: 'var(--text-muted)' }}>{t.congelado}</p>
            <DataTable
              rows={items}
              columns={[
                { key: 'nombre', label: t.columnas.elemento },
                { key: 'cantidad', label: t.columnas.cantidad, align: 'right', render: (i: ItemInventario) => <span className="mono">{i.cantidad}</span> },
                { key: 'notas', label: t.columnas.notas, render: (i: ItemInventario) => i.notas ?? '—' },
              ]}
            />
          </>
        )}
      </Card>

      <Card title={t.revisionTitulo}>
        {revision.isError ? (
          <ErrorAlerta error={revision.error} titulo={t.revisionTitulo} />
        ) : revision.data ? (
          <RevisionFirmada revision={revision.data} moneda={moneda} />
        ) : items.length === 0 ? (
          <p style={{ margin: 0, color: 'var(--text-muted)' }}>{t.revisionVacio}</p>
        ) : puedeFirmar ? (
          <FormularioRevision contratoId={contratoId} items={items} moneda={moneda} />
        ) : (
          <p style={{ margin: 0, color: 'var(--text-muted)' }}>{t.revisionVacio}</p>
        )}
      </Card>
    </div>
  );
}

function RevisionFirmada({ revision, moneda }: { revision: NonNullable<ReturnType<typeof useRevision>['data']>; moneda: MonedaApi }): JSX.Element {
  return (
    <div style={{ display: 'grid', gap: 'var(--space-6)' }}>
      <p style={{ margin: 0, color: 'var(--text-muted)' }}>{t.revisionFirmada.replace('{fecha}', formatearMarca(revision.firmadaAt))}</p>
      <DataTable
        rows={revision.lineas}
        columns={[
          { key: 'nombre', label: t.columnasRevision.elemento },
          {
            key: 'estado',
            label: t.columnasRevision.estado,
            render: (l: RevisionLinea) => (
              <Badge tone={l.estado === 'Presente' ? 'success' : l.estado === 'Ausente' ? 'warning' : 'danger'} dot>
                {t.estados[l.estado]}
              </Badge>
            ),
          },
          { key: 'nota', label: t.columnasRevision.nota, render: (l: RevisionLinea) => l.nota ?? '—' },
        ]}
      />
      <div style={{ display: 'flex', justifyContent: 'space-between', gap: 'var(--space-6)', flexWrap: 'wrap' }}>
        <span>
          {t.importePropuesto}{' '}
          {revision.importePropuesto != null ? <Money amount={revision.importePropuesto} currency={moneda} strong tone="danger" /> : <span style={{ color: 'var(--text-muted)' }}>{t.sinFaltante}</span>}
        </span>
        {revision.observacion && (
          <span style={{ color: 'var(--text-muted)' }}>
            {t.observacion}: {revision.observacion}
          </span>
        )}
      </div>
    </div>
  );
}

function FormularioRevision({ contratoId, items, moneda }: { contratoId: string; items: ItemInventario[]; moneda: MonedaApi }): JSX.Element {
  const firmar = useFirmarRevision(contratoId);
  const [filas, setFilas] = useState<ItemRevision[]>([]);
  const [observacion, setObservacion] = useState('');
  const [intentado, setIntentado] = useState(false);
  const [fallo, setFallo] = useState<unknown>(null);

  useEffect(() => {
    setFilas(items.map((i) => ({ id: i.id, name: i.nombre, qty: Number(i.cantidad), state: undefined, amount: '', nota: '' })));
  }, [items]);

  const validacion = validarRevision(filas);
  const conIncidencia = filas.filter((f) => f.state === 'faltante' || f.state === 'danado');

  function alCambiar(nuevos: InventoryItem[]) {
    setFilas((actuales) => actuales.map((f, i) => ({ ...f, state: nuevos[i]?.state, amount: String(nuevos[i]?.amount ?? '') })));
  }

  async function enviar() {
    setIntentado(true);
    setFallo(null);
    if (validacion.sinRevisar > 0 || validacion.sinNota.length > 0) return;
    try {
      await firmar.mutateAsync(cuerpoRevision(filas, observacion));
    } catch (e) {
      setFallo(e);
    }
  }

  return (
    <div style={{ display: 'grid', gap: 'var(--space-9)' }}>
      <Alert tone="info">{t.aviso}</Alert>
      {fallo !== null && <AvisoBloqueo error={fallo} titulo={t.error} />}
      <InventoryChecklist
        editable
        currency={moneda}
        frozenFrom={t.congelado.toLowerCase()}
        items={filas.map((f) => ({ name: f.name, qty: f.qty, state: f.state, amount: f.amount }))}
        onChange={alCambiar}
      />
      {conIncidencia.length > 0 && (
        <div style={{ display: 'grid', gap: 'var(--space-6)' }}>
          <span className="ar-hint">{t.notaAyuda}</span>
          {conIncidencia.map((f) => (
            <Input
              key={f.id}
              id={`nota-${f.id}`}
              label={t.notaItem.replace('{nombre}', f.name)}
              required
              maxLength={1000}
              value={f.nota}
              onChange={(e) => setFilas((actuales) => actuales.map((x) => (x.id === f.id ? { ...x, nota: e.target.value } : x)))}
              error={intentado && validacion.sinNota.includes(f.id) ? t.faltanNotas : undefined}
            />
          ))}
        </div>
      )}
      <Textarea id="revision-observacion" label={t.observacionCampo} rows={2} maxLength={1000} value={observacion} onChange={(e) => setObservacion(e.target.value)} />
      {intentado && validacion.sinRevisar > 0 && <Alert tone="danger">{t.faltanPorRevisar.replace('{n}', String(validacion.sinRevisar))}</Alert>}
      <div>
        <Button icon="pen-line" onClick={() => void enviar()} disabled={firmar.isPending}>
          {firmar.isPending ? t.firmando : t.firmar}
        </Button>
      </div>
    </div>
  );
}
