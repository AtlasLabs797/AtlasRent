/** Global search + quick actions (⌘K / Ctrl K). One place to jump to any cliente, unidad, factura or contrato, or run an action. */
export interface CommandItem { label: string; value?: string; sub?: string; /** Mono ID, e.g. FAC-A-2026-00412 */ meta?: string; icon?: string; tone?: 'ai'; }
export interface CommandGroup { label: string; items: CommandItem[]; }
export interface CommandPaletteProps { open: boolean; onOpenChange?: (open: boolean) => void; groups: CommandGroup[]; onSelect?: (value: string, item: CommandItem) => void; placeholder?: string; emptyText?: string; /** Bind ⌘K / Ctrl K and Esc. Default true */ hotkey?: boolean; /** Render in place without scrim (specimens) */ inline?: boolean; }
export declare function CommandPalette(props: CommandPaletteProps): JSX.Element | null;
