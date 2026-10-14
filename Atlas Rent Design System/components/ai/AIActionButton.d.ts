/** Contextual AI menu (botones contextuales) for a record header. Built on Menu with tone="ai". Hidden when the user has IA disabled. Match the size of the buttons beside it. */
export interface AIActionButtonProps { actions: (string | { label: string; value?: string; icon?: string; meta?: string })[]; label?: string; size?: 'sm' | 'md'; onSelect?: (a: string) => void; style?: React.CSSProperties; }
export declare function AIActionButton(props: AIActionButtonProps): JSX.Element;
