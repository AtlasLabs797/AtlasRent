import { useState } from 'react';
import { useNavigate, useParams } from 'react-router';
import { aErrorApi } from '../../../api/errores';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { ConfirmarDestructivo } from '../../../componentes/ConfirmarDestructivo';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { DataTable } from '../../../design-system/components/data/DataTable';
import { KeyValueList } from '../../../design-system/components/data/KeyValueList';
import { Money } from '../../../design-system/components/data/Money';
import { Timeline } from '../../../design-system/components/data/Timeline';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { Skeleton } from '../../../design-system/components/feedback/Skeleton';
import { DatePicker } from '../../../design-system/components/forms/DatePicker';
import { Input } from '../../../design-system/components/forms/Input';
import { Select } from '../../../design-system/components/forms/Select';
import { Textarea } from '../../../design-system/components/forms/Textarea';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { Stepper } from '../../../design-system/components/navigation/Stepper';
import { Tabs } from '../../../design-system/components/navigation/Tabs';
import { Drawer } from '../../../design-system/components/feedback/Drawer';
import { formatearFecha, formatearMarca } from '../../../dominio/fechas';
import { textos } from '../../../i18n/textos';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { BadgeEstado, abrirPdf } from '../../operacion/Comunes';
import { hoyNegocio } from '../../operacion/fechas';
import { ESTADOS_CONTRATO, REGLAS_RENOVACION, etiquetaResultadoSolicitud, resultadoSolicitud, type ReglaRenovacionApi } from '../../operacion/etiquetas';
import { AvisoBloqueo } from '../../reservas/AvisoBloqueo';
import { useCambioContrato, useContrato, usePlantillas, useTransicion, type ContratoDetalle, type VersionContrato } from '../api';
import { DialogoAccion } from '../DialogoAccion';
import { TabIncrementos } from '../TabIncrementos';
import { TabInventario } from '../TabInventario';
import { accionesDeEstado, cuerpoCambio, diasDePreaviso, fechaMinimaBaja, mesesDeRegla, pasosDelFlujo, puedeFinalizar, validarBaja, validarCambio, type FormularioCambio } from '../modelo';
import { textosContratos } from '../textos';

const t = textosContratos;
const f = t.ficha;
type Dialogo = 'enviar' | 'rechazar' | 'activar' | 'baja' | 'anular-baja' | 'finalizar' | 'liquidar' | 'cambio' | null;

