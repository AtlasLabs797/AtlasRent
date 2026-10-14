import React from 'react';
import { SideNav } from './SideNav.jsx';
import { TopBar } from './TopBar.jsx';
export function AppShell({ nav, topbar, children, narrow, centered, style }) {
  return <div className="ar-shell" style={style}>
    {nav && <SideNav {...nav} />}
    <div className="ar-shell__main">
      {topbar && <TopBar {...topbar} />}
      <main className={'ar-shell__content' + (narrow ? ' is-narrow' : '') + (centered ? ' is-centered' : '')}>{children}</main>
    </div>
  </div>;
}
