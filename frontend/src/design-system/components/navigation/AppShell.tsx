import type { CSSProperties, JSX, ReactNode } from 'react';
import { SideNav, type SideNavProps } from './SideNav';
import { TopBar, type TopBarProps } from './TopBar';

/** App frame: frosted 264px SideNav (72px rail under 1100px, hidden under 720px) + 64px TopBar + content column (max 1440, 980 when narrow). */
export interface AppShellProps {
  nav?: SideNavProps;
  topbar?: TopBarProps;
  children?: ReactNode;
  /** 980px column for forms and text-heavy screens */
  narrow?: boolean;
  centered?: boolean;
  style?: CSSProperties;
}

export function AppShell({ nav, topbar, children, narrow, centered, style }: AppShellProps): JSX.Element {
  return (
    <div className="ar-shell" style={style}>
      {nav && <SideNav {...nav} />}
      <div className="ar-shell__main">
        {topbar && <TopBar {...topbar} />}
        <main className={'ar-shell__content' + (narrow ? ' is-narrow' : '') + (centered ? ' is-centered' : '')}>
          {children}
        </main>
      </div>
    </div>
  );
}