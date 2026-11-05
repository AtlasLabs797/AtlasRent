import type { CSSProperties, JSX } from 'react';
import { Icon } from '../core/Icon';
import { IconButton } from '../core/IconButton';

/** One generated PDF (cotización, contrato, inventario/anexo, factura, recibo) with download + send and its last delivery. */
export interface DocumentRowProps {
  name: string;
  type?: 'cotizacion' | 'contrato' | 'inventario' | 'anexo' | 'factura' | 'recibo';
  record?: string;
  date?: string;
  size?: string;
  /** e.g. "Leído por WhatsApp · hace 2h" */
  sent?: string;
  onDownload?: () => void;
  onSend?: () => void;
  style?: CSSProperties;
}

const TIPOS = { cotizacion: 'Cotización', contrato: 'Contrato', inventario: 'Inventario', anexo: 'Anexo', factura: 'Factura', recibo: 'Recibo' } as const;

export function DocumentRow({ name, type = 'factura', record, date, size, sent, onDownload, onSend, style }: DocumentRowProps): JSX.Element {
  const icono = type === 'contrato' ? 'scroll-text' : type === 'recibo' ? 'receipt' : type === 'inventario' ? 'clipboard-list' : 'file-text';
  return (
    <div className="ar-doc" style={style}>
      <span className="ar-doc__icon">
        <Icon name={icono} size={18} />
      </span>
      <div style={{ minWidth: 0 }}>
        <div style={{ font: 'var(--type-body-sm)', color: 'var(--text-strong)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{name}</div>
        <div style={{ font: 'var(--type-fine)', color: 'var(--text-subtle)', display: 'flex', gap: 6, flexWrap: 'wrap' }}>
          <span>{TIPOS[type] || type}</span>
          {record && <span className="mono">· {record}</span>}
          {date && <span className="mono">· {date}</span>}
          {size && <span className="mono">· {size}</span>}
          {sent && <span style={{ color: 'var(--success)' }}>· {sent}</span>}
        </div>
      </div>
      <div style={{ display: 'flex', gap: 4 }}>
        <IconButton size="sm" icon="download" label="Descargar PDF" onClick={onDownload} />
        <IconButton size="sm" icon="send" label="Enviar por WhatsApp" onClick={onSend} />
      </div>
    </div>
  );
}
