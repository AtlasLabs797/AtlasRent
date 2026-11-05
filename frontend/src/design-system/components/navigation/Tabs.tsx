import type { CSSProperties, JSX } from 'react';

/** Underlined tabs for sub-views within a record. */
export interface TabItem {
  label: string;
  value?: string;
  count?: number;
}
export interface TabsProps {
  tabs: TabItem[];
  value?: string;
  onChange?: (v: string) => void;
  style?: CSSProperties;
}

export function Tabs({ tabs = [], value, onChange, style }: TabsProps): JSX.Element {
  return (
    <div className="ar-tabs" role="tablist" style={style}>
      {tabs.map((t) => {
        const k = t.value ?? t.label;
        return (
          <button key={k} type="button" role="tab" className="ar-tab" aria-selected={k === value} onClick={() => onChange?.(k)}>
            {t.label}
            {t.count != null && <span className="ar-tab__count">{t.count}</span>}
          </button>
        );
      })}
    </div>
  );
}
