import { useState } from 'react';
import { useNavigate, useParams } from 'react-router';
import { aErrorApi } from '../../../api/errores';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Badge } from '../../../design-system/components/core/Badge';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { KeyValueList } from '../../../design-system/components/data/KeyValueList';
import { OccupancyBar } from '../../../design-system/components/data/OccupancyBar';
import { UnitCard } from '../../../design-system/components/data/UnitCard';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { Dialog } from '../../../design-system/components/feedback/Dialog';
import { EmptyState } from '../../../design-system/components/feedback/EmptyState';
import { Skeleton } from '../../../design-system/components/feedback/Skeleton';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { Tabs } from '../../../design-system/components/navigation/Tabs';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { useDesactivarInmueble, useInmueble, useReactivarInmueble } from '../api';
import { DocumentosInmueble } from '../DocumentosInmueble';
import { FormularioInmueble } from '../FormularioInmueble';
import { FormularioUnidad } from '../FormularioUnidad';
import { contarEstados, estadoBadge } from '../logica';
import { textosUnidades } from '../textos';
import type { Unidad } from '../tipos';

const t = textosUnidades;

/** Primera tarifa de la unidad para la tarjeta: la mensual si existe. Se muestra tal cual, sin sumar ni convertir. */
function tarifaDestacada(u: Unidad) {
  return u.tarifas.find((x) => x.modalidad === 'Mensual') ?? u.tarifas[0];
}

