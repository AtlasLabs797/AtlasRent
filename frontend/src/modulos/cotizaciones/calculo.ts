/**
 * VISTA PREVIA del total de una cotización (AGENTS §6): suma de líneas redondeadas half-up a 2
 * decimales, como `Math.Round(x, 2, MidpointRounding.AwayFromZero)` del backend. Usa BigInt sobre los
 * dígitos: nunca coma flotante. El importe oficial lo calcula la API.
 */
const DECIMAL = /^(\d+)(?:\.(\d+))?$/;

function aEscalado(texto: string): { valor: bigint; escala: number } | null {
  const m = DECIMAL.exec(texto.trim().replace(',', '.'));
  if (!m) return null;
  const fraccion = m[2] ?? '';
  return { valor: BigInt(m[1]! + fraccion), escala: fraccion.length };
}

/** Importe de una línea en céntimos (BigInt), o `null` si cantidad o precio no son números válidos. */
export function centimosDeLinea(cantidad: string, precio: string): bigint | null {
  const c = aEscalado(cantidad);
  const p = aEscalado(precio);
  if (!c || !p) return null;
  const producto = c.valor * p.valor;
  const escala = c.escala + p.escala;
  if (escala <= 2) return producto * 10n ** BigInt(2 - escala);
  const divisor = 10n ** BigInt(escala - 2);
  const cociente = producto / divisor;
  // Half-up (alejándose de cero; aquí todo es no negativo): el resto igual o mayor que la mitad sube.
  return producto % divisor * 2n >= divisor ? cociente + 1n : cociente;
}

export function centimosATexto(centimos: bigint): string {
  const s = centimos.toString().padStart(3, '0');
  return `${s.slice(0, -2)}.${s.slice(-2)}`;
}

export interface LineaEditable {
  descripcion: string;
  cantidad: string;
  precioUnitario: string;
}

export interface VistaPrevia {
  /** Importe de cada línea ("1250.00"), o `null` si esa línea aún no es válida. */
  importes: Array<string | null>;
  total: string;
  /** Hay líneas con cantidad o precio que no se pueden leer. */
  incompleta: boolean;
}

export function vistaPrevia(lineas: LineaEditable[]): VistaPrevia {
  let total = 0n;
  let incompleta = false;
  const importes = lineas.map((l) => {
    const c = centimosDeLinea(l.cantidad, l.precioUnitario);
    if (c === null) {
      incompleta = true;
      return null;
    }
    total += c;
    return centimosATexto(c);
  });
  return { importes, total: centimosATexto(total), incompleta };
}
