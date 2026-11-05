import type { CSSProperties, JSX } from 'react';
import { Badge, type EstadoUnitad } from '../core/Badge';
import { Money } from './Money';
import { Icon } from '../core/Icon';

/** Rentable resource tile (inmueble completo o unidad): state, modalities (Mensual · Diario · Por horas), current client, standard price. */
export interface UnitCardProps {
  name: string;
  property?: string;
  type?: string;
  status?: EstadoUnitad;
  rent?: number;
  currency?: 'USD' | 'DOP';
  period?: string;
  modalities?: string[];
  client?: string;
  until?: string;
  alert?: string;
  selected?: boolean;
  onClick?: () => void;
  style?: CSSProperties;
}

export function UnitCard({
  name,
  property,
  type,
  status = 'disponible',
  rent,
  currency = 'USD',
  period = 'mes',
  modalities = [],
  client,
  until,
  alert,
  selected,
  onClick,
  style,
}: UnitCardProps): JSX.Element {
  const contenido = (
    <>
      <div className="ar-unit__top">
        <div style={{ minWidth: 0 }}>
          <div className="ar-unit__name">{name}</div>
          <div className="ar-unit__sub">{[property, type].filter(Boolean).join(' · ')}</div>
        </div>
        <Badge status={status} />
      </div>
      {modalities.length > 0 && (
        <div className="ar-unit__mods">
          {modalities.map((modalidad) => (
            <span key={modalidad} className="ar-mod">
              {modalidad}
            </span>
          ))}
        </div>
      )}
      {alert && (
        <div style={{ display: 'flex', alignItems: 'center', gap: 6, font: 'var(--type-caption)', color: 'var(--warning)' }}>
          <Icon name="triangle-alert" size={13} />
          {alert}
        </div>
      )}
      <div className="ar-unit__foot">
        <div style={{ minWidth: 0 }}>
          <div style={{ font: 'var(--type-fine)', color: 'var(--text-subtle)' }}>{client ? 'Cliente' : 'Sin contrato'}</div>
          <div
            style={{
              font: 'var(--type-body-sm)',
              color: 'var(--text-strong)',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
            }}
          >
            {client || '—'}
            {until && <span style={{ color: 'var(--text-muted)' }}> · hasta {until}</span>}
          </div>
        </div>
        {rent != null && (
          <div style={{ textAlign: 'right', flex: 'none' }}>
            <Money amount={rent} currency={currency} strong />
            <div style={{ font: 'var(--type-fine)', color: 'var(--text-subtle)' }}>/ {period}</div>
          </div>
        )}
      </div>
    </>
  );

  const clase = 'ar-card ar-unit';
  if (onClick) {
    return (
      <button type="button" className={clase} data-selected={selected ? 'true' : undefined} onClick={onClick} style={style}>
        {contenido}
      </button>
    );
  }
  return (
    <div className={clase} data-selected={selected ? 'true' : undefined} style={style}>
      {contenido}
    </div>
  );
}