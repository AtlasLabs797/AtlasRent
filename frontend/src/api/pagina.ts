/** Sobre de las listas paginadas del backend (`EnvoltorioAsync` de los controladores). */
export interface Pagina<T> {
  data: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

/** Cabecera `Idempotency-Key` de una mutación: un reintento con la misma clave no repite el efecto. */
export function claveIdempotencia(): { 'Idempotency-Key': string } {
  return { 'Idempotency-Key': globalThis.crypto.randomUUID() };
}
