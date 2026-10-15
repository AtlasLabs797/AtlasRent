import type { CSSProperties, JSX } from 'react';
import { Icon } from '../core/Icon';

/** Initials chip for people; violet bot for agents; dark cog for system/automatic. No photos. */
export interface AvatarProps {
  name?: string;
  kind?: 'user' | 'agent' | 'system';
  size?: number;
  style?: CSSProperties;
}

export function Avatar({ name = '', kind = 'user', size = 32, style }: AvatarProps): JSX.Element {
  const iniciales = name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((palabra) => palabra.charAt(0))
    .join('')
    .toUpperCase();
  return (
    <span
      className={'ar-avatar' + (kind !== 'user' ? ' ar-avatar--' + kind : '')}
      title={name}
      style={{ width: size, height: size, fontSize: Math.round(size * 0.38), ...style }}
    >
      {kind === 'agent' ? (
        <Icon name="bot" size={Math.round(size * 0.5)} />
      ) : kind === 'system' ? (
        <Icon name="cog" size={Math.round(size * 0.5)} />
      ) : (
        iniciales
      )}
    </span>
  );
}