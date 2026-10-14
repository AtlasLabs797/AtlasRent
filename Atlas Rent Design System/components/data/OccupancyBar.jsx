import React from 'react';
const L = { disponible: 'Disponibles', reservado: 'Reservadas', ocupado: 'Ocupadas', mantenimiento: 'Mantenimiento', fuera: 'Fuera de servicio' };
export function OccupancyBar({ counts = {}, legend = true, style }) {
  const keys = Object.keys(L).filter(k => counts[k] != null);
  return <div style={style}>
    <div className="ar-occ__bar" role="img" aria-label={keys.map(k => L[k] + ' ' + counts[k]).join(', ')}>{keys.map(k => counts[k] > 0 && <div key={k} style={{ flex: counts[k], background: 'var(--unit-' + k + ')' }} />)}</div>
    {legend && <div className="ar-occ__legend">{keys.map(k => <div key={k}><div className="ar-occ__k"><i style={{ background: 'var(--unit-' + k + ')' }} />{L[k]}</div><div className="ar-occ__v">{counts[k]}</div></div>)}</div>}
  </div>;
}