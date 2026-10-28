import { useState } from 'react';
import { useNavigate, useParams } from 'react-router';
import { aErrorApi } from '../../../api/errores';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { DataTable } from '../../../design-system/components/data/DataTable';
import { KeyValueList } from '../../../design-system/components/data/KeyValueList';
import { Money } from '../../../design-system/components/data/Money';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { Dialog } from '../../../design-system/components/feedback/Dialog';
import { Skeleton } from '../../../design-system/components/feedback/Skeleton';
import { Input } from '../../../design-system/components/forms/Input';
import { Select } from '../../../design-system/components/forms/Select';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { formatearFecha } from '../../../dominio/fechas';
import { textos } from '../../../i18n/textos';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { BadgeEstado } from '../../operacion/Comunes';
import { ESTADOS_RESERVA, etiquetaModalidad } from '../../operacion/etiquetas';
import { useCancelarReserva, useFacturarReserva, useReserva, useSeries, type FacturaReservaRespuesta, type ReservaDia } from '../api';
import { AvisoBloqueo } from '../AvisoBloqueo';
import { reservaCancelable, reservaConvertible, reservaEditable, reservaFacturable } from '../cuerpo';
import { FormularioReservaDrawer } from '../FormularioReserva';
import { textosReservas as t } from '../textos';

export default function ReservaDetalle() {
  const { id = '' } = useParams();
  const navegar = useNavigate();
  const identidad = useSesion((s) => s.identidad);
  const reserva = useReserva(id);
  const cancelar = useCancelarReserva(id);
  const [editando, setEditando] = useState(false);
  const [cancelando, setCancelando] = useState(false);
  const [facturando, setFacturando] = useState(false);
  const [fallo, setFallo] = useState<unknown>(null);
  const d = t.detalle;
  const r = reserva.data;

  if (reserva.isError) {
    const sinRegistro = aErrorApi(reserva.error).status === 404;
    return (
      <>
        <PageHeader eyebrow={textos.navegacion.modulos.reservas.etiqueta} title={sinRegistro ? d.noEncontrada : t.errorTitulo} />
        <ErrorAlerta error={reserva.error} titulo={t.errorTitulo} />
        <div style={{ marginTop: 'var(--space-9)' }}>
          <Button variant="secondary" onClick={() => navegar('/reservas')}>
            {d.volver}
          </Button>
        </div>
      </>
    );
  }
  if (!r) return <Skeleton height={120} />;

  const enConflicto = r.estado === 'EnConflicto';
  const puedeEditar = puede(identidad, ACCIONES.clienteEditar);
  // En conflicto decide el gerente: la salida (cancelar) se ofrece a quien tiene `contrato.estado`.
  const puedeCancelar = reservaCancelable(r.estado) && puedeEditar && (!enConflicto || puede(identidad, ACCIONES.contratoEstado));
  const puedeFacturar = reservaFacturable(r.tipo, r.estado, r.clienteId !== null) && puede(identidad, ACCIONES.facturaEmitir);
  const puedeContrato = reservaConvertible(r.tipo, r.estado) && puede(identidad, ACCIONES.contratoBorrador);
  const para = r.cliente ?? '—';

  return (
    <>
      <PageHeader
        eyebrow={textos.navegacion.modulos.reservas.etiqueta}
        title={`${r.unidad} · ${para}`}
        description={<BadgeEstado estado={ESTADOS_RESERVA[r.estado]} />}
        breadcrumbs={[{ label: textos.navegacion.modulos.reservas.etiqueta, onClick: () => navegar('/reservas') }, { label: r.unidad }]}
      >
        {puedeContrato && (
          <Button icon="file-pen-line" onClick={() => navegar(`/contratos/nuevo?reservaId=${r.id}`)}>
            {d.crearContrato}
          </Button>
        )}
        {puedeFacturar && (
          <Button variant={puedeContrato ? 'secondary' : 'primary'} icon="receipt" onClick={() => setFacturando(true)}>
            {d.facturar}
          </Button>
        )}
        {reservaEditable(r.estado) && puedeEditar && (
          <Button variant="secondary" icon="pencil" onClick={() => setEditando(true)}>
            {d.editar}
          </Button>
        )}
        {puedeCancelar && (
          <Button variant="ghost" icon="x" onClick={() => setCancelando(true)}>
            {d.cancelar}
          </Button>
        )}
      </PageHeader>

      {fallo !== null && <AvisoBloqueo error={fallo} titulo={d.error} />}
      {enConflicto && (
        <Alert tone="danger" title={d.conflictoTitulo}>
          {d.conflictoCuerpo}
        </Alert>
      )}
      {!r.activo && !enConflicto && <Alert tone="info">{d.inactivaAviso}</Alert>}

      <div style={{ display: 'grid', gap: 'var(--space-9)', marginTop: 'var(--space-9)' }}>
        <Card>
          <KeyValueList
            columns={3}
            items={[
              { label: d.unidad, value: r.unidad },
              { label: d.para, value: para },
              { label: d.tipo, value: r.tipo === 'Corta' ? `Corta · ${etiquetaModalidad[r.modalidad]}` : 'Mensual' },
              { label: d.fechaReserva, value: formatearFecha(r.fechaReserva), mono: true },
              { label: d.entradaPrevista, value: formatearFecha(r.entradaPrevista), mono: true },
              { label: d.salidaPrevista, value: r.salidaPrevista ? formatearFecha(r.salidaPrevista) : r.tipo === 'Mensual' ? d.sinSalida : '—', mono: Boolean(r.salidaPrevista) },
              { label: d.importe, value: <Money amount={r.importe} currency={r.moneda} strong /> },
              {
                label: d.anticipo,
                value: r.anticipoImporte != null ? (
                  <span>
                    <Money amount={r.anticipoImporte} currency={r.moneda} />
                    {r.anticipoNota ? ` · ${r.anticipoNota}` : ''}
                  </span>
                ) : (
                  d.sinAnticipo
                ),
              },
              { label: d.observaciones, value: r.observaciones ?? '—', span: true },
            ]}
          />
          <p style={{ margin: 'var(--space-6) 0 0', font: 'var(--type-caption)', color: 'var(--text-muted)' }}>{t.anticipoNota}</p>
        </Card>

        {r.tipo === 'Corta' && (
          <Card title={d.dias} padded={false}>
            <DataTable
              rows={r.dias}
              columns={[
                { key: 'fecha', label: t.formulario.dia, render: (x: ReservaDia) => <span className="mono">{formatearFecha(x.fecha)}</span> },
                { key: 'tipo', label: t.formulario.modalidad, render: (x: ReservaDia) => (x.tipo === 'MedioDia' ? t.formulario.medioDia : t.formulario.diaCompleto) },
                { key: 'hora', label: t.formulario.hora, render: (x: ReservaDia) => x.horaOrientativa ?? <span style={{ color: 'var(--text-subtle)' }}>{d.sinHora}</span> },
              ]}
            />
          </Card>
        )}
      </div>

      {editando && <FormularioReservaDrawer open reserva={r} onCerrar={() => setEditando(false)} onGuardada={() => setEditando(false)} />}

      {/* El DELETE no lleva motivo (ReservasController.Cancelar): diálogo simple. TODO(A#): ¿cancelar debe pedir motivo? */}
      <Dialog
        open={cancelando}
        title={d.cancelarTitulo}
        onClose={() => setCancelando(false)}
        footer={
          <>
            <Button variant="ghost" onClick={() => setCancelando(false)}>
              {d.volverAtras}
            </Button>
            <Button
              variant="danger"
              disabled={cancelar.isPending}
              onClick={async () => {
                setFallo(null);
                try {
                  await cancelar.mutateAsync();
                  setCancelando(false);
                } catch (e) {
                  setCancelando(false);
                  setFallo(e);
                }
              }}
            >
              {d.cancelarConfirmar}
            </Button>
          </>
        }
      >
        <p style={{ margin: 0, color: 'var(--text-muted)' }}>{d.cancelarConsecuencia}</p>
      </Dialog>

      <DialogoFacturar abierto={facturando} reservaId={r.id} moneda={r.moneda} onCerrar={() => setFacturando(false)} />
    </>
  );
}

