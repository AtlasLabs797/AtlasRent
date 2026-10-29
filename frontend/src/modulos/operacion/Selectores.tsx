import { useMemo, useState, type JSX } from 'react';
import { Combobox } from '../../design-system/components/forms/Combobox';
import { SegmentedControl } from '../../design-system/components/navigation/SegmentedControl';
import { useRetardo } from '../clientes/useRetardo';
import { useBuscarClientes, useClientePorId, useProspectos, useUnidades } from './api';
import type { ClienteLista } from './tipos.ts';
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
  const [consulta, setConsulta] = useState('');
  const consultaRetardada = useRetardo(consulta, 300);
  const esCliente = valor.tipo === 'cliente';
  const clientes = useBuscarClientes(esCliente ? consultaRetardada : '');
  const elegido = useClientePorId(esCliente ? valor.id : null);
  const prospectos = useProspectos();
  const opciones = useMemo(() => {
    if (!esCliente) return (prospectos.data?.data ?? []).map((p) => ({ value: p.id, label: p.nombre, sub: p.email ?? undefined }));
    const encontrados = clientes.data?.data ?? [];
    // El cliente ya elegido puede no salir en la búsqueda actual: sin él el campo perdería su texto.
    const lista: ClienteLista[] = elegido.data && !encontrados.some((c) => c.id === elegido.data.id) ? [elegido.data, ...encontrados] : encontrados;
    return lista.map((c) => ({ value: c.id, label: c.nombre, sub: c.email ?? undefined, meta: `CLI-${String(c.numero).padStart(5, '0')}` }));
  }, [esCliente, clientes.data, elegido.data, prospectos.data]);
  return (
    <div style={{ display: 'grid', gap: 'var(--space-4)' }}>
      <SegmentedControl
        options={[
          { value: 'cliente', label: 'Cliente' },
          { value: 'prospecto', label: 'Prospecto' },
        ]}
        value={valor.tipo}
        onChange={(v) => {
          if (bloqueado) return;
          setConsulta('');
          onChange({ tipo: v as Destinatario['tipo'], id: null });
        }}
      />
      <Combobox
        label={valor.tipo === 'cliente' ? 'Cliente' : 'Prospecto'}
        required
        placeholder="Buscar por nombre"
        options={opciones}
        value={valor.id}
        onChange={(v) => onChange({ tipo: valor.tipo, id: v })}
        {...(esCliente ? { onSearch: setConsulta } : {})}
        error={error}
        disabled={bloqueado}
        emptyText={esCliente && clientes.isFetching ? 'Buscando…' : 'Sin resultados'}
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