export default function ContratoFicha() {
  const { id = '' } = useParams();
  const navegar = useNavigate();
  const identidad = useSesion((s) => s.identidad);
  const contrato = useContrato(id);
  const [pestana, setPestana] = useState('resumen');
  const [dialogo, setDialogo] = useState<Dialogo>(null);
  const [fallo, setFallo] = useState<unknown>(null);
  const c = contrato.data;

  if (contrato.isError) {
    const sinRegistro = aErrorApi(contrato.error).status === 404;
    return (
      <>
        <PageHeader eyebrow={textos.navegacion.modulos.contratos.etiqueta} title={sinRegistro ? f.noEncontrado : t.errorTitulo} />
        <ErrorAlerta error={contrato.error} titulo={t.errorTitulo} />
        <div style={{ marginTop: 'var(--space-9)' }}>
          <Button variant="secondary" onClick={() => navegar('/contratos')}>
            {f.volver}
          </Button>
        </div>
      </>
    );
  }
  if (!c) return <Skeleton height={160} />;

  const hoy = hoyNegocio();
  const a = accionesDeEstado(c.estado);
  const gestiona = puede(identidad, ACCIONES.contratoEstado);
  const redacta = puede(identidad, ACCIONES.contratoBorrador);
  const finalizable = puedeFinalizar(c.fechaBajaProgramada, hoy);
  const titulo = `${c.unidad ?? '—'} · ${c.cliente ?? '—'}`;

  return (
    <>
      <PageHeader
        eyebrow={textos.navegacion.modulos.contratos.etiqueta}
        title={titulo}
        description={<BadgeEstado estado={ESTADOS_CONTRATO[c.estado]} />}
        breadcrumbs={[{ label: textos.navegacion.modulos.contratos.etiqueta, onClick: () => navegar('/contratos') }, { label: titulo }]}
      >
        <Button
          variant="secondary"
          icon="file-down"
          onClick={() => void abrirPdf(`/contratos/${c.id}/pdf`).catch(setFallo)}
        >
          {f.pdf}
        </Button>
        {a.editar && redacta && (
          <Button variant="secondary" icon="pencil" onClick={() => navegar(`/contratos/${c.id}/editar`)}>
            {f.editar}
          </Button>
        )}
        {a.enviarAprobacion && redacta && <Button onClick={() => setDialogo('enviar')}>{f.enviar}</Button>}
        {a.decidirAprobacion && gestiona && (
          <>
            <Button variant="secondary" onClick={() => setDialogo('rechazar')}>
              {f.rechazar}
            </Button>
            <AprobarBoton contratoId={c.id} onFallo={setFallo} />
          </>
        )}
        {a.activar && gestiona && <Button onClick={() => setDialogo('activar')}>{f.activar}</Button>}
        {a.cambiar && gestiona && (
          <Button variant="secondary" onClick={() => setDialogo('cambio')}>
            {f.cambio}
          </Button>
        )}
        {a.darBaja && gestiona && (
          <Button variant="secondary" onClick={() => setDialogo('baja')}>
            {f.baja}
          </Button>
        )}
        {a.anularBaja && gestiona && (
          <Button variant="ghost" onClick={() => setDialogo('anular-baja')}>
            {f.anularBaja}
          </Button>
        )}
        {a.finalizar && gestiona && (
          <Button disabled={!finalizable} title={finalizable ? undefined : f.finalizarDesde.replace('{fecha}', formatearFecha(c.fechaBajaProgramada ?? hoy))} onClick={() => setDialogo('finalizar')}>
            {f.finalizar}
          </Button>
        )}
        {a.liquidar && gestiona && <Button onClick={() => setDialogo('liquidar')}>{f.liquidar}</Button>}
      </PageHeader>

      {fallo !== null && <AvisoBloqueo error={fallo} titulo={f.error} />}
      {a.finalizar && !finalizable && c.fechaBajaProgramada && (
        <Alert tone="info">{f.finalizarDesde.replace('{fecha}', formatearFecha(c.fechaBajaProgramada))}</Alert>
      )}

      <div style={{ display: 'grid', gap: 'var(--space-9)', marginTop: 'var(--space-9)' }}>
        <Card title={f.flujo}>
          <Stepper steps={pasosDelFlujo(c.estado, c.estadoActualDesde ? formatearFecha(c.estadoActualDesde) : undefined)} />
        </Card>

        <Tabs
          value={pestana}
          onChange={setPestana}
          tabs={[
            { value: 'resumen', label: f.pestanas.resumen },
            { value: 'historial', label: f.pestanas.historial, count: c.versiones.length + c.solicitudes.length },
            { value: 'incrementos', label: f.pestanas.incrementos },
            { value: 'inventario', label: f.pestanas.inventario },
          ]}
        />

        {pestana === 'resumen' && <Resumen c={c} />}
        {pestana === 'historial' && <Historial c={c} />}
        {pestana === 'incrementos' && <TabIncrementos contratoId={c.id} moneda={c.moneda} puedeEscribir={gestiona} permitido={a.incrementar} />}
        {pestana === 'inventario' && <TabInventario contratoId={c.id} moneda={c.moneda} puedeFirmar={gestiona && a.revisarSalida} />}
      </div>

      <DialogoEnviar contratoId={c.id} abierto={dialogo === 'enviar'} onCerrar={() => setDialogo(null)} />
      <DialogoRechazar contratoId={c.id} abierto={dialogo === 'rechazar'} onCerrar={() => setDialogo(null)} />
      <DialogoSimple contratoId={c.id} ruta="activacion" abierto={dialogo === 'activar'} d={t.activarDialogo} onCerrar={() => setDialogo(null)} />
      <DialogoSimple contratoId={c.id} ruta="anular-baja" abierto={dialogo === 'anular-baja'} d={t.anularBajaDialogo} onCerrar={() => setDialogo(null)} />
      <DialogoSimple contratoId={c.id} ruta="finalizacion" abierto={dialogo === 'finalizar'} d={t.finalizarDialogo} onCerrar={() => setDialogo(null)} />
      <DialogoSimple
        contratoId={c.id}
        ruta="liquidacion"
        abierto={dialogo === 'liquidar'}
        d={t.liquidarDialogo}
        tituloError={t.liquidarDialogo.bloqueoTitulo}
        ayudaError={t.liquidarDialogo.bloqueoAyuda}
        onCerrar={() => setDialogo(null)}
      />
      <DialogoBaja c={c} hoy={hoy} abierto={dialogo === 'baja'} onCerrar={() => setDialogo(null)} />
      <DrawerCambio c={c} abierto={dialogo === 'cambio'} onCerrar={() => setDialogo(null)} />
    </>
  );
}

