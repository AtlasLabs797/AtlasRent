import { useState } from 'react';
import { useNavigate, useParams } from 'react-router';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Badge } from '../../../design-system/components/core/Badge';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { DataTable, type DataTableColumn } from '../../../design-system/components/data/DataTable';
import { DepositSummary, type DepositSummaryProps } from '../../../design-system/components/data/DepositSummary';
import { Money } from '../../../design-system/components/data/Money';
import { Dialog } from '../../../design-system/components/feedback/Dialog';
import { Input } from '../../../design-system/components/forms/Input';
import { Textarea } from '../../../design-system/components/forms/Textarea';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { formatearFecha } from '../../../dominio/fechas';
import { textos } from '../../../i18n/textos';
import SinPermiso from '../../../paginas/SinPermiso';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { useAccionFianza, useFianza } from '../api';
import { CargandoTabla, FalloCarga } from '../componentes/Comunes';
import { compararImportes, entradaADecimal, sumarImportes } from '../decimal';
import { motivoObligatorio } from '../logica';
import { textosFinanzas } from '../textos';
import type { EstadoFianza, FianzaDevolucion, FianzaResumen } from '../tipos';

const f = textosFinanzas.fianzas.ficha;
const d = textosFinanzas.fianzas.devolver;
const tc = textosFinanzas.comun;

const ESTADO_DS: Record<EstadoFianza, DepositSummaryProps['status']> = {
  Pendiente: 'pendiente',
  Parcial: 'parcial',
  Retenida: 'retenida',
  EnLiquidacion: 'devolucion',
  Devuelta: 'devuelta',
  RetenidaParcial: 'retenida',
};

function Devolver({ fianza, onCerrar }: { fianza: FianzaResumen; onCerrar: () => void }) {
  const devolver = useAccionFianza(fianza.id, 'devolver');
  const [importe, setImporte] = useState('');
  const [descontado, setDescontado] = useState('0');
  const [motivo, setMotivo] = useState('');
  const [fecha, setFecha] = useState('');
  const i = entradaADecimal(importe || '0');
  const dd = entradaADecimal(descontado || '0');
  const validos = i !== null && dd !== null && compararImportes(sumarImportes([i, dd]), '0') > 0;
  const exigeMotivo = validos && motivoObligatorio(i, dd, fianza.saldo);
  const falta = exigeMotivo && motivo.trim() === '';
  return (
    <Dialog
      open
      title={d.titulo}
      description={d.descripcion}
      onClose={onCerrar}
      footer={
        <>
          <Button variant="ghost" onClick={onCerrar}>{tc.cancelar}</Button>
          <Button
            disabled={!validos || falta || devolver.isPending}
            onClick={() =>
              devolver.mutate(
                { importe: i, importeDescontado: dd, ...(motivo.trim() ? { motivo: motivo.trim() } : {}), ...(fecha ? { fecha } : {}) },
                { onSuccess: onCerrar },
              )
            }
          >
            {d.confirmar}
          </Button>
        </>
      }
    >
      <div style={{ display: 'grid', gap: 'var(--space-6)' }}>
        <p style={{ margin: 0, color: 'var(--text-muted)' }}>
          {f.saldo}: <Money amount={fianza.saldo} currency={fianza.moneda} />
        </p>
        <Input label={d.importe} inputMode="decimal" suffix={fianza.moneda} value={importe} onChange={(e) => setImporte(e.target.value)} error={i === null ? 'Importe no válido' : undefined} />
        <Input label={d.descontado} inputMode="decimal" suffix={fianza.moneda} value={descontado} onChange={(e) => setDescontado(e.target.value)} error={dd === null ? 'Importe no válido' : undefined} />
        <Textarea label={d.motivo} required={exigeMotivo} rows={3} value={motivo} onChange={(e) => setMotivo(e.target.value)} hint={exigeMotivo ? d.motivoObligatorio : undefined} />
        <Input label={d.fecha} type="date" value={fecha} onChange={(e) => setFecha(e.target.value)} />
        {devolver.isError && <ErrorAlerta error={devolver.error} titulo={d.titulo} />}
      </div>
    </Dialog>
  );
}

