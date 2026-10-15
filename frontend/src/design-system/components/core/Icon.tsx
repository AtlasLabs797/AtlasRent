import type { CSSProperties, JSX } from 'react';
import { Circle, icons, type LucideIcon } from 'lucide-react';

/**
 * Lucide icon rendered inline.
 * DESIGN.md §1.3: reescrito con `lucide-react` (el .jsx original baixaba el SVG de unpkg por `mask`).
 * La clase `ar-icon` del DS está pensada para esa máscara y pintaría un cuadro macizo sobre el SVG,
 * así que aquí se aplica el layout equivalente en línea y sin clase.
 */
export interface IconProps {
  /** Lucide icon name, kebab-case (p. ej. "file-text") */
  name: string;
  size?: number;
  color?: string;
  style?: CSSProperties;
  className?: string;
}

/** "chevron-right" → "ChevronRight", que es como lucide-react exporta cada icono. */
function nombreExportado(name: string): string {
  return name
    .split('-')
    .map((parte) => parte.charAt(0).toUpperCase() + parte.slice(1))
    .join('');
}

export function Icon({ name, size = 16, color, style, className = '' }: IconProps): JSX.Element {
  const catalogo = icons as unknown as Record<string, LucideIcon>;
  const Componente = catalogo[nombreExportado(name)] ?? Circle;
  return (
    <Componente
      aria-hidden="true"
      focusable="false"
      size={size}
      color={color}
      className={className}
      style={{ display: 'inline-block', flex: 'none', verticalAlign: '-0.125em', ...style }}
    />
  );
}