function DialogoFacturar({ abierto, reservaId, moneda, onCerrar }: { abierto: boolean; reservaId: string; moneda: 'USD' | 'DOP'; onCerrar: () => void }) {
  const identidad = useSesion((s) => s.identidad);
  const f = t.facturar;
  const veSeries = puede(identidad, ACCIONES.configuracionEditar);
  const series = useSeries(veSeries && abierto);
  const facturar = useFacturarReserva(reservaId);
  const [serieId, setSerieId] = useState('');
  const [fallo, setFallo] = useState<unknown>(null);
  const [hecha, setHecha] = useState<FacturaReservaRespuesta | null>(null);

  function cerrar() {
    setFallo(null);
    setHecha(null);
    onCerrar();
  }

  const opciones = (series.data?.data ?? [])
    .filter((s) => s.activa && (s.moneda === null || s.moneda === moneda))
    .map((s) => ({ value: s.id, label: `Serie ${s.numero}${s.prefijo ? ` · ${s.prefijo}` : ''}` }));

  return (
    <Dialog
      open={abierto}
      title={hecha ? f.hecho : f.titulo}
      description={hecha ? undefined : f.descripcion}
      onClose={cerrar}
      footer={
        hecha ? (
          <Button onClick={cerrar}>{f.cerrar}</Button>
        ) : (
          <>
            <Button variant="ghost" onClick={cerrar}>
              {t.formulario.cancelar}
            </Button>
            <Button
              disabled={serieId.trim() === '' || facturar.isPending}
              onClick={async () => {
                setFallo(null);
                try {
                  setHecha(await facturar.mutateAsync(serieId.trim()));
                } catch (e) {
                  setFallo(e);
                }
              }}
            >
              {facturar.isPending ? f.emitiendo : f.emitir}
            </Button>
          </>
        )
      }
    >
      {hecha ? (
        <div style={{ display: 'grid', gap: 'var(--space-6)' }}>
          <p style={{ margin: 0 }}>
            {f.hechoCuerpo.replace('{numero}', hecha.numero?.toString() ?? f.sinNumero).replace('{fecha}', formatearFecha(hecha.vencimiento))} <Money amount={hecha.total} currency={hecha.moneda} strong />
          </p>
          {hecha.anticipoAviso && <Alert tone="warning">{hecha.anticipoAviso}</Alert>}
        </div>
      ) : (
        <div style={{ display: 'grid', gap: 'var(--space-6)' }}>
          {fallo !== null && <AvisoBloqueo error={fallo} titulo={f.error} />}
          {veSeries ? (
            <Select label={f.serie} required placeholder={f.serie} value={serieId} onChange={(e) => setSerieId(e.target.value)} options={opciones} />
          ) : (
            <Input id="serie-id" label={f.serieId} required hint={f.serieAyuda} value={serieId} onChange={(e) => setSerieId(e.target.value)} className="mono" />
          )}
          <Alert tone="info">{t.anticipoNota}</Alert>
        </div>
      )}
    </Dialog>
  );
}
