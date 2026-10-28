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
import { formatearNumero } from '../../../dominio/dinero';
import { formatearFecha } from '../../../dominio/fechas';
import { textos } from '../../../i18n/textos';
import SinPermiso from '../../../paginas/SinPermiso';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { useCobros } from '../api';
import { CargandoTabla, FalloCarga, RejillaFiltros, SelectorCliente } from '../componentes/Comunes';
import { textosFinanzas } from '../textos';
import type { CobroResumen } from '../tipos';

const t = textosFinanzas.cobros;
const tc = textosFinanzas.comun;
const PAGE_SIZE = 25;

export default function Cobros() {
  const identidad = useSesion((s) => s.identidad);
  const navegar = useNavigate();
  const [f, setF] = useState({ estado: '', moneda: '', clienteId: '', desde: '', hasta: '', page: 1 });
  const lista = useCobros({ ...f, pageSize: PAGE_SIZE });
  if (!puede(identidad, ACCIONES.facturaVer)) return <SinPermiso />;
  const cambiar = (c: Partial<typeof f>) => setF((a) => ({ ...a, page: 1, ...c }));

  const columnas: DataTableColumn[] = [
    {
      key: 'fecha',
      label: t.columnas.fecha,
      render: (x: CobroResumen) => (
        <Link className="mono" to={`/cobros/${x.id}`} onClick={(e) => e.stopPropagation()}>
          {formatearFecha(x.fechaEfectiva)}
        </Link>
      ),
    },
    { key: 'cliente', label: t.columnas.cliente, render: (x: CobroResumen) => x.clienteNombre ?? '—' },
    { key: 'recibido', label: t.columnas.recibido, align: 'right', render: (x: CobroResumen) => <Money amount={x.importeRecibido} currency={x.monedaRecibida} strong /> },
    { key: 'forma', label: t.columnas.forma, render: (x: CobroResumen) => x.formaPagoNombre ?? '—' },
    { key: 'cuenta', label: t.columnas.cuenta, render: (x: CobroResumen) => x.cuentaReceptoraNombre ?? '—' },
    { key: 'cambio', label: t.columnas.cambio, align: 'right', render: (x: CobroResumen) => <span className="mono">{formatearNumero(x.tipoCambioValor)}</span> },
    { key: 'saldoUsado', label: t.columnas.saldoUsado, align: 'right', render: (x: CobroResumen) => <Money amount={x.saldoFavorUsado} currency={x.monedaRecibida} tone="muted" /> },
    { key: 'excedente', label: t.columnas.excedente, align: 'right', render: (x: CobroResumen) => <Money amount={x.excedente} currency={x.monedaRecibida} tone="muted" /> },
    {
      key: 'estado',
      label: t.columnas.estado,
      render: (x: CobroResumen) => (x.estado === 'Anulado' ? <Badge status="anulada">{t.estados.Anulado}</Badge> : <Badge tone="success" dot>{t.estados.Registrado}</Badge>),
    },
  ];

  return (
    <>
      <PageHeader
        eyebrow={textos.navegacion.grupos.finanzas}
        title={t.titulo}
        description={t.descripcion}
        breadcrumbs={[{ label: textos.navegacion.grupos.finanzas }, { label: textos.navegacion.modulos.cobros.etiqueta }]}
      >
        {puede(identidad, ACCIONES.cobroRegistrar) && (
          <Button icon="banknote" onClick={() => navegar('/cuentas')}>
            {textosFinanzas.cuentas.registrarCobro}
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
            <DataTable columns={columnas} rows={lista.data.data} empty={tc.vacio} onRowClick={(x: CobroResumen) => navegar(`/cobros/${x.id}`)} />
            <Pagination page={lista.data.page} pageSize={PAGE_SIZE} total={lista.data.total} onChange={(page) => setF((a) => ({ ...a, page }))} />
          </>
        )}
      </Card>
    </>
  );
}