function AprobarBoton({ contratoId, onFallo }: { contratoId: string; onFallo: (e: unknown) => void }) {
  const aprobar = useTransicion(contratoId, 'aprobacion');
  return (
    <Button
      disabled={aprobar.isPending}
      onClick={() => {
        onFallo(null);
        aprobar.mutate(undefined, { onError: onFallo });
      }}
    >
      {f.aprobar}
    </Button>
  );
}

function DialogoEnviar({ contratoId, abierto, onCerrar }: { contratoId: string; abierto: boolean; onCerrar: () => void }) {
  const enviar = useTransicion(contratoId, 'solicitud');
  const [comentario, setComentario] = useState('');
  return (
    <DialogoAccion
      abierto={abierto}
      titulo={t.enviarDialogo.titulo}
      descripcion={t.enviarDialogo.descripcion}
      confirmar={t.enviarDialogo.confirmar}
      tituloError={f.error}
      onConfirmar={() => enviar.mutateAsync({ comentario: comentario.trim() === '' ? null : comentario.trim() })}
      onCerrar={onCerrar}
    >
      <Textarea id="envio-comentario" label={t.enviarDialogo.comentario} rows={3} maxLength={1000} value={comentario} onChange={(e) => setComentario(e.target.value)} />
    </DialogoAccion>
  );
}

/** El rechazo exige comentario (la API lo pide): `ConfirmarDestructivo` lo recoge como motivo. */
function DialogoRechazar({ contratoId, abierto, onCerrar }: { contratoId: string; abierto: boolean; onCerrar: () => void }) {
  const rechazar = useTransicion(contratoId, 'rechazo');
  return (
    <ConfirmarDestructivo
      open={abierto}
      accion={t.rechazoDialogo.accion}
      consecuencia={t.rechazoDialogo.consecuencia}
      onConfirmar={async (motivo) => {
        await rechazar.mutateAsync({ comentario: motivo });
      }}
      onCerrar={onCerrar}
    />
  );
}

function DialogoSimple({
  contratoId,
  ruta,
  abierto,
  d,
  tituloError,
  ayudaError,
  onCerrar,
}: {
  contratoId: string;
  ruta: 'activacion' | 'anular-baja' | 'finalizacion' | 'liquidacion';
  abierto: boolean;
  d: { titulo: string; descripcion: string; confirmar: string };
  tituloError?: string;
  ayudaError?: string;
  onCerrar: () => void;
}) {
  const accion = useTransicion(contratoId, ruta);
  return (
    <DialogoAccion
      abierto={abierto}
      titulo={d.titulo}
      descripcion={d.descripcion}
      confirmar={d.confirmar}
      tituloError={tituloError ?? f.error}
      ayudaError={ayudaError}
      onConfirmar={() => accion.mutateAsync(undefined)}
      onCerrar={onCerrar}
    />
  );
}

function DialogoBaja({ c, hoy, abierto, onCerrar }: { c: ContratoDetalle; hoy: string; abierto: boolean; onCerrar: () => void }) {
  const baja = useTransicion(c.id, 'baja');
  const [fecha, setFecha] = useState('');
  const [comentario, setComentario] = useState('');
  const [intentado, setIntentado] = useState(false);
  const error = validarBaja(fecha, c.fechaInicio, hoy);
  const d = t.bajaDialogo;
  return (
    <DialogoAccion
      abierto={abierto}
      titulo={d.titulo}
      descripcion={d.descripcion}
      confirmar={d.confirmar}
      tituloError={f.error}
      onConfirmar={async () => {
        setIntentado(true);
        if (error) throw new Error(error);
        await baja.mutateAsync({ fechaFin: fecha, comentario: comentario.trim() === '' ? null : comentario.trim() });
      }}
      onCerrar={onCerrar}
    >
      <DatePicker label={d.fecha} required min={fechaMinimaBaja(c.fechaInicio, hoy)} value={fecha} onChange={(e) => setFecha(e.target.value)} error={intentado ? (error ?? undefined) : undefined} />
      {fecha && !error && <span className="ar-hint">{d.preaviso.replace('{n}', String(diasDePreaviso(fecha, hoy)))}</span>}
      <Input id="baja-comentario" label={d.comentario} maxLength={1000} value={comentario} onChange={(e) => setComentario(e.target.value)} />
    </DialogoAccion>
  );
}

