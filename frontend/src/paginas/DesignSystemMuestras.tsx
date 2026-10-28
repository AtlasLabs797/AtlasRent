import { useState, type JSX, type ReactNode } from 'react';
import { AIActionButton } from '../design-system/components/ai/AIActionButton';
import { AgentBadge } from '../design-system/components/ai/AgentBadge';
import { ApprovalCard } from '../design-system/components/ai/ApprovalCard';
import { ChatPanel } from '../design-system/components/ai/ChatPanel';
import { Button } from '../design-system/components/core/Button';
import { IconButton } from '../design-system/components/core/IconButton';
import { Card } from '../design-system/components/core/Card';
import { AuditLog } from '../design-system/components/data/AuditLog';
import { DepositSummary } from '../design-system/components/data/DepositSummary';
import { HourSlots } from '../design-system/components/data/HourSlots';
import { InventoryChecklist, type InventoryItem } from '../design-system/components/data/InventoryChecklist';
import { InvoiceLines, type InvoiceLine } from '../design-system/components/data/InvoiceLines';
import { Money } from '../design-system/components/data/Money';
import { OccupancyBar } from '../design-system/components/data/OccupancyBar';
import { PaymentAllocation, type AllocationInvoice } from '../design-system/components/data/PaymentAllocation';
import { ProvenanceTag } from '../design-system/components/data/ProvenanceTag';
import { RestrictedValue } from '../design-system/components/data/RestrictedValue';
import { Timeline } from '../design-system/components/data/Timeline';
import { DocumentRow } from '../design-system/components/documents/DocumentRow';
import { MessagePreview } from '../design-system/components/documents/MessagePreview';
import { Dialog } from '../design-system/components/feedback/Dialog';
import { Drawer } from '../design-system/components/feedback/Drawer';
import { Skeleton } from '../design-system/components/feedback/Skeleton';
import { Toast } from '../design-system/components/feedback/Toast';
import { Tooltip } from '../design-system/components/feedback/Tooltip';
import { Checkbox } from '../design-system/components/forms/Checkbox';
import { Combobox } from '../design-system/components/forms/Combobox';
import { DatePicker } from '../design-system/components/forms/DatePicker';
import { Radio } from '../design-system/components/forms/Radio';
import { Select } from '../design-system/components/forms/Select';
import { Switch } from '../design-system/components/forms/Switch';
import { Textarea } from '../design-system/components/forms/Textarea';
import { CommandPalette } from '../design-system/components/navigation/CommandPalette';
import { FilterBar } from '../design-system/components/navigation/FilterBar';
import { Menu } from '../design-system/components/navigation/Menu';
import { Pagination } from '../design-system/components/navigation/Pagination';
import { SectionHeader } from '../design-system/components/navigation/SectionHeader';
import { Stepper } from '../design-system/components/navigation/Stepper';
import { Tabs } from '../design-system/components/navigation/Tabs';
import { ImportBatch } from '../design-system/components/workflow/ImportBatch';
import { ReviewItem } from '../design-system/components/workflow/ReviewItem';
import { ConfirmarDestructivo } from '../componentes/ConfirmarDestructivo';
import { TablaVirtual } from '../componentes/TablaVirtual';
import { formatearImporte } from '../dominio/dinero';
import { textos } from '../i18n/textos';

/**
 * Datos de muestra de la página interna de componentes (no es texto de producto): nombres y cifras
 * inventados para ver cada componente con contenido realista. Importes en string decimal, como la API.
 */
const m = textos.designSystem.muestras;

const FILAS_LARGAS = Array.from({ length: 2000 }, (_, i) => ({
  id: `FAC-A-2026-${String(i + 1).padStart(5, '0')}`,
  cliente: `Cliente de muestra ${i + 1}`,
  saldo: formatearImporte(String(((i * 37) % 5000) + 0.5), 'USD'),
}));

