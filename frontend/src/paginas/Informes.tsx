import { lazy, Suspense, useMemo, useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useSearchParams } from 'react-router';
import { Button } from '../design-system/components/core/Button';
import { Card } from '../design-system/components/core/Card';
import { Money } from '../design-system/components/data/Money';
import { StatCard } from '../design-system/components/data/StatCard';
import { SegmentedControl } from '../design-system/components/navigation/SegmentedControl';
import { Alert } from '../design-system/components/feedback/Alert';
import { EmptyState } from '../design-system/components/feedback/EmptyState';
import { Input } from '../design-system/components/forms/Input';
import { PageHeader } from '../design-system/components/navigation/PageHeader';
import { textos } from '../i18n/textos';
import { ACCIONES } from '../permisos';
import { puede, useSesion } from '../sesion/store';
import {
  descargarExportacion,
  pedirDashboard,
  pedirResumen,
  type FormatoExportacion,
  type ImporteInforme,
} from '../informes/api';
import {
  clavesDeEntidad,
  claveDeFiltros,
  ESTADOS,
  fechaCorta,
  filtrosDesdeParametros,
  MONEDAS,
  parametrosDeFiltros,
  type FiltrosInformes,
} from '../informes/filtros';
import SinPermiso from './SinPermiso';

// Recharts solo hace falta aquí, y esta pantalla ya es un trozo aparte: cargarlo también diferido
// deja el resto de la aplicación sin esa dependencia en la primera descarga.
const GraficoMensual = lazy(() => import('../informes/GraficoMensual'));

/**
 * Informes y dashboard (F5-01, `docs/ESPECIFICACION.md` §3.15).
 *
 * El estado de los filtros vive en la query string, no en un `useState`: la pantalla se puede
 * enlazar y el `queryKey` sale de los mismos filtros, así que el informe de la URL es el informe que
 * se ve. Los importes se muestran tal cual los devuelve la API — el consolidado en dólares lo calcula
 * el backend con el cambio congelado de cada documento (D9).
 *
 * `informe.ver` se comprueba aquí y en la ruta: el backend lo exigiría igual, pero una pantalla que
 * promete importes y solo puede dar un 403 es una pantalla que miente (AGENTS.md §6).
 */
