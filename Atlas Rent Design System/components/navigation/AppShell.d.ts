import type { SideNavProps } from './SideNav';
import type { TopBarProps } from './TopBar';
/** App frame: frosted 264px SideNav (72px rail under 1100px, hidden under 720px) + 64px TopBar + content column (max 1440, 980 when narrow). */
export interface AppShellProps { nav?: SideNavProps; topbar?: TopBarProps; children?: React.ReactNode; /** 980px column for forms and text-heavy screens */ narrow?: boolean; centered?: boolean; style?: React.CSSProperties; }
export declare function AppShell(props: AppShellProps): JSX.Element;
