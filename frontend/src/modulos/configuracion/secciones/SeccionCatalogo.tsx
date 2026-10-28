import { useState, type JSX } from 'react';
import { ConfirmarAccion } from '../../../componentes/ConfirmarAccion';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Badge } from '../../../design-system/components/core/Badge';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { DataTable } from '../../../design-system/components/data/DataTable';
import { EmptyState } from '../../../design-system/components/feedback/EmptyState';
import { Skeleton } from '../../../design-system/components/feedback/Skeleton';
import { useCatalogo, useDarDeBaja, useGuardarCatalogo } from '../api';
import type { DefinicionCatalogo } from '../catalogos';
import type { Fila } from '../esquema';
import { textosConfiguracion as t } from '../textos';
import { FormularioEsquema } from './FormularioEsquema';

/** Lista + alta + edición + baja de un catálogo de `/config/*`. La definición (ruta, esquema, columnas) está en `catalogos.ts`. */
export function SeccionCatalogo({ def }: { def: DefinicionCatalogo }): JSX.Element {
  const lista = useCatalogo(def.ruta);
  const guardar = useGuardarCatalogo(def.ruta);
  const baja = useDarDeBaja(def.ruta);
  const [editando, setEditando] = useState<Fila | 'nuevo' | null>(null);
  const [dandoDeBaja, setDandoDeBaja] = useState<Fila | null>(null);
  const filas = lista.data ?? [];

  const columnas = [
    ...def.columnas.map((c) => ({
      key: c.key,
      label: c.etiqueta,
      align: c.align,
      render: (f: Fila) => (c.mono ? <span className="mono">{c.formato(f)}</span> : c.formato(f)),
    })),
    {
      key: '__estado',
      label: 'Estado',
      render: (f: Fila) => (
        <Badge tone={f[def.campoActivo] === true ? 'success' : 'neutral'} dot>
          {f[def.campoActivo] === true ? t.comun.activo : t.comun.inactivo}
        </Badge>
      ),
    },
  ];

  return (
    <Card
      title={def.titulo}
      padded={false}
      actions={
        <Button size="sm" icon="plus" variant="secondary" onClick={() => setEditando('nuevo')}>
          {def.nuevo}
        </Button>
      }
    >
      {def.descripcion && <p style={{ margin: 0, padding: '0 var(--space-9) var(--space-6)', color: 'var(--text-muted)' }}>{def.descripcion}</p>}
      {lista.isError && (
        <div style={{ padding: '0 var(--space-9) var(--space-6)' }}>
          <ErrorAlerta error={lista.error} titulo={t.comun.errorLista} />
        </div>
      )}
      {lista.isPending ? (
        <div style={{ padding: 'var(--space-9)' }}>
          <Skeleton lines={3} height={20} />
        </div>
      ) : filas.length === 0 && !lista.isError ? (
        <EmptyState title={t.comun.vacioTitulo}>{def.nuevo}</EmptyState>
      ) : (
        <DataTable rows={filas} columns={columnas} onRowClick={(f: Fila) => setEditando(f)} />
      )}

      {editando !== null && (
        <FormularioEsquema
          titulo={editando === 'nuevo' ? def.nuevo : def.editar(editando)}
          campos={def.campos}
          fila={editando === 'nuevo' ? null : editando}
          bloqueo={editando === 'nuevo' ? null : (def.bloqueo?.(editando) ?? null)}
          porDefecto={Object.fromEntries(def.campos.filter((c) => c.tipo === 'switch' && c.clave === def.campoActivo).map((c) => [c.clave, true]))}
          onGuardar={(cuerpo) => guardar.mutateAsync({ ...(editando === 'nuevo' ? {} : { id: String(editando.id) }), cuerpo })}
          onCerrar={() => setEditando(null)}
          pie={
            editando !== 'nuevo' && editando[def.campoActivo] === true ? (
              <Button variant="danger" onClick={() => setDandoDeBaja(editando)}>
                {t.comun.darDeBaja}
              </Button>
            ) : undefined
          }
        />
      )}
      <ConfirmarAccion
        open={dandoDeBaja !== null}
        accion={t.comun.darDeBaja}
        consecuencia={t.comun.bajaConsecuencia}
        onCerrar={() => setDandoDeBaja(null)}
        onConfirmar={async () => {
          if (dandoDeBaja) await baja.mutateAsync(String(dandoDeBaja.id));
          setEditando(null);
        }}
      />
    </Card>
  );
}
