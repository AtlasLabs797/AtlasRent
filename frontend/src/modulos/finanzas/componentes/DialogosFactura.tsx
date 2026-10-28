import { useState, type JSX } from 'react';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { ConfirmarDestructivo } from '../../../componentes/ConfirmarDestructivo';
import { Button } from '../../../design-system/components/core/Button';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { Dialog } from '../../../design-system/components/feedback/Dialog';
import { Drawer } from '../../../design-system/components/feedback/Drawer';
import { Input } from '../../../design-system/components/forms/Input';
import { Select } from '../../../design-system/components/forms/Select';
import { Textarea } from '../../../design-system/components/forms/Textarea';
import { formatearFecha } from '../../../dominio/fechas';
import { formatearImporte } from '../../../dominio/dinero';
import {
  useAjusteCambio,
  useAnularFactura,
  useConvertirProforma,
  useModificarFactura,
  useRehacerMoneda,
  useSeries,
  useSimularAnulacion,
} from '../api';
import { aDecimal, entradaADecimal, esPositivo } from '../decimal';
import { textosFinanzas } from '../textos';
import type { Factura, FacturaLinea, Moneda } from '../tipos';

const t = textosFinanzas.facturas;
const tc = textosFinanzas.comun;

/** Anular: primero se calcula el efecto (cobros afectados) y solo entonces se pide el motivo. */
export function AnularFactura({ factura, abierto, onCerrar }: { factura: Factura; abierto: boolean; onCerrar: () => void }): JSX.Element | null {
  const simulacion = useSimularAnulacion(factura.id, abierto);
  const anular = useAnularFactura(factura.id);
  if (!abierto) return null;
  const a = t.anular;
  if (simulacion.isError) {
    return (
      <Dialog open title={a.accion} onClose={onCerrar} footer={<Button variant="ghost" onClick={onCerrar}>{tc.cerrar}</Button>}>
        <ErrorAlerta error={simulacion.error} titulo={a.errorSimular} />
      </Dialog>
    );
  }
  if (!simulacion.data) {
    return (
      <Dialog open title={a.accion} onClose={onCerrar}>
        {a.calculando}
      </Dialog>
    );
  }
  const s = simulacion.data;
  const lista = (titulo: string, cobros: typeof s.cobrosQueSeAnulan) =>
    cobros.length > 0 && (
      <div>
        <div className="ar-overline">{titulo}</div>
        <ul style={{ margin: '4px 0 0', paddingLeft: 18 }}>
          {cobros.map((c) => (
            <li key={c.cobroId} className="mono">
              {a.cobroLinea
                .replace('{fecha}', formatearFecha(c.fechaEfectiva))
                .replace('{recibido}', formatearImporte(c.importeRecibido, c.moneda))
                .replace('{parte}', formatearImporte(c.importeDeEstaFactura, c.moneda))}
            </li>
          ))}
        </ul>
      </div>
    );
  return (
    <ConfirmarDestructivo
      open
      accion={a.accion}
      consecuencia={
        <span style={{ display: 'grid', gap: 12 }}>
          <span>{a.consecuencia}</span>
          {s.numeroSeConserva && <span>{a.numeroSeConserva}</span>}
          {s.comprobanteFiscalSeAnula && <span>{a.comprobanteSeAnula}</span>}
          {lista(a.cobrosAnulan, s.cobrosQueSeAnulan)}
          {lista(a.cobrosTocan, s.cobrosQueSeToCan)}
        </span>
      }
      onConfirmar={async (motivo) => {
        await anular.mutateAsync({ motivo });
      }}
      onCerrar={onCerrar}
    />
  );
}

