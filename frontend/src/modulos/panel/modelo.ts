import { textos } from '../../i18n/textos.ts';
import { MODULOS } from '../lista.ts';

/**
 * Lógica pura del panel de inicio. Sin aritmética de dinero: las comparaciones enseñan dos cifras que
 * ya calculó la API, no una diferencia calculada aquí (AGENTS §6).
 */

export interface SerieMes {
  mes: string;
  facturadoUsd: number;
  cobradoUsd: number;
}

export interface ComparacionMensual {
  /** Mes en curso (`MM/AAAA`) y su importe; `null` si la serie está vacía. */
  mes: string | null;
  actual: number | null;
  /** Mes anterior de la serie, si lo hay. */
  mesAnterior: string | null;
  anterior: number | null;
}

/** `AAAA-MM` → `MM/AAAA` (DESIGN §5 pide dd/mm/aaaa; un mes lleva solo mm/aaaa). */
export function mesLegible(mes: string): string {
  const m = /^(\d{4})-(\d{2})$/.exec(mes);
  return m ? `${m[2]}/${m[1]}` : mes;
}

/** Los dos últimos meses de la serie para un campo, en el orden en que los devuelve la API. */
export function comparacionMensual(serie: readonly SerieMes[], campo: 'facturadoUsd' | 'cobradoUsd'): ComparacionMensual {
  const ultimo = serie[serie.length - 1];
  const previo = serie.length > 1 ? serie[serie.length - 2] : undefined;
  return {
    mes: ultimo ? mesLegible(ultimo.mes) : null,
    actual: ultimo ? ultimo[campo] : null,
    mesAnterior: previo ? mesLegible(previo.mes) : null,
    anterior: previo ? previo[campo] : null,
  };
}

/** Módulos con acceso directo en el panel, en este orden. */
const ACCESOS = ['bandeja', 'envios', 'informes', 'asistente'] as const;

export interface AccesoRapido {
  clave: string;
  etiqueta: string;
  icono: string;
  ruta: string;
}

/** Accesos que el usuario puede usar: módulo registrado y su misma acción de backend concedida. */
export function accesosRapidos(permisos: readonly string[]): AccesoRapido[] {
  const concedidas = new Set(permisos);
  return ACCESOS.flatMap((clave) => {
    const modulo = MODULOS.find((m) => m.clave === clave);
    if (!modulo || !concedidas.has(modulo.accion)) return [];
    const texto = textos.navegacion.modulos[clave];
    return [{ clave, etiqueta: texto.etiqueta, icono: texto.icono, ruta: modulo.ruta }];
  });
}
