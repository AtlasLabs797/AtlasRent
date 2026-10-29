import { useRef, useState, type JSX } from 'react';
import { useNavigate, useParams } from 'react-router';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { DataTable, type DataTableColumn } from '../../../design-system/components/data/DataTable';
import { Money } from '../../../design-system/components/data/Money';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { DatePicker } from '../../../design-system/components/forms/DatePicker';
import { Input } from '../../../design-system/components/forms/Input';
import { Select } from '../../../design-system/components/forms/Select';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { formatearNumero } from '../../../dominio/dinero';
import { textos } from '../../../i18n/textos';
import SinPermiso from '../../../paginas/SinPermiso';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { centimosATexto, centimosDeLinea } from '../../cotizaciones/calculo';
import { useActualizarBorrador, useCrearBorrador, useFactura, useLineaBorrador, useTiposImpuesto } from '../api';
import { CargandoTabla, FalloCarga, SelectorCliente, SelectorSerie, SelectoresInmuebleUnidad } from '../componentes/Comunes';
import { aDecimal, entradaADecimal } from '../decimal';
import { claveParaIntento } from '../logica';
import { textosFinanzas } from '../textos';
import { TIPOS_LINEA, type Factura, type FacturaLinea, type FacturaPeticion, type Moneda, type TipoLinea } from '../tipos';

const t = textosFinanzas.borrador;

function Cabecera({ inicial }: { inicial: Factura | null }): JSX.Element {
  const navegar = useNavigate();
  const crear = useCrearBorrador();
  const actualizar = useActualizarBorrador(inicial?.id ?? '');
  const [clienteId, setClienteId] = useState(inicial?.clienteId ?? '');
  const [inmuebleId, setInmuebleId] = useState('');
  const [unidadId, setUnidadId] = useState('');
  const [serieId, setSerieId] = useState(inicial?.serieId ?? '');
  const [moneda, setMoneda] = useState<Moneda>(inicial?.moneda ?? 'USD');
  const [vencimiento, setVencimiento] = useState(inicial?.fechaVencimiento ?? '');
  const [concepto, setConcepto] = useState(inicial?.concepto ?? '');
  const intento = useRef<{ huella: string; clave: string } | null>(null);

  const valido = clienteId !== '' && serieId !== '' && vencimiento !== '' && concepto.trim() !== '';
  const mutacion = inicial ? actualizar : crear;

  function guardar() {
    if (!valido) return;
    const cuerpo: FacturaPeticion = {
      clienteId,
      serieId,
      moneda,
      fechaVencimiento: vencimiento,
      concepto: concepto.trim(),
      // En edición la unidad y el contrato no se tocan: se reenvían tal cual.
      ...(inicial ? { ...(inicial.unidadId ? { unidadId: inicial.unidadId } : {}), ...(inicial.contratoId ? { contratoId: inicial.contratoId } : {}) } : unidadId ? { unidadId } : {}),
    };
    if (inicial) {
      actualizar.mutate(cuerpo);
      return;
    }
    intento.current = claveParaIntento(intento.current, JSON.stringify(cuerpo), () => crypto.randomUUID());
    crear.mutate({ cuerpo, clave: intento.current.clave }, { onSuccess: (f) => navegar(`/facturas/${f.id}/editar`, { replace: true }) });
  }

  return (
    <Card title={t.cabecera}>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(240px,1fr))', gap: 'var(--space-6)', alignItems: 'end' }}>
        <SelectorCliente etiqueta={t.cliente} required value={clienteId} onChange={setClienteId} />
        {inicial ? (
          <Input label={t.unidad} value={inicial.unidadNombre ?? '—'} readOnly />
        ) : (
          <SelectoresInmuebleUnidad
            inmuebleId={inmuebleId}
            unidadId={unidadId}
            onChange={(c) => {
              if (c.inmuebleId !== undefined) setInmuebleId(c.inmuebleId);
              if (c.unidadId !== undefined) setUnidadId(c.unidadId);
            }}
          />
        )}
        <SelectorSerie etiqueta={t.serie} valor={serieId} onChange={setSerieId} moneda={moneda} />
        <Select label={t.moneda} value={moneda} options={['USD', 'DOP']} onChange={(e) => setMoneda(e.target.value as Moneda)} />
        <DatePicker label={t.vencimiento} required value={vencimiento} onChange={(e) => setVencimiento(e.target.value)} />
        <Input id="borrador-concepto" label={t.concepto} required maxLength={300} value={concepto} onChange={(e) => setConcepto(e.target.value)} />
      </div>
      <div style={{ marginTop: 'var(--space-7)' }}>
        <Button disabled={!valido || mutacion.isPending} onClick={guardar}>
          {mutacion.isPending ? t.guardando : inicial ? t.guardar : t.crear}
        </Button>
      </div>
      {mutacion.isError && (
        <div style={{ marginTop: 16 }}>
          <ErrorAlerta error={mutacion.error} titulo={t.errorGuardar} />
        </div>
      )}
    </Card>
  );
}

