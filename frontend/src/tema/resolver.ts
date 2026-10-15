/** Preferencia de tema del usuario. Se guarda en este navegador. */
export type PreferenciaTema = 'claro' | 'oscuro' | 'sistema';

/** Tema efectivo que se fija en <html data-theme="…">. */
export type Tema = 'light' | 'dark';

/** Clave de localStorage. La copia de esta clave está en el script en línea de index.html. */
export const CLAVE_TEMA = 'atlas-rent.tema';

export const PREFERENCIAS: PreferenciaTema[] = ['claro', 'oscuro', 'sistema'];

/**
 * Resuelve la preferencia contra lo que dice el sistema operativo.
 * "Sistema" sigue al sistema; las otras dos manda sobre él.
 */
export function resolverTema(preferencia: PreferenciaTema, sistemaOscuro: boolean): Tema {
  if (preferencia === 'oscuro') return 'dark';
  if (preferencia === 'claro') return 'light';
  return sistemaOscuro ? 'dark' : 'light';
}

/** Lee la preferencia guardada. Cualquier valor corrupto cae en "sistema". */
export function leerPreferencia(almacen: Pick<Storage, 'getItem'>): PreferenciaTema {
  const valor = almacen.getItem(CLAVE_TEMA);
  return valor === 'claro' || valor === 'oscuro' || valor === 'sistema' ? valor : 'sistema';
}