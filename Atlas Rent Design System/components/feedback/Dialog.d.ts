/** Modal dialog with scrim; footer holds actions right-aligned. */
export interface DialogProps { open?: boolean; title: React.ReactNode; description?: React.ReactNode; children?: React.ReactNode; footer?: React.ReactNode; onClose?: () => void; width?: number; /** Render without scrim (for previews) */ inline?: boolean; }
export declare function Dialog(props: DialogProps): JSX.Element | null;