export default function FianzaFicha() {
  const { id = '' } = useParams();
  const identidad = useSesion((s) => s.identidad);
  const navegar = useNavigate();
  const consulta = useFianza(id);
  const retener = useAccionFianza(id, 'retener');
  const liquidar = useAccionFianza(id, 'liquidar');
  const [devolviendo, setDevolviendo] = useState(false);

  if (!puede(identidad, ACCIONES.facturaVer)) return <SinPermiso />;
  const cabecera = (
    <PageHeader
      eyebrow={textos.navegacion.grupos.finanzas}
      title={textosFinanzas.fianzas.titulo}
      breadcrumbs={[{ label: textos.navegacion.modulos.fianzas.etiqueta, onClick: () => navegar('/fianzas') }, { label: textosFinanzas.fianzas.titulo }]}
    />
  );
  if (consulta.isPending) return <>{cabecera}<CargandoTabla /></>;
  if (consulta.isError) return <>{cabecera}<FalloCarga error={consulta.error} reintentar={() => void consulta.refetch()} /></>;

  const x = consulta.data.fianza;
  const devoluciones = consulta.data.devoluciones;
  const puedeMover = puede(identidad, ACCIONES.fianzaMover);
  const cerrada = x.estado === 'Devuelta' || x.estado === 'RetenidaParcial';
  const ultima = devoluciones[devoluciones.length - 1];

  const columnas: DataTableColumn[] = [
    { key: 'fecha', label: f.fecha, render: (r: FianzaDevolucion) => <span className="mono">{formatearFecha(r.fecha)}</span> },
    { key: 'importe', label: f.devuelto, align: 'right', render: (r: FianzaDevolucion) => <Money amount={r.importe} currency={x.moneda} /> },
    { key: 'descontado', label: f.descontado, align: 'right', render: (r: FianzaDevolucion) => <Money amount={r.importeDescontado} currency={x.moneda} tone="muted" /> },
    { key: 'motivo', label: f.motivo, render: (r: FianzaDevolucion) => r.motivo ?? '—' },
  ];

  return (
    <>
      <PageHeader
        eyebrow={textos.navegacion.grupos.finanzas}
        title={textosFinanzas.fianzas.titulo}
        description={<span className="mono">{x.contratoId}</span>}
        breadcrumbs={[{ label: textos.navegacion.modulos.fianzas.etiqueta, onClick: () => navegar('/fianzas') }, { label: textosFinanzas.fianzas.titulo }]}
      >
        <Button variant="secondary" icon="wallet" onClick={() => navegar(`/cuentas?contratoId=${x.contratoId}`)}>{f.verCuentas}</Button>
        {x.estado === 'RetenidaParcial' && <Badge tone="info" dot>{f.retenidaParcial}</Badge>}
        {puedeMover && (x.estado === 'Pendiente' || x.estado === 'Parcial') && (
          <Button variant="secondary" disabled={retener.isPending} onClick={() => retener.mutate(undefined)}>{f.retener}</Button>
        )}
        {puedeMover && !cerrada && x.estado !== 'EnLiquidacion' && (
          <Button variant="secondary" disabled={liquidar.isPending} onClick={() => liquidar.mutate(undefined)}>{f.liquidar}</Button>
        )}
        {puedeMover && !cerrada && (
          <Button icon="undo-2" onClick={() => setDevolviendo(true)}>{f.devolver}</Button>
        )}
      </PageHeader>

      <div style={{ display: 'grid', gap: 'var(--space-7)' }}>
        {retener.isError && <ErrorAlerta error={retener.error} titulo={f.retener} />}
        {liquidar.isError && <ErrorAlerta error={liquidar.error} titulo={f.liquidar} />}
        {/* Los importes llegan como número JSON de 2 decimales; el componente del DS los suma solo para mostrar. */}
        <DepositSummary
          required={Number(x.importeExigido)}
          count={x.numeroFianzas}
          collected={Number(x.cobrado)}
          currency={x.moneda}
          contract={`${x.clienteNombre} · ${x.unidadNombre}`}
          status={ESTADO_DS[x.estado]}
          deductions={devoluciones.filter((r) => Number(r.importeDescontado) > 0).map((r) => ({ reason: r.motivo ?? f.descontado, amount: Number(r.importeDescontado) }))}
          {...(devoluciones.length > 0 ? { returned: Number(x.devuelto) } : {})}
          {...(ultima ? { returnedAt: formatearFecha(ultima.fecha) } : {})}
        />
        <Card title={f.devoluciones} padded={false}>
          <DataTable columns={columnas} rows={devoluciones} empty={f.sinDevoluciones} />
        </Card>
        {puedeMover && !cerrada && <p style={{ margin: 0, color: 'var(--text-muted)' }}>{f.cobrarAyuda}</p>}
      </div>

      {devolviendo && <Devolver fianza={x} onCerrar={() => setDevolviendo(false)} />}
    </>
  );
}
