/** Custom Atlas dropdown (listbox popover, keyboard accessible). onChange receives { target: { value, name } }. */
export interface SelectProps {
  label?: string; hint?: string; error?: string; required?: boolean; placeholder?: string; disabled?: boolean; id?: string; name?: string;
  value?: string; defaultValue?: string; onChange?: (e: { target: { value: string; name?: string } }) => void;
  options?: Array<string | { value: string; label: string }>; style?: React.CSSProperties;
}
export declare function Select(props: SelectProps): JSX.Element;
