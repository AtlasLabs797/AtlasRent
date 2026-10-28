import { useEffect, useMemo, useRef, useState, type JSX } from 'react';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Button } from '../../../design-system/components/core/Button';
import { Money } from '../../../design-system/components/data/Money';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { Drawer } from '../../../design-system/components/feedback/Drawer';
import { DatePicker } from '../../../design-system/components/forms/DatePicker';
import { Input } from '../../../design-system/components/forms/Input';
import { Select } from '../../../design-system/components/forms/Select';
import { SegmentedControl } from '../../../design-system/components/navigation/SegmentedControl';
import { formatearFecha } from '../../../dominio/fechas';
import { formatearImporte, formatearNumero } from '../../../dominio/dinero';
import { useCuentasReceptoras, useFormasPago, useRegistrarCobro, useSimularCobro, useTipoCambioActual } from '../api';
import { aDecimal, entradaADecimal, equivalenteEnMonedaFactura, sumarImportes } from '../decimal';
import {
  claveParaIntento,
  cuerpoCobro,
  huellaCuerpo,
  lineasDeSimulacion,
  necesitaCambio,
  numeroDocumento,
  validarFormulario,
  type FormularioCobro,
  type LineaAsignacion,
  type ModoAsignacion,
} from '../logica';
import { textosFinanzas } from '../textos';
import type { Cobro, CobroSimulacion, CuentaFila, Moneda } from '../tipos';
import { esSinPermiso, fechaHoyNegocio } from './Comunes';

const t = textosFinanzas.panel;
const tc = textosFinanzas.comun;

export interface PanelCobroProps {
  seleccion: { clienteId: string; contratoId: string; filas: CuentaFila[] };
  onCerrar: () => void;
  onRegistrado: (cobro: Cobro) => void;
}

/** Importe tecleado de 6 decimales sin ceros de relleno por encima de 2 ("59.500000" -> "59.50"). */
function cambioParaMostrar(valor: string): string {
  return aDecimal(valor, 6).replace(/(\.\d{2}\d*?)0+$/, '$1');
}

