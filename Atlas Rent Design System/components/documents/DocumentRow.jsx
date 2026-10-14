import React from 'react';
import { Icon } from '../core/Icon.jsx';
import { IconButton } from '../core/IconButton.jsx';
const T = { cotizacion: 'Cotización', contrato: 'Contrato', inventario: 'Inventario', anexo: 'Anexo', factura: 'Factura', recibo: 'Recibo' };
export function DocumentRow({ name, type = 'factura', record, date, size, sent, onDownload, onSend, style }) {
  return <div className="ar-doc" style={style}>
    <span className="ar-doc__icon"><Icon name={type === 'contrato' ? 'scroll-text' : type === 'recibo' ? 'receipt' : type === 'inventario' ? 'clipboard-list' : 'file-text'} size={18} /></span>
    <div style={{ minWidth: 0 }}><div style={{ font: 'var(--type-body-sm)', color: 'var(--text-strong)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{name}</div><div style={{ font: 'var(--type-fine)', color: 'var(--text-subtle)', display: 'flex', gap: 6, flexWrap: 'wrap' }}><span>{T[type] || type}</span>{record && <span className="mono">· {record}</span>}{date && <span className="mono">· {date}</span>}{size && <span className="mono">· {size}</span>}{sent && <span style={{ color: 'var(--success)' }}>· {sent}</span>}</div></div>
    <div style={{ display: 'flex', gap: 4 }}><IconButton size="sm" icon="download" label="Descargar PDF" onClick={onDownload} /><IconButton size="sm" icon="send" label="Enviar por WhatsApp" onClick={onSend} /></div>
  </div>;
}