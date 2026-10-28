import { useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Badge } from '../../../design-system/components/core/Badge';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { IconButton } from '../../../design-system/components/core/IconButton';
import { DataTable, type DataTableColumn } from '../../../design-system/components/data/DataTable';
import { KeyValueList } from '../../../design-system/components/data/KeyValueList';
import { Money } from '../../../design-system/components/data/Money';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { Input } from '../../../design-system/components/forms/Input';
import { Menu } from '../../../design-system/components/navigation/Menu';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { formatearNumero } from '../../../dominio/dinero';
import { formatearFecha } from '../../../dominio/fechas';
import { textos } from '../../../i18n/textos';
import SinPermiso from '../../../paginas/SinPermiso';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { abrirPdf, useDuplicarFactura, useEmitirFactura, useFactura, useReferenciaEfactura } from '../api';
import { CargandoTabla, FalloCarga, InsigniaFila } from '../componentes/Comunes';
import { AjusteDiferenciaCambio, AnularFactura, ConvertirProforma, ModificarLineas, RehacerEnOtraMoneda } from '../componentes/DialogosFactura';
import { esPositivo } from '../decimal';
import { numeroDocumento } from '../logica';
import { textosFinanzas } from '../textos';
import type { FacturaDuplicadaParecida, FacturaLinea } from '../tipos';

const t = textosFinanzas.facturas;
const f = t.ficha;
const tc = textosFinanzas.comun;

type Dialogo = 'modificar' | 'anular' | 'rehacer' | 'convertir' | 'ajuste' | null;

