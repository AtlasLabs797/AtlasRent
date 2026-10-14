/** Text date field (dd/mm/aaaa, typed by hand) with a calendar dropdown. Value is ISO yyyy-mm-dd. */
export interface DatePickerProps {
  label?: string; hint?: string; error?: string; required?: boolean; disabled?: boolean; id?: string; name?: string; placeholder?: string;
  value?: string; defaultValue?: string; min?: string; max?: string; style?: React.CSSProperties;
  onChange?: (e: { target: { value: string; name?: string } }) => void;
}
export declare function DatePicker(props: DatePickerProps): JSX.Element;
