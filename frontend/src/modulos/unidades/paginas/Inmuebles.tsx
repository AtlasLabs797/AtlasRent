import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Badge } from '../../../design-system/components/core/Badge';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { DataTable } from '../../../design-system/components/data/DataTable';
import { EmptyState } from '../../../design-system/components/feedback/EmptyState';
import { Skeleton } from '../../../design-system/components/feedback/Skeleton';
import { Select } from '../../../design-system/components/forms/Select';
import { Switch } from '../../../design-system/components/forms/Switch';
import { FilterBar } from '../../../design-system/components/navigation/FilterBar';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { Pagination } from '../../../design-system/components/navigation/Pagination';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { useRetardo } from '../../clientes/useRetardo';
import { useListaInmuebles } from '../api';
import { FormularioInmueble } from '../FormularioInmueble';
import { TIPOS_INMUEBLE } from '../logica';
import { textosUnidades } from '../textos';
import type { InmuebleResumen, TipoInmueble } from '../tipos';

const t = textosUnidades;
const POR_PAGINA = 25;

export default function Inmuebles() {
  const navegar = useNavigate();
  const editar = puede(useSesion((s) => s.identidad), ACCIONES.clienteEditar);
  const [q, setQ] = useState('');
  const [tipo, setTipo] = useState<TipoInmueble | ''>('');
  const [incluirInactivos, setIncluirInactivos] = useState(false);
  const [page, setPage] = useState(1);
  const [alta, setAlta] = useState(false);
  const qRetardada = useRetardo(q, 300);
  useEffect(() => setPage(1), [qRetardada, tipo, incluirInactivos]);

  const lista = useListaInmuebles({ q: qRetardada, tipo, incluirInactivos, page, pageSize: POR_PAGINA });
  const filas = lista.data?.data ?? [];
  const filtrando = qRetardada.trim() !== '' || tipo !== '';

  return (
    <>
      <PageHeader eyebrow={t.eyebrow} title={t.titulo} description={t.descripcion} breadcrumbs={[{ label: t.eyebrow }, { label: t.titulo }]}>
        {editar && (
          <Button icon="plus" onClick={() => setAlta(true)}>
            {t.nuevo}
          </Button>
        )}
      </PageHeader>
      <Card padded={false}>
        <FilterBar
          searchPlaceholder={t.lista.buscar}
          search={q}
          onSearch={setQ}
          filters={tipo !== '' ? [{ key: 'tipo', label: t.lista.tipo, value: t.tiposInmueble[tipo] }] : []}
          onRemove={() => setTipo('')}
          onClear={() => {
            setTipo('');
            setQ('');
          }}
        >
          <Select
            value={tipo}
            placeholder={t.lista.todosTipos}
            options={[{ value: '', label: t.lista.todosTipos }, ...TIPOS_INMUEBLE.map((v) => ({ value: v, label: t.tiposInmueble[v] }))]}
            onChange={(e) => setTipo(e.target.value as TipoInmueble | '')}
            style={{ minWidth: 170 }}
          />
          <Switch label={t.lista.inactivos} checked={incluirInactivos} onChange={setIncluirInactivos} />
        </FilterBar>
        {lista.isPending ? (
          <div style={{ padding: 'var(--space-9)' }}>
            <Skeleton lines={6} height={20} />
          </div>
        ) : lista.isError ? (
          <div style={{ padding: 'var(--space-9)' }}>
            <ErrorAlerta error={lista.error} titulo={t.lista.errorTitulo} />
            <div style={{ marginTop: 12 }}>
              <Button variant="secondary" onClick={() => void lista.refetch()}>
                {t.lista.reintentar}
              </Button>
            </div>
          </div>
        ) : filas.length === 0 ? (
          <EmptyState icon="building-2" title={filtrando ? t.lista.vacioTitulo : t.lista.vacioSinDatosTitulo}>
            {filtrando ? t.lista.vacioCuerpo : t.lista.vacioSinDatosCuerpo}
          </EmptyState>
        ) : (
          <>
            <DataTable
              rows={filas}
              onRowClick={(f: InmuebleResumen) => navegar(`/unidades/${f.id}`)}
              columns={[
                { key: 'nombre', label: t.lista.columnas.nombre, render: (f: InmuebleResumen) => <strong style={{ color: 'var(--text-strong)', fontWeight: 600 }}>{f.nombre}</strong> },
                { key: 'tipo', label: t.lista.columnas.tipo, render: (f: InmuebleResumen) => t.tiposInmueble[f.tipo] },
                { key: 'direccion', label: t.lista.columnas.direccion, render: (f: InmuebleResumen) => f.direccion ?? '—' },
                { key: 'estado', label: t.lista.columnas.estado, render: (f: InmuebleResumen) => (f.activo ? <Badge status="activo" /> : <Badge tone="neutral">{t.inactivo}</Badge>) },
              ]}
            />
            <Pagination page={lista.data?.page ?? page} pageSize={POR_PAGINA} total={lista.data?.total ?? 0} onChange={setPage} />
          </>
        )}
      </Card>
      {editar && (
        <FormularioInmueble
          open={alta}
          onClose={() => setAlta(false)}
          onGuardado={(i) => {
            setAlta(false);
            navegar(`/unidades/${i.id}`);
          }}
        />
      )}
    </>
  );
}
