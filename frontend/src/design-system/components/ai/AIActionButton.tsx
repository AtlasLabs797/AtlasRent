import type { CSSProperties, JSX } from 'react';
import { Button } from '../core/Button';
import { Menu, type MenuItem } from '../navigation/Menu';

/** Contextual AI menu for a record header. Built on Menu with tone="ai". Hidden when the user has IA disabled. Match the size of the buttons beside it. */
export interface AIActionButtonProps {
  actions: (string | { label: string; value?: string; icon?: string; meta?: string })[];
  label?: string;
  size?: 'sm' | 'md';
  onSelect?: (a: string) => void;
  style?: CSSProperties;
}

export function AIActionButton({ actions = [], label = 'IA', size = 'md', onSelect, style }: AIActionButtonProps): JSX.Element {
  const items: MenuItem[] = actions.map((a) => (typeof a === 'string' ? { label: a, value: a, icon: 'sparkle' } : { icon: 'sparkle', ...a }));
  return (
    <Menu
      align="right"
      tone="ai"
      style={style}
      onSelect={onSelect}
      trigger={
        <Button variant="ai" size={size} icon="sparkles" iconRight="chevron-down">
          {label}
        </Button>
      }
      items={items}
    />
  );
}