export function PanelCobro({ seleccion, onCerrar, onRegistrado }: PanelCobroProps): JSX.Element {
  const filas = seleccion.filas;
  const monedasDestino = useMemo(() => [...new Set(filas.map((f) => f.moneda))], [filas]);
  const mismaMoneda = monedasDestino.length === 1 ? monedasDestino[0]! : null;

  const [form, setForm] = useState<FormularioCobro>(() => ({
    fechaEfectiva: fechaHoyNegocio(),
    monedaRecibida: filas[0]!.moneda,
    importeRecibido: mismaMoneda ? sumarImportes(filas.map((f) => f.pendiente ?? '0')) : '',
    formaPagoId: '',
    cuentaReceptoraId: '',
    referencia: '',
    usarSaldoAFavor: '',
    tipoCambioAplicado: '',
  }));
  const [modo, setModo] = useState<ModoAsignacion>('seleccionadas');
  const [lineas, setLineas] = useState<LineaAsignacion[] | null>(null);
  const [sim, setSim] = useState<{ datos: CobroSimulacion; huella: string } | null>(null);
  const intento = useRef<{ huella: string; clave: string } | null>(null);

  const formas = useFormasPago();
  const cuentas = useCuentasReceptoras();
  const simular = useSimularCobro();
  const registrar = useRegistrarCobro();

  const requiereCambio = necesitaCambio(form.monedaRecibida, monedasDestino);
  const cambioDia = useTipoCambioActual('DOP', requiereCambio && form.monedaRecibida === 'DOP');

  // Propone el cambio del día solo mientras no se haya tocado el campo.
  useEffect(() => {
    if (requiereCambio && cambioDia.data && form.tipoCambioAplicado === '') {
      setForm((f) => ({ ...f, tipoCambioAplicado: cambioParaMostrar(cambioDia.data.valor) }));
    }
  }, [requiereCambio, cambioDia.data, form.tipoCambioAplicado]);

  // Editar el formulario descarta el reparto: el reparto depende del importe, la moneda y el cambio.
  function cambiar(cambios: Partial<FormularioCobro>) {
    setForm((f) => ({ ...f, ...cambios }));
    if (!('referencia' in cambios) && !('formaPagoId' in cambios) && !('cuentaReceptoraId' in cambios)) setLineas(null);
  }

  const errores = validarFormulario(form, requiereCambio);
  const lineasEfectivas = lineas?.map((l) => ({ ...l, importe: entradaADecimal(l.importe) ?? '0.00' })) ?? null;
  const lineasInvalidas = lineas?.map((l) => entradaADecimal(l.importe) === null) ?? [];
  const hayLineaInvalida = lineasInvalidas.some(Boolean);
  const hayErrores = Object.keys(errores).length > 0;
  const cuerpo = hayErrores || hayLineaInvalida ? null : cuerpoCobro(form, seleccion, modo, lineasEfectivas, requiereCambio);
  const huella = cuerpo ? huellaCuerpo(cuerpo) : null;
  const desactualizada = sim !== null && sim.huella !== huella;

  function lanzarSimulacion() {
    if (!cuerpo) return;
    simular.mutate(cuerpo, {
      onSuccess: (datos) => {
        // Primera propuesta: sus líneas pasan a ser las editables y lo registrado será exactamente lo simulado.
        const nuevas = lineas ?? lineasDeSimulacion(datos.asignaciones);
        if (lineas === null) setLineas(nuevas);
        const efectivas = nuevas.map((l) => ({ ...l, importe: entradaADecimal(l.importe) ?? '0.00' }));
        setSim({ datos, huella: huellaCuerpo(cuerpoCobro(form, seleccion, modo, efectivas, requiereCambio)) });
      },
    });
  }

  function lanzarRegistro() {
    if (!cuerpo || !huella || desactualizada || sim === null) return;
    intento.current = claveParaIntento(intento.current, huella, () => crypto.randomUUID());
    registrar.mutate({ cuerpo, clave: intento.current.clave }, { onSuccess: onRegistrado });
  }

  function cambiarLinea(indice: number, importe: string) {
    setLineas((actuales) => actuales?.map((l, i) => (i === indice ? { ...l, importe } : l)) ?? null);
  }

  const catalogoDenegado = esSinPermiso(formas.error) || esSinPermiso(cuentas.error);
  const opcionesCuenta = (() => {
    const activas = (cuentas.data ?? []).filter((c) => c.activa);
    const deMoneda = activas.filter((c) => c.moneda === form.monedaRecibida);
    return (deMoneda.length > 0 ? deMoneda : activas).map((c) => ({ value: c.id, label: `${c.nombre} · ${c.moneda}` }));
  })();
  const msgError = (campo: keyof FormularioCobro) => (errores[campo] ? t.errores[errores[campo]!] : undefined);

  const equivalente =
    requiereCambio && form.monedaRecibida === 'DOP' && !errores.importeRecibido && !errores.tipoCambioAplicado
      ? equivalenteEnMonedaFactura(entradaADecimal(form.importeRecibido)!, entradaADecimal(form.tipoCambioAplicado, 6)!)
      : null;

  return (
    <Drawer
      open
      width={680}
      title={t.titulo}
      description={t.descripcion}
      onClose={onCerrar}
      footer={
        <>
          <Button variant="ghost" onClick={onCerrar}>
            {tc.cancelar}
          </Button>
          <Button variant="secondary" disabled={!cuerpo || simular.isPending} onClick={lanzarSimulacion}>
            {simular.isPending ? t.simulando : t.simular}
          </Button>
          <Button disabled={!cuerpo || sim === null || desactualizada || registrar.isPending} onClick={lanzarRegistro}>
            {registrar.isPending ? t.registrando : t.registrar}
          </Button>
        </>
      }
    >
      <div style={{ display: 'grid', gap: 'var(--space-7)' }}>
        <section aria-label={t.documentos}>
          <div className="ar-overline" style={{ marginBottom: 8 }}>
            {t.documentos}
          </div>
          {filas.map((f) => (
            <div key={f.id} style={{ display: 'flex', justifyContent: 'space-between', gap: 12, padding: '6px 0', borderBottom: '1px solid var(--border-subtle)' }}>
              <span className="mono">
                {f.tipo === 'Fianza' ? textosFinanzas.cuentas.tipos.Fianza : numeroDocumento(f.serieNumero, f.numero)} · {f.cliente}
              </span>
              <Money amount={f.pendiente ?? '0'} currency={f.moneda} />
            </div>
          ))}
        </section>

        {catalogoDenegado && (
          <Alert tone="warning" title={tc.sinCatalogoTitulo}>
            {/* TODO(A#): lectura de catálogos con cobro.registrar; hoy solo configuracion.editar. */}
            {tc.sinCatalogoCuerpo}
          </Alert>
        )}

        <div>
          <div className="ar-overline" style={{ marginBottom: 8 }}>
            {t.modo}
          </div>
          <SegmentedControl
            value={modo}
            options={[
              { value: 'seleccionadas', label: t.modoSeleccionadas },
              { value: 'fifo', label: t.modoFifo },
            ]}
            onChange={(v) => {
              setModo(v as ModoAsignacion);
              setLineas(null);
            }}
          />
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--space-6)' }}>
          <DatePicker label={t.fecha} required value={form.fechaEfectiva} onChange={(e) => cambiar({ fechaEfectiva: e.target.value })} error={msgError('fechaEfectiva')} />
          <Select
            label={t.monedaRecibida}
            value={form.monedaRecibida}
            options={['USD', 'DOP']}
            onChange={(e) => cambiar({ monedaRecibida: e.target.value as Moneda, tipoCambioAplicado: '' })}
          />
          <Input
            label={t.importeRecibido}
            required
            inputMode="decimal"
            suffix={form.monedaRecibida}
            value={form.importeRecibido}
            onChange={(e) => cambiar({ importeRecibido: e.target.value })}
            error={msgError('importeRecibido')}
          />
          {requiereCambio ? (
            <Input
              label={t.tipoCambio}
              required
              inputMode="decimal"
              value={form.tipoCambioAplicado}
              onChange={(e) => cambiar({ tipoCambioAplicado: e.target.value })}
              error={msgError('tipoCambioAplicado')}
              hint={cambioDia.data ? t.tipoCambioAyuda.replace('{fecha}', formatearFecha(cambioDia.data.fecha)) : cambioDia.isError ? t.tipoCambioSinDia : undefined}
            />
          ) : (
            <Input
              label={t.usarSaldo}
              inputMode="decimal"
              value={form.usarSaldoAFavor}
              onChange={(e) => cambiar({ usarSaldoAFavor: e.target.value })}
              error={msgError('usarSaldoAFavor')}
              hint={sim ? t.usarSaldoAyuda.replace('{importe}', formatearImporte(sim.datos.saldoAFavorDisponible, form.monedaRecibida)) : undefined}
            />
          )}
          <Select
            label={t.formaPago}
            required
            placeholder="—"
            value={form.formaPagoId}
            options={(formas.data ?? []).filter((x) => x.activa).map((x) => ({ value: x.id, label: x.nombre }))}
            onChange={(e) => cambiar({ formaPagoId: e.target.value })}
            error={msgError('formaPagoId')}
          />
          <Select
            label={t.cuentaReceptora}
            required
            placeholder="—"
            value={form.cuentaReceptoraId}
            options={opcionesCuenta}
            onChange={(e) => cambiar({ cuentaReceptoraId: e.target.value })}
            error={msgError('cuentaReceptoraId')}
          />
          <Input label={t.referencia} value={form.referencia} onChange={(e) => cambiar({ referencia: e.target.value })} />
          {requiereCambio && (
            <Input
              label={t.usarSaldo}
              inputMode="decimal"
              value={form.usarSaldoAFavor}
              onChange={(e) => cambiar({ usarSaldoAFavor: e.target.value })}
              error={msgError('usarSaldoAFavor')}
            />
          )}
        </div>

        {equivalente && (
          <Alert tone="info" title={t.conversionNota}>
            <span className="mono">
              {t.conversion
                .replace('{recibido}', formatearImporte(entradaADecimal(form.importeRecibido)!, 'DOP'))
                .replace('{cambio}', formatearNumero(entradaADecimal(form.tipoCambioAplicado, 6)!))
                .replace('{equivalente}', formatearImporte(equivalente, 'USD'))}
            </span>
          </Alert>
        )}

        {simular.isError && <ErrorAlerta error={simular.error} titulo={t.errorSimular} />}
        {desactualizada && (
          <Alert tone="warning">{t.desactualizada}</Alert>
        )}

        {sim && lineas && (
          <section aria-label={t.reparto}>
            <div className="ar-overline" style={{ marginBottom: 4 }}>
              {t.reparto}
            </div>
            <p style={{ margin: '0 0 8px', color: 'var(--text-muted)' }}>{t.repartoAyuda}</p>
            {sim.datos.asignaciones.map((a, i) => {
              const linea = lineas[i];
              if (!linea) return null;
              return (
                <div key={(a.facturaId ?? a.fianzaId) ?? i} className="ar-alloc__row">
                  <div style={{ minWidth: 0 }}>
                    <div className="mono">{a.facturaId ? textosFinanzas.facturas.ficha.numero + ' ' + (a.numero ?? '—') : textosFinanzas.cuentas.tipos.Fianza}</div>
                    {a.importeRecibido !== null && a.tipoCambioValor !== null && (
                      <div style={{ font: 'var(--type-caption)', color: 'var(--text-muted)' }}>
                        {t.equivale.replace('{importe}', formatearImporte(a.importeRecibido, form.monedaRecibida)).replace('{cambio}', formatearNumero(a.tipoCambioValor))}
                      </div>
                    )}
                  </div>
                  <div style={{ textAlign: 'right' }}>
                    <div style={{ font: 'var(--type-fine)', color: 'var(--text-subtle)' }}>{t.pendienteAntes}</div>
                    <Money amount={a.pendienteAntes} currency={a.monedaDestino} />
                  </div>
                  <Input
                    aria-label={`${t.aplicar} ${a.facturaId ? a.numero : textosFinanzas.cuentas.tipos.Fianza}`}
                    inputMode="decimal"
                    suffix={a.monedaDestino}
                    value={linea.importe}
                    onChange={(e) => cambiarLinea(i, e.target.value)}
                    error={lineasInvalidas[i] ? t.errores.invalido : undefined}
                  />
                </div>
              );
            })}
            <dl className="ar-totals" style={{ marginTop: 12 }}>
              <div>
                <dt>{t.totalAsignado}</dt>
                <dd>
                  <Money amount={sim.datos.totalAsignado} currency={form.monedaRecibida} />
                </dd>
              </div>
              <div>
                <dt>{t.saldoUsado}</dt>
                <dd>
                  <Money amount={sim.datos.saldoFavorUsado} currency={form.monedaRecibida} />
                </dd>
              </div>
              <div className="is-total">
                <dt>{t.excedente}</dt>
                <dd>
                  <Money amount={sim.datos.excedente} currency={form.monedaRecibida} strong tone="success" />
                </dd>
              </div>
            </dl>
            {sim.datos.deudaEnOtraMoneda.map((d) => (
              <Alert key={d.moneda} tone="info" style={{ marginTop: 12 }}>
                {t.otraMoneda.replace('{moneda}', d.moneda).replace('{importe}', formatearImporte(d.pendiente, d.moneda)).replace('{n}', String(d.facturas))}
              </Alert>
            ))}
          </section>
        )}

        {registrar.isError && <ErrorAlerta error={registrar.error} titulo={t.errorRegistrar} />}
      </div>
    </Drawer>
  );
}
