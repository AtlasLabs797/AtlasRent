import { useCallback, useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { clienteHttp } from '../../../api/cliente';
import { Badge } from '../../../design-system/components/core/Badge';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { Money } from '../../../design-system/components/data/Money';
import { RestrictedValue } from '../../../design-system/components/data/RestrictedValue';
import { DataTable, type DataTableColumn } from '../../../design-system/components/data/DataTable';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { Checkbox } from '../../../design-system/components/forms/Checkbox';
import { DatePicker } from '../../../design-system/components/forms/DatePicker';
import { Input } from '../../../design-system/components/forms/Input';
import { Select } from '../../../design-system/components/forms/Select';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { Pagination } from '../../../design-system/components/navigation/Pagination';
import { formatearFecha } from '../../../dominio/fechas';
import { textos } from '../../../i18n/textos';
import SinPermiso from '../../../paginas/SinPermiso';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { useCuentas, abrirPdf } from '../api';
import { CargandoTabla, FalloCarga, InsigniaFila, RejillaFiltros, SelectorCliente, SelectoresInmuebleUnidad, useReinicio } from '../componentes/Comunes';
import { PanelCobro } from '../componentes/PanelCobro';
import {
  consultaCuentas,
  evaluarSeleccion,
  filtrosDesdeParametros,
  numeroDocumento,
  parametrosDeFiltros,
  type FiltrosCuentas,
} from '../logica';
import { textosFinanzas } from '../textos';
import type { Cobro, CuentaFila, Pagina } from '../tipos';

const t = textosFinanzas.cuentas;
const tc = textosFinanzas.comun;
const PAGE_SIZE = 25;

interface Subempresa {
  id: string;
  nombre: string;
  propietaria: boolean;
  facturadora: boolean;
  activa: boolean;
}

export default function Cuentas() {
  const identidad = useSesion((s) => s.identidad);
  const navegar = useNavigate();
  const [parametros, setParametros] = useSearchParams();
  const filtros = useMemo(() => filtrosDesdeParametros(parametros), [parametros]);
  const [marcadas, setMarcadas] = useState<Map<string, CuentaFila>>(new Map());
  const [panelAbierto, setPanelAbierto] = useState(false);
  const [registrado, setRegistrado] = useState<Cobro | null>(null);

  const cuentas = useCuentas(consultaCuentas(filtros, PAGE_SIZE));
  const esAdmin = puede(identidad, ACCIONES.configuracionEditar);
  const subempresas = useQuery({
    queryKey: ['finanzas', 'subempresas'],
    enabled: esAdmin,
    queryFn: async () => (await clienteHttp.get<Pagina<Subempresa>>('/config/subempresas', { params: { pageSize: 200 } })).data.data,
  });

  const huellaFiltros = parametrosDeFiltros({ ...filtros, page: 1 }).toString();
  const limpiarSeleccion = useCallback(() => setMarcadas(new Map()), []);
  useReinicio(huellaFiltros, limpiarSeleccion);

  if (!puede(identidad, ACCIONES.facturaVer)) return <SinPermiso />;

  const puedeCobrar = puede(identidad, ACCIONES.cobroRegistrar);
  const puedeVerTotales = puede(identidad, ACCIONES.informeVer);
  const seleccion = evaluarSeleccion([...marcadas.values()]);

  function cambiar(cambios: Partial<FiltrosCuentas>) {
    setParametros(parametrosDeFiltros({ ...filtros, page: 1, ...cambios }), { replace: true });
  }

  function alternar(fila: CuentaFila) {
    setMarcadas((actuales) => {
      const siguiente = new Map(actuales);
      if (siguiente.has(fila.id)) siguiente.delete(fila.id);
      else siguiente.set(fila.id, fila);
      return siguiente;
    });
  }

  const columnas: DataTableColumn[] = [
    {
      key: 'sel',
      label: '',
      width: 44,
      render: (f: CuentaFila) =>
        f.cobrable && puedeCobrar ? (
          <Checkbox
            aria-label={t.columnas.seleccionar.replace('{doc}', f.tipo === 'Fianza' ? t.tipos.Fianza : numeroDocumento(f.serieNumero, f.numero))}
            checked={marcadas.has(f.id)}
            onChange={() => alternar(f)}
            onClick={(e) => e.stopPropagation()}
          />
        ) : null,
    },
    {
      key: 'documento',
      label: t.columnas.documento,
      render: (f: CuentaFila) => (
        <span style={{ display: 'inline-flex', gap: 8, alignItems: 'center' }}>
          <Link
            className="mono"
            to={f.tipo === 'Fianza' ? `/fianzas/${f.id}` : `/facturas/${f.id}`}
            onClick={(e) => e.stopPropagation()}
          >
            {f.tipo === 'Fianza' ? t.tipos.Fianza : numeroDocumento(f.serieNumero, f.numero)}
          </Link>
          {f.tipo !== 'Factura' && <Badge tone="neutral">{t.tipos[f.tipo]}</Badge>}
          {f.comprobanteTipo && f.comprobanteNumero !== null && (
            <span className="mono" style={{ color: 'var(--text-muted)' }}>
              {f.comprobanteTipo} {f.comprobanteNumero}
            </span>
          )}
        </span>
      ),
    },
    { key: 'fecha', label: t.columnas.fecha, render: (f: CuentaFila) => <span className="mono">{f.fecha ? formatearFecha(f.fecha) : '—'}</span> },
    {
      key: 'cliente',
      label: t.columnas.cliente,
      render: (f: CuentaFila) => (
        <div>
          <div>{f.cliente ?? '—'}</div>
          <div style={{ font: 'var(--type-caption)', color: 'var(--text-muted)' }}>{[f.unidad, f.concepto].filter(Boolean).join(' · ')}</div>
        </div>
      ),
    },
    { key: 'total', label: t.columnas.total, align: 'right', render: (f: CuentaFila) => (f.total === null ? '—' : <Money amount={f.total} currency={f.moneda} />) },
    { key: 'retencion', label: t.columnas.retencion, align: 'right', render: (f: CuentaFila) => (f.retencion === null ? '—' : <Money amount={f.retencion} currency={f.moneda} tone="muted" />) },
    { key: 'cobrado', label: t.columnas.cobrado, align: 'right', render: (f: CuentaFila) => (f.cobrado === null ? '—' : <Money amount={f.cobrado} currency={f.moneda} />) },
    {
      key: 'pendiente',
      label: t.columnas.pendiente,
      align: 'right',
      render: (f: CuentaFila) => (f.pendiente === null ? '—' : <Money amount={f.pendiente} currency={f.moneda} strong tone={f.vencida ? 'danger' : undefined} />),
    },
    {
      key: 'vence',
      label: t.columnas.vence,
      align: 'right',
      render: (f: CuentaFila) =>
        f.vencimiento ? (
          <div>
            <div className="mono">{formatearFecha(f.vencimiento)}</div>
            {f.vencida && f.diasVencida ? <div style={{ font: 'var(--type-caption)', color: 'var(--danger)' }}>{t.diasVencida.replace('{n}', String(f.diasVencida))}</div> : null}
          </div>
        ) : (
          '—'
        ),
    },
    {
      key: 'estado',
      label: t.columnas.estado,
      render: (f: CuentaFila) => (
        <span style={{ display: 'inline-flex', gap: 6, flexWrap: 'wrap' }}>
          <InsigniaFila tipo={f.tipo} estado={f.estado} />
          {f.vencida && <Badge status="vencida" />}
          {f.modificadaTrasEnvio && <Badge tone="warning">{tc.modificadaTrasEnvio}</Badge>}
        </span>
      ),
    },
    { key: 'envio', label: t.columnas.envio, render: (f: CuentaFila) => f.estadoEnvio ?? '—' },
  ];

  const datos = cuentas.data;
  const subs = subempresas.data ?? [];

  return (
    <>
      <PageHeader
        eyebrow={textos.navegacion.grupos.finanzas}
        title={t.titulo}
        description={t.descripcion}
        breadcrumbs={[{ label: textos.navegacion.grupos.finanzas }, { label: textos.navegacion.modulos.cuentas.etiqueta }]}
      >
        {puedeCobrar && (
          <Button icon="banknote" disabled={marcadas.size === 0 || !seleccion.ok} onClick={() => setPanelAbierto(true)}>
            {t.registrarCobro}
          </Button>
        )}
      </PageHeader>

      {registrado && (
        <Alert
          tone="success"
          title={t.cobroRegistrado}
          action={
            <span style={{ display: 'flex', gap: 8 }}>
              <Button size="sm" variant="secondary" onClick={() => navegar(`/cobros/${registrado.id}`)}>
                {t.verCobro}
              </Button>
              <Button size="sm" variant="ghost" onClick={() => void abrirPdf(`/cobros/${registrado.id}/pdf`)}>
                {t.reciboPdf}
              </Button>
            </span>
          }
        />
      )}

      <Card title={t.filtros.titulo}>
        <RejillaFiltros>
          <SelectorCliente value={filtros.clienteId} onChange={(clienteId) => cambiar({ clienteId })} />
          <SelectoresInmuebleUnidad inmuebleId={filtros.inmuebleId} unidadId={filtros.unidadId} onChange={cambiar} />
          <Select label={tc.moneda} value={filtros.moneda} options={[{ value: '', label: tc.todas }, 'USD', 'DOP']} onChange={(e) => cambiar({ moneda: e.target.value as FiltrosCuentas['moneda'] })} />
          <Select
            label={t.filtros.estado}
            value={filtros.estado}
            options={[{ value: '', label: tc.todos }, ...Object.entries(t.estados).map(([value, label]) => ({ value, label }))]}
            onChange={(e) => cambiar({ estado: e.target.value })}
          />
          <DatePicker label={tc.desde} value={filtros.desde} onChange={(e) => cambiar({ desde: e.target.value })} />
          <DatePicker label={tc.hasta} value={filtros.hasta} onChange={(e) => cambiar({ hasta: e.target.value })} />
          <Input label={t.filtros.anio} inputMode="numeric" value={filtros.anio} onChange={(e) => cambiar({ anio: e.target.value.replace(/\D/g, '').slice(0, 4) })} />
          {esAdmin && (
            <>
              <Select
                label={t.filtros.propietaria}
                value={filtros.propietariaId}
                options={[{ value: '', label: tc.todas }, ...subs.filter((s) => s.propietaria).map((s) => ({ value: s.id, label: s.nombre }))]}
                onChange={(e) => cambiar({ propietariaId: e.target.value })}
              />
              <Select
                label={t.filtros.facturadora}
                value={filtros.facturadoraId}
                options={[{ value: '', label: tc.todas }, ...subs.filter((s) => s.facturadora).map((s) => ({ value: s.id, label: s.nombre }))]}
                onChange={(e) => cambiar({ facturadoraId: e.target.value })}
              />
            </>
          )}
        </RejillaFiltros>
        {/* TODO(A#): propietaria y facturadora salen de /config/subempresas, que solo lee configuracion.editar. */}
        {!esAdmin && <p style={{ margin: '12px 0 0', color: 'var(--text-subtle)' }}>{t.filtros.sinCatalogo}</p>}
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--space-9)', marginTop: 'var(--space-7)', alignItems: 'center' }}>
          <Checkbox label={t.filtros.vencidas} checked={filtros.vencidas} onChange={(e) => cambiar({ vencidas: e.target.checked })} />
          <Checkbox label={t.filtros.noEnviadas} checked={filtros.noEnviadas} onChange={(e) => cambiar({ noEnviadas: e.target.checked })} />
          <Checkbox label={t.filtros.envioFallido} checked={filtros.envioFallido} onChange={(e) => cambiar({ envioFallido: e.target.checked })} />
          <Checkbox label={t.filtros.modificadasTrasEnvio} checked={filtros.modificadasTrasEnvio} onChange={(e) => cambiar({ modificadasTrasEnvio: e.target.checked })} />
          <Checkbox label={t.filtros.incluirProformas} checked={filtros.incluirProformas} onChange={(e) => cambiar({ incluirProformas: e.target.checked })} />
          <Checkbox label={t.filtros.incluirFianzas} checked={filtros.incluirFianzas} onChange={(e) => cambiar({ incluirFianzas: e.target.checked })} />
          <Button variant="ghost" size="sm" onClick={() => setParametros(new URLSearchParams(), { replace: true })}>
            {tc.limpiar}
          </Button>
        </div>
      </Card>

      <div style={{ height: 24 }} />

      {marcadas.size > 0 && !seleccion.ok && seleccion.motivo !== 'vacia' && (
        <>
          <Alert tone="warning">{t.seleccion[seleccion.motivo]}</Alert>
          <div style={{ height: 16 }} />
        </>
      )}

      <Card padded={false}>
        {cuentas.isPending ? (
          <CargandoTabla />
        ) : cuentas.isError ? (
          <div style={{ padding: 'var(--space-9)' }}>
            <FalloCarga error={cuentas.error} reintentar={() => void cuentas.refetch()} />
          </div>
        ) : (
          <>
            <DataTable
              columns={columnas}
              rows={datos?.data ?? []}
              empty={tc.vacio}
              selectedKey={undefined}
              onRowClick={(f: CuentaFila) => navegar(f.tipo === 'Fianza' ? `/fianzas/${f.id}` : `/facturas/${f.id}`)}
            />
            <Pagination page={datos?.page ?? 1} pageSize={PAGE_SIZE} total={datos?.total ?? 0} onChange={(page) => setParametros(parametrosDeFiltros({ ...filtros, page }), { replace: true })} />
          </>
        )}
      </Card>

      <div style={{ height: 24 }} />

      <Card title={t.totales.titulo}>
        {/* Sin informe.ver la API manda totales = null: se muestra el candado, nunca una suma hecha aquí. */}
        <p style={{ margin: '0 0 var(--space-6)', color: 'var(--text-muted)' }}>{t.totales.nota}</p>
        <RestrictedValue allowed={puedeVerTotales && datos?.totales != null} label={tc.restringido}>
          <table className="ar-table" aria-label={t.totales.titulo}>
            <thead>
              <tr>
                <th>{tc.moneda}</th>
                <th className="is-num">{t.columnas.total}</th>
                <th className="is-num">{t.columnas.retencion}</th>
                <th className="is-num">{t.columnas.cobrado}</th>
                <th className="is-num">{t.columnas.pendiente}</th>
              </tr>
            </thead>
            <tbody>
              {(datos?.totales ?? []).map((x) => (
                <tr key={x.moneda}>
                  <td className="mono">{x.moneda}</td>
                  <td className="is-num">
                    <Money amount={x.total} currency={x.moneda} />
                  </td>
                  <td className="is-num">
                    <Money amount={x.retencion} currency={x.moneda} tone="muted" />
                  </td>
                  <td className="is-num">
                    <Money amount={x.cobrado} currency={x.moneda} />
                  </td>
                  <td className="is-num">
                    <Money amount={x.pendiente} currency={x.moneda} strong />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </RestrictedValue>
      </Card>

      {panelAbierto && seleccion.ok && (
        <PanelCobro
          seleccion={seleccion}
          onCerrar={() => setPanelAbierto(false)}
          onRegistrado={(cobro) => {
            setPanelAbierto(false);
            setMarcadas(new Map());
            setRegistrado(cobro);
          }}
        />
      )}
    </>
  );
}

