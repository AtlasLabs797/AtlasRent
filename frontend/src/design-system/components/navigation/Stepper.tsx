import type { CSSProperties, JSX } from 'react';
import { Icon } from '../core/Icon';

/** Horizontal progress for multi-step flows: contract lifecycle, migration, reserva → contrato. */
export interface StepperProps {
  steps: { label: string; meta?: string; status?: 'done' | 'current' | 'todo' | 'error' }[];
  style?: CSSProperties;
}

export function Stepper({ steps = [], style }: StepperProps): JSX.Element {
  return (
    <ol className="ar-steps" style={style}>
      {steps.map((s, i) => (
        <li key={i} className="ar-step" data-status={s.status || 'todo'}>
          <div className="ar-step__row">
            <span className="ar-step__dot">
              {s.status === 'done' ? <Icon name="check" size={13} /> : s.status === 'error' ? <Icon name="x" size={13} /> : i + 1}
            </span>
            {i < steps.length - 1 && <span className="ar-step__line" />}
          </div>
          <span className="ar-step__label">{s.label}</span>
          {s.meta && <span className="ar-step__meta">{s.meta}</span>}
        </li>
      ))}
    </ol>
  );
}
