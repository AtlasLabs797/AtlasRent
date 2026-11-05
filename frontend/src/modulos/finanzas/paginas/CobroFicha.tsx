import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { ConfirmarDestructivo } from '../../../componentes/ConfirmarDestructivo';
import { Badge } from '../../../design-system/components/core/Badge';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { DataTable, type DataTableColumn } from '../../../design-system/components/data/DataTable';
import { KeyValueList } from '../../../design-system/components/data/KeyValueList';
import { Money } from '../../../design-system/components/data/Money';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { formatearNumero } from '../../../dominio/dinero';
import { formatearFecha, formatearMarca } from '../../../dominio/fechas';
import { textos } from '../../../i18n/textos';
import SinPermiso from '../../../paginas/SinPermiso';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { abrirPdf, useAnularCobro, useCobro } from '../api';
import { CargandoTabla, FalloCarga } from '../componentes/Comunes';
import { esPositivo } from '../decimal';
import { textosFinanzas } from '../textos';
import type { CobroAsignacion } from '../tipos';

const f = textosFinanzas.cobros.ficha;
const tc = textosFinanzas.comun;

export default function CobroFicha() {
  const { id = '' } = useParams();
  const identidad = useSesion((s) => s.identidad);
  const navegar = useNavigate();
  const consulta = useCobro(id);
  const anular = useAnularCobro(id);
  const [anulando, setAnulando] = useState(false);
  const [falloPdf, setFalloPdf] = useState(false);

  if (!puede(identidad, ACCIONES.facturaVer)) return <SinPermiso />;
  const cabecera = (
    <PageHeader
      eyebrow={textos.navegacion.grupos.finanzas}
      title={f.titulo}
      breadcrumbs={[{ label: textos.navegacion.modulos.cobros.etiqueta, onClick: () => navegar('/cobros') }, { label: f.titulo }]}
    />
  );
  if (consulta.isPending) return <>{cabecera}<CargandoTabla /></>;
  if (consulta.isError) return <>{cabecera}<FalloCarga error={consulta.error} reintentar={() => void consulta.refetch()} /></>;

  const x = consulta.data;
  const anulado = x.estado === 'Anulado';

  const columnas: DataTableColumn[] = [
    {
      key: 'destino',
      label: f.destino,
      render: (a: CobroAsignacion) =>
        a.facturaId ? (
          <Link className="mono" to={`/facturas/${a.facturaId}`}>
            {f.factura.replace('{n}', String(a.facturaNumero ?? '—'))}
          </Link>
        ) : a.fianzaId ? (
          <Link className="mono" to={`/fianzas/${a.fianzaId}`}>{f.fianza}</Link>
        ) : (
          '—'
        ),
    },
    { key: 'importe', label: f.importe, align: 'right', render: (a: CobroAsignacion) => <Money amount={a.importe} currency={a.facturaMoneda ?? x.monedaRecibida} tone={a.vigente ? undefined : 'muted'} /> },
    { key: 'recibido', label: f.recibidoAsignacion, align: 'right', render: (a: CobroAsignacion) => (a.importeRecibido === null ? '—' : <Money amount={a.importeRecibido} currency={x.monedaRecibida} tone="muted" />) },
    { key: 'cambio', label: f.cambioAsignacion, align: 'right', render: (a: CobroAsignacion) => (a.tipoCambioValor === null ? '—' : <span className="mono">{formatearNumero(a.tipoCambioValor)}</span>) },
    { key: 'vigente', label: tc.estado, render: (a: CobroAsignacion) => (a.vigente ? <Badge tone="success" dot>{f.vigente}</Badge> : <Badge status="anulada">{f.anulada}</Badge>) },
  ];

  return (
    <>
      <PageHeader
        eyebrow={textos.navegacion.grupos.finanzas}
        title={<Money amount={x.importeRecibido} currency={x.monedaRecibida} strong />}
        description={`${x.clienteNombre ?? ''} · ${formatearFecha(x.fechaEfectiva)}`}
        breadcrumbs={[{ label: textos.navegacion.modulos.cobros.etiqueta, onClick: () => navegar('/cobros') }, { label: f.titulo }]}
      >
        {anulado ? <Badge status="anulada">{f.anulado}</Badge> : <Badge tone="success" dot>{textosFinanzas.cobros.estados.Registrado}</Badge>}
        <Button
          variant="secondary"
          icon="file-text"
          onClick={() => {
            setFalloPdf(false);
            abrirPdf(`/cobros/${x.id}/pdf`).catch(() => setFalloPdf(true));
          }}
        >
          {f.recibo}
        </Button>
        {puede(identidad, ACCIONES.cobroRegistrar) && !anulado && (
          <Button variant="danger" icon="ban" onClick={() => setAnulando(true)}>
            {f.anular}
          </Button>
        )}
      </PageHeader>

      <div style={{ display: 'grid', gap: 'var(--space-7)' }}>
        {falloPdf && <Alert tone="danger" title={tc.pdfError} />}
        {anulado && (
          <Alert tone="info" title={f.anulado}>
            {x.anuladoAt ? formatearMarca(x.anuladoAt) : ''} · {f.motivoAnulacion}: {x.motivoAnulacion}
          </Alert>
        )}
        <Card title={f.titulo}>
          <KeyValueList
            columns={3}
            items={[
              { label: f.cliente, value: x.clienteNombre },
              { label: f.fecha, value: formatearFecha(x.fechaEfectiva), mono: true },
              { label: f.forma, value: x.formaPagoNombre },
              { label: f.cuenta, value: x.cuentaReceptoraNombre },
              { label: f.referencia, value: x.referencia, mono: true },
              { label: f.cambio, value: `${formatearNumero(x.tipoCambioValor)} · ${formatearFecha(x.tipoCambioFecha)}`, mono: true },
              { label: f.saldoUsado, value: <Money amount={x.saldoFavorUsado} currency={x.monedaRecibida} /> },
              { label: f.excedente, value: <Money amount={x.excedente} currency={x.monedaRecibida} /> },
              { label: f.asignado, value: <Money amount={x.totalAsignado} currency={x.monedaRecibida} /> },
              ...(esPositivo(x.importeDevuelto) ? [{ label: f.devuelto, value: <Money amount={x.importeDevuelto} currency={x.monedaRecibida} /> }] : []),
            ]}
          />
        </Card>
        <Card title={f.asignaciones} padded={false}>
          <DataTable columns={columnas} rows={x.asignaciones} empty={tc.vacio} />
        </Card>
      </div>

      <ConfirmarDestructivo
        open={anulando}
        accion={f.anular}
        consecuencia={f.anularConsecuencia}
        onConfirmar={async (motivo) => {
          await anular.mutateAsync(motivo);
        }}
        onCerrar={() => setAnulando(false)}
      />
    </>
  );
}