function DrawerCambio({ c, abierto, onCerrar }: { c: ContratoDetalle; abierto: boolean; onCerrar: () => void }) {
  const cambio = useCambioContrato(c.id);
  const plantillas = usePlantillas();
  const inicial = (): FormularioCambio => ({
    renta: String(c.renta),
    diaPago: String(c.diaPago),
    fianza: String(c.fianza),
    reglaRenovacion: c.reglaRenovacion,
    renovacionMeses: String(c.renovacionMeses),
    plantillaContratoId: c.plantillaContratoId,
    observaciones: c.observaciones ?? '',
    motivo: '',
  });
  const [v, setV] = useState<FormularioCambio>(inicial);
  const [intentado, setIntentado] = useState(false);
  const [fallo, setFallo] = useState<unknown>(null);
  const errores = validarCambio(v);
  const d = t.cambioDialogo;
  const opcionesPlantilla = (plantillas.data?.data ?? []).filter((p) => p.activa || p.codigo === c.plantillaContratoId).map((p) => ({ value: p.codigo, label: `${p.nombre} · ${p.codigo}` }));

  async function enviar() {
    setIntentado(true);
    setFallo(null);
    if (errores.motivo || errores.renta) return;
    try {
      await cambio.mutateAsync(cuerpoCambio(v, c.moneda));
      setIntentado(false);
      setV(inicial());
      onCerrar();
    } catch (e) {
      setFallo(e);
    }
  }

  return (
    <Drawer
      open={abierto}
      onClose={cambio.isPending ? undefined : onCerrar}
      title={d.titulo}
      description={d.descripcion}
      footer={
        <>
          <Button variant="ghost" onClick={onCerrar} disabled={cambio.isPending}>
            Cancelar
          </Button>
          <Button onClick={() => void enviar()} disabled={cambio.isPending}>
            {d.confirmar}
          </Button>
        </>
      }
    >
      <div style={{ display: 'grid', gap: 'var(--space-9)' }}>
        {fallo !== null && <AvisoBloqueo error={fallo} titulo={f.error} />}
        <Input id="cambio-renta" label={t.formulario.renta} required inputMode="decimal" suffix={c.moneda} value={v.renta} onChange={(e) => setV({ ...v, renta: e.target.value })} error={intentado ? errores.renta : undefined} />
        <Input id="cambio-fianza" label={t.formulario.fianza} inputMode="decimal" suffix={c.moneda} value={v.fianza} onChange={(e) => setV({ ...v, fianza: e.target.value })} />
        <Input id="cambio-dia" label={t.formulario.diaPago} inputMode="numeric" value={v.diaPago} onChange={(e) => setV({ ...v, diaPago: e.target.value })} />
        <Select
          label={t.formulario.regla}
          value={v.reglaRenovacion}
          onChange={(e) => {
            const regla = e.target.value as ReglaRenovacionApi;
            setV({ ...v, reglaRenovacion: regla, renovacionMeses: mesesDeRegla(regla, v.renovacionMeses) });
          }}
          options={(Object.keys(REGLAS_RENOVACION) as ReglaRenovacionApi[]).map((r) => ({ value: r, label: REGLAS_RENOVACION[r].texto }))}
        />
        <Select label={t.formulario.plantilla} value={v.plantillaContratoId} onChange={(e) => setV({ ...v, plantillaContratoId: e.target.value })} options={opcionesPlantilla} />
        <Textarea id="cambio-motivo" label={d.motivo} required rows={3} maxLength={500} value={v.motivo} onChange={(e) => setV({ ...v, motivo: e.target.value })} error={intentado ? errores.motivo : undefined} />
      </div>
    </Drawer>
  );
}

