import { formatearNumero } from '../../dominio/dinero.ts';
import { useEffect, useState, type JSX } from 'react';
import { Button } from '../../design-system/components/core/Button';
import { Badge } from '../../design-system/components/core/Badge';
import { DataTable } from '../../design-system/components/data/DataTable';
import { Money } from '../../design-system/components/data/Money';
import { Drawer } from '../../design-system/components/feedback/Drawer';
import { EmptyState } from '../../design-system/components/feedback/EmptyState';
import { DatePicker } from '../../design-system/components/forms/DatePicker';
import { Input } from '../../design-system/components/forms/Input';
import { SegmentedControl } from '../../design-system/components/navigation/SegmentedControl';
import { formatearFecha } from '../../dominio/fechas';
import { ErrorAlerta } from '../../componentes/ErrorAlerta';
import { CargandoFilas } from '../operacion/Comunes';
import type { MonedaApi } from '../operacion/tipos';
import { AvisoBloqueo } from '../reservas/AvisoBloqueo';
import { useCancelarIncremento, useGuardarIncremento, useIncrementos, type Incremento } from './api';
import { DialogoAccion } from './DialogoAccion';
import { cuerpoIncremento, estadoDeIncremento, validarIncremento, type FormularioIncremento } from './modelo';
import { textosContratos } from './textos';

const t = textosContratos.incrementos;
const TONO = { Pendiente: 'warning', Aplicado: 'success', Cancelado: 'neutral' } as const;

/** Incrementos de renta con fecha de vigencia. Escribir exige `contrato.estado`. */
export function TabIncrementos({ contratoId, moneda, puedeEscribir, permitido }: { contratoId: string; moneda: MonedaApi; puedeEscribir: boolean; permitido: boolean }): JSX.Element {
  const lista = useIncrementos(contratoId);
  const [editando, setEditando] = useState<Incremento | 'nuevo' | null>(null);
  const [cancelando, setCancelando] = useState<Incremento | null>(null);
  const cancelar = useCancelarIncremento(contratoId);
  const filas = lista.data ?? [];
  const escribir = puedeEscribir && permitido;

  return (
    <div style={{ display: 'grid', gap: 'var(--space-6)' }}>
      {escribir && (
        <div>
          <Button variant="secondary" icon="plus" onClick={() => setEditando('nuevo')}>
            {t.nuevo}
          </Button>
        </div>
      )}
      {lista.isError && <ErrorAlerta error={lista.error} titulo={t.error} />}
      {lista.isPending ? (
        <CargandoFilas filas={3} />
      ) : filas.length === 0 ? (
        <EmptyState icon="trending-up" title={t.vacio}>
          {t.vacioCuerpo}
        </EmptyState>
      ) : (
        <DataTable
          rows={filas}
          columns={[
            { key: 'tipo', label: t.columnas.tipo, render: (i: Incremento) => (i.tipo === 'Porcentaje' ? t.porcentaje : t.importeFijo) },
            {
              key: 'valor',
              label: t.columnas.valor,
              align: 'right',
              render: (i: Incremento) => (i.tipo === 'Porcentaje' ? <span className="mono">{formatearNumero(i.porcentaje ?? '0')} %</span> : <Money amount={i.importe ?? '0'} currency={moneda} />),
            },
            { key: 'vigencia', label: t.columnas.vigencia, align: 'right', render: (i: Incremento) => <span className="mono">{formatearFecha(i.fechaVigencia)}</span> },
            {
              key: 'estado',
              label: t.columnas.estado,
              render: (i: Incremento) => {
                const e = estadoDeIncremento(i);
                return (
                  <Badge tone={TONO[e]} dot>
                    {t.estados[e]}
                  </Badge>
                );
              },
            },
            {
              key: 'renta',
              label: t.columnas.renta,
              align: 'right',
              render: (i: Incremento) =>
                i.rentaAntes != null && i.rentaDespues != null ? (
                  <span>
                    <Money amount={i.rentaAntes} currency={moneda} /> → <Money amount={i.rentaDespues} currency={moneda} />
                  </span>
                ) : (
                  '—'
                ),
            },
            {
              key: 'acciones',
              label: '',
              align: 'right',
              render: (i: Incremento) =>
                escribir && estadoDeIncremento(i) === 'Pendiente' ? (
                  <span style={{ display: 'inline-flex', gap: 'var(--space-2)' }}>
                    <Button size="sm" variant="ghost" onClick={() => setEditando(i)}>
                      {textosContratos.plantillasPagina.editar}
                    </Button>
                    <Button size="sm" variant="ghost" onClick={() => setCancelando(i)}>
                      {t.cancelarIncremento}
                    </Button>
                  </span>
                ) : null,
            },
          ]}
        />
      )}

      <FormularioIncrementoDrawer contratoId={contratoId} moneda={moneda} incremento={editando === 'nuevo' ? null : editando} abierto={editando !== null} onCerrar={() => setEditando(null)} />

      <DialogoAccion
        abierto={cancelando !== null}
        titulo={t.cancelarTitulo}
        descripcion={t.cancelarConsecuencia}
        confirmar={t.cancelarIncremento}
        tono="danger"
        onConfirmar={() => cancelar.mutateAsync(cancelando!.id)}
        onCerrar={() => setCancelando(null)}
      />
    </div>
  );
}

