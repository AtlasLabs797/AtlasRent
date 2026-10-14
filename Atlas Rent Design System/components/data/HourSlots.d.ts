/** Day schedule for hourly resources (oficinas, salas · §6.2). Shows existing bookings and a proposed slot; overlap is flagged as conflicto. */
export interface HourSlotsBooking { start: string; end: string; label?: string; status?: 'reservado' | 'ocupado'; }
export interface HourSlotsProps { from?: number; to?: number; resources: { name: string; meta?: string; bookings?: HourSlotsBooking[] }[]; proposed?: { resource: string; start: string; end: string }; style?: React.CSSProperties; }
export declare function HourSlots(props: HourSlotsProps): JSX.Element;