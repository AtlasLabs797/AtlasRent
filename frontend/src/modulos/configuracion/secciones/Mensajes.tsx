import { useState, type JSX } from 'react';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Badge } from '../../../design-system/components/core/Badge';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { DataTable } from '../../../design-system/components/data/DataTable';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { Skeleton } from '../../../design-system/components/feedback/Skeleton';
import { formatearMarca } from '../../../dominio/fechas';
import { useCatalogo, useGuardarPlantilla } from '../api';
import { CAMPOS_PLANTILLA } from '../catalogos';
import type { Fila } from '../esquema';
import { textosConfiguracion as textos } from '../textos';
import { FormularioEsquema } from './FormularioEsquema';

const t = textos.mensajes;
const tipoDe = (f: Fila) => (t.tipos as Record<string, string>)[String(f.tipo)] ?? String(f.tipo);

/** Plantillas de mensaje por tipo e idioma. Ver es `configuracion.editar`; guardar es `plantilla.editar`. */
export function Mensajes({ puedeEditar }: { puedeEditar: boolean }): JSX.Element {
  const lista = useCatalogo('/config/plantillas-mensaje');
  const guardar = useGuardarPlantilla();
  const [editando, setEditando] = useState<Fila | 'nueva' | null>(null);

  return (
    <div style={{ display: 'grid', gap: 'var(--space-7)' }}>
      {!puedeEditar && <Alert tone="info">{t.soloLectura}</Alert>}
      <Card
        title={t.titulo}
        padded={false}
        actions={
          puedeEditar ? (
            <Button size="sm" icon="plus" variant="secondary" onClick={() => setEditando('nueva')}>
              {t.nueva}
            </Button>
          ) : undefined
        }
      >
        <div style={{ padding: '0 var(--space-9) var(--space-6)', display: 'grid', gap: 'var(--space-3)' }}>
          <p style={{ margin: 0, color: 'var(--text-muted)' }}>{t.descripcion}</p>
          <p className="mono" style={{ margin: 0, color: 'var(--text-subtle)', font: 'var(--type-caption)' }}>
            {t.variables}
          </p>
        </div>
        {lista.isError && (
          <div style={{ padding: '0 var(--space-9) var(--space-6)' }}>
            <ErrorAlerta error={lista.error} titulo={textos.comun.errorLista} />
          </div>
        )}
        {lista.isPending ? (
          <div style={{ padding: 'var(--space-9)' }}>
            <Skeleton lines={3} height={20} />
          </div>
        ) : (
          <DataTable
            rows={lista.data ?? []}
            onRowClick={(f: Fila) => setEditando(f)}
            columns={[
              { key: 'tipo', label: t.columnas.tipo, render: tipoDe },
              { key: 'idioma', label: t.columnas.idioma, render: (f: Fila) => <span className="mono">{String(f.idioma)}</span> },
              { key: 'asunto', label: t.columnas.asunto, render: (f: Fila) => String(f.asunto) },
              {
                key: 'activa',
                label: t.columnas.estado,
                render: (f: Fila) => (
                  <Badge tone={f.activa === true ? 'success' : 'neutral'} dot>
                    {f.activa === true ? textos.comun.activo : textos.comun.inactivo}
                  </Badge>
                ),
              },
              { key: 'updatedAt', label: t.columnas.actualizado, align: 'right', render: (f: Fila) => <span className="mono">{formatearMarca(String(f.updatedAt ?? f.createdAt))}</span> },
            ]}
          />
        )}
      </Card>
      {editando !== null && (
        <FormularioEsquema
          titulo={editando === 'nueva' ? t.nueva : t.editar.replace('{tipo}', tipoDe(editando).toLowerCase())}
          campos={CAMPOS_PLANTILLA}
          fila={editando === 'nueva' ? null : editando}
          soloLectura={!puedeEditar}
          porDefecto={{ tipo: 'Cotizacion', idioma: 'es', asunto: '', cuerpo: '', activa: true }}
          onGuardar={(cuerpo) => guardar.mutateAsync(cuerpo)}
          onCerrar={() => setEditando(null)}
        />
      )}
    </div>
  );
}
