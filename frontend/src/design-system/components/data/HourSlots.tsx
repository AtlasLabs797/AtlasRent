import { Fragment, type CSSProperties, type JSX } from 'react';

/** Day schedule for hourly resources (oficinas, salas). Shows existing bookings and a proposed slot; overlap is flagged as conflicto. */
export interface HourSlotsBooking {
  start: string;
  end: string;
  label?: string;
  status?: 'reservado' | 'ocupado';
}
export interface HourSlotsProps {
  from?: number;
  to?: number;
  resources: { name: string; meta?: string; bookings?: HourSlotsBooking[] }[];
  proposed?: { resource: string; start: string; end: string };
  style?: CSSProperties;
}

const horas = (t: string) => {
  const [a, b] = String(t).split(':').map(Number);
  return (a ?? 0) + (b || 0) / 60;
};

export function HourSlots({ from = 8, to = 20, resources = [], proposed, style }: HourSlotsProps): JSX.Element {
  const tramo = to - from;
  const pos = (t: string) => ((horas(t) - from) / tramo) * 100;
  const marcas: number[] = [];
  for (let i = from; i <= to; i += 2) marcas.push(i);
  const choca = (r: { name: string; bookings?: HourSlotsBooking[] }) =>
    proposed && proposed.resource === r.name && (r.bookings || []).some((b) => horas(b.start) < horas(proposed.end) && horas(proposed.start) < horas(b.end));
  return (
    <div className="ar-slots" style={style}>
      <div className="ar-slots__hours" />
      <div className="ar-slots__hours">
        {marcas.map((x) => (
          <span key={x} className="ar-slots__hour" style={{ left: ((x - from) / tramo) * 100 + '%' }}>
            {String(x).padStart(2, '0')}:00
          </span>
        ))}
      </div>
      {resources.map((r) => {
        const c = choca(r);
        return (
          <Fragment key={r.name}>
            <div className="ar-slots__name">
              <b>{r.name}</b>
              {r.meta && <span>{r.meta}</span>}
            </div>
            <div className="ar-slots__track">
              {marcas.map((x) => (
                <span key={x} className="ar-slots__grid" style={{ left: ((x - from) / tramo) * 100 + '%' }} />
              ))}
              {(r.bookings || []).map((b, i) => (
                <div
                  key={i}
                  className={'ar-slot ar-slot--' + (b.status || 'reservado')}
                  style={{ left: pos(b.start) + '%', width: pos(b.end) - pos(b.start) + '%' }}
                  title={b.start + '–' + b.end + ' ' + (b.label || '')}
                >
                  <b>
                    {b.start}–{b.end}
                  </b>
                  {b.label && <span style={{ overflow: 'hidden', textOverflow: 'ellipsis' }}>{b.label}</span>}
                </div>
              ))}
              {proposed && proposed.resource === r.name && (
                <div
                  className={'ar-slot ar-slot--' + (c ? 'conflicto' : 'propuesta')}
                  style={{ left: pos(proposed.start) + '%', width: pos(proposed.end) - pos(proposed.start) + '%', zIndex: 2 }}
                >
                  <b>
                    {proposed.start}–{proposed.end}
                  </b>
                  <span>{c ? 'Solapa con otra reserva' : 'Nueva reserva'}</span>
                </div>
              )}
            </div>
          </Fragment>
        );
      })}
    </div>
  );
}