interface FormLinea {
  tipo: TipoLinea;
  concepto: string;
  cantidad: string;
  precio: string;
  tipoItbisId: string;
  tipoRetencionId: string;
}

const LINEA_VACIA: FormLinea = { tipo: 'Servicio', concepto: '', cantidad: '1', precio: '', tipoItbisId: '', tipoRetencionId: '' };

/** El descuento se teclea en positivo y viaja en negativo: la API exige el signo en la línea. */
const signo = (tipo: TipoLinea) => (tipo === 'Descuento' ? '-' : '');

function Lineas({ factura }: { factura: Factura }): JSX.Element {
  const itbis = useTiposImpuesto('itbis');
  const retenciones = useTiposImpuesto('retencion');
  const mutar = useLineaBorrador(factura.id);
  const [form, setForm] = useState<FormLinea>(LINEA_VACIA);
  const [editando, setEditando] = useState<FacturaLinea | null>(null);

  const esRetencion = form.tipo === 'Retencion';
  const cantidad = esRetencion ? '1.00' : entradaADecimal(form.cantidad, 4);
  const precio = esRetencion ? '0.00' : entradaADecimal(form.precio);
  const previa = cantidad !== null && precio !== null ? centimosDeLinea(cantidad, precio) : null;
  const valido =
    form.concepto.trim() !== '' && cantidad !== null && precio !== null && (!esRetencion || form.tipoRetencionId !== '');

  function reiniciar() {
    setForm(LINEA_VACIA);
    setEditando(null);
  }

  function guardar() {
    if (!valido || cantidad === null || precio === null) return;
    mutar.mutate(
      {
        ...(editando ? { lineaId: editando.id } : {}),
        cuerpo: {
          tipo: form.tipo,
          concepto: form.concepto.trim(),
          cantidad,
          precioUnitario: `${signo(form.tipo)}${precio}`,
          tipoItbisId: !esRetencion && form.tipoItbisId ? form.tipoItbisId : null,
          tipoRetencionId: esRetencion ? form.tipoRetencionId : null,
        },
      },
      { onSuccess: reiniciar },
    );
  }

  function editar(l: FacturaLinea) {
    setEditando(l);
    setForm({
      tipo: (TIPOS_LINEA as readonly string[]).includes(l.tipo) ? (l.tipo as TipoLinea) : 'Servicio',
      concepto: l.concepto,
      cantidad: aDecimal(l.cantidad, 4),
      precio: aDecimal(l.precioUnitario).replace('-', ''),
      tipoItbisId: l.tipoItbisId ?? '',
      tipoRetencionId: l.tipoRetencionId ?? '',
    });
  }

  const columnas: DataTableColumn[] = [
    { key: 'concepto', label: textosFinanzas.facturas.ficha.lineaConcepto, render: (l: FacturaLinea) => l.concepto },
    { key: 'tipo', label: textosFinanzas.facturas.ficha.lineaTipo, render: (l: FacturaLinea) => l.tipo },
    { key: 'cantidad', label: textosFinanzas.facturas.ficha.lineaCantidad, align: 'right', render: (l: FacturaLinea) => <span className="mono">{formatearNumero(l.cantidad)}</span> },
    { key: 'precio', label: textosFinanzas.facturas.ficha.lineaPrecio, align: 'right', render: (l: FacturaLinea) => <Money amount={l.precioUnitario} currency={factura.moneda} /> },
    { key: 'itbis', label: textosFinanzas.facturas.ficha.lineaItbis, align: 'right', render: (l: FacturaLinea) => (l.tipoItbisNombre ? <Money amount={l.itbisImporte} currency={factura.moneda} tone="muted" /> : '—') },
    { key: 'importe', label: textosFinanzas.facturas.ficha.lineaImporte, align: 'right', render: (l: FacturaLinea) => <Money amount={l.importe} currency={factura.moneda} /> },
    {
      key: 'acciones',
      label: '',
      align: 'right',
      render: (l: FacturaLinea) => (
        <span style={{ display: 'inline-flex', gap: 4 }}>
          <Button size="sm" variant="ghost" onClick={() => editar(l)}>{t.editar}</Button>
          <Button size="sm" variant="ghost" disabled={mutar.isPending} onClick={() => mutar.mutate({ lineaId: l.id, borrar: true })}>{t.quitar}</Button>
        </span>
      ),
    },
  ];

  return (
    <Card title={t.lineas} padded={false}>
      <DataTable columns={columnas} rows={factura.lineas} empty={t.sinLineas} />
      <div style={{ display: 'flex', justifyContent: 'flex-end', padding: 'var(--space-9)' }}>
        <dl className="ar-totals" aria-label={t.totalApi}>
          <div>
            <dt>{textosFinanzas.facturas.ficha.subtotal}</dt>
            <dd><Money amount={factura.subtotal} currency={factura.moneda} /></dd>
          </div>
          <div>
            <dt>{textosFinanzas.facturas.ficha.itbis}</dt>
            <dd><Money amount={factura.itbis} currency={factura.moneda} /></dd>
          </div>
          <div className="is-total">
            <dt>{t.total}</dt>
            <dd><Money amount={factura.total} currency={factura.moneda} strong /></dd>
          </div>
        </dl>
      </div>
      <div style={{ padding: 'var(--space-9)', borderTop: '1px solid var(--border-subtle)', display: 'grid', gap: 'var(--space-6)' }}>
        <div className="ar-overline">{editando ? t.editarLinea : t.nuevaLinea}</div>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(180px,1fr))', gap: 'var(--space-6)', alignItems: 'end' }}>
          <Select label={t.tipo} value={form.tipo} options={TIPOS_LINEA.map((x) => ({ value: x, label: x }))} onChange={(e) => setForm((f) => ({ ...f, tipo: e.target.value as TipoLinea }))} />
          <Input id="linea-concepto" label={textosFinanzas.facturas.ficha.lineaConcepto} required maxLength={300} value={form.concepto} onChange={(e) => setForm((f) => ({ ...f, concepto: e.target.value }))} />
          {!esRetencion && (
            <>
              <Input
                label={t.cantidad}
                inputMode="decimal"
                value={form.cantidad}
                error={cantidad === null ? t.cantidadInvalida : undefined}
                onChange={(e) => setForm((f) => ({ ...f, cantidad: e.target.value }))}
              />
              <Input
                label={t.precio}
                inputMode="decimal"
                suffix={factura.moneda}
                value={form.precio}
                error={form.precio !== '' && precio === null ? t.precioInvalido : undefined}
                onChange={(e) => setForm((f) => ({ ...f, precio: e.target.value }))}
              />
            </>
          )}
          {!esRetencion && (
            <Select
              label={t.itbis}
              value={form.tipoItbisId}
              options={[{ value: '', label: t.sinImpuesto }, ...(itbis.data ?? []).map((x) => ({ value: x.id, label: `${x.nombre} (${formatearNumero(x.porcentaje)} %)` }))]}
              onChange={(e) => setForm((f) => ({ ...f, tipoItbisId: e.target.value }))}
            />
          )}
          {esRetencion && (
            <Select
              label={t.retencion}
              required
              placeholder="—"
              value={form.tipoRetencionId}
              options={(retenciones.data ?? []).map((x) => ({ value: x.id, label: `${x.nombre} (${formatearNumero(x.porcentaje)} %)` }))}
              onChange={(e) => setForm((f) => ({ ...f, tipoRetencionId: e.target.value }))}
            />
          )}
        </div>
        {previa !== null && !esRetencion && (
          <p style={{ margin: 0, color: 'var(--text-muted)' }} className="mono">
            {t.previa.replace('{importe}', `${signo(form.tipo)}${centimosATexto(previa)} ${factura.moneda}`)}
          </p>
        )}
        <div style={{ display: 'flex', gap: 8 }}>
          <Button disabled={!valido || mutar.isPending} onClick={guardar}>
            {editando ? t.guardarLinea : t.anadir}
          </Button>
          {editando && <Button variant="ghost" onClick={reiniciar}>{t.cancelarEdicion}</Button>}
        </div>
        {mutar.isError && <ErrorAlerta error={mutar.error} titulo={t.errorLinea} />}
      </div>
    </Card>
  );
}

