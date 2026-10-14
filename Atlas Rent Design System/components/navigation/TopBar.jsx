import React from 'react';
import { Input } from '../forms/Input.jsx';
import { Icon } from '../core/Icon.jsx';
import { Kbd } from '../core/Kbd.jsx';
import { IconButton } from '../core/IconButton.jsx';
import { Button } from '../core/Button.jsx';
import { ExchangeRate } from '../data/ExchangeRate.jsx';
export function TopBar({ searchPlaceholder = 'Buscar cliente, unidad, factura o contrato', onSearch, onOpenSearch, rate, notifications = 0, onNotifications, aiEnabled = true, onChat, children, style }) {
  return <header className="ar-topbar" style={style}>
    {onOpenSearch
      ? <button type="button" className="ar-searchbtn" onClick={onOpenSearch}><Icon name="search" size={15} /><span>{searchPlaceholder}</span><Kbd>⌘K</Kbd></button>
      : <Input icon="search" placeholder={searchPlaceholder} onChange={onSearch ? e => onSearch(e.target.value) : undefined} style={{ width: 420, maxWidth: '100%', flex: '0 1 420px' }} />}
    <div style={{ flex: 1 }} />{children}
    {rate && <ExchangeRate {...rate} />}
    <span className="ar-bell"><IconButton icon="bell" label="Notificaciones" onClick={onNotifications} />{notifications > 0 && <span className="ar-bell__dot" />}</span>
    {aiEnabled && <Button variant="ai" size="sm" icon="sparkles" onClick={onChat}>Chat IA</Button>}
  </header>;
}
