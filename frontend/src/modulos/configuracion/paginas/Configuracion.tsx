import type { JSX } from 'react';
import { useSearchParams } from 'react-router';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { Tabs } from '../../../design-system/components/navigation/Tabs';
import { textos } from '../../../i18n/textos';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { CUENTAS, FORMAS_PAGO, ITBIS, RETENCIONES, SERIES, SUBEMPRESAS } from '../catalogos';
import { Agentes } from '../secciones/Agentes';
import { Ajustes } from '../secciones/Ajustes';
import { Cambio } from '../secciones/Cambio';
import { Fiscal } from '../secciones/Fiscal';
import { Mensajes } from '../secciones/Mensajes';
import { SeccionCatalogo } from '../secciones/SeccionCatalogo';
import { textosConfiguracion as t } from '../textos';

type Seccion = keyof typeof t.pestanas;

const apilar = { display: 'grid', gap: 'var(--space-9)' } as const;

/** Configuración (spec §2 fila 1): un grupo de pestañas, cada una con la acción que exige su endpoint. */
export default function Configuracion(): JSX.Element {
  const identidad = useSesion((s) => s.identidad);
  const [params, setParams] = useSearchParams();

  const disponibles: Seccion[] = [
    'series',
    'fiscal',
    'impuestos',
    'pagos',
    'subempresas',
    ...(puede(identidad, ACCIONES.facturaVer) ? (['cambio'] as const) : []),
    'ajustes',
    'mensajes',
    ...(puede(identidad, ACCIONES.agenteToken) ? (['agentes'] as const) : []),
  ];
  const pedida = params.get('seccion');
  const actual: Seccion = disponibles.find((s) => s === pedida) ?? 'series';

  return (
    <>
      <PageHeader
        eyebrow={textos.navegacion.grupos.control}
        title={t.titulo}
        description={t.descripcion}
        breadcrumbs={[{ label: textos.navegacion.grupos.control }, { label: t.titulo }]}
      />
      <Tabs
        tabs={disponibles.map((s) => ({ value: s, label: t.pestanas[s] }))}
        value={actual}
        onChange={(v) => setParams({ seccion: v }, { replace: true })}
        style={{ marginBottom: 'var(--space-9)' }}
      />
      {actual === 'series' && <SeccionCatalogo def={SERIES} />}
      {actual === 'fiscal' && <Fiscal />}
      {actual === 'impuestos' && (
        <div style={apilar}>
          <SeccionCatalogo def={ITBIS} />
          <SeccionCatalogo def={RETENCIONES} />
        </div>
      )}
      {actual === 'pagos' && (
        <div style={apilar}>
          <SeccionCatalogo def={FORMAS_PAGO} />
          <SeccionCatalogo def={CUENTAS} />
        </div>
      )}
      {actual === 'subempresas' && <SeccionCatalogo def={SUBEMPRESAS} />}
      {actual === 'cambio' && <Cambio puedeCargar={puede(identidad, ACCIONES.configuracionEditar)} />}
      {actual === 'ajustes' && <Ajustes />}
      {actual === 'mensajes' && <Mensajes puedeEditar={puede(identidad, ACCIONES.plantillaEditar)} />}
      {actual === 'agentes' && <Agentes />}
    </>
  );
}
