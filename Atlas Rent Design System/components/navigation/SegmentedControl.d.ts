/** Compact pill switch for filters/views (USD · DOP, Lista · Cuadrícula). */
export interface SegmentedControlProps { options: Array<string | { value: string; label: string }>; value?: string; onChange?: (v: string) => void; style?: React.CSSProperties; }
export declare function SegmentedControl(props: SegmentedControlProps): JSX.Element;
