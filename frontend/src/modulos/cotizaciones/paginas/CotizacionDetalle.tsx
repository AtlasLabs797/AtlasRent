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
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { formatearMarca } from '../../../dominio/fechas';
import { textos } from '../../../i18n/textos';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { BadgeEstado, abrirPdf } from '../../operacion/Comunes';
import { ESTADOS_COTIZACION, type EstadoCotizacionApi } from '../../operacion/etiquetas';
import { useCambiarEstadoCotizacion, useCotizacion, useDesactivarCotizacion, type CotizacionLinea } from '../api';
import { cotizacionEditable, puedeConvertir, transicionesCotizacion } from '../cuerpo';
import { FormularioCotizacionDrawer } from '../FormularioCotizacion';
import { textosCotizaciones as t } from '../textos';

export default function CotizacionDetalle() {
  const { id } = useParams();
  const navegar = useNavigate();
  const identidad = useSesion((s) => s.identidad);
  const cotizacion = useCotizacion(id);
  const cambiar = useCambiarEstadoCotizacion(id ?? '');
  const baja = useDesactivarCotizacion(id ?? '');
  const [editando, setEditando] = useState(false);
  const [confirmandoBaja, setConfirmandoBaja] = useState(false);
  const [fallo, setFallo] = useState<unknown>(null);

  const c = cotizacion.data;
  const d = t.detalle;

  async function accion(operacion: () => Promise<unknown>) {
    setFallo(null);
    try {
      await operacion();
    } catch (e) {
      setFallo(e);
    }
  }

  if (cotizacion.isError) {
    const sinRegistro = aErrorApi(cotizacion.error).status === 404;
    return (
      <>
        <PageHeader eyebrow={textos.navegacion.modulos.cotizaciones.etiqueta} title={sinRegistro ? d.noEncontrada : t.errorTitulo} />
        <ErrorAlerta error={cotizacion.error} titulo={t.errorTitulo} />
        <div style={{ marginTop: 'var(--space-9)' }}>
          <Button variant="secondary" onClick={() => navegar('/cotizaciones')}>
            {d.volver}
          </Button>
        </div>
      </>
    );
  }
  if (!c) return <Skeleton height={120} />;

  const editable = cotizacionEditable(c.estado);
  const puedeEscribir = puede(identidad, ACCIONES.facturaBorrador);
  const puedeEstado = puede(identidad, ACCIONES.cotizacionEnviar);
  const destinatario = c.clienteNombre ?? c.prospectoNombre ?? '—';
  const destinos: EstadoCotizacionApi[] = transicionesCotizacion(c.estado);

  return (
    <>
      <PageHeader
        eyebrow={textos.navegacion.modulos.cotizaciones.etiqueta}
        title={destinatario}
        description={<BadgeEstado estado={ESTADOS_COTIZACION[c.estado]} />}
        breadcrumbs={[{ label: textos.navegacion.modulos.cotizaciones.etiqueta, onClick: () => navegar('/cotizaciones') }, { label: destinatario }]}
      >
        <Button variant="secondary" icon="file-down" onClick={() => void accion(() => abrirPdf(`/cotizaciones/${c.id}/pdf`))}>
          {d.pdf}
        </Button>
        {editable && puedeEscribir && (
          <Button variant="secondary" icon="pencil" onClick={() => setEditando(true)}>
            {d.editar}
          </Button>
        )}
        {puedeEstado &&
          destinos.map((destino, i) => (
            <Button
              key={destino}
              variant={i === 0 && destino !== 'Rechazada' ? 'primary' : 'secondary'}
              disabled={cambiar.isPending}
              onClick={() => void accion(() => cambiar.mutateAsync(destino))}
            >
              {d.cambioEstado[destino as keyof typeof d.cambioEstado]}
            </Button>
          ))}
      </PageHeader>

      {fallo !== null && <ErrorAlerta error={fallo} titulo={t.errorTitulo} />}
      {!puedeEstado && destinos.length > 0 && <Alert tone="info">{d.sinPermisoEstado}</Alert>}
      {!editable && <Alert tone="info">{d.cerradaAviso}</Alert>}

      <div style={{ display: 'grid', gap: 'var(--space-9)', marginTop: 'var(--space-9)' }}>
        <Card>
          <KeyValueList
            columns={3}
            items={[
              { label: d.destinatario, value: destinatario },
              { label: d.unidad, value: c.unidadNombre ?? d.sinUnidad },
              { label: d.moneda, value: c.moneda, mono: true },
              { label: d.creada, value: formatearMarca(c.createdAt), mono: true },
            ]}
          />
        </Card>

        <Card title={d.lineas} padded={false}>
          <DataTable
            rows={c.lineas}
            rowKey="posicion"
            columns={[
              { key: 'descripcion', label: t.formulario.descripcion },
              { key: 'cantidad', label: t.formulario.cantidad, align: 'right', render: (l: CotizacionLinea) => <span className="mono">{l.cantidad}</span> },
              { key: 'precio', label: t.formulario.precio, align: 'right', render: (l: CotizacionLinea) => <Money amount={l.precioUnitario} currency={c.moneda} /> },
              { key: 'importe', label: t.formulario.importe, align: 'right', render: (l: CotizacionLinea) => <Money amount={l.importe} currency={c.moneda} /> },
            ]}
          />
          <div style={{ display: 'grid', justifyItems: 'end', gap: 'var(--space-4)', padding: 'var(--space-9)' }}>
            <span>
              {d.subtotal} <Money amount={c.subtotal} currency={c.moneda} />
            </span>
            <span>
              {d.total} <Money amount={c.total} currency={c.moneda} strong />
            </span>
          </div>
        </Card>

        {puedeConvertir(c.estado) && (
          <Card title={`${d.crearReserva} / ${d.crearContrato}`}>
            <p style={{ margin: '0 0 var(--space-6)', color: 'var(--text-muted)' }}>{d.convertirAyuda}</p>
            <div style={{ display: 'flex', gap: 'var(--space-6)' }}>
              {puede(identidad, ACCIONES.clienteEditar) && (
                <Button icon="calendar-plus" onClick={() => navegar(`/reservas?nueva=1&cotizacionId=${c.id}`)}>
                  {d.crearReserva}
                </Button>
              )}
              {puede(identidad, ACCIONES.contratoBorrador) && (
                <Button variant="secondary" icon="file-pen-line" onClick={() => navegar(`/contratos/nuevo?cotizacionId=${c.id}`)}>
                  {d.crearContrato}
                </Button>
              )}
            </div>
          </Card>
        )}

        {editable && puedeEscribir && (
          <div>
            <Button variant="ghost" icon="trash" onClick={() => setConfirmandoBaja(true)}>
              {d.darDeBaja}
            </Button>
          </div>
        )}
      </div>

      {editando && <FormularioCotizacionDrawer open cotizacion={c} onCerrar={() => setEditando(false)} onGuardada={() => setEditando(false)} />}

      {/* El DELETE no recibe motivo (CotizacionesController.Desactivar): por eso un diálogo simple y no ConfirmarDestructivo. TODO(A#): ¿la baja de una cotización debe llevar motivo? */}
      <Dialog
        open={confirmandoBaja}
        title={d.bajaTitulo}
        onClose={() => setConfirmandoBaja(false)}
        footer={
          <>
            <Button variant="ghost" onClick={() => setConfirmandoBaja(false)}>
              {d.cancelar}
            </Button>
            <Button
              variant="danger"
              disabled={baja.isPending}
              onClick={() =>
                void accion(async () => {
                  await baja.mutateAsync();
                  setConfirmandoBaja(false);
                  navegar('/cotizaciones');
                })
              }
            >
              {d.bajaConfirmar}
            </Button>
          </>
        }
      >
        <p style={{ margin: 0, color: 'var(--text-muted)' }}>{d.bajaConsecuencia}</p>
      </Dialog>
    </>
  );
}
