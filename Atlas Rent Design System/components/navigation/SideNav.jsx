import React from 'react';
import { Icon } from '../core/Icon.jsx';
import { Avatar } from '../data/Avatar.jsx';
export function SideNav({ groups = [], active, onNavigate, user, collapsed, caption = 'by Atlas Labs', style }) {
  return <aside className={'ar-sidenav' + (collapsed ? ' ar-sidenav--rail' : '')} style={style}>
    <div className="ar-sidenav__brand">
      <div className="ar-brand"><span className="ar-brand__mark">AR</span>{!collapsed && <span className="ar-brand__name">Atlas <b>Rent</b></span>}</div>
      {!collapsed && <span style={{ font: 'var(--type-caption)', color: 'var(--text-subtle)' }}>{caption}</span>}
    </div>
    <nav className="ar-nav">{groups.map(g => <div key={g.label} className="ar-nav__group">
      {!collapsed && <div className="ar-overline ar-nav__label">{g.label}</div>}
      {g.items.map(it => <button key={it.key} type="button" title={collapsed ? it.label : undefined} className="ar-nav__item" aria-current={active === it.key ? 'page' : undefined} onClick={() => onNavigate && onNavigate(it.key)}>
        <Icon name={it.icon} size={16} />{!collapsed && <span className="ar-nav__text">{it.label}</span>}
        {!collapsed && it.count ? <span className={'ar-count' + (it.countTone ? ' ar-count--' + it.countTone : '')}>{it.count}</span> : null}
      </button>)}
    </div>)}</nav>
    {user && <div className="ar-sidenav__user"><Avatar name={user.name} size={32} />{!collapsed && <div style={{ flex: 1, minWidth: 0 }}><div style={{ font: 'var(--type-label)', letterSpacing: 'var(--tracking-label)', color: 'var(--text-strong)' }}>{user.name}</div><div style={{ font: 'var(--type-fine)', color: 'var(--text-subtle)', marginTop: 2 }}>{user.role}</div></div>}</div>}
  </aside>;
}