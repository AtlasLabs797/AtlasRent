import { useMemo, type JSX } from 'react';
import { Combobox } from '../../design-system/components/forms/Combobox';
import { SegmentedControl } from '../../design-system/components/navigation/SegmentedControl';
import { useClientes, useProspectos, useUnidades } from './api';
import type { UnidadConInmueble } from './tipos.ts';

/** Quien recibe el documento: exactamente uno de cliente o prospecto. */
export interface Destinatario {
  tipo: 'cliente' | 'prospecto';
  id: string | null;
}

export function SelectorDestinatario({
  valor,
  onChange,
  error,
  bloqueado,
}: {
  valor: Destinatario;
  onChange: (valor: Destinatario) => void;
  error?: string;
  bloqueado?: boolean;
}): JSX.Element {
  const clientes = useClientes();
  const prospectos = useProspectos();
  const opciones = useMemo(
    () =>
      valor.tipo === 'cliente'
        ? (clientes.data?.data ?? []).map((c) => ({ value: c.id, label: c.nombre, sub: c.email ?? undefined, meta: `CLI-${String(c.numero).padStart(5, '0')}` }))
        : (prospectos.data?.data ?? []).map((p) => ({ value: p.id, label: p.nombre, sub: p.email ?? undefined })),
    [valor.tipo, clientes.data, prospectos.data],
  );
  return (
    <div style={{ display: 'grid', gap: 'var(--space-4)' }}>
      <SegmentedControl
        options={[
          { value: 'cliente', label: 'Cliente' },
          { value: 'prospecto', label: 'Prospecto' },
        ]}
        value={valor.tipo}
        onChange={(v) => !bloqueado && onChange({ tipo: v as Destinatario['tipo'], id: null })}
      />
      <Combobox
        label={valor.tipo === 'cliente' ? 'Cliente' : 'Prospecto'}
        required
        placeholder="Buscar por nombre"
        options={opciones}
        value={valor.id}
        onChange={(v) => onChange({ tipo: valor.tipo, id: v })}
        error={error}
        disabled={bloqueado}
        emptyText="Sin resultados"
      />
    </div>
  );
}

export function SelectorUnidad({
  valor,
  onChange,
  error,
  bloqueado,
  filtrar,
  etiqueta = 'Unidad',
  requerida = true,
}: {
  valor: string | null;
  onChange: (id: string | null, unidad: UnidadConInmueble | null) => void;
  error?: string;
  bloqueado?: boolean;
  /** Para dejar solo las unidades que admiten cierta modalidad. */
  filtrar?: (unidad: UnidadConInmueble) => boolean;
  etiqueta?: string;
  requerida?: boolean;
}): JSX.Element {
  const { unidades } = useUnidades();
  const visibles = useMemo(() => unidades.filter((u) => u.activo && (filtrar ? filtrar(u) : true)), [unidades, filtrar]);
  return (
    <Combobox
      label={etiqueta}
      required={requerida}
      placeholder="Buscar unidad"
      options={visibles.map((u) => ({ value: u.id, label: u.nombre, sub: u.inmueble }))}
      value={valor}
      onChange={(v) => onChange(v, visibles.find((u) => u.id === v) ?? null)}
      error={error}
      disabled={bloqueado}
      emptyText="Sin resultados"
    />
  );
}
