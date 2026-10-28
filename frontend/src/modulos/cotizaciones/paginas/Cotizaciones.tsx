import { useState } from 'react';
import { useNavigate } from 'react-router';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { DataTable } from '../../../design-system/components/data/DataTable';
import { Money } from '../../../design-system/components/data/Money';
import { EmptyState } from '../../../design-system/components/feedback/EmptyState';
import { Select } from '../../../design-system/components/forms/Select';
import { FilterBar } from '../../../design-system/components/navigation/FilterBar';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { Pagination } from '../../../design-system/components/navigation/Pagination';
import { formatearMarca } from '../../../dominio/fechas';
import { textos } from '../../../i18n/textos';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { BadgeEstado, CargandoFilas } from '../../operacion/Comunes';
import { ESTADOS_COTIZACION, type EstadoCotizacionApi } from '../../operacion/etiquetas';
import { TAMANO_PAGINA, useCotizaciones, type CotizacionResumen } from '../api';
import { FormularioCotizacionDrawer } from '../FormularioCotizacion';
import { textosCotizaciones as t } from '../textos';

export default function Cotizaciones() {
  const navegar = useNavigate();
  const identidad = useSesion((s) => s.identidad);
  const [filtros, setFiltros] = useState<{ page: number; q: string; estado: EstadoCotizacionApi | '' }>({ page: 1, q: '', estado: '' });
  const [creando, setCreando] = useState(false);
  const lista = useCotizaciones(filtros);
  const filas = lista.data?.data ?? [];
  const hayFiltros = filtros.q !== '' || filtros.estado !== '';

  return (
    <>
      <PageHeader
        eyebrow={textos.navegacion.grupos.operacion}
        title={t.titulo}
        description={t.descripcion}
        breadcrumbs={[{ label: textos.navegacion.grupos.operacion }, { label: textos.navegacion.modulos.cotizaciones.etiqueta }]}
      >
        {puede(identidad, ACCIONES.facturaBorrador) && (
          <Button icon="plus" onClick={() => setCreando(true)}>
            {t.nueva}
          </Button>
        )}
      </PageHeader>

      {lista.isError && <ErrorAlerta error={lista.error} titulo={t.errorTitulo} />}

      <Card padded={false}>
        <FilterBar
          searchPlaceholder={t.buscar}
          search={filtros.q}
          onSearch={(q) => setFiltros({ ...filtros, q, page: 1 })}
          filters={filtros.estado ? [{ key: 'estado', label: t.estado, value: ESTADOS_COTIZACION[filtros.estado].texto }] : []}
          onRemove={() => setFiltros({ ...filtros, estado: '', page: 1 })}
          onClear={() => setFiltros({ page: 1, q: '', estado: '' })}
        >
          <Select
            placeholder={t.todos}
            value={filtros.estado}
            onChange={(e) => setFiltros({ ...filtros, estado: e.target.value as EstadoCotizacionApi | '', page: 1 })}
            options={(Object.keys(ESTADOS_COTIZACION) as EstadoCotizacionApi[]).map((k) => ({ value: k, label: ESTADOS_COTIZACION[k].texto }))}
            style={{ minWidth: 200 }}
          />
        </FilterBar>

        {lista.isPending ? (
          <CargandoFilas />
        ) : filas.length === 0 && !lista.isError ? (
          <EmptyState icon="file-text" title={hayFiltros ? t.sinResultados : t.vacioTitulo}>
            {hayFiltros ? undefined : t.vacioCuerpo}
          </EmptyState>
        ) : (
          <DataTable
            rows={filas}
            onRowClick={(c: CotizacionResumen) => navegar(`/cotizaciones/${c.id}`)}
            columns={[
              { key: 'destinatario', label: t.columnas.destinatario, render: (c: CotizacionResumen) => c.clienteNombre ?? (c.prospectoNombre ? `${c.prospectoNombre} · ${t.prospecto}` : '—') },
              { key: 'unidad', label: t.columnas.unidad, render: (c: CotizacionResumen) => c.unidadNombre ?? '—' },
              { key: 'estado', label: t.columnas.estado, render: (c: CotizacionResumen) => <BadgeEstado estado={ESTADOS_COTIZACION[c.estado]} /> },
              { key: 'lineas', label: t.columnas.lineas, align: 'right', render: (c: CotizacionResumen) => <span className="mono">{c.lineas}</span> },
              { key: 'total', label: t.columnas.total, align: 'right', render: (c: CotizacionResumen) => <Money amount={c.total} currency={c.moneda} /> },
              { key: 'fecha', label: t.columnas.fecha, align: 'right', render: (c: CotizacionResumen) => <span className="mono">{formatearMarca(c.createdAt).slice(0, 10)}</span> },
            ]}
          />
        )}
        {lista.data && lista.data.total > 0 && (
          <Pagination page={lista.data.page} pageSize={TAMANO_PAGINA} total={lista.data.total} onChange={(page) => setFiltros({ ...filtros, page })} />
        )}
      </Card>

      <FormularioCotizacionDrawer
        open={creando}
        onCerrar={() => setCreando(false)}
        onGuardada={(id) => {
          setCreando(false);
          navegar(`/cotizaciones/${id}`);
        }}
      />
    </>
  );
}
