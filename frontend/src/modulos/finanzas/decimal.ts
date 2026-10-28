import { normalizarDecimal } from '../../dominio/dinero.ts';

/**
 * Aritmética de importes para la VISTA PREVIA de finanzas (AGENTS §6): todo es texto decimal y
 * `BigInt`, nunca `number`. La API calcula y manda; esto solo sirve para enseñar una cifra antes de
 * pedirla (equivalente de una conversión, suma de lo que se está tecleando) y para validar lo
 * tecleado. Redondeo half-up (`MidpointRounding.AwayFromZero`) como el backend.
 */

/** Lo que llega de la API (JSON number) o se teclea (string). Siempre se pasa por `aDecimal` antes de operar. */
export type Importe = string | number;

const DIEZ = 10n;

function potencia10(n: number): bigint {
  return DIEZ ** BigInt(n);
}

/** Texto decimal canónico con `decimales` decimales ("1250.50"), redondeado half-up si venía con más. */
export function aDecimal(valor: Importe, decimales = 2): string {
  return normalizarDecimal(valor, decimales);
}

/** Valor escalado a entero: `("12.50", 2) -> 1250n`. */
function escalar(valor: Importe, decimales: number): bigint {
  const texto = normalizarDecimal(valor, decimales);
  const negativo = texto.startsWith('-');
  const digitos = BigInt(texto.replace('-', '').replace('.', ''));
  return negativo ? -digitos : digitos;
}

function desescalar(valor: bigint, decimales: number): string {
  const negativo = valor < 0n;
  const absoluto = (negativo ? -valor : valor).toString().padStart(decimales + 1, '0');
  const entera = absoluto.slice(0, absoluto.length - decimales);
  const fraccion = absoluto.slice(absoluto.length - decimales);
  return (negativo ? '-' : '') + entera + (decimales > 0 ? '.' + fraccion : '');
}

export function sumarImportes(valores: readonly Importe[]): string {
  return desescalar(valores.reduce<bigint>((suma, v) => suma + escalar(v, 2), 0n), 2);
}

export function restarImportes(a: Importe, b: Importe): string {
  return desescalar(escalar(a, 2) - escalar(b, 2), 2);
}

/** -1, 0 o 1. */
export function compararImportes(a: Importe, b: Importe): -1 | 0 | 1 {
  const x = escalar(a, 2);
  const y = escalar(b, 2);
  return x < y ? -1 : x > y ? 1 : 0;
}

export function esPositivo(valor: Importe): boolean {
  return escalar(valor, 2) > 0n;
}

export function minimoImporte(a: Importe, b: Importe): string {
  return compararImportes(a, b) <= 0 ? aDecimal(a) : aDecimal(b);
}

/**
 * Lo que teclea una persona -> texto decimal canónico, o `null` si no es válido. Acepta coma o punto
 * como separador decimal y rechaza el separador de miles ("1.250" no es 1250: es ambiguo y un
 * importe mal leído es dinero mal cobrado). Sin signo, sin notación científica.
 */
export function entradaADecimal(texto: string, decimales = 2): string | null {
  const limpio = texto.trim();
  const m = /^(\d+)(?:[.,](\d+))?$/.exec(limpio);
  if (!m) return null;
  const fraccion = m[2] ?? '';
  if (fraccion.length > decimales) return null;
  return normalizarDecimal(`${m[1]}${fraccion ? '.' + fraccion : ''}`, decimales);
}

/**
 * Vista previa de la conversión de un cobro en otra moneda (§3.10.2): lo recibido entre el tipo de
 * cambio da el equivalente en la moneda de la factura. `59500.00 / 59.50 = 1000.00`. El tipo de
 * cambio es DOP por USD (`numeric(18,6)`). Devuelve `null` si el cambio no es mayor que cero.
 * La conversión real y su redondeo los hace la API al simular y al registrar.
 */
export function equivalenteEnMonedaFactura(recibido: Importe, tipoCambio: Importe): string | null {
  const cambio = escalar(tipoCambio, 6);
  if (cambio <= 0n) return null;
  const centimos = escalar(recibido, 2);
  if (centimos < 0n) return null;
  // centimos / 10^2 / (cambio / 10^6) * 10^2 = centimos * 10^6 / cambio; half-up sobre enteros positivos.
  const resultado = (2n * centimos * potencia10(6) + cambio) / (2n * cambio);
  return desescalar(resultado, 2);
}

/** Inverso para explicar el cobro: importe de la factura * cambio = lo que hay que recibir (half-up). */
export function equivalenteRecibido(importeFactura: Importe, tipoCambio: Importe): string | null {
  const cambio = escalar(tipoCambio, 6);
  if (cambio <= 0n) return null;
  const centimos = escalar(importeFactura, 2);
  if (centimos < 0n) return null;
  const resultado = (2n * centimos * cambio + potencia10(6)) / (2n * potencia10(6));
  return desescalar(resultado, 2);
}

