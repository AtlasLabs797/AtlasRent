import type { CSSProperties, JSX } from 'react';

/** Stacked bar + legend of the five unit states. Colours come from --unit-* tokens, never the accent. */
export interface OccupancyBarProps {
  counts: Partial<Record<'disponible' | 'reservado' | 'ocupado' | 'mantenimiento' | 'fuera', number>>;
  legend?: boolean;
  style?: CSSProperties;
}

const ETIQUETAS = { disponible: 'Disponibles', reservado: 'Reservadas', ocupado: 'Ocupadas', mantenimiento: 'Mantenimiento', fuera: 'Fuera de servicio' } as const;

export function OccupancyBar({ counts = {}, legend = true, style }: OccupancyBarProps): JSX.Element {
  const claves = (Object.keys(ETIQUETAS) as Array<keyof typeof ETIQUETAS>).filter((k) => counts[k] != null);
  return (
    <div style={style}>
      <div className="ar-occ__bar" role="img" aria-label={claves.map((k) => ETIQUETAS[k] + ' ' + counts[k]).join(', ')}>
        {claves.map((k) => (counts[k] ?? 0) > 0 && <div key={k} style={{ flex: counts[k], background: 'var(--unit-' + k + ')' }} />)}
      </div>
      {legend && (
        <div className="ar-occ__legend">
          {claves.map((k) => (
            <div key={k}>
              <div className="ar-occ__k">
                <i style={{ background: 'var(--unit-' + k + ')' }} />
                {ETIQUETAS[k]}
              </div>
              <div className="ar-occ__v">{counts[k]}</div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