/** Edita concepto, cantidad y precio de las líneas; manda solo las que cambian. */
export function ModificarLineas({ factura, onCerrar }: { factura: Factura; onCerrar: () => void }): JSX.Element {
  const m = t.modificar;
  const modificar = useModificarFactura(factura.id);
  const [edicion, setEdicion] = useState(() =>
    factura.lineas.map((l) => ({ concepto: l.concepto, cantidad: aDecimal(l.cantidad, 4), precio: aDecimal(l.precioUnitario) })),
  );
  const [aviso, setAviso] = useState<string | null>(null);

  const invalida = edicion.map((e) => entradaADecimal(e.cantidad, 4) === null || entradaADecimal(e.precio) === null || !e.concepto.trim());
  const cambiadas = factura.lineas.filter((l, i) => {
    const e = edicion[i]!;
    return e.concepto !== l.concepto || e.cantidad !== aDecimal(l.cantidad, 4) || e.precio !== aDecimal(l.precioUnitario);
  });

  function guardar() {
    if (cambiadas.length === 0) return setAviso(m.sinCambios);
    setAviso(null);
    modificar.mutate(
      cambiadas.map((l: FacturaLinea) => {
        const e = edicion[factura.lineas.indexOf(l)]!;
        return {
          lineaId: l.id,
          tipo: l.tipo,
          concepto: e.concepto.trim(),
          cantidad: entradaADecimal(e.cantidad, 4)!,
          precioUnitario: entradaADecimal(e.precio)!,
          tipoItbisId: l.tipoItbisId,
          tipoRetencionId: l.tipoRetencionId,
        };
      }),
      { onSuccess: onCerrar },
    );
  }

  return (
    <Drawer
      open
      width={720}
      title={m.titulo}
      description={m.descripcion}
      onClose={onCerrar}
      footer={
        <>
          <Button variant="ghost" onClick={onCerrar}>{tc.cancelar}</Button>
          <Button disabled={invalida.some(Boolean) || modificar.isPending} onClick={guardar}>
            {modificar.isPending ? m.guardando : m.guardar}
          </Button>
        </>
      }
    >
      <div style={{ display: 'grid', gap: 'var(--space-7)' }}>
        {factura.lineas.map((l, i) => (
          <div key={l.id} style={{ display: 'grid', gridTemplateColumns: '2fr 1fr 1fr', gap: 12 }}>
            <Input
              label={t.ficha.lineaConcepto}
              value={edicion[i]!.concepto}
              onChange={(e) => setEdicion((a) => a.map((x, j) => (j === i ? { ...x, concepto: e.target.value } : x)))}
            />
            <Input
              label={t.ficha.lineaCantidad}
              inputMode="decimal"
              value={edicion[i]!.cantidad}
              error={entradaADecimal(edicion[i]!.cantidad, 4) === null ? m.cantidadInvalida : undefined}
              onChange={(e) => setEdicion((a) => a.map((x, j) => (j === i ? { ...x, cantidad: e.target.value } : x)))}
            />
            <Input
              label={t.ficha.lineaPrecio}
              inputMode="decimal"
              suffix={factura.moneda}
              value={edicion[i]!.precio}
              error={entradaADecimal(edicion[i]!.precio) === null ? m.precioInvalido : undefined}
              onChange={(e) => setEdicion((a) => a.map((x, j) => (j === i ? { ...x, precio: e.target.value } : x)))}
            />
          </div>
        ))}
        {aviso && <Alert tone="info">{aviso}</Alert>}
        {modificar.isError && <ErrorAlerta error={modificar.error} titulo={m.error} />}
      </div>
    </Drawer>
  );
}

function SelectorSerie({ valor, onChange, opcional }: { valor: string; onChange: (v: string) => void; opcional?: boolean }): JSX.Element {
  const series = useSeries();
  if (series.isError) {
    return <Alert tone="warning" title={tc.sinCatalogoTitulo}>{tc.sinCatalogoCuerpo}</Alert>;
  }
  const opciones = (series.data ?? []).filter((s) => s.activa).map((s) => ({ value: s.id, label: `${s.numero}${s.prefijo ? ' · ' + s.prefijo : ''} · ${s.tipo}` }));
  return (
    <Select
      label={opcional ? t.convertir.serie : t.rehacer.serie}
      required={!opcional}
      placeholder="—"
      value={valor}
      options={opcional ? [{ value: '', label: '—' }, ...opciones] : opciones}
      onChange={(e) => onChange(e.target.value)}
    />
  );
}