export default function InmuebleFicha() {
  const { id = '' } = useParams();
  const navegar = useNavigate();
  const identidad = useSesion((s) => s.identidad);
  const editar = puede(identidad, ACCIONES.clienteEditar);
  const verDocumentos = puede(identidad, ACCIONES.inmuebleDocumento);

  const inmueble = useInmueble(id);
  const desactivar = useDesactivarInmueble();
  const reactivar = useReactivarInmueble();
  const [pestana, setPestana] = useState('unidades');
  const [editando, setEditando] = useState(false);
  const [confirmando, setConfirmando] = useState(false);
  const [unidad, setUnidad] = useState<Unidad | 'nueva' | null>(null);
  const [errorAccion, setErrorAccion] = useState<string | null>(null);

  if (inmueble.isPending) {
    return (
      <div style={{ padding: 'var(--space-9)' }}>
        <Skeleton lines={6} height={20} />
      </div>
    );
  }
  if (inmueble.isError) {
    const noExiste = aErrorApi(inmueble.error).status === 404;
    return (
      <>
        <PageHeader eyebrow={t.ficha.eyebrow} title={t.ficha.errorTitulo} breadcrumbs={[{ label: t.ficha.migas, onClick: () => navegar('/unidades') }]} />
        {noExiste ? (
          <EmptyState icon="building-2" title={t.ficha.errorTitulo}>
            {t.ficha.noEncontrado}
          </EmptyState>
        ) : (
          <ErrorAlerta error={inmueble.error} titulo={t.ficha.errorTitulo} />
        )}
      </>
    );
  }

  const i = inmueble.data;
  const activas = i.unidades.filter((u) => u.activo);
  const conteo = contarEstados(i.unidades);
  const pestanas = [
    { label: t.ficha.pestanas.unidades, value: 'unidades', count: i.unidades.length },
    // El empleado no tiene `inmueble.documento`: la API respondería 403, así que la pestaña no existe.
    ...(verDocumentos ? [{ label: t.ficha.pestanas.documentos, value: 'documentos' }] : []),
  ];

  return (
    <>
      <PageHeader
        eyebrow={t.ficha.eyebrow}
        title={i.nombre}
        description={`${t.tiposInmueble[i.tipo]}${i.direccion ? ` · ${i.direccion}` : ''}`}
        breadcrumbs={[{ label: t.ficha.migas, onClick: () => navegar('/unidades') }, { label: i.nombre }]}
      >
        {editar && i.activo && (
          <Button variant="ghost" onClick={() => setConfirmando(true)}>
            {t.ficha.desactivar}
          </Button>
        )}
        {editar && !i.activo && (
          <Button
            variant="secondary"
            disabled={reactivar.isPending}
            onClick={async () => {
              setErrorAccion(null);
              try {
                await reactivar.mutateAsync(i.id);
              } catch (e) {
                setErrorAccion(aErrorApi(e).detail ?? t.ficha.errorReactivar);
              }
            }}
          >
            {reactivar.isPending ? t.ficha.reactivando : t.ficha.reactivar}
          </Button>
        )}
        {editar && (
          <Button icon="pencil" onClick={() => setEditando(true)}>
            {t.ficha.editar}
          </Button>
        )}
      </PageHeader>

      {!i.activo && (
        <Alert tone="warning" style={{ marginBottom: 16 }}>
          {t.ficha.inactivoAviso}
        </Alert>
      )}
      {errorAccion && (
        <Alert tone="danger" style={{ marginBottom: 16 }}>
          {errorAccion}
        </Alert>
      )}

      <div style={{ display: 'grid', gap: 24 }}>
        <Card title={t.ficha.datos}>
          <KeyValueList
            columns={3}
            items={[
              { label: t.ficha.campos.tipo, value: t.tiposInmueble[i.tipo] },
              { label: t.ficha.campos.plantilla, value: i.plantillaContratoPorDefecto },
              {
                label: t.ficha.campos.renovacion,
                value: i.renovacionDefectoMeses != null ? t.ficha.campos.meses.replace('{n}', String(i.renovacionDefectoMeses)) : null,
                mono: true,
              },
              { label: t.ficha.campos.estado, value: i.activo ? <Badge status="activo" /> : <Badge tone="neutral">{t.inactivo}</Badge> },
              { label: t.ficha.campos.notas, value: i.notas, span: true },
            ]}
          />
        </Card>

        <div>
          <Tabs tabs={pestanas} value={pestana} onChange={setPestana} />
          <div style={{ height: 16 }} />

          {pestana === 'unidades' && (
            <div style={{ display: 'grid', gap: 24 }}>
              <Card
                title={t.ficha.pestanas.unidades}
                actions={
                  editar && i.activo ? (
                    <Button variant="secondary" size="sm" icon="plus" onClick={() => setUnidad('nueva')}>
                      {t.unidades.nueva}
                    </Button>
                  ) : undefined
                }
              >
                {activas.length > 0 && (
                  <div style={{ marginBottom: 'var(--space-9)' }}>
                    <OccupancyBar counts={conteo} />
                    <p style={{ margin: 'var(--space-5) 0 0', font: 'var(--type-fine)', color: 'var(--text-subtle)' }}>{t.ficha.estadoManual}</p>
                  </div>
                )}
                {i.unidades.length === 0 ? (
                  <EmptyState icon="door-open" title={t.unidades.vacioTitulo}>
                    {t.unidades.vacioCuerpo}
                  </EmptyState>
                ) : (
                  <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(260px, 1fr))', gap: 'var(--space-7)' }}>
                    {i.unidades.map((u) => {
                      const tarifa = tarifaDestacada(u);
                      return (
                        <UnitCard
                          key={u.id}
                          name={u.nombre}
                          type={t.tiposUnidad[u.tipo]}
                          status={u.activo ? estadoBadge(u.estadoManual) : 'fuera'}
                          modalities={u.modalidades.map((m) => t.modalidades[m])}
                          alert={!u.activo ? t.unidades.inactivaAviso : tarifa ? undefined : t.unidades.sinTarifa}
                          {...(tarifa ? { rent: Number(tarifa.importe), currency: tarifa.moneda, period: t.periodos[tarifa.modalidad] } : {})}
                          onClick={() => setUnidad(u)}
                        />
                      );
                    })}
                  </div>
                )}
              </Card>
            </div>
          )}
          {pestana === 'documentos' && verDocumentos && <DocumentosInmueble inmuebleId={i.id} />}
        </div>
      </div>

      {editar && <FormularioInmueble open={editando} ficha={i} onClose={() => setEditando(false)} onGuardado={() => setEditando(false)} />}
      <FormularioUnidad inmuebleId={i.id} unidad={unidad} onClose={() => setUnidad(null)} />

      <Dialog
        open={confirmando}
        title={t.ficha.desactivarTitulo}
        onClose={() => setConfirmando(false)}
        footer={
          <>
            <Button variant="ghost" onClick={() => setConfirmando(false)}>
              {t.formularioInmueble.cancelar}
            </Button>
            <Button
              variant="danger"
              disabled={desactivar.isPending}
              onClick={async () => {
                try {
                  await desactivar.mutateAsync(i.id);
                } catch (e) {
                  setErrorAccion(aErrorApi(e).detail ?? t.formularioInmueble.errorGeneral);
                }
                setConfirmando(false);
              }}
            >
              {t.ficha.desactivarConfirmar}
            </Button>
          </>
        }
      >
        <p style={{ margin: 0, color: 'var(--text-muted)' }}>{t.ficha.desactivarCuerpo}</p>
      </Dialog>
    </>
  );
}
