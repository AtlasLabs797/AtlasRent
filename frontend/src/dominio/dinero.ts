/**
 * Formato de importes para mostrar (AGENTS §6): el dinero llega y se envía como string decimal y aquí
 * nunca pasa por coma flotante. Solo se reagrupan dígitos; si hay más de 2 decimales se redondea
 * half-up sobre los dígitos, como el backend (`MidpointRounding.AwayFromZero`).
 */
const DECIMAL = /^([+-]?)(\d+)(?:\.(\d+))?$/;

export type Moneda = 'USD' | 'DOP';

/** Un importe en forma de texto decimal con exactamente 2 decimales ("1250.50"), con signo "-" si es negativo. */
export function normalizarDecimal(valor: string | number, decimales = 2): string {
  let texto = typeof valor === 'number' ? (String(valor).includes('e') ? valor.toFixed(decimales + 2) : String(valor)) : valor.trim();
  texto = texto.replace(/^\+/, '');
  const m = DECIMAL.exec(texto);
  if (!m) throw new Error(`Importe no válido: "${String(valor)}"`);
  const negativo = m[1] === '-';
  let entera = m[2]!.replace(/^0+(?=\d)/, '');
  const fraccion = m[3] ?? '';
  let dec = fraccion.slice(0, decimales).padEnd(decimales, '0');
  const siguiente = fraccion.charAt(decimales);
  if (siguiente !== '' && siguiente >= '5') {
    // Redondeo half-up por dígitos: sumar una unidad en el último decimal con acarreo.
    const digitos = (entera + dec).split('').map(Number);
    let i = digitos.length - 1;
    while (i >= 0) {
      if (digitos[i] === 9) {
        digitos[i] = 0;
        i--;
      } else {
        digitos[i] = digitos[i]! + 1;
        break;
      }
    }
    if (i < 0) digitos.unshift(1);
    const todo = digitos.join('');
    entera = todo.slice(0, todo.length - decimales);
    dec = todo.slice(todo.length - decimales);
  }
  const esCero = /^0*$/.test(entera + dec);
  return (negativo && !esCero ? '-' : '') + entera + (decimales > 0 ? '.' + dec : '');
}

/** "1250.5" -> "1.250,50" (punto de miles, coma decimal, 2 decimales). El signo negativo es "−" (U+2212). */
export function formatearNumero(valor: string | number): string {
  const normal = normalizarDecimal(valor);
  const negativo = normal.startsWith('-');
  const [entera = '0', dec = '00'] = normal.replace('-', '').split('.');
  const miles = entera.replace(/\B(?=(\d{3})+(?!\d))/g, '.');
  return `${negativo ? '−' : ''}${miles},${dec}`;
}

/** "1250.5", "USD" -> "1.250,50 USD" (DESIGN §5). */
export function formatearImporte(valor: string | number, moneda: Moneda): string {
  return `${formatearNumero(valor)} ${moneda}`;
}
