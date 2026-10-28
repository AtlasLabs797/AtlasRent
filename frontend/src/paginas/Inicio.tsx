import { useQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router';
import { ErrorAlerta } from '../componentes/ErrorAlerta';
import { Button } from '../design-system/components/core/Button';
import { Card } from '../design-system/components/core/Card';
import { Money } from '../design-system/components/data/Money';
import { RestrictedValue } from '../design-system/components/data/RestrictedValue';
import { StatCard } from '../design-system/components/data/StatCard';
import { PageHeader } from '../design-system/components/navigation/PageHeader';
import { pedirDashboard, type DashboardRespuesta } from '../informes/api';
import { FILTROS_VACIOS } from '../informes/filtros';
import { textos } from '../i18n/textos';
import { usePropuestasIa, useSolicitudesContrato } from '../modulos/bandeja/api';
import { accesosRapidos, comparacionMensual } from '../modulos/panel/modelo';
import { ACCIONES } from '../permisos';
import { permisosDe, puede, useSesion } from '../sesion/store';

const REJILLA = { display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(220px,1fr))', gap: 'var(--space-6)' } as const;

/**
 * Panel de inicio. KPIs de F5-01 solo con `informe.ver` (el empleado ve el hueco con
 * `RestrictedValue`, no un panel vacío), aprobaciones pendientes y accesos según permisos.
 * TODO(A#): alertas abiertas (§3.16) cuando la API tenga un endpoint para listarlas.
 */
export default function Inicio() {
  const { panel } = textos;
  const t = textos.panelInicio;
  const identidad = useSesion((e) => e.identidad);
  const navegar = useNavigate();
  const verInformes = puede(identidad, ACCIONES.informeVer);
  const verContratos = puede(identidad, ACCIONES.contratoEstado);
  const verPropuestas = puede(identidad, ACCIONES.iaProponer);

  const dashboard = useQuery({
    queryKey: ['informes', 'dashboard', 'panel'],
    enabled: verInformes,
    queryFn: () => pedirDashboard(FILTROS_VACIOS),
  });
  const contratos = useSolicitudesContrato(1, verContratos);
  const propuestas = usePropuestasIa(1, verPropuestas);
  const accesos = accesosRapidos(permisosDe(identidad));

  return (
    <>
      <PageHeader
        eyebrow={panel.sobrelinea}
        title={panel.titulo}
        description={panel.descripcion}
        breadcrumbs={[{ label: panel.sobrelinea }, { label: textos.navegacion.modulos.panel.etiqueta }]}
      />

      <section style={{ marginBottom: 'var(--space-14)' }}>
        <div className="ar-overline" style={{ marginBottom: 'var(--space-6)' }}>
          {t.indicadores}
        </div>
        {verInformes && dashboard.isError ? (
          <ErrorAlerta error={dashboard.error} titulo={t.errorIndicadores} />
        ) : (
          <div style={REJILLA}>
            <Indicadores dashboard={verInformes ? (dashboard.data ?? null) : null} permitido={verInformes} />
          </div>
        )}
      </section>

      <section style={{ marginBottom: 'var(--space-14)' }}>
        <div style={{ ...REJILLA, alignItems: 'start' }}>
          {(verContratos || verPropuestas) && (
            <Card
              title={t.pendiente}
              actions={
                <Button variant="ghost" size="sm" onClick={() => navegar('/bandeja')}>
                  {t.abrirBandeja}
                </Button>
              }
            >
              <dl style={{ margin: 0, display: 'grid', gap: 'var(--space-4)' }}>
                {verContratos && <Fila etiqueta={t.contratos} valor={contratos.data?.total} />}
                {verPropuestas && <Fila etiqueta={t.propuestas} valor={propuestas.data?.total} />}
              </dl>
            </Card>
          )}
          <Card title={t.accesos}>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--space-4)' }}>
              {accesos.map((a) => (
                <Button key={a.clave} variant="secondary" size="sm" icon={a.icono} onClick={() => navegar(a.ruta)}>
                  {a.etiqueta}
                </Button>
              ))}
              {accesos.length === 0 && <span style={{ color: 'var(--text-muted)' }}>{t.sinAccesos}</span>}
            </div>
          </Card>
        </div>
      </section>
    </>
  );
}

function Fila({ etiqueta, valor }: { etiqueta: string; valor: number | undefined }) {
  return (
    <div style={{ display: 'flex', justifyContent: 'space-between', gap: 'var(--space-6)' }}>
      <dt style={{ color: 'var(--text-muted)' }}>{etiqueta}</dt>
      <dd className="mono" style={{ margin: 0 }}>
        {valor ?? '—'}
      </dd>
    </div>
  );
}

/** Cuatro KPI, uno destacado. Cada uno lleva su comparación; sin `informe.ver` solo el hueco. */
function Indicadores({ dashboard, permitido }: { dashboard: DashboardRespuesta | null; permitido: boolean }) {
  const t = textos.panelInicio;
  const r = dashboard?.resumen;
  const fact = r ? comparacionMensual(r.serieMensual, 'facturadoUsd') : null;
  const cob = r ? comparacionMensual(r.serieMensual, 'cobradoUsd') : null;
  const u = dashboard?.unidades;
  const totalUnidades = u ? u.disponibles + u.reservadas + u.ocupadas + u.mantenimiento : null;

  const mensual = (c: NonNullable<typeof fact>) =>
    c.anterior !== null ? (
      <>
        {t.mesAnterior.replace('{mes}', c.mesAnterior ?? '')} <Money amount={c.anterior} currency="USD" tone="muted" />
      </>
    ) : (
      t.sinMesAnterior
    );

  return (
    <>
      <StatCard
        featured
        label={fact?.mes ? t.facturadoMes.replace('{mes}', fact.mes) : t.facturado}
        icon="receipt"
        value={
          <RestrictedValue allowed={permitido} label={t.soloGerencia}>
            {r && fact && (fact.actual !== null ? <Money amount={fact.actual} currency="USD" /> : <Money amount={r.facturado.consolidadoUsd} currency="USD" />)}
          </RestrictedValue>
        }
        meta={permitido && fact ? mensual(fact) : undefined}
      />
      <StatCard
        label={cob?.mes ? t.cobradoMes.replace('{mes}', cob.mes) : t.cobrado}
        icon="banknote"
        value={
          <RestrictedValue allowed={permitido} label={t.soloGerencia}>
            {r && cob && (cob.actual !== null ? <Money amount={cob.actual} currency="USD" /> : <Money amount={r.cobrado.total.consolidadoUsd} currency="USD" />)}
          </RestrictedValue>
        }
        meta={permitido && cob ? mensual(cob) : undefined}
      />
      <StatCard
        label={t.vencido}
        icon="circle-alert"
        tone="danger"
        value={
          <RestrictedValue allowed={permitido} label={t.soloGerencia}>
            {r && <Money amount={r.vencido.consolidadoUsd} currency="USD" tone="danger" />}
          </RestrictedValue>
        }
        meta={
          permitido && r ? (
            <>
              {t.dePendiente} <Money amount={r.pendiente.total.consolidadoUsd} currency="USD" tone="muted" />
            </>
          ) : undefined
        }
      />
      <StatCard
        label={t.ocupadas}
        icon="building-2"
        value={
          <RestrictedValue allowed={permitido} label={t.soloGerencia}>
            {u && <span className="mono">{u.ocupadas}</span>}
          </RestrictedValue>
        }
        meta={permitido && totalUnidades !== null ? t.deUnidades.replace('{n}', String(totalUnidades)) : undefined}
      />
    </>
  );
}
