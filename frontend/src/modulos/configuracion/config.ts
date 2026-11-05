import { ZONA_NEGOCIO } from '../../dominio/fechas.ts';
import { normalizarDecimal } from '../../dominio/dinero.ts';
import { aTexto } from './esquema.ts';

/** Lógica pura de la configuración: estados del numerador fiscal, formato de tasas y cuerpos de tokens. */

/** Día de hoy en Santo Domingo como `yyyy-MM-dd`. */
export function hoyNegocio(ahora: Date = new Date()): string {
  const p = Object.fromEntries(
    new Intl.DateTimeFormat('en-CA', { timeZone: ZONA_NEGOCIO, year: 'numeric', month: '2-digit', day: '2-digit' })
      .formatToParts(ahora)
      .map((x) => [x.type, x.value]),
  );
  return `${p.year}-${p.month}-${p.day}`;
}

export type EstadoNumerador = 'vigente' | 'porAgotar' | 'agotado' | 'vencido';

export interface NumeradorFiscal {
  restantes: number;
  umbralAlerta: number;
  fechaVencimiento: string;
}

/**
 * Estado de un numerador de comprobante. Vencido y agotado impiden emitir; el rango vale hasta el
 * día de vencimiento incluido. Si está vencido y agotado a la vez, manda el vencido (lo primero que
 * habría que resolver es pedir un rango nuevo).
 */
export function estadoNumerador(n: NumeradorFiscal, hoy: string = hoyNegocio()): EstadoNumerador {
  if (n.fechaVencimiento < hoy) return 'vencido';
  if (n.restantes <= 0) return 'agotado';
  if (n.restantes <= n.umbralAlerta) return 'porAgotar';
  return 'vigente';
}

/** Valida el rango del numerador. Devuelve errores por campo (claves del esquema), vacío si está bien. */
export function validarNumerador(v: { rangoDesde: number; rangoHasta: number; siguienteNumero: number }): Record<string, 'rango' | 'siguiente'> {
  const errores: Record<string, 'rango' | 'siguiente'> = {};
  if (v.rangoHasta < v.rangoDesde) errores.rangoHasta = 'rango';
  if (v.siguienteNumero < v.rangoDesde || v.siguienteNumero > v.rangoHasta + 1) errores.siguienteNumero = 'siguiente';
  return errores;
}

/** Tasa de cambio con 6 decimales y coma decimal: `62.5` -> `62,500000`. Sin coma flotante. */
export function formatearTasa(valor: string): string {
  const normal = normalizarDecimal(aTexto(valor), 6);
  const [entera = '0', dec = '000000'] = normal.split('.');
  return `${entera.replace(/\B(?=(\d{3})+(?!\d))/g, '.')},${dec}`;
}

/** Porcentaje con 2 decimales: `18` -> `18,00 %`. */
export function formatearPorcentaje(valor: string): string {
  const normal = normalizarDecimal(aTexto(valor), 2);
  return `${normal.replace('.', ',')} %`;
}

/** Cuerpo de `POST /tipos-cambio`: la fecha se omite si está vacía (la API pone hoy). Solo DOP: USD vale 1 y se rechaza. */
export function cuerpoTipoCambio(fecha: string, valor: string): { fecha?: string; moneda: 'DOP'; valor: string } {
  const v = valor.trim().replace(',', '.');
  if (!/^\d+(\.\d{1,6})?$/.test(v) || /^0+(\.0*)?$/.test(v)) throw new Error('El valor debe ser mayor que cero y tener hasta 6 decimales.');
  const f = fecha.trim();
  return { ...(f ? { fecha: f } : {}), moneda: 'DOP', valor: v };
}

export const ACCIONES_AGENTE = ['ia.consultar', 'ia.proponer', 'cliente.ver', 'factura.ver', 'informe.ver'] as const;

export interface ValoresToken {
  nombre: string;
  usuarioId: string;
  acciones: readonly string[];
  diasValidez: string;
  limiteConsultasDia: string;
}

/** Cuerpo de `POST /agente/tokens`: solo acciones de la lista blanca; validez y límite opcionales. */
export function cuerpoToken(v: ValoresToken): Record<string, unknown> {
  const nombre = v.nombre.trim();
  if (nombre === '') throw new Error('El token necesita un nombre.');
  if (v.usuarioId === '') throw new Error('El token necesita un propietario.');
  const acciones = v.acciones.filter((a) => (ACCIONES_AGENTE as readonly string[]).includes(a));
  if (acciones.length === 0) throw new Error('El token necesita al menos una acción.');
  const entero = (t: string): number | undefined => {
    const x = t.trim();
    if (x === '') return undefined;
    if (!/^\d+$/.test(x) || Number(x) < 1) throw new Error('Escribe un número entero de 1 en adelante.');
    return Number(x);
  };
  const dias = entero(v.diasValidez);
  const limite = entero(v.limiteConsultasDia);
  return { nombre, usuarioId: v.usuarioId, acciones, ...(dias ? { diasValidez: dias } : {}), ...(limite ? { limiteConsultasDia: limite } : {}) };
}

export type EstadoToken = 'activo' | 'revocado' | 'caducado';

export function estadoToken(t: { caduca: string | null; revocado: string | null }, ahora: number = Date.now()): EstadoToken {
  if (t.revocado) return 'revocado';
  if (t.caduca && new Date(t.caduca).getTime() <= ahora) return 'caducado';
  return 'activo';
}