/** Alta y edición de una factura en borrador (`factura.borrador`, también el empleado). */
export default function FacturaBorrador() {
  const { id = '' } = useParams();
  const identidad = useSesion((s) => s.identidad);
  const navegar = useNavigate();
  const consulta = useFactura(id);

  if (!puede(identidad, ACCIONES.facturaBorrador)) return <SinPermiso />;
  const titulo = id ? t.tituloEditar : t.tituloNueva;
  const cabecera = (
    <PageHeader
      eyebrow={textos.navegacion.grupos.finanzas}
      title={titulo}
      description={t.descripcion}
      breadcrumbs={[{ label: textos.navegacion.modulos.facturas.etiqueta, onClick: () => navegar('/facturas') }, { label: titulo }]}
    >
      {id && (
        <Button variant="secondary" onClick={() => navegar(`/facturas/${id}`)}>
          {t.ver}
        </Button>
      )}
    </PageHeader>
  );
  if (id && consulta.isPending) return <>{cabecera}<CargandoTabla /></>;
  if (id && consulta.isError) return <>{cabecera}<FalloCarga error={consulta.error} reintentar={() => void consulta.refetch()} /></>;

  const factura = id ? (consulta.data ?? null) : null;
  if (factura && factura.estado !== 'Borrador') {
    return (
      <>
        {cabecera}
        <Alert tone="warning">{t.soloBorrador}</Alert>
      </>
    );
  }

  return (
    <>
      {cabecera}
      <div style={{ display: 'grid', gap: 'var(--space-7)' }}>
        <Cabecera key={factura?.id ?? 'nueva'} inicial={factura} />
        {factura && <Lineas factura={factura} />}
      </div>
    </>
  );
}
