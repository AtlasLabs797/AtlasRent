import { useState, type JSX } from 'react';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Badge } from '../../../design-system/components/core/Badge';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { KeyValueList } from '../../../design-system/components/data/KeyValueList';
import { Money } from '../../../design-system/components/data/Money';
import { DatePicker } from '../../../design-system/components/forms/DatePicker';
import { Input } from '../../../design-system/components/forms/Input';
import { formatearFecha } from '../../../dominio/fechas';
import { useComprobanteRetencion, useRecibirComprobanteRetencion } from '../api';
import { textosFinanzas } from '../textos';
import type { ComprobanteRetencion, Moneda } from '../tipos';
import { fechaHoyNegocio } from './Comunes';

const f = textosFinanzas.facturas.ficha;

function FormularioRecibido({ comprobante }: { comprobante: ComprobanteRetencion }): JSX.Element {
  const recibir = useRecibirComprobanteRetencion(comprobante.id);
  const [numero, setNumero] = useState('');
  const [fecha, setFecha] = useState(fechaHoyNegocio());
  return (
    <form
      style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(220px,1fr))', gap: 12, alignItems: 'end', marginTop: 'var(--space-7)' }}
      onSubmit={(e) => {
        e.preventDefault();
        recibir.mutate({ numero: numero.trim(), fechaRecepcion: fecha });
      }}
    >
      <Input label={f.retencionNumero} required value={numero} maxLength={120} onChange={(e) => setNumero(e.target.value)} />
      <DatePicker label={f.retencionFecha} required value={fecha} onChange={(e) => setFecha(e.target.value)} />
      <div>
        <Button type="submit" disabled={numero.trim() === '' || fecha === '' || recibir.isPending}>
          {recibir.isPending ? f.retencionRegistrando : f.retencionRegistrar}
        </Button>
      </div>
      <p style={{ margin: 0, gridColumn: '1 / -1', font: 'var(--type-caption)', color: 'var(--text-subtle)' }}>{f.retencionAyuda}</p>
      {recibir.isError && (
        <div style={{ gridColumn: '1 / -1' }}>
          <ErrorAlerta error={recibir.error} titulo={f.retencionError} />
        </div>
      )}
    </form>
  );
}

/** Estado del comprobante de retención de la factura y, para quien emite, el registro del recibido. */
export function TarjetaComprobanteRetencion({ facturaId, moneda, puedeRegistrar }: { facturaId: string; moneda: Moneda; puedeRegistrar: boolean }): JSX.Element | null {
  const consulta = useComprobanteRetencion(facturaId, true);
  const c = consulta.data;
  if (!c) return null;
  const recibido = c.estado === 'Recibido';
  return (
    <Card title={f.comprobanteRetencion} actions={recibido ? <Badge tone="success" dot>{f.retencionEstados.Recibido}</Badge> : <Badge tone="warning" dot>{f.retencionEstados.Pendiente}</Badge>}>
      <KeyValueList
        columns={3}
        items={[
          { label: f.retencionTipo, value: c.tipo },
          { label: f.retencionImporte, value: <Money amount={c.importe} currency={moneda} /> },
          { label: f.retencionNumero, value: c.numero, mono: true },
          ...(recibido && c.fechaRecepcion ? [{ label: f.retencionFecha, value: formatearFecha(c.fechaRecepcion), mono: true }] : []),
        ]}
      />
      {!recibido && puedeRegistrar && <FormularioRecibido comprobante={c} />}
    </Card>
  );
}