export default function Informes() {
  const identidad = useSesion((estado) => estado.identidad);
  const [parametrosUrl, setParametrosUrl] = useSearchParams();
  const filtros = useMemo(() => filtrosDesdeParametros(parametrosUrl), [parametrosUrl]);
  const clave = claveDeFiltros(filtros);
  const { filtros: f, ...t } = textos.informes;

  const resumen = useQuery({
    queryKey: ['informes', 'resumen', clave],
    queryFn: () => pedirResumen(filtros),
  });
  const dashboard = useQuery({
    queryKey: ['informes', 'dashboard', clave],
    queryFn: () => pedirDashboard(filtros),
  });

  const [exportando, setExportando] = useState<FormatoExportacion | null>(null);
  const [falloExportacion, setFalloExportacion] = useState(false);

  function cambiar(cambios: Partial<FiltrosInformes>) {
    setParametrosUrl(parametrosDeFiltros({ ...filtros, ...cambios }), { replace: true });
  }

  async function exportar(formato: FormatoExportacion) {
    setExportando(formato);
    setFalloExportacion(false);
    try {
      await descargarExportacion(filtros, formato);
    } catch {
      setFalloExportacion(true);
    } finally {
      setExportando(null);
    }
  }

  if (!puede(identidad, ACCIONES.informeVer)) return <SinPermiso />;

  const informe = resumen.data ?? null;
  const dia = dashboard.data ?? null;
  const cargando = resumen.isPending || dashboard.isPending;
  // Una llamada que falla es un error, nunca un informe a cero: distinguirlo importa.
  const fallo = resumen.isError || dashboard.isError;
  const entidades = clavesDeEntidad(filtros);

  return (
    <>
      <PageHeader
        eyebrow={t.sobrelinea}
        title={t.titulo}
        description={t.descripcion}
        breadcrumbs={[
          { label: textos.navegacion.grupos.finanzas },
          { label: textos.navegacion.modulos.informes.etiqueta },
        ]}
      >
        <Button
          variant="secondary"
          icon="file-spreadsheet"
          disabled={exportando !== null}
          onClick={() => void exportar('xlsx')}
        >
          {exportando === 'xlsx' ? t.exportar.enCurso : t.exportar.excel}
        </Button>
        <Button
          variant="ghost"
          icon="file-text"
          disabled={exportando !== null}
          onClick={() => void exportar('csv')}
        >
          {exportando === 'csv' ? t.exportar.enCurso : t.exportar.csv}
        </Button>
      </PageHeader>

      {falloExportacion && (
        <Alert tone="danger" title={t.exportar.error}>
          {t.exportar.errorCuerpo}
        </Alert>
      )}

      {entidades.length > 0 && (
        <Alert tone="info" title={t.entidades.titulo}>
          {t.entidades.cuerpo} · {entidades.join(' · ')}
        </Alert>
      )}

      <Card title={f.titulo}>
        <p style={{ margin: '0 0 var(--space-9)', color: 'var(--text-muted)' }}>{f.descripcion}</p>
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fit,minmax(180px,1fr))',
            gap: 'var(--space-6)',
          }}
        >
          <Input
            label={f.desde}
            type="date"
            value={filtros.desde ?? ''}
            onChange={(e) => cambiar({ desde: e.target.value || null })}
          />
          <Input
            label={f.hasta}
            type="date"
            value={filtros.hasta ?? ''}
            onChange={(e) => cambiar({ hasta: e.target.value || null })}
          />
          <div className="ar-field">
            <span className="ar-label" id="etiqueta-moneda">
              {f.moneda}
            </span>
            <div role="group" aria-labelledby="etiqueta-moneda">
              <SegmentedControl
                options={[
                  { value: '', label: f.todasMonedas },
                  ...MONEDAS.map((valor) => ({ value: valor, label: valor })),
                ]}
                value={filtros.moneda ?? ''}
                onChange={(valor) =>
                  cambiar({ moneda: valor === '' ? null : (valor as (typeof MONEDAS)[number]) })
                }
              />
            </div>
          </div>
          {/* El design system trae `Select` pero todavía no está portado al frontend; este es un
              `<select>` nativo con las clases del DS (`.ar-field` + `.ar-control`), que es lo que
              sus hojas de estilo ya contemplan. Pendiente de portar `Select` a `paginas/` de F1-13. */}
          <div className="ar-field">
            <label className="ar-label" htmlFor="filtro-estado">
              {f.estado}
            </label>
            <div className="ar-control">
              <select
                id="filtro-estado"
                value={filtros.estado ?? ''}
                onChange={(e) => cambiar({ estado: (e.target.value || null) as FiltrosInformes['estado'] })}
              >
                <option value="">{f.todosEstados}</option>
                {ESTADOS.map((estado) => (
                  <option key={estado} value={estado}>
                    {f.estados[estado]}
                  </option>
                ))}
              </select>
            </div>
          </div>
        </div>
        <p style={{ margin: 'var(--space-9) 0 0', color: 'var(--text-subtle)' }}>{f.sinFechas}</p>
      </Card>

      <div style={{ height: 24 }} />

      {cargando && (
        <Card>
          <p style={{ margin: 0, color: 'var(--text-muted)' }}>{t.cargando}</p>
        </Card>
      )}

      {fallo && (
        <Alert tone="danger" title={t.error.titulo}>
          {t.error.cuerpo}
        </Alert>
      )}

      {!cargando && !fallo && informe && dia && (
        <>
          <Seccion titulo={t.unidades.titulo} descripcion={t.unidades.descripcion}>
            <div
              style={{
                display: 'grid',
                gridTemplateColumns: 'repeat(auto-fit,minmax(180px,1fr))',
                gap: 'var(--space-6)',
              }}
            >
              <StatCard label={t.unidades.disponibles} value={dia.unidades.disponibles} />
              <StatCard label={t.unidades.reservadas} value={dia.unidades.reservadas} />
              <StatCard label={t.unidades.ocupadas} value={dia.unidades.ocupadas} />
              <StatCard label={t.unidades.mantenimiento} value={dia.unidades.mantenimiento} />
              <StatCard
                label={t.unidades.contratosActivos}
                value={dia.contratosActivos}
                meta={t.unidades.fecha.replace('{fecha}', fechaCorta(dia.fecha))}
              />
            </div>
          </Seccion>

          {informe.documentosSinTipoCambioCongelado > 0 && (
            <Alert tone="warning" title={t.sinCambio.titulo}>
              {t.sinCambio.cuerpo.replace('{n}', String(informe.documentosSinTipoCambioCongelado))}
            </Alert>
          )}

          {informe.saldoAFavorFueraDePeriodo && (
            <Alert tone="warning" title={t.saldoFueraDePeriodo.titulo}>
              {t.saldoFueraDePeriodo.cuerpo}
            </Alert>
          )}

          <Seccion titulo={t.resumen.titulo} descripcion={t.resumen.descripcion}>
            <div
              style={{
                display: 'grid',
                gridTemplateColumns: 'repeat(auto-fit,minmax(280px,1fr))',
                gap: 'var(--space-6)',
              }}
            >
              <Bloque etiqueta={t.resumen.proformado} importe={informe.proformado} />
              <Bloque etiqueta={t.resumen.facturado} importe={informe.facturado} />
              <Bloque etiqueta={t.resumen.cobrado} importe={informe.cobrado.total} />
              <Bloque etiqueta={t.resumen.retenido} importe={informe.retenido.total} />
              <Bloque etiqueta={t.resumen.pendiente} importe={informe.pendiente.total} />
              <Bloque etiqueta={t.resumen.vencido} importe={informe.vencido} tone="danger" />
              <Bloque etiqueta={t.resumen.fianzasRetenidas} importe={informe.fianzasRetenidas} />
              <Bloque etiqueta={t.resumen.diferenciasCambio} importe={informe.diferenciasCambio} />
            </div>
            <p style={{ margin: 'var(--space-6) 0 0', color: 'var(--text-subtle)' }}>
              {t.resumen.fechaCorte.replace('{fecha}', fechaCorta(informe.fechaCorte))}
            </p>
          </Seccion>

          <Seccion titulo={t.grafico.titulo} descripcion={t.grafico.descripcion}>
            <Card>
              {informe.serieMensual.length === 0 ? (
                <EmptyState icon="chart-column" title={t.grafico.vacioTitulo}>
                  {t.grafico.vacioCuerpo}
                </EmptyState>
              ) : (
                <Suspense
                  fallback={<p style={{ margin: 0, color: 'var(--text-muted)' }}>{t.cargando}</p>}
                >
                  <GraficoMensual serie={informe.serieMensual} />
                </Suspense>
              )}
            </Card>
          </Seccion>
        </>
      )}
    </>
  );
}

/**
 * Un bloque de §3.15. Arriba el consolidado en dólares —la única cifra comparable entre documentos de
 * distinta moneda— y abajo las dos monedas de los documentos, tal como vinieron.
 */
function Bloque({ etiqueta, importe, tone }: { etiqueta: string; importe: ImporteInforme; tone?: 'danger' }) {
  return (
    <StatCard
      label={etiqueta}
      value={<Money amount={importe.consolidadoUsd} currency="USD" tone={tone} />}
      meta={
        <>
          <Money amount={importe.usd} currency="USD" tone="muted" /> ·{' '}
          <Money amount={importe.dop} currency="DOP" tone="muted" />
        </>
      }
    />
  );
}

/** Sobrelinea + descripción, como `PageHeader` pero para una sección dentro de la pantalla. */
function Seccion({ titulo, descripcion, children }: { titulo: string; descripcion: string; children: ReactNode }) {
  return (
    <section style={{ marginBottom: 'var(--space-14)' }}>
      <div className="ar-overline">{titulo}</div>
      <p style={{ margin: '4px 0 var(--space-9)', color: 'var(--text-muted)' }}>{descripcion}</p>
      {children}
    </section>
  );
}