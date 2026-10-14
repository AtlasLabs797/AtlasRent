import React from 'react';
const h = t => { const [a, b] = String(t).split(':').map(Number); return a + (b || 0) / 60; };
export function HourSlots({ from = 8, to = 20, resources = [], proposed, style }) {
  const span = to - from, pos = t => ((h(t) - from) / span) * 100;
  const hrs = []; for (let i = from; i <= to; i += 2) hrs.push(i);
  const clash = (r) => proposed && proposed.resource === r.name && (r.bookings || []).some(b => h(b.start) < h(proposed.end) && h(proposed.start) < h(b.end));
  return <div className="ar-slots" style={style}>
    <div className="ar-slots__hours" /><div className="ar-slots__hours">{hrs.map(x => <span key={x} className="ar-slots__hour" style={{ left: ((x - from) / span) * 100 + '%' }}>{String(x).padStart(2, '0')}:00</span>)}</div>
    {resources.map(r => { const c = clash(r); return <React.Fragment key={r.name}>
      <div className="ar-slots__name"><b>{r.name}</b>{r.meta && <span>{r.meta}</span>}</div>
      <div className="ar-slots__track">{hrs.map(x => <span key={x} className="ar-slots__grid" style={{ left: ((x - from) / span) * 100 + '%' }} />)}
        {(r.bookings || []).map((b, i) => <div key={i} className={'ar-slot ar-slot--' + (b.status || 'reservado')} style={{ left: pos(b.start) + '%', width: (pos(b.end) - pos(b.start)) + '%' }} title={b.start + '–' + b.end + ' ' + (b.label || '')}><b>{b.start}–{b.end}</b>{b.label && <span style={{ overflow: 'hidden', textOverflow: 'ellipsis' }}>{b.label}</span>}</div>)}
        {proposed && proposed.resource === r.name && <div className={'ar-slot ar-slot--' + (c ? 'conflicto' : 'propuesta')} style={{ left: pos(proposed.start) + '%', width: (pos(proposed.end) - pos(proposed.start)) + '%', zIndex: 2 }}><b>{proposed.start}–{proposed.end}</b><span>{c ? 'Solapa con otra reserva' : 'Nueva reserva'}</span></div>}
      </div></React.Fragment>; })}
  </div>;
}