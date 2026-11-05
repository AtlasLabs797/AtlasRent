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
import { formatearFecha } from '../../../dominio/fechas';
import { textos } from '../../../i18n/textos';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { BadgeEstado, CargandoFilas } from '../../operacion/Comunes';
import { ESTADOS_CONTRATO, FLUJO_CONTRATO, estadoContrato, type EstadoContratoApi } from '../../operacion/etiquetas';
import { TAMANO_PAGINA, useContratos, type ContratoResumen, type FiltrosContratos } from '../api';
import { textosContratos as t } from '../textos';

export default function Contratos() {
  const navegar = useNavigate();
  const identidad = useSesion((s) => s.identidad);
  const [filtros, setFiltros] = useState<FiltrosContratos>({ page: 1, q: '', estado: '' });
  const lista = useContratos(filtros);
  const filas = lista.data?.data ?? [];
  const hayFiltros = filtros.q !== '' || filtros.estado !== '';

  return (
    <>
      <PageHeader
        eyebrow={textos.navegacion.grupos.operacion}
        title={t.titulo}
        description={t.descripcion}
        breadcrumbs={[{ label: textos.navegacion.grupos.operacion }, { label: textos.navegacion.modulos.contratos.etiqueta }]}
      >
        <Button variant="secondary" icon="file-text" onClick={() => navegar('/contratos/plantillas')}>
          {t.plantillas}
        </Button>
        {puede(identidad, ACCIONES.contratoBorrador) && (
          <Button icon="plus" onClick={() => navegar('/contratos/nuevo')}>
            {t.nuevo}
          </Button>
        )}
      </PageHeader>

      {lista.isError && <ErrorAlerta error={lista.error} titulo={t.errorTitulo} />}

      <Card padded={false}>
        <FilterBar
          searchPlaceholder={t.buscar}
          search={filtros.q}
          onSearch={(q) => setFiltros({ ...filtros, q, page: 1 })}
          filters={filtros.estado ? [{ key: 'estado', label: t.estado, value: ESTADOS_CONTRATO[filtros.estado].texto }] : []}
          onRemove={() => setFiltros({ ...filtros, estado: '', page: 1 })}
          onClear={() => setFiltros({ page: 1, q: '', estado: '' })}
        >
          <Select
            placeholder={t.todos}
            value={filtros.estado}
            onChange={(e) => setFiltros({ ...filtros, estado: e.target.value as EstadoContratoApi | '', page: 1 })}
            options={FLUJO_CONTRATO.map((k) => ({ value: k, label: ESTADOS_CONTRATO[k].texto }))}
            style={{ minWidth: 220 }}
          />
        </FilterBar>

        {lista.isPending ? (
          <CargandoFilas />
        ) : filas.length === 0 && !lista.isError ? (
          <EmptyState icon="file-pen-line" title={hayFiltros ? t.sinResultados : t.vacioTitulo}>
            {hayFiltros ? undefined : t.vacioCuerpo}
          </EmptyState>
        ) : (
          <DataTable
            rows={filas}
            onRowClick={(c: ContratoResumen) => navegar(`/contratos/${c.id}`)}
            columns={[
              { key: 'unidad', label: t.columnas.unidad, render: (c: ContratoResumen) => c.unidad ?? '—' },
              { key: 'cliente', label: t.columnas.cliente, render: (c: ContratoResumen) => c.cliente ?? '—' },
              { key: 'inicio', label: t.columnas.inicio, align: 'right', render: (c: ContratoResumen) => <span className="mono">{formatearFecha(c.fechaInicio)}</span> },
              { key: 'fin', label: t.columnas.fin, align: 'right', render: (c: ContratoResumen) => <span className="mono">{formatearFecha(c.fechaFinVigente)}</span> },
              { key: 'renta', label: t.columnas.renta, align: 'right', render: (c: ContratoResumen) => <Money amount={c.renta} currency={c.moneda} /> },
              { key: 'estado', label: t.columnas.estado, render: (c: ContratoResumen) => <BadgeEstado estado={ESTADOS_CONTRATO[estadoContrato(c.estado)]} /> },
            ]}
          />
        )}
        {lista.data && lista.data.total > 0 && (
          <Pagination page={lista.data.page} pageSize={TAMANO_PAGINA} total={lista.data.total} onChange={(page) => setFiltros({ ...filtros, page })} />
        )}
      </Card>
    </>
  );
}
