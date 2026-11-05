import { useState } from 'react';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { Tabs } from '../../../design-system/components/navigation/Tabs';
import { textos } from '../../../i18n/textos';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { EnvioMasivo } from '../componentes/EnvioMasivo';
import { HistorialEnvios } from '../componentes/HistorialEnvios';
import { Recordatorios } from '../componentes/Recordatorios';
import { textosEnvios as t } from '../textos';

type Pestana = 'historial' | 'masivo' | 'recordatorios';

/**
 * Envíos (F4-02, F4-03, F4-05; §3.14). Historial para quien ve facturas; envío masivo y avisos de
 * pago solo con `mensajeria.masivo` (gerente y administrador), la misma acción de sus endpoints.
 */
export default function Envios() {
  const identidad = useSesion((e) => e.identidad);
  const masivo = puede(identidad, ACCIONES.mensajeriaMasivo);
  const [pestana, setPestana] = useState<Pestana>('historial');

  const pestanas = [
    { label: t.pestanas.historial, value: 'historial' },
    ...(masivo
      ? [
          { label: t.pestanas.masivo, value: 'masivo' },
          { label: t.pestanas.recordatorios, value: 'recordatorios' },
        ]
      : []),
  ];
  const activa = pestanas.some((p) => p.value === pestana) ? pestana : 'historial';

  return (
    <>
      <PageHeader
        eyebrow={textos.navegacion.grupos.operacion}
        title={t.titulo}
        description={t.descripcion}
        breadcrumbs={[{ label: textos.navegacion.grupos.operacion }, { label: textos.navegacion.modulos.envios.etiqueta }]}
      />
      <Tabs tabs={pestanas} value={activa} onChange={(v) => setPestana(v as Pestana)} />
      <div style={{ height: 'var(--space-9)' }} />
      {activa === 'historial' && <HistorialEnvios puedeReintentar={puede(identidad, ACCIONES.cotizacionEnviar)} />}
      {activa === 'masivo' && <EnvioMasivo />}
      {activa === 'recordatorios' && (
        <Recordatorios puedePausar={puede(identidad, ACCIONES.clienteEditar)} />
      )}
    </>
  );
}