export function RehacerEnOtraMoneda({ factura, onCerrar, onCreado }: { factura: Factura; onCerrar: () => void; onCreado: (id: string) => void }): JSX.Element {
  const r = t.rehacer;
  const rehacer = useRehacerMoneda(factura.id);
  const otra: Moneda = factura.moneda === 'USD' ? 'DOP' : 'USD';
  const [moneda, setMoneda] = useState<Moneda>(otra);
  const [serieId, setSerieId] = useState('');
  const [total, setTotal] = useState('');
  const [motivo, setMotivo] = useState('');
  const importe = entradaADecimal(total);
  const valido = serieId !== '' && importe !== null && esPositivo(importe) && motivo.trim() !== '';
  return (
    <Dialog
      open
      title={r.titulo}
      description={r.descripcion}
      onClose={onCerrar}
      footer={
        <>
          <Button variant="ghost" onClick={onCerrar}>{tc.cancelar}</Button>
          <Button
            variant="danger"
            disabled={!valido || rehacer.isPending}
            onClick={() => rehacer.mutate({ moneda, serieId, total: importe, motivo: motivo.trim() }, { onSuccess: (x) => onCreado(x.nueva.id) })}
          >
            {r.confirmar}
          </Button>
        </>
      }
    >
      <div style={{ display: 'grid', gap: 'var(--space-6)' }}>
        <Select label={r.moneda} value={moneda} options={['USD', 'DOP']} onChange={(e) => setMoneda(e.target.value as Moneda)} />
        <SelectorSerie valor={serieId} onChange={setSerieId} />
        <Input label={r.total} required inputMode="decimal" suffix={moneda} value={total} onChange={(e) => setTotal(e.target.value)} />
        <Textarea label={r.motivo} required rows={3} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
        {rehacer.isError && <ErrorAlerta error={rehacer.error} titulo={r.titulo} />}
      </div>
    </Dialog>
  );
}

export function ConvertirProforma({ factura, onCerrar, onCreado }: { factura: Factura; onCerrar: () => void; onCreado: (id: string) => void }): JSX.Element {
  const c = t.convertir;
  const convertir = useConvertirProforma(factura.id);
  const [serieId, setSerieId] = useState('');
  return (
    <Dialog
      open
      title={c.titulo}
      description={c.descripcion}
      onClose={onCerrar}
      footer={
        <>
          <Button variant="ghost" onClick={onCerrar}>{tc.cancelar}</Button>
          <Button disabled={convertir.isPending} onClick={() => convertir.mutate(serieId ? { serieId } : {}, { onSuccess: (x) => onCreado(x.factura.id) })}>
            {c.confirmar}
          </Button>
        </>
      }
    >
      <div style={{ display: 'grid', gap: 'var(--space-6)' }}>
        <SelectorSerie valor={serieId} onChange={setSerieId} opcional />
        {convertir.isError && <ErrorAlerta error={convertir.error} titulo={c.titulo} />}
      </div>
    </Dialog>
  );
}

export function AjusteDiferenciaCambio({ factura, onCerrar }: { factura: Factura; onCerrar: () => void }): JSX.Element {
  const a = t.ajuste;
  const ajuste = useAjusteCambio();
  const [cambio, setCambio] = useState('');
  const [motivo, setMotivo] = useState('');
  const tcv = entradaADecimal(cambio, 6);
  const valido = tcv !== null && esPositivo(tcv) && motivo.trim() !== '';
  return (
    <Dialog
      open
      title={a.titulo}
      description={a.descripcion}
      onClose={onCerrar}
      footer={
        <>
          <Button variant="ghost" onClick={onCerrar}>{tc.cancelar}</Button>
          <Button
            disabled={!valido || ajuste.isPending}
            onClick={() => ajuste.mutate({ facturaId: factura.id, tipoCambioValor: tcv!, motivo: motivo.trim() }, { onSuccess: onCerrar })}
          >
            {a.confirmar}
          </Button>
        </>
      }
    >
      <div style={{ display: 'grid', gap: 'var(--space-6)' }}>
        <Input label={a.cambio} required inputMode="decimal" value={cambio} onChange={(e) => setCambio(e.target.value)} />
        <Textarea label={a.motivo} required rows={3} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
        {ajuste.isError && <ErrorAlerta error={ajuste.error} titulo={a.titulo} />}
      </div>
    </Dialog>
  );
}

