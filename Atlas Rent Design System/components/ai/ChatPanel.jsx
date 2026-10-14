import React from 'react';
import { Icon } from '../core/Icon.jsx';
import { IconButton } from '../core/IconButton.jsx';
export function ChatPanel({ title = 'Asistente Atlas Rent', context, messages = [], suggestions = [], typing, paused, onSend, onClose, style }) {
  const [v, setV] = React.useState(''); const log = React.useRef(null);
  React.useEffect(() => { if (log.current) log.current.scrollTop = log.current.scrollHeight; }, [messages.length, typing]);
  const send = t => { const x = (t ?? v).trim(); if (!x || paused) return; onSend && onSend(x); setV(''); };
  return <section className="ar-chat" style={style}>
    <header className="ar-chat__head"><span style={{ width: 28, height: 28, borderRadius: '50%', background: 'var(--ai-solid)', color: '#fff', display: 'grid', placeItems: 'center', flex: 'none' }}><Icon name="sparkles" size={14} /></span>
      <div style={{ flex: 1, minWidth: 0 }}><div style={{ font: 'var(--type-label)', color: 'var(--text-strong)' }}>{title}</div><div style={{ font: 'var(--type-fine)', color: paused ? 'var(--warning)' : 'var(--text-subtle)' }}>{paused ? 'IA en pausa por el administrador' : context || 'Usa la API con tus permisos'}</div></div>
      {onClose && <IconButton icon="x" label="Cerrar" size="sm" onClick={onClose} />}</header>
    <div className="ar-chat__log" ref={log}>{messages.map((m, i) => <div key={i} className={'ar-msg ar-msg--' + m.role}>
      <div className="ar-msg__bubble">{m.tools && <div>{m.tools.map(t => <span key={t} className="ar-tool"><Icon name="wrench" size={10} />{t}</span>)}</div>}{m.text}{m.content}</div>
    </div>)}{typing && <div className="ar-typing" aria-label="Escribiendo"><i /><i /><i /></div>}</div>
    {suggestions.length > 0 && !paused && <div className="ar-chat__sugs">{suggestions.map(s => <button key={s} type="button" className="ar-sug" onClick={() => send(s)}>{s}</button>)}</div>}
    <form className="ar-chat__input" onSubmit={e => { e.preventDefault(); send(); }}><input value={v} disabled={paused} onChange={e => setV(e.target.value)} placeholder={paused ? 'Chat no disponible' : 'Pregunta o pide una tarea'} /><button type="submit" className="ar-btn ar-btn--icon ar-btn--sm" disabled={paused || !v.trim()} aria-label="Enviar" style={{ background: 'var(--ai-solid)', color: '#fff' }}><Icon name="arrow-up" size={14} /></button></form>
  </section>;
}