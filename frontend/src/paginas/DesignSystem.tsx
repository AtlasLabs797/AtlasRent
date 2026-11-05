import type { JSX, ReactNode } from 'react';
import { Badge, type EstadoBadge } from '../design-system/components/core/Badge';
import { Button } from '../design-system/components/core/Button';
import { Card } from '../design-system/components/core/Card';
import { Kbd } from '../design-system/components/core/Kbd';
import { DataTable, type DataTableColumn } from '../design-system/components/data/DataTable';
import { Money } from '../design-system/components/data/Money';
import { StatCard } from '../design-system/components/data/StatCard';
import { UnitCard } from '../design-system/components/data/UnitCard';
import { Alert } from '../design-system/components/feedback/Alert';
import { EmptyState } from '../design-system/components/feedback/EmptyState';
import { Input } from '../design-system/components/forms/Input';
import { PageHeader } from '../design-system/components/navigation/PageHeader';
import { textos } from '../i18n/textos';
import { MuestraAmpliada } from './DesignSystemMuestras';
import { useTema } from '../tema/store';

const ESTADOS_UNIDAD: EstadoBadge[] = ['disponible', 'reservado', 'ocupado', 'mantenimiento', 'fuera'];
const ESTADOS_PAGO: EstadoBadge[] = ['pendiente', 'parcial', 'pagado', 'vencida', 'anulada'];

interface Fila {
  id: string;
  unidad: string;
  cliente: string;
  contrato: string;
  saldo: number;
  moneda: 'USD' | 'DOP';
}

const FILAS: Fila[] = [
  { id: 'u-4b', unidad: 'Torre Coral · 4B', cliente: 'María Santos', contrato: 'CT-0217', saldo: 1250, moneda: 'USD' },
  { id: 'u-2a', unidad: 'Torre Coral · 2A', cliente: 'Pedro Díaz', contrato: 'CT-0208', saldo: 18000, moneda: 'DOP' },
  { id: 'u-1c', unidad: 'Jardín Norte · 1C', cliente: 'Luisa Peña', contrato: 'CT-0199', saldo: 720, moneda: 'USD' },
];

const ds = textos.designSystem;

const COLUMNAS: DataTableColumn[] = [
  { key: 'unidad', label: ds.tabla.columnas.unidad },
  { key: 'cliente', label: ds.tabla.columnas.cliente },
  { key: 'contrato', label: ds.tabla.columnas.contrato },
  {
    key: 'saldo',
    label: ds.tabla.columnas.saldo,
    align: 'right',
    render: (fila: Fila) => <Money amount={fila.saldo} currency={fila.moneda} />,
  },
];

/**
 * Muestra de los componentes del design system.
 *
 * El design system solo define un bloque `[data-theme="dark"]`: los tokens claros viven en `:root`.
 * Por eso el bloque de la derecha se fuerza a oscuro con `data-theme="dark"` y el de la izquierda
 * muestra el tema activo, que cambia con el control de tema de la barra superior. No se inventa ningún token.
 */
export default function PaginaDesignSystem(): JSX.Element {
  const { tema } = useTema();

  return (
    <>
      <PageHeader eyebrow={ds.sobrelinea} title={ds.titulo} description={ds.descripcion}>
        <Button icon={ds.botones.primariaIcono}>{ds.botones.primariaAccion}</Button>
      </PageHeader>

      <div style={{ display: 'grid', gap: 'var(--space-10)' }}>
        <div>
          <div className="ar-overline">
            {ds.temaActivo} · {tema === 'dark' ? ds.temaOscuro : ds.temaClaro}
          </div>
          <MuestraComponentes />
          <div style={{ marginTop: 'var(--space-9)' }}>
            <MuestraAmpliada />
          </div>
        </div>
        <div
          data-theme="dark"
          style={{ background: 'var(--surface-app)', borderRadius: 'var(--radius-card)', padding: 'var(--space-9)' }}
        >
          <div className="ar-overline">{ds.temaOscuro}</div>
          <MuestraComponentes />
        </div>
      </div>
    </>
  );
}