function inicialDe(incremento: Incremento | null): FormularioIncremento {
  return incremento
    ? { tipo: incremento.tipo, valor: String(incremento.tipo === 'Porcentaje' ? incremento.porcentaje : incremento.importe), fechaVigencia: incremento.fechaVigencia, comentario: '' }
    : { tipo: 'Porcentaje', valor: '', fechaVigencia: '', comentario: '' };
}

function FormularioIncrementoDrawer({ contratoId, moneda, incremento, abierto, onCerrar }: { contratoId: string; moneda: MonedaApi; incremento: Incremento | null; abierto: boolean; onCerrar: () => void }): JSX.Element {
  const guardar = useGuardarIncremento(contratoId, incremento?.id);
  const [f, setF] = useState<FormularioIncremento>(inicialDe(incremento));
  const [intentado, setIntentado] = useState(false);
  const [fallo, setFallo] = useState<unknown>(null);

  useEffect(() => {
    if (abierto) {
      setF(inicialDe(incremento));
      setIntentado(false);
      setFallo(null);
    }
  }, [abierto, incremento]);

  const errores = validarIncremento(f);

  async function enviar() {
    setIntentado(true);
    setFallo(null);
    if (errores.valor || errores.fecha) return;
    try {
      await guardar.mutateAsync(cuerpoIncremento(f));
      onCerrar();
    } catch (e) {
      setFallo(e);
    }
  }

  return (
    <Drawer
      open={abierto}
      onClose={guardar.isPending ? undefined : onCerrar}
      title={incremento ? t.editar : t.nuevo}
      footer={
        <>
          <Button variant="ghost" onClick={onCerrar} disabled={guardar.isPending}>
            Cancelar
          </Button>
          <Button onClick={() => void enviar()} disabled={guardar.isPending}>
            {t.guardar}
          </Button>
        </>
      }
    >
      <div style={{ display: 'grid', gap: 'var(--space-9)' }}>
        {fallo !== null && <AvisoBloqueo error={fallo} titulo={t.error} />}
        <SegmentedControl
          options={[
            { value: 'Porcentaje', label: t.porcentaje },
            { value: 'ImporteFijo', label: t.importeFijo },
          ]}
          value={f.tipo}
          onChange={(tipo) => setF({ ...f, tipo: tipo as FormularioIncremento['tipo'], valor: '' })}
        />
        <Input
          id="incremento-valor"
          label={f.tipo === 'Porcentaje' ? t.valorPorcentaje : t.valorImporte}
          required
          inputMode="decimal"
          suffix={f.tipo === 'Porcentaje' ? '%' : moneda}
          value={f.valor}
          onChange={(e) => setF({ ...f, valor: e.target.value })}
          error={intentado ? errores.valor : undefined}
        />
        <DatePicker label={t.vigencia} required hint={t.vigenciaAyuda} value={f.fechaVigencia} onChange={(e) => setF({ ...f, fechaVigencia: e.target.value })} error={intentado ? errores.fecha : undefined} />
        <Input id="incremento-comentario" label={t.comentario} maxLength={500} value={f.comentario} onChange={(e) => setF({ ...f, comentario: e.target.value })} />
      </div>
    </Drawer>
  );
}