export default function FacturaFicha() {
  const { id = '' } = useParams();
  const identidad = useSesion((s) => s.identidad);
  const navegar = useNavigate();
  const similares = (useLocation().state as { similares?: FacturaDuplicadaParecida[] } | null)?.similares ?? [];
  const consulta = useFactura(id);
  const emitir = useEmitirFactura(id);
  const duplicar = useDuplicarFactura(id);
  const efactura = useReferenciaEfactura(id);
  const [dialogo, setDialogo] = useState<Dialogo>(null);
  const [referencia, setReferencia] = useState<string | null>(null);
  const [falloPdf, setFalloPdf] = useState(false);

  if (!puede(identidad, ACCIONES.facturaVer)) return <SinPermiso />;
  const cabecera = (
    <PageHeader
      eyebrow={textos.navegacion.grupos.finanzas}
      title={t.titulo}
      breadcrumbs={[{ label: textos.navegacion.modulos.facturas.etiqueta, onClick: () => navegar('/facturas') }, { label: t.titulo }]}
    />
  );
  if (consulta.isPending)
    return (
      <>
        {cabecera}
        <CargandoTabla />
      </>
    );
  if (consulta.isError)
    return (
      <>
        {cabecera}
        <FalloCarga error={consulta.error} reintentar={() => void consulta.refetch()} />
      </>
    );

  const x = consulta.data;
  const esProforma = x.tipoSerie === 'Proforma';
  const titulo = x.numero === null ? t.borrador : numeroDocumento(x.serieNumero, x.numero);
  const puedeEmitir = puede(identidad, ACCIONES.facturaEmitir);
  const puedeGestionar = puede(identidad, ACCIONES.facturaAnular);
  const puedeCobrar = puede(identidad, ACCIONES.cobroRegistrar);
  const emitida = x.estado === 'Emitida';
  const conPendiente = emitida && !esProforma && esPositivo(x.pendiente);
  const referenciaActual = referencia ?? x.referenciaEfactura ?? '';

  const columnas: DataTableColumn[] = [
    { key: 'concepto', label: f.lineaConcepto, render: (l: FacturaLinea) => l.concepto },
    { key: 'tipo', label: f.lineaTipo, render: (l: FacturaLinea) => l.tipo },
    { key: 'cantidad', label: f.lineaCantidad, align: 'right', render: (l: FacturaLinea) => <span className="mono">{formatearNumero(l.cantidad)}</span> },
    { key: 'precio', label: f.lineaPrecio, align: 'right', render: (l: FacturaLinea) => <Money amount={l.precioUnitario} currency={x.moneda} /> },
    { key: 'itbis', label: f.lineaItbis, align: 'right', render: (l: FacturaLinea) => (l.tipoItbisNombre ? <Money amount={l.itbisImporte} currency={x.moneda} tone="muted" /> : '—') },
    {
      key: 'importe',
      label: f.lineaImporte,
      align: 'right',
      render: (l: FacturaLinea) => <Money amount={l.importe} currency={x.moneda} tone={l.tipo === 'Descuento' || l.tipo === 'Retencion' ? 'danger' : undefined} />,
    },
  ];

  const acciones = [
    ...(puedeGestionar && emitida && !esProforma ? [{ label: f.modificar, icon: 'pencil', onClick: () => setDialogo('modificar') }] : []),
    ...(puedeGestionar
      ? [{ label: f.duplicar, icon: 'copy', onClick: () => duplicar.mutate({}, { onSuccess: (r) => navegar(`/facturas/${r.factura.id}`, { state: { similares: r.similares } }) }) }]
      : []),
    ...(puedeGestionar && emitida && !esProforma ? [{ label: f.rehacer, icon: 'repeat', onClick: () => setDialogo('rehacer') }] : []),
    ...(puedeGestionar && esProforma && emitida ? [{ label: f.convertir, icon: 'file-check', onClick: () => setDialogo('convertir') }] : []),
    ...(puedeCobrar && conPendiente && x.moneda === 'USD' ? [{ label: f.cerrarDiferencia, icon: 'scale', onClick: () => setDialogo('ajuste') }] : []),
    ...(puedeGestionar && (emitida || x.estado === 'Borrador') ? [{ label: f.anular, icon: 'ban', danger: true, onClick: () => setDialogo('anular') }] : []),
  ];

  return (
    <>
      <PageHeader
        eyebrow={textos.navegacion.grupos.finanzas}
        title={<span className="mono">{titulo}</span>}
        description={x.concepto}
        breadcrumbs={[{ label: textos.navegacion.modulos.facturas.etiqueta, onClick: () => navegar('/facturas') }, { label: titulo }]}
      >
        <InsigniaFila tipo={esProforma ? 'Proforma' : 'Factura'} estado={emitida && !esProforma ? x.estadoCobro : x.estado} />
        {x.vencida && <Badge status="vencida" />}
        <IconButton
          icon="file-text"
          label={tc.pdf}
          onClick={() => {
            setFalloPdf(false);
            abrirPdf(`/facturas/${x.id}/pdf`).catch(() => setFalloPdf(true));
          }}
        />
        {acciones.length > 0 && (
          <Menu
            align="right"
            trigger={
              <Button variant="secondary" iconRight="chevron-down">
                {f.mas}
              </Button>
            }
            items={acciones}
          />
        )}
        {x.estado === 'Borrador' && puedeEmitir && (
          <Button icon="send" disabled={emitir.isPending} onClick={() => emitir.mutate({})}>
            {emitir.isPending ? f.emitiendo : f.emitir}
          </Button>
        )}
        {conPendiente && puedeCobrar && (
          <Button icon="banknote" onClick={() => navegar(`/cuentas?clienteId=${x.clienteId}&moneda=${x.moneda}`)}>
            {f.cobrar}
          </Button>
        )}
      </PageHeader>

      <div style={{ display: 'grid', gap: 'var(--space-7)' }}>
        {falloPdf && <Alert tone="danger" title={tc.pdfError} />}
        {emitir.isError && (
          <div>
            <ErrorAlerta error={emitir.error} titulo={f.emitirError} />
            <p style={{ margin: '8px 0 0', color: 'var(--text-muted)' }}>{f.emitirAyuda}</p>
          </div>
        )}
        {duplicar.isError && <ErrorAlerta error={duplicar.error} titulo={f.duplicar} />}
        {similares.length > 0 && (
          <Alert tone="warning" title={f.similares}>
            {f.similaresCuerpo} {similares.map((s) => (s.numero === null ? s.concepto : `N.º ${s.numero}`)).join(', ')}
          </Alert>
        )}
        {x.retencionSinComprobante && (
          <Alert tone="warning" title={f.retencionAviso}>
            {f.retencionAvisoCuerpo}
          </Alert>
        )}

        <Card title={f.resumen}>
          <KeyValueList
            columns={3}
            items={[
              { label: f.cliente, value: x.clienteNombre },
              { label: f.unidad, value: x.unidadNombre },
              { label: f.moneda, value: x.moneda, mono: true },
              { label: f.emision, value: x.fechaEmision ? formatearFecha(x.fechaEmision) : null, mono: true },
              { label: f.vencimiento, value: formatearFecha(x.fechaVencimiento), mono: true },
              {
                label: f.cambioCongelado,
                value: x.tipoCambioValor !== null ? `${formatearNumero(x.tipoCambioValor)} · ${x.tipoCambioFecha ? formatearFecha(x.tipoCambioFecha) : ''}` : null,
                mono: true,
              },
            ]}
          />
          {puedeEmitir && x.estado !== 'Anulada' && (
            <form
              style={{ display: 'flex', gap: 12, alignItems: 'end', marginTop: 'var(--space-7)' }}
              onSubmit={(e) => {
                e.preventDefault();
                efactura.mutate(referenciaActual.trim());
              }}
            >
              <Input label={f.efactura} value={referenciaActual} hint={f.efacturaAyuda} onChange={(e) => setReferencia(e.target.value)} style={{ flex: 1 }} />
              <Button type="submit" variant="secondary" disabled={efactura.isPending}>
                {f.efacturaGuardar}
              </Button>
            </form>
          )}
          {!puedeEmitir && x.referenciaEfactura && <KeyValueList columns={1} style={{ marginTop: 16 }} items={[{ label: f.efactura, value: x.referenciaEfactura, mono: true }]} />}
          {efactura.isError && <ErrorAlerta error={efactura.error} titulo={f.efactura} />}
        </Card>

        <Card title={f.lineas} padded={false}>
          <DataTable columns={columnas} rows={x.lineas} empty={tc.vacio} />
          <div style={{ display: 'flex', justifyContent: 'flex-end', padding: 'var(--space-9)' }}>
            <dl className="ar-totals">
              <div>
                <dt>{f.subtotal}</dt>
                <dd><Money amount={x.subtotal} currency={x.moneda} /></dd>
              </div>
              <div>
                <dt>{f.itbis}</dt>
                <dd><Money amount={x.itbis} currency={x.moneda} /></dd>
              </div>
              <div>
                <dt>{f.retencion}</dt>
                <dd><Money amount={x.retencion} currency={x.moneda} tone="muted" /></dd>
              </div>
              <div className="is-total">
                <dt>{f.total}</dt>
                <dd><Money amount={x.total} currency={x.moneda} strong /></dd>
              </div>
              <div>
                <dt>{f.neto}</dt>
                <dd><Money amount={x.netoACobrar} currency={x.moneda} /></dd>
              </div>
              <div>
                <dt>{f.cobrado}</dt>
                <dd><Money amount={x.cobrado} currency={x.moneda} /></dd>
              </div>
              <div>
                <dt>{f.ajustado}</dt>
                <dd><Money amount={x.ajustado} currency={x.moneda} tone="muted" /></dd>
              </div>
              <div className="is-total">
                <dt>{f.pendiente}</dt>
                <dd><Money amount={x.pendiente} currency={x.moneda} strong tone={x.vencida ? 'danger' : undefined} /></dd>
              </div>
            </dl>
          </div>
        </Card>
      </div>

      {dialogo === 'modificar' && <ModificarLineas factura={x} onCerrar={() => setDialogo(null)} />}
      <AnularFactura factura={x} abierto={dialogo === 'anular'} onCerrar={() => setDialogo(null)} />
      {dialogo === 'rehacer' && <RehacerEnOtraMoneda factura={x} onCerrar={() => setDialogo(null)} onCreado={(nuevo) => navegar(`/facturas/${nuevo}`)} />}
      {dialogo === 'convertir' && <ConvertirProforma factura={x} onCerrar={() => setDialogo(null)} onCreado={(nuevo) => navegar(`/facturas/${nuevo}`)} />}
      {dialogo === 'ajuste' && <AjusteDiferenciaCambio factura={x} onCerrar={() => setDialogo(null)} />}
    </>
  );
}
