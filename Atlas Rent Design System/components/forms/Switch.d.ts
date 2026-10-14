/** On/off toggle for settings that apply immediately. */
export interface SwitchProps { checked?: boolean; defaultChecked?: boolean; onChange?: (v: boolean) => void; label?: React.ReactNode; tone?: 'accent' | 'ai'; disabled?: boolean; style?: React.CSSProperties; }
export declare function Switch(props: SwitchProps): JSX.Element;
