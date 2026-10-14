/**
 * Underlined tabs for sub-views within a record.
 */
export interface TabItem { label: string; value?: string; count?: number; }
export interface TabsProps { tabs: TabItem[]; value?: string; onChange?: (v: string) => void; style?: React.CSSProperties; }
export declare function Tabs(props: TabsProps): JSX.Element;