const COLUMNAS_LARGAS = [
  { key: 'id', label: m.tablaVirtual.numero, render: (f: { id: string }) => <span className="mono">{f.id}</span> },
  { key: 'cliente', label: m.tablaVirtual.cliente },
  { key: 'saldo', label: m.tablaVirtual.saldo, align: 'right' as const, render: (f: { saldo: string }) => <span className="mono">{f.saldo}</span> },
];

function Bloque({ titulo, children }: { titulo: string; children: ReactNode }): JSX.Element {
  return (
    <Card title={titulo}>
      <div style={{ display: 'grid', gap: 'var(--space-7)' }}>{children}</div>
    </Card>
  );
}

/** Los componentes añadidos al portar el design system completo, más los patrones propios (tabla virtual, confirmación destructiva). */
export function MuestraAmpliada(): JSX.Element {
  const [pestana, setPestana] = useState('general');
  const [pagina, setPagina] = useState(2);
  const [lineas, setLineas] = useState<InvoiceLine[]>([
    { concept: 'Renta mensual', detail: 'Marzo 2026', qty: 1, price: 1250 },
    { concept: 'Descuento', detail: 'Pronto pago', qty: 1, price: 50 },
  ]);
  const [cobros, setCobros] = useState<AllocationInvoice[]>([
    { id: 'FAC-A-2026-00410', concept: 'Renta mensual', period: 'Feb 2026', pending: 1250, overdue: true },
    { id: 'FAC-A-2026-00412', concept: 'Renta mensual', period: 'Mar 2026', pending: 1250 },
  ]);
  const [inventario, setInventario] = useState<InventoryItem[]>([
    { name: 'Sofá de tres plazas', qty: 1, state: 'ok' },
    { name: 'Silla de comedor', qty: 4, state: 'faltante', amount: '80' },
    { name: 'Televisor 43 pulgadas', qty: 1, state: 'danado', amount: '220' },
  ]);
  const [dialogo, setDialogo] = useState(false);
  const [drawer, setDrawer] = useState(false);
  const [anular, setAnular] = useState(false);
  const [ultimaAnulacion, setUltimaAnulacion] = useState<string | null>(null);

  return (
    <div style={{ display: 'grid', gap: 'var(--space-7)' }}>
      <SectionHeader eyebrow={m.sobrelinea} title={m.titulo} description={m.descripcion} />

      <Bloque titulo={m.formularios.titulo}>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--space-9)' }}>
          <Select label={m.formularios.moneda} options={['USD', 'DOP']} style={{ width: 160 }} />
          <Combobox
            label={m.formularios.cliente}
            style={{ width: 280 }}
            options={[
              { value: '1', label: 'María Santos', sub: m.formularios.clienteSub, meta: 'CLI-01873' },
              { value: '2', label: 'Pedro Díaz', sub: m.formularios.clienteSub, meta: 'CLI-01874' },
            ]}
          />
          <DatePicker label={m.formularios.fecha} defaultValue="2026-03-05" />
        </div>
        <Textarea label={m.formularios.observaciones} hint={m.formularios.observacionesPista} />
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--space-9)', alignItems: 'center' }}>
          <Checkbox label={m.formularios.casilla} defaultChecked />
          <Radio name="muestra-radio" label={m.formularios.radioA} defaultChecked />
          <Radio name="muestra-radio" label={m.formularios.radioB} />
          <Switch label={m.formularios.interruptor} defaultChecked />
          <Switch tone="ai" label={m.formularios.interruptorIa} />
        </div>
      </Bloque>

      <Bloque titulo={m.navegacion.titulo}>
        <Tabs
          tabs={[
            { label: m.navegacion.general, value: 'general' },
            { label: m.navegacion.cobros, value: 'cobros', count: 3 },
            { label: m.navegacion.documentos, value: 'documentos' },
          ]}
          value={pestana}
          onChange={setPestana}
        />
        <FilterBar
          searchPlaceholder={m.navegacion.buscar}
          filters={[{ key: 'estado', label: m.navegacion.estado, value: m.navegacion.pendiente }]}
        />
        <Stepper
          steps={[
            { label: m.navegacion.reserva, status: 'done' },
            { label: m.navegacion.contrato, status: 'current' },
            { label: m.navegacion.factura, status: 'todo' },
          ]}
        />
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--space-6)', alignItems: 'center' }}>
          <Menu
            trigger={<Button variant="secondary" iconRight="chevron-down">{m.navegacion.mas}</Button>}
            items={[
              { label: m.navegacion.editar, icon: 'pencil' },
              { label: m.navegacion.duplicar, icon: 'copy' },
              { separator: true },
              { label: m.navegacion.anular, icon: 'ban', danger: true },
            ]}
          />
          <AIActionButton actions={[m.navegacion.resumir, m.navegacion.redactar]} />
          <Tooltip content={m.navegacion.ayuda}>
            <IconButton icon="info" label={m.navegacion.ayuda} />
          </Tooltip>
        </div>
        <Pagination page={pagina} pageSize={25} total={150} onChange={setPagina} />
        <CommandPalette
          inline
          open
          hotkey={false}
          groups={[
            { label: m.navegacion.grupoClientes, items: [{ label: 'María Santos', sub: m.formularios.clienteSub, meta: 'CLI-01873', icon: 'user' }] },
            { label: m.navegacion.grupoAcciones, items: [{ label: m.navegacion.registrarCobro, icon: 'banknote' }] },
          ]}
        />
      </Bloque>

      <Bloque titulo={m.datos.titulo}>
        <OccupancyBar counts={{ disponible: 28, reservado: 9, ocupado: 104, mantenimiento: 6, fuera: 3 }} />
        <HourSlots
          resources={[
            { name: 'Sala Gardenia', meta: m.datos.capacidad, bookings: [{ start: '09:00', end: '11:00', label: 'Reunión', status: 'reservado' }] },
            { name: 'Oficina 2', bookings: [{ start: '14:00', end: '18:00', status: 'ocupado' }] },
          ]}
          proposed={{ resource: 'Sala Gardenia', start: '10:00', end: '12:00' }}
        />
        <InventoryChecklist items={inventario} onChange={setInventario} editable frozenFrom={m.datos.inventarioBase} />
        <InvoiceLines lines={lineas} onChange={setLineas} editable rate={62} />
        <PaymentAllocation amount={1500} invoices={cobros} onChange={setCobros} />
        <DepositSummary required={2500} count={2} collected={2500} contract="CT-0217" deductions={[{ reason: m.datos.descuentoFianza, amount: 300 }]} />
        <Timeline
          items={[
            { title: m.datos.lineaContrato, time: '01/03/2026', icon: 'file-signature', tone: 'success' },
            { title: m.datos.lineaIncremento, body: m.datos.lineaIncrementoCuerpo, time: '01/03/2027', icon: 'trending-up', tone: 'info' },
          ]}
        />
        <AuditLog
          entries={[
            { actor: 'Marta Peña', action: m.datos.auditoriaAccion, record: 'CT-0217', field: m.datos.auditoriaCampo, before: '1.200,00 USD', after: '1.250,00 USD', reason: m.datos.auditoriaMotivo, at: '05/03/2026 10:14', origin: 'manual' },
          ]}
        />
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--space-6)', alignItems: 'center' }}>
          <ProvenanceTag origin="factusol" />
          <ProvenanceTag origin="agente" />
          <RestrictedValue>
            <Money amount="1250.00" currency="USD" />
          </RestrictedValue>
          <RestrictedValue label={m.datos.soloGerencia} />
        </div>
      </Bloque>

      <Bloque titulo={m.retroalimentacion.titulo}>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(260px,1fr))', gap: 'var(--space-6)' }}>
          <Skeleton height={20} />
          <Skeleton lines={3} />
        </div>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--space-6)' }}>
          <Button variant="secondary" onClick={() => setDialogo(true)}>
            {m.retroalimentacion.abrirDialogo}
          </Button>
          <Button variant="secondary" onClick={() => setDrawer(true)}>
            {m.retroalimentacion.abrirPanel}
          </Button>
          <Button variant="danger" onClick={() => setAnular(true)}>
            {m.retroalimentacion.anular}
          </Button>
        </div>
        {ultimaAnulacion && <p style={{ margin: 0, color: 'var(--text-muted)' }}>{m.retroalimentacion.motivoGuardado.replace('{motivo}', ultimaAnulacion)}</p>}
        <Toast tone="success" style={{ position: 'static', transform: 'none' }}>
          {m.retroalimentacion.toast}
        </Toast>
        <Dialog open={dialogo} title={m.retroalimentacion.dialogoTitulo} description={m.retroalimentacion.dialogoDescripcion} onClose={() => setDialogo(false)} footer={<Button onClick={() => setDialogo(false)}>{m.retroalimentacion.entendido}</Button>}>
          {m.retroalimentacion.dialogoCuerpo}
        </Dialog>
        <Drawer open={drawer} title={m.retroalimentacion.panelTitulo} onClose={() => setDrawer(false)}>
          {m.retroalimentacion.panelCuerpo}
        </Drawer>
        <ConfirmarDestructivo
          open={anular}
          accion={m.retroalimentacion.anular}
          consecuencia={m.retroalimentacion.anularConsecuencia}
          onConfirmar={async (motivo) => setUltimaAnulacion(motivo)}
          onCerrar={() => setAnular(false)}
        />
      </Bloque>

      <Bloque titulo={m.tablaVirtual.titulo}>
        <p style={{ margin: 0, color: 'var(--text-muted)' }}>{m.tablaVirtual.descripcion}</p>
        <TablaVirtual columns={COLUMNAS_LARGAS} rows={FILAS_LARGAS} alto={320} />
      </Bloque>

      <Bloque titulo={m.documentos.titulo}>
        <DocumentRow name="FAC-A-2026-00412.pdf" type="factura" record="CT-0217" date="05/03/2026" size="84 kB" sent={m.documentos.leido} />
        <MessagePreview to="María Santos" channel="WhatsApp" template={m.documentos.plantilla} text={m.documentos.mensaje} attachments={[{ name: 'FAC-A-2026-00412.pdf', size: '84 kB' }]} status="leido" time="10:14" />
      </Bloque>

      <Bloque titulo={m.ia.titulo}>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--space-6)' }}>
          <AgentBadge agent="cobros" />
          <AgentBadge agent="migracion" />
        </div>
        <ApprovalCard agent="Agente de cobros" action={m.ia.accion} reason={m.ia.motivo} record="FAC-A-2026-00410" current="1.250,00 DOP" proposed="1.300,00 DOP" origin={m.ia.origen} risk="medio" requestedAt="hoy 09:12" />
        <ChatPanel
          style={{ position: 'static', width: '100%', maxWidth: 420, height: 320 }}
          messages={[
            { role: 'user', text: m.ia.pregunta },
            { role: 'assistant', text: m.ia.respuesta, tools: ['facturas.listar'] },
          ]}
        />
      </Bloque>

      <Bloque titulo={m.flujos.titulo}>
        <ReviewItem preparedBy="Marta Peña" type={m.flujos.tipo} title={m.flujos.titulo2} record="FAC-A-2026-00410" date="05/03/2026" reason={m.flujos.motivo} />
        <ImportBatch id="LOTE-0007" file="clientes.xlsx" entity={m.flujos.entidad} date="05/03/2026" counts={{ total: 150, ok: 142, warnings: 6, errors: 2 }} status="errores" />
      </Bloque>
    </div>
  );
}
