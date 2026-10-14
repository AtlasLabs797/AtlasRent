import React from 'react';
import { Button } from '../core/Button.jsx';
import { Menu } from '../navigation/Menu.jsx';
export function AIActionButton({ actions = [], label = 'IA', size = 'md', onSelect, style }) {
  return <Menu align="right" tone="ai" style={style} onSelect={onSelect}
    trigger={<Button variant="ai" size={size} icon="sparkles" iconRight="chevron-down">{label}</Button>}
    items={actions.map(a => typeof a === 'string' ? { label: a, value: a, icon: 'sparkle' } : { icon: 'sparkle', ...a })} />;
}