function Resumen({ c }: { c: ContratoDetalle }) {
  const k = f.campos;
  return (
    <Card>
      <KeyValueList
        columns={3}
        items={[
          { label: k.cliente, value: c.cliente ?? '—' },
          { label: k.unidad, value: c.unidad ?? '—' },
          { label: k.facturadora, value: c.facturadora ?? '—' },
          { label: k.inicio, value: formatearFecha(c.fechaInicio), mono: true },
          { label: k.duracion, value: k.meses.replace('{n}', String(c.duracionInicialMeses)) },
          { label: k.finInicial, value: formatearFecha(c.fechaFinInicial), mono: true },
          { label: k.finVigente, value: formatearFecha(c.fechaFinVigente), mono: true },
          { label: k.renta, value: <Money amount={c.renta} currency={c.moneda} strong /> },
          { label: k.diaPago, value: String(c.diaPago), mono: true },
          { label: k.fianza, value: <Money amount={c.fianza} currency={c.moneda} /> },
          { label: k.renovacion, value: `${REGLAS_RENOVACION[c.reglaRenovacion].texto} · ${k.meses.replace('{n}', String(c.renovacionMeses))}` },
          { label: k.plantilla, value: c.plantillaContratoId, mono: true },
          { label: k.plantillaCongelada, value: c.plantillaContratoCongeladaId ? `${c.plantillaContratoCongeladaId} · ${c.plantillaCongeladaAt ? formatearMarca(c.plantillaCongeladaAt) : ''}` : k.sinCongelar },
          { label: k.bajaProgramada, value: c.fechaBajaProgramada ? formatearFecha(c.fechaBajaProgramada) : '—', mono: Boolean(c.fechaBajaProgramada) },
          { label: k.finReal, value: c.fechaFinReal ? formatearFecha(c.fechaFinReal) : '—', mono: Boolean(c.fechaFinReal) },
          { label: k.observaciones, value: c.observaciones ?? c.estadoActualComentario ?? '—', span: true },
        ]}
      />
    </Card>
  );
}

function Historial({ c }: { c: ContratoDetalle }) {
  const h = t.historial;
  return (
    <div style={{ display: 'grid', gap: 'var(--space-9)' }}>
      <Card title={h.versiones} padded={false}>
        {c.versiones.length === 0 ? (
          <p style={{ margin: 0, padding: 'var(--space-9)', color: 'var(--text-muted)' }}>{h.sinVersiones}</p>
        ) : (
          <DataTable
            rows={c.versiones}
            rowKey="numero"
            columns={[
              { key: 'numero', label: h.columnas.version, render: (v: VersionContrato) => <span className="mono">v{v.numero}</span> },
              { key: 'cambio', label: h.columnas.cambio },
              {
                key: 'renta',
                label: h.columnas.renta,
                align: 'right',
                render: (v: VersionContrato) =>
                  v.rentaAnterior != null && v.rentaNueva != null ? (
                    <span>
                      <Money amount={v.rentaAnterior} currency={v.moneda} /> → <Money amount={v.rentaNueva} currency={v.moneda} />
                    </span>
                  ) : (
                    '—'
                  ),
              },
              { key: 'alcance', label: h.columnas.alcance },
              { key: 'fecha', label: h.columnas.fecha, align: 'right', render: (v: VersionContrato) => <span className="mono">{formatearMarca(v.createdAt)}</span> },
            ]}
          />
        )}
      </Card>
      <Card title={h.solicitudes}>
        {c.solicitudes.length === 0 ? (
          <p style={{ margin: 0, color: 'var(--text-muted)' }}>{h.sinSolicitudes}</p>
        ) : (
          <Timeline
            items={c.solicitudes.map((s) => {
              const r = etiquetaResultadoSolicitud[resultadoSolicitud(s.resultado)];
              return {
                title: r.texto,
                body: [s.comentario, s.motivoCaducidad, s.resueltoAt ? h.resueltaEl.replace('{fecha}', formatearMarca(s.resueltoAt)) : null].filter(Boolean).join(' · ') || undefined,
                time: h.solicitadaEl.replace('{fecha}', formatearMarca(s.solicitadoAt)),
                icon: r.tono === 'success' ? 'check' : r.tono === 'danger' ? 'x' : 'clock',
                tone: r.tono === 'neutral' ? undefined : (r.tono as 'success' | 'warning' | 'danger' | 'info'),
              };
            })}
          />
        )}
      </Card>
    </div>
  );
}
