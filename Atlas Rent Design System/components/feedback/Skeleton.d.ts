/** Loading placeholder with the system's only looping shimmer. Mirror the final layout; never show a spinner for lists or KPIs. */
export interface SkeletonProps { width?: number | string; height?: number; radius?: number | string; circle?: boolean; /** Stack of text lines; last one is 60% */ lines?: number; style?: React.CSSProperties; }
export declare function Skeleton(props: SkeletonProps): JSX.Element;
