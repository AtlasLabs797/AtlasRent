import type { CSSProperties, JSX, ReactNode } from 'react';
import { Input } from '../forms/Input';
import { Icon } from '../core/Icon';
import { Kbd } from '../core/Kbd';
import { IconButton } from '../core/IconButton';
import { Button } from '../core/Button';
import { ExchangeRate } from '../data/ExchangeRate';

/** Sticky frosted topbar: global search, live USD/DOP rate, notifications, Chat IA. Hide Chat IA when the user has IA disabled. */
export interface TopBarProps {
  searchPlaceholder?: string;
  onSearch?: (q: string) => void;
  /** When set, search renders as a ⌘K pill that opens CommandPalette (preferred) */
  onOpenSearch?: () => void;
  rate?: { market: number; margin: number; final?: number };
  notifications?: number;
  onNotifications?: () => void;
  /** false = user has IA disabled (§22.2): button hidden. */
  aiEnabled?: boolean;
  onChat?: () => void;
  children?: ReactNode;
  style?: CSSProperties;
}

export function TopBar({
  searchPlaceholder = 'Buscar cliente, unidad, factura o contrato',
  onSearch,
  onOpenSearch,
  rate,
  notifications = 0,
  onNotifications,
  aiEnabled = true,
  onChat,
  children,
  style,
}: TopBarProps): JSX.Element {
  return (
    <header className="ar-topbar" style={style}>
      {onOpenSearch ? (
        <button type="button" className="ar-searchbtn" onClick={onOpenSearch}>
          <Icon name="search" size={15} />
          <span>{searchPlaceholder}</span>
          <Kbd>⌘K</Kbd>
        </button>
      ) : (
        <Input
          icon="search"
          placeholder={searchPlaceholder}
          onChange={onSearch ? (e) => onSearch(e.target.value) : undefined}
          style={{ width: 420, maxWidth: '100%', flex: '0 1 420px' }}
        />
      )}
      <div style={{ flex: 1 }} />
      {children}
      {rate && <ExchangeRate {...rate} />}
      <span className="ar-bell">
        <IconButton icon="bell" label="Notificaciones" onClick={onNotifications} />
        {notifications > 0 && <span className="ar-bell__dot" />}
      </span>
      {aiEnabled && (
        <Button variant="ai" size="sm" icon="sparkles" onClick={onChat}>
          Chat IA
        </Button>
      )}
    </header>
  );
}