import { useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { Card } from '../../../design-system/components/core/Card';
import { DataTable, type DataTableColumn } from '../../../design-system/components/data/DataTable';
import { Money } from '../../../design-system/components/data/Money';
import { Select } from '../../../design-system/components/forms/Select';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { Pagination } from '../../../design-system/components/navigation/Pagination';
import { textos } from '../../../i18n/textos';
import SinPermiso from '../../../paginas/SinPermiso';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { useFianzas } from '../api';
import { CargandoTabla, FalloCarga, InsigniaFila, RejillaFiltros } from '../componentes/Comunes';
import { textosFinanzas } from '../textos';
import type { FianzaResumen } from '../tipos';

const t = textosFinanzas.fianzas;
const tc = textosFinanzas.comun;
const PAGE_SIZE = 25;

export default function Fianzas() {
  const identidad = useSesion((s) => s.identidad);
  const navegar = useNavigate();
  const [f, setF] = useState({ moneda: '', page: 1 });
  const lista = useFianzas({ ...f, pageSize: PAGE_SIZE });
  if (!puede(identidad, ACCIONES.facturaVer)) return <SinPermiso />;

  const columnas: DataTableColumn[] = [
    {
      key: 'contrato',
      label: t.columnas.contrato,
      render: (x: FianzaResumen) => (
        <Link className="mono" to={`/fianzas/${x.id}`} onClick={(e) => e.stopPropagation()}>
          {x.contratoId.slice(0, 8)}
        </Link>
      ),
    },
    { key: 'exigido', label: t.columnas.exigido, align: 'right', render: (x: FianzaResumen) => <Money amount={x.importeExigido} currency={x.moneda} /> },
    { key: 'numero', label: t.columnas.numero, align: 'right', render: (x: FianzaResumen) => <span className="mono">{x.numeroFianzas}</span> },
    { key: 'cobrado', label: t.columnas.cobrado, align: 'right', render: (x: FianzaResumen) => <Money amount={x.cobrado} currency={x.moneda} /> },
    { key: 'devuelto', label: t.columnas.devuelto, align: 'right', render: (x: FianzaResumen) => <Money amount={x.devuelto} currency={x.moneda} tone="muted" /> },
    { key: 'descontado', label: t.columnas.descontado, align: 'right', render: (x: FianzaResumen) => <Money amount={x.descontado} currency={x.moneda} tone="muted" /> },
    { key: 'saldo', label: t.columnas.saldo, align: 'right', render: (x: FianzaResumen) => <Money amount={x.saldo} currency={x.moneda} strong /> },
    { key: 'estado', label: t.columnas.estado, render: (x: FianzaResumen) => <InsigniaFila tipo="Fianza" estado={x.estado} /> },
  ];

  return (
    <>
      <PageHeader
        eyebrow={textos.navegacion.grupos.finanzas}
        title={t.titulo}
        description={t.descripcion}
        breadcrumbs={[{ label: textos.navegacion.grupos.finanzas }, { label: textos.navegacion.modulos.fianzas.etiqueta }]}
      />
      <Card>
        <RejillaFiltros>
          <Select label={tc.moneda} value={f.moneda} options={[{ value: '', label: tc.todas }, 'USD', 'DOP']} onChange={(e) => setF({ moneda: e.target.value, page: 1 })} />
        </RejillaFiltros>
      </Card>
      <div style={{ height: 24 }} />
      <Card padded={false}>
        {lista.isPending ? (
          <CargandoTabla />
        ) : lista.isError ? (
          <div style={{ padding: 'var(--space-9)' }}>
            <FalloCarga error={lista.error} reintentar={() => void lista.refetch()} />
          </div>
        ) : (
          <>
            <DataTable columns={columnas} rows={lista.data.data} empty={tc.vacio} onRowClick={(x: FianzaResumen) => navegar(`/fianzas/${x.id}`)} />
            <Pagination page={lista.data.page} pageSize={PAGE_SIZE} total={lista.data.total} onChange={(page) => setF((a) => ({ ...a, page }))} />
          </>
        )}
      </Card>
    </>
  );
}
