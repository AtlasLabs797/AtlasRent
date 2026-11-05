import type { CSSProperties, JSX } from 'react';

/** Compact pill switch for filters/views (USD · DOP, Lista · Cuadrícula). */
export interface SegmentedControlProps {
  options: (string | { value: string; label: string })[];
  value?: string;
  onChange?: (v: string) => void;
  style?: CSSProperties;
}

export function SegmentedControl({ options = [], value, onChange, style }: SegmentedControlProps): JSX.Element {
  return (
    <div className="ar-seg" style={style}>
      {options.map((opcion) => {
        const clave = typeof opcion === 'string' ? opcion : opcion.value;
        const etiqueta = typeof opcion === 'string' ? opcion : opcion.label;
        return (
          <button key={clave} aria-pressed={clave === value} onClick={() => onChange && onChange(clave)}>
            {etiqueta}
          </button>
        );
      })}
    </div>
  );
}