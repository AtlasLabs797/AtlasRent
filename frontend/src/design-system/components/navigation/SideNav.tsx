import type { CSSProperties, JSX } from 'react';
import { Icon } from '../core/Icon';
import { Avatar } from '../data/Avatar';

export interface SideNavItem {
  key: string;
  icon: string;
  label: string;
  count?: number;
  countTone?: 'ai' | 'danger' | 'accent';
}

/** App sidebar: Atlas Rent lockup, grouped nav (Operación · Finanzas · Control), user footer. 264px, 72px rail when collapsed. */
export interface SideNavProps {
  groups: { label: string; items: SideNavItem[] }[];
  active?: string;
  onNavigate?: (key: string) => void;
  user?: { name: string; role: string };
  collapsed?: boolean;
  caption?: string;
  style?: CSSProperties;
}

export function SideNav({
  groups = [],
  active,
  onNavigate,
  user,
  collapsed,
  caption = 'by Atlas Labs',
  style,
}: SideNavProps): JSX.Element {
  return (
    <aside className={'ar-sidenav' + (collapsed ? ' ar-sidenav--rail' : '')} style={style}>
      <div className="ar-sidenav__brand">
        <div className="ar-brand">
          <span className="ar-brand__mark">AR</span>
          {!collapsed && (
            <span className="ar-brand__name">
              Atlas <b>Rent</b>
            </span>
          )}
        </div>
        {!collapsed && (
          <span style={{ font: 'var(--type-caption)', color: 'var(--text-subtle)' }}>{caption}</span>
        )}
      </div>
      <nav className="ar-nav">
        {groups.map((grupo) => (
          <div key={grupo.label} className="ar-nav__group">
            {!collapsed && <div className="ar-overline ar-nav__label">{grupo.label}</div>}
            {grupo.items.map((item) => (
              <button
                key={item.key}
                type="button"
                title={collapsed ? item.label : undefined}
                className="ar-nav__item"
                aria-current={active === item.key ? 'page' : undefined}
                onClick={() => onNavigate && onNavigate(item.key)}
              >
                <Icon name={item.icon} size={16} />
                {!collapsed && <span className="ar-nav__text">{item.label}</span>}
                {!collapsed && item.count ? (
                  <span className={'ar-count' + (item.countTone ? ' ar-count--' + item.countTone : '')}>
                    {item.count}
                  </span>
                ) : null}
              </button>
            ))}
          </div>
        ))}
      </nav>
      {user && (
        <div className="ar-sidenav__user">
          <Avatar name={user.name} size={32} />
          {!collapsed && (
            <div style={{ flex: 1, minWidth: 0 }}>
              <div
                style={{
                  font: 'var(--type-label)',
                  letterSpacing: 'var(--tracking-label)',
                  color: 'var(--text-strong)',
                }}
              >
                {user.name}
              </div>
              <div style={{ font: 'var(--type-fine)', color: 'var(--text-subtle)', marginTop: 2 }}>{user.role}</div>
            </div>
          )}
        </div>
      )}
    </aside>
  );
}