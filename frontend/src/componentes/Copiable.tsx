import { useState, type JSX } from 'react';
import { IconButton } from '../design-system/components/core/IconButton';
import { textos } from '../i18n/textos';

/** Texto en mono que se puede seleccionar y copiar. Sin portapapeles (HTTP, permisos) deja el texto seleccionable y avisa. */
export function Copiable({ texto, etiqueta, multilinea = false }: { texto: string; etiqueta: string; multilinea?: boolean }): JSX.Element {
  const [estado, setEstado] = useState<'inicial' | 'copiado' | 'fallo'>('inicial');
  async function copiar() {
    try {
      await navigator.clipboard.writeText(texto);
      setEstado('copiado');
    } catch {
      setEstado('fallo');
    }
  }
  return (
    <div>
      <div style={{ display: 'flex', alignItems: 'flex-start', gap: 'var(--space-4)' }}>
        <code
          className="mono"
          aria-label={etiqueta}
          style={{
            flex: 1,
            minWidth: 0,
            padding: 'var(--space-5) var(--space-6)',
            background: 'var(--surface-sunken)',
            border: '1px solid var(--border-default)',
            borderRadius: 'var(--radius-sm)',
            color: 'var(--text-strong)',
            wordBreak: 'break-all',
            whiteSpace: multilinea ? 'pre-wrap' : 'normal',
            userSelect: 'all',
          }}
        >
          {texto}
        </code>
        <IconButton icon={estado === 'copiado' ? 'check' : 'copy'} label={`${textos.acceso.altaMfa.copiar}: ${etiqueta}`} onClick={() => void copiar()} />
      </div>
      <div role="status" style={{ font: 'var(--type-fine)', color: 'var(--text-muted)', minHeight: 16, marginTop: 4 }}>
        {estado === 'copiado' ? textos.acceso.altaMfa.copiado : estado === 'fallo' ? textos.acceso.altaMfa.sinPortapapeles : ''}
      </div>
    </div>
  );
}
