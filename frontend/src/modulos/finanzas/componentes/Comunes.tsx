import { useEffect, useState, type JSX, type ReactNode } from 'react';
import { Badge } from '../../../design-system/components/core/Badge';
import { Button } from '../../../design-system/components/core/Button';
import { Skeleton } from '../../../design-system/components/feedback/Skeleton';
import { Combobox } from '../../../design-system/components/forms/Combobox';
import { Select } from '../../../design-system/components/forms/Select';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { aErrorApi } from '../../../api/errores';
import { useClientes, useInmuebles, useUnidadesDeInmueble } from '../api';
import { insigniaDeFila } from '../logica';
import { textosFinanzas } from '../textos';

const t = textosFinanzas.comun;

export function InsigniaFila({ tipo, estado }: { tipo: Parameters<typeof insigniaDeFila>[0]; estado: string }): JSX.Element {
  const i = insigniaDeFila(tipo, estado);
  return (
    <Badge {...(i.status ? { status: i.status } : {})} {...(i.tone ? { tone: i.tone, dot: true } : {})}>
      {i.label}
    </Badge>
  );
}

export function CargandoTabla(): JSX.Element {
  return <Skeleton lines={5} style={{ padding: 'var(--space-9)' }} />;
}

/** Fallo de una lectura: mensaje de la API y botón para repetirla. */
export function FalloCarga({ error, reintentar }: { error: unknown; reintentar: () => void }): JSX.Element {
  return (
    <div style={{ display: 'grid', gap: 'var(--space-4)' }}>
      <ErrorAlerta error={error} titulo={t.errorCarga} />
      <div>
        <Button variant="secondary" size="sm" onClick={reintentar}>
          {t.reintentar}
        </Button>
      </div>
    </div>
  );
}

export function SelectorCliente({ value, onChange }: { value: string; onChange: (id: string) => void }): JSX.Element {
  const clientes = useClientes();
  return (
    <Combobox
      label={t.cliente}
      placeholder={t.cliente}
      options={clientes.data ?? []}
      value={value || null}
      onChange={(v) => onChange(v ?? '')}
    />
  );
}

/** Inmueble y, dentro de él, unidad. La unidad sola no se puede listar: la API las da por inmueble. */
export function SelectoresInmuebleUnidad({
  inmuebleId,
  unidadId,
  onChange,
}: {
  inmuebleId: string;
  unidadId: string;
  onChange: (cambios: { inmuebleId?: string; unidadId?: string }) => void;
}): JSX.Element {
  const inmuebles = useInmuebles();
  const unidades = useUnidadesDeInmueble(inmuebleId);
  return (
    <>
      <Select
        label={t.inmueble}
        value={inmuebleId}
        options={[{ value: '', label: t.todos }, ...(inmuebles.data ?? []).map((i) => ({ value: i.id, label: i.nombre }))]}
        onChange={(e) => onChange({ inmuebleId: e.target.value, unidadId: '' })}
      />
      <Select
        label={t.unidad}
        value={unidadId}
        disabled={!inmuebleId}
        options={[{ value: '', label: t.todas }, ...(unidades.data ?? []).map((u) => ({ value: u.id, label: u.nombre }))]}
        onChange={(e) => onChange({ unidadId: e.target.value })}
      />
    </>
  );
}

/** Rejilla de filtros: columnas que se adaptan al ancho. */
export function RejillaFiltros({ children }: { children: ReactNode }): JSX.Element {
  return (
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(200px,1fr))', gap: 'var(--space-6)', alignItems: 'end' }}>
      {children}
    </div>
  );
}

/** Texto que solo existe si la API devuelve 403 en un catálogo de configuración. */
export function esSinPermiso(error: unknown): boolean {
  return aErrorApi(error).status === 403;
}

/** Valor que se reinicia cuando cambia `clave` (selección al cambiar de filtros). */
export function useReinicio(clave: string, reiniciar: () => void): void {
  const [anterior, setAnterior] = useState(clave);
  useEffect(() => {
    if (anterior !== clave) {
      setAnterior(clave);
      reiniciar();
    }
  }, [anterior, clave, reiniciar]);
}

export function fechaHoyNegocio(): string {
  const partes = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Santo_Domingo', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());
  return partes;
}
