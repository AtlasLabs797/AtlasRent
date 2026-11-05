import { useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { Badge } from '../../../design-system/components/core/Badge';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { DataTable, type DataTableColumn } from '../../../design-system/components/data/DataTable';
import { Money } from '../../../design-system/components/data/Money';
import { DatePicker } from '../../../design-system/components/forms/DatePicker';
import { Select } from '../../../design-system/components/forms/Select';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { Pagination } from '../../../design-system/components/navigation/Pagination';
import { formatearFecha } from '../../../dominio/fechas';
import { textos } from '../../../i18n/textos';
import SinPermiso from '../../../paginas/SinPermiso';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { useFacturas } from '../api';
import { CargandoTabla, FalloCarga, InsigniaFila, RejillaFiltros, SelectorCliente } from '../componentes/Comunes';
import { numeroDocumento } from '../logica';
import { textosFinanzas } from '../textos';
import type { FacturaResumen } from '../tipos';

const t = textosFinanzas.facturas;
const tc = textosFinanzas.comun;
const PAGE_SIZE = 25;

export default function Facturas() {
  const identidad = useSesion((s) => s.identidad);
  const navegar = useNavigate();
  const [f, setF] = useState({ estado: '', moneda: '', clienteId: '', desde: '', hasta: '', page: 1 });
  const lista = useFacturas({ ...f, pageSize: PAGE_SIZE });
  if (!puede(identidad, ACCIONES.facturaVer)) return <SinPermiso />;
  const cambiar = (c: Partial<typeof f>) => setF((a) => ({ ...a, page: 1, ...c }));

  const columnas: DataTableColumn[] = [
    {
      key: 'numero',
      label: t.columnas.numero,
      render: (x: FacturaResumen) => (
        <Link className="mono" to={`/facturas/${x.id}`} onClick={(e) => e.stopPropagation()}>
          {x.numero === null ? t.borrador : numeroDocumento(x.serieNumero, x.numero)}
        </Link>
      ),
    },
    {
      key: 'cliente',
      label: t.columnas.cliente,
      render: (x: FacturaResumen) => (
        <div>
          <div>{x.clienteNombre ?? '—'}</div>
          <div style={{ font: 'var(--type-caption)', color: 'var(--text-muted)' }}>{[x.unidadNombre, x.concepto].filter(Boolean).join(' · ')}</div>
        </div>
      ),
    },
    { key: 'emision', label: t.columnas.emision, render: (x: FacturaResumen) => <span className="mono">{x.fechaEmision ? formatearFecha(x.fechaEmision) : '—'}</span> },
    { key: 'vence', label: t.columnas.vence, render: (x: FacturaResumen) => <span className="mono">{formatearFecha(x.fechaVencimiento)}</span> },
    { key: 'total', label: t.columnas.total, align: 'right', render: (x: FacturaResumen) => <Money amount={x.total} currency={x.moneda} /> },
    { key: 'cobrado', label: t.columnas.cobrado, align: 'right', render: (x: FacturaResumen) => <Money amount={x.cobrado} currency={x.moneda} /> },
    { key: 'pendiente', label: t.columnas.pendiente, align: 'right', render: (x: FacturaResumen) => <Money amount={x.pendiente} currency={x.moneda} strong tone={x.vencida ? 'danger' : undefined} /> },
    {
      key: 'estado',
      label: t.columnas.estado,
      render: (x: FacturaResumen) => (
        <span style={{ display: 'inline-flex', gap: 6, flexWrap: 'wrap' }}>
          {x.estado === 'Emitida' ? <InsigniaFila tipo="Factura" estado={x.estadoCobro} /> : <InsigniaFila tipo={x.tipoSerie === 'Proforma' ? 'Proforma' : 'Factura'} estado={x.estado} />}
          {x.vencida && <Badge status="vencida" />}
          {x.retencionSinComprobante && <Badge tone="warning">{t.retencionSinComprobante}</Badge>}
        </span>
      ),
    },
  ];

  return (
    <>
      <PageHeader
        eyebrow={textos.navegacion.grupos.finanzas}
        title={t.titulo}
        description={t.descripcion}
        breadcrumbs={[{ label: textos.navegacion.grupos.finanzas }, { label: textos.navegacion.modulos.facturas.etiqueta }]}
      >
        {puede(identidad, ACCIONES.facturaBorrador) && (
          <Button icon="plus" onClick={() => navegar('/facturas/nueva')}>
            {t.nueva}
          </Button>
        )}
        {puede(identidad, ACCIONES.facturaEmitir) && (
          <Button variant="secondary" icon="calendar-clock" onClick={() => navegar('/facturas/periodicas')}>
            {t.periodicas}
          </Button>
        )}
      </PageHeader>
      <Card>
        <RejillaFiltros>
          <SelectorCliente value={f.clienteId} onChange={(clienteId) => cambiar({ clienteId })} />
          <Select
            label={tc.estado}
            value={f.estado}
            options={[{ value: '', label: tc.todos }, ...Object.entries(t.estados).map(([value, label]) => ({ value, label }))]}
            onChange={(e) => cambiar({ estado: e.target.value })}
          />
          <Select label={tc.moneda} value={f.moneda} options={[{ value: '', label: tc.todas }, 'USD', 'DOP']} onChange={(e) => cambiar({ moneda: e.target.value })} />
          <DatePicker label={tc.desde} value={f.desde} onChange={(e) => cambiar({ desde: e.target.value })} />
          <DatePicker label={tc.hasta} value={f.hasta} onChange={(e) => cambiar({ hasta: e.target.value })} />
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
            <DataTable columns={columnas} rows={lista.data.data} empty={tc.vacio} onRowClick={(x: FacturaResumen) => navegar(`/facturas/${x.id}`)} />
            <Pagination page={lista.data.page} pageSize={PAGE_SIZE} total={lista.data.total} onChange={(page) => setF((a) => ({ ...a, page }))} />
          </>
        )}
      </Card>
    </>
  );
}