function MuestraComponentes(): JSX.Element {
  return (
    <div style={{ display: 'grid', gap: 'var(--space-7)', marginTop: 'var(--space-6)' }}>
      <Seccion titulo={ds.botones.titulo} descripcion={ds.botones.descripcion}>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--space-6)', alignItems: 'center' }}>
          <Button icon={ds.botones.primariaIcono}>{ds.botones.primaria}</Button>
          <Button variant="secondary">{ds.botones.secundaria}</Button>
          <Button variant="ghost">{ds.botones.terciaria}</Button>
          <Button variant="danger">{ds.botones.peligro}</Button>
          <Button variant="ai" icon="sparkles">
            {ds.botones.ia}
          </Button>
        </div>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--space-6)', alignItems: 'center', marginTop: 'var(--space-6)' }}>
          <Button size="sm">{ds.botones.pequeno}</Button>
          <Button size="md">{ds.botones.mediano}</Button>
          <Button size="lg">{ds.botones.grande}</Button>
          <Kbd>⌘ K</Kbd>
        </div>
      </Seccion>

      <Seccion titulo={ds.badges.titulo} descripcion={ds.badges.descripcion}>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--space-4)' }}>
          {ESTADOS_UNIDAD.map((estado) => (
            <Badge key={estado} status={estado} />
          ))}
        </div>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--space-4)', marginTop: 'var(--space-6)' }}>
          {ESTADOS_PAGO.map((estado) => (
            <Badge key={estado} status={estado} />
          ))}
          <Badge tone="ai" square>
            {ds.badges.etiqueta}
          </Badge>
        </div>
      </Seccion>

      <Seccion titulo={ds.formularios.titulo} descripcion={ds.formularios.descripcion}>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--space-9)' }}>
          <Input label={ds.formularios.etiqueta} hint={ds.formularios.pista} placeholder="CLI-01873" />
          <Input label={ds.formularios.monto} defaultValue="1.250,00" suffix={ds.formularios.montoSufijo} />
          <Input label={ds.formularios.conError} error={ds.formularios.error} defaultValue="000" />
        </div>
      </Seccion>

      <Seccion titulo={ds.alertas.titulo} descripcion={ds.alertas.descripcion}>
        <div style={{ display: 'grid', gap: 'var(--space-6)' }}>
          <Alert tone="info" title={ds.alertas.infoTitulo}>
            {ds.alertas.infoCuerpo}
          </Alert>
          <Alert tone="warning" title={ds.alertas.avisoTitulo}>
            {ds.alertas.avisoCuerpo}
          </Alert>
          <Alert tone="danger" title={ds.alertas.peligroTitulo}>
            {ds.alertas.peligroCuerpo}
          </Alert>
        </div>
      </Seccion>

      <Seccion titulo={ds.kpi.titulo} descripcion={ds.kpi.descripcion}>
        <div style={{ display: 'grid', gap: 'var(--space-6)' }}>
          <StatCard featured label={ds.kpi.destacado} value={<Money amount={1250} currency="USD" />} meta={ds.kpi.destacadoMeta} />
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(160px,1fr))', gap: 'var(--space-6)' }}>
            <StatCard label={ds.kpi.pendiente} value={<Money amount={27900} currency="DOP" />} tone="warning" />
            <StatCard label={ds.kpi.vencido} value={<Money amount={3480} currency="DOP" />} tone="danger" />
            <StatCard label={ds.kpi.unidades} value="112 / 150" meta={ds.kpi.unidadesMeta} />
          </div>
        </div>
      </Seccion>

      <Seccion titulo={ds.tabla.titulo} descripcion={ds.tabla.descripcion}>
        <DataTable columns={COLUMNAS} rows={FILAS} empty={ds.tabla.vacia} />
      </Seccion>

      <Seccion titulo={ds.unidades.titulo} descripcion={ds.unidades.descripcion}>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(240px,1fr))', gap: 'var(--space-6)' }}>
          <UnitCard
            name="Torre Coral · 4B"
            property="Torre Coral"
            status="disponible"
            rent={1250}
            modalities={[ds.unidades.alquiler, ds.unidades.diario, ds.unidades.porHoras]}
          />
          <UnitCard name="Torre Coral · 2A" property="Torre Coral" status="ocupado" client="María Santos" until="31/12/2026" rent={1250} />
          <UnitCard name="Jardín Norte · 1C" property="Jardín Norte" status="reservado" rent={720} />
          <UnitCard name="Jardín Norte · 1D" property="Jardín Norte" status="mantenimiento" alert={ds.unidades.mantenimiento} />
          <UnitCard name="Torre Coral · 3F" property="Torre Coral" status="fuera" />
        </div>
      </Seccion>

      <Seccion titulo={ds.vacio.titulo} descripcion={ds.vacio.descripcion}>
        <EmptyState icon="file-text" title={ds.vacio.facturaTitulo}>
          {ds.vacio.facturaCuerpo}
        </EmptyState>
        <p style={{ margin: 'var(--space-7) 0 0', color: 'var(--text-subtle)' }}>{ds.vacio.atajo}</p>
      </Seccion>
    </div>
  );
}

function Seccion({ titulo, descripcion, children }: { titulo: string; descripcion: string; children: ReactNode }) {
  return (
    <Card title={titulo}>
      <p style={{ margin: '0 0 var(--space-6)', color: 'var(--text-muted)' }}>{descripcion}</p>
      {children}
    </Card>
  );
}