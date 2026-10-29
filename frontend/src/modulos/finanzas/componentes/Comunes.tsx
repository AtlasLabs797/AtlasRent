import { useEffect, useState, type JSX, type ReactNode } from 'react';
import { Badge } from '../../../design-system/components/core/Badge';
import { Button } from '../../../design-system/components/core/Button';
import { Skeleton } from '../../../design-system/components/feedback/Skeleton';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { Combobox, type ComboboxOption } from '../../../design-system/components/forms/Combobox';
import { Select } from '../../../design-system/components/forms/Select';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import type { Serie } from '../tipos';
import { useBuscarClientes, useClienteElegido, useInmuebles, useSeries, useUnidadesDeInmueble } from '../api';
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

/** Valor que sigue a `valor` con retraso: evita una petición por pulsación de tecla. */
export function useRetardado<T>(valor: T, ms = 300): T {
  const [retardado, setRetardado] = useState(valor);
  useEffect(() => {
    const id = setTimeout(() => setRetardado(valor), ms);
    return () => clearTimeout(id);
  }, [valor, ms]);
  return retardado;
}

/**
 * Cliente con búsqueda remota (`GET /clientes?q=`). El Combobox del DS filtra en local y no avisa de
 * lo que se teclea: se escucha el `input` que burbujea hasta el contenedor. El cliente elegido se
 * conserva en las opciones aunque la búsqueda siguiente ya no lo traiga.
 */
export function SelectorCliente({
  value,
  onChange,
  etiqueta = t.cliente,
  required,
  error,
}: {
  value: string;
  onChange: (id: string) => void;
  etiqueta?: string;
  required?: boolean;
  error?: string;
}): JSX.Element {
  const [texto, setTexto] = useState('');
  const q = useRetardado(texto);
  const resultados = useBuscarClientes(q);
  const elegido = useClienteElegido(value);
  const opciones: ComboboxOption[] = [...(resultados.data ?? [])];
  if (elegido.data && !opciones.some((o) => o.value === elegido.data.value)) opciones.unshift(elegido.data);
  return (
    <div onInput={(e) => setTexto((e.target as HTMLInputElement).value)}>
      <Combobox
        label={etiqueta}
        placeholder={t.buscarCliente}
        required={required}
        error={error}
        options={opciones}
        value={value || null}
        onChange={(v) => {
          setTexto('');
          onChange(v ?? '');
        }}
      />
    </div>
  );
}

export const etiquetaSerie = (s: Serie): string => `${s.numero}${s.prefijo ? ' · ' + s.prefijo : ''} · ${s.tipo}`;

/** Serie del catálogo mínimo (`GET /catalogos/series`, `factura.borrador`). */
export function SelectorSerie({
  valor,
  onChange,
  etiqueta,
  opcional,
  moneda,
  error,
}: {
  valor: string;
  onChange: (v: string) => void;
  etiqueta: string;
  opcional?: boolean;
  /** Si se indica, solo series de esa moneda o sin moneda fija. */
  moneda?: string;
  error?: string;
}): JSX.Element {
  const series = useSeries();
  if (series.isError) {
    return (
      <Alert tone="warning" title={t.sinCatalogoTitulo}>
        {t.sinCatalogoCuerpo}
      </Alert>
    );
  }
  const opciones = (series.data ?? []).filter((s) => !moneda || s.moneda === null || s.moneda === moneda).map((s) => ({ value: s.id, label: etiquetaSerie(s) }));
  return (
    <Select
      label={etiqueta}
      required={!opcional}
      placeholder="—"
      value={valor}
      error={error}
      options={opcional ? [{ value: '', label: '—' }, ...opciones] : opciones}
      onChange={(e) => onChange(e.target.value)}
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
