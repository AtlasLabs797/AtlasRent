/**
 * Filtros de la pantalla de informes (F5-01, `docs/ESPECIFICACION.md` §3.15) y su viaje entre la
 * URL y la API.
 *
 * El estado vive en la query string: así la pantalla se puede enlazar (`/informes?clienteId=…`) y el
 * `queryKey` de TanStack Query sale de los mismos filtros, de modo que cambiar un filtro pide datos
 * nuevos sin duplicar estado entre el router, la caché y el formulario.
 *
 * Módulo puro, sin React: lo carga el runner de Node en `tests/informes.test.ts`.
 */

/** Monedas de `Moneda` (backend). Los códigos van en mayúsculas porque son códigos. */
export const MONEDAS = ['USD', 'DOP'] as const;
export type MonedaFiltro = (typeof MONEDAS)[number];

/** Estados de `EstadoFactura` (backend). El `JsonStringEnumConverter` los manda por nombre. */
export const ESTADOS = ['Borrador', 'Emitida', 'Anulada', 'Convertida'] as const;
export type EstadoFiltro = (typeof ESTADOS)[number];

export interface FiltrosInformes {
  /** Primer día de emisión, inclusive, en `AAAA-MM-DD`. */
  desde: string | null;
  /** Último día de emisión, inclusive. Es también la fecha de corte de lo vencido. */
  hasta: string | null;
  moneda: MonedaFiltro | null;
  estado: EstadoFiltro | null;
  serieId: string | null;
  clienteId: string | null;
  unidadId: string | null;
  contratoId: string | null;
  inmuebleId: string | null;
  propietariaId: string | null;
  facturadoraId: string | null;
}

/** Orden estable de los parámetros: mantiene la URL y el `queryKey` comparables. */
const CLAVES = [
  'desde',
  'hasta',
  'moneda',
  'estado',
  'serieId',
  'clienteId',
  'unidadId',
  'contratoId',
  'inmuebleId',
  'propietariaId',
  'facturadoraId',
] as const satisfies readonly (keyof FiltrosInformes)[];

export const FILTROS_VACIOS: FiltrosInformes = Object.freeze({
  desde: null,
  hasta: null,
  moneda: null,
  estado: null,
  serieId: null,
  clienteId: null,
  unidadId: null,
  contratoId: null,
  inmuebleId: null,
  propietariaId: null,
  facturadoraId: null,
});

const FECHA = /^(\d{4})-(\d{2})-(\d{2})$/;
const IDENTIFICADOR = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

/**
 * `AAAA-MM-DD` y además una fecha que existe: `2026-02-30` o `2026-13-01` son fechas que el
 * `<input type="date">` no produce pero una URL manipulada sí, y mandarlas a la API es pedir un 400.
 */
export function esFecha(iso: string): boolean {
  const partes = FECHA.exec(iso);
  if (!partes) return false;
  const anio = Number(partes[1]);
  const mes = Number(partes[2]);
  const dia = Number(partes[3]);
  if (anio < 100) return false;
  const fecha = new Date(anio, mes - 1, dia);
  return (
    fecha.getFullYear() === anio && fecha.getMonth() === mes - 1 && fecha.getDate() === dia
  );
}

function unoDe<T extends string>(valor: string | null, valores: readonly T[]): T | null {
  if (valor === null) return null;
  const exacto = valores.find((v) => v === valor);
  return exacto ?? null;
}

function identificador(valor: string | null): string | null {
  return valor !== null && IDENTIFICADOR.test(valor) ? valor : null;
}

/**
 * Filtros desde la query string. Lo que no es un valor que la API entendería se descarta en lugar de
 * propagarse: una URL antigua o escrita a mano no puede romper la pantalla.
 */
export function filtrosDesdeParametros(parametros: URLSearchParams): FiltrosInformes {
  const fecha = (clave: 'desde' | 'hasta'): string | null => {
    const valor = parametros.get(clave);
    return valor !== null && esFecha(valor) ? valor : null;
  };
  const id = (clave: string): string | null => identificador(parametros.get(clave));

  return {
    desde: fecha('desde'),
    hasta: fecha('hasta'),
    moneda: unoDe(parametros.get('moneda'), MONEDAS),
    estado: unoDe(parametros.get('estado'), ESTADOS),
    serieId: id('serieId'),
    clienteId: id('clienteId'),
    unidadId: id('unidadId'),
    contratoId: id('contratoId'),
    inmuebleId: id('inmuebleId'),
    propietariaId: id('propietariaId'),
    facturadoraId: id('facturadoraId'),
  };
}

/** Los mismos filtros como parámetros de la API: solo lo que tiene valor, en orden estable. */
export function parametrosDeFiltros(filtros: FiltrosInformes): Record<string, string> {
  const salida: Record<string, string> = {};
  for (const clave of CLAVES) {
    const valor = filtros[clave];
    if (valor !== null && valor !== '') salida[clave] = valor;
  }
  return salida;
}

/**
 * Identificador de un conjunto de filtros para el `queryKey`. Mismo conjunto, misma clave: si no,
 * TanStack Query no distingue dos informes distintos.
 */
export function claveDeFiltros(filtros: FiltrosInformes): string {
  const partes = CLAVES.filter((clave) => filtros[clave] !== null && filtros[clave] !== '');
  return partes.length === 0 ? 'sin-filtros' : partes.map((clave) => `${clave}=${filtros[clave]}`).join('&');
}

/** Identificadores que llegan de otra pantalla y que aquí no se pueden elegir (no hay catálogos). */
export function clavesDeEntidad(filtros: FiltrosInformes): string[] {
  return CLAVES.filter(
    (clave) =>
      clave !== 'desde' && clave !== 'hasta' && clave !== 'moneda' && clave !== 'estado' && filtros[clave] !== null,
  ).map((clave) => clave);
}

/** `AAAA-MM-DD` en `dd/mm/aaaa` (DESIGN.md §5). Devuelve la cadena tal cual si no es una fecha. */
export function fechaCorta(iso: string | null): string {
  if (iso === null || !esFecha(iso)) return '—';
  const [, mes, dia] = iso.split('-');
  return `${dia}/${mes}/${iso.slice(0, 4)}`;
}