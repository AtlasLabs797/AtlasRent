/** Initials chip for people; violet bot for agents; dark cog for system/automatic. No photos. */
export interface AvatarProps { name?: string; kind?: 'user' | 'agent' | 'system'; size?: number; style?: React.CSSProperties; }
export declare function Avatar(props: AvatarProps): JSX.Element;