import type { JSX, ReactNode } from 'react';
import { Card } from '../design-system/components/core/Card';

/** Tarjeta centrada sobre el fondo de la app: acceso, segundo factor y cambio de contraseña forzado (sin menú). */
export function PantallaCentrada({ titulo, ancho = 400, children }: { titulo: string; ancho?: number; children: ReactNode }): JSX.Element {
  return (
    <main
      style={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 'var(--space-9)',
        background: 'var(--surface-app)',
      }}
    >
      <Card title={titulo} style={{ width: '100%', maxWidth: ancho }}>
        <div style={{ display: 'grid', gap: 'var(--space-8)' }}>{children}</div>
      </Card>
    </main>
  );
}
