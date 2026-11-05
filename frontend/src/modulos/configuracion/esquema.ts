/**
 * Formularios dirigidos por esquema para la configuración: cada campo dice su tipo y límites, y de
 * ahí salen los valores iniciales, la validación y el cuerpo de la petición. Es lógica pura (sin JSX)
 * para probarla con el Node Test Runner. Los decimales viajan como string (AGENTS §6).
 */
export interface Opcion {
  value: string;
  label: string;
}

interface CampoBase {
  clave: string;
  etiqueta: string;
  hint?: string;
  /** Solo se puede escribir al crear; al editar se envía el valor que ya tenía. */
  soloAlta?: boolean;
}

export type Campo =
  | (CampoBase & { tipo: 'texto'; requerido?: boolean; max: number; multilinea?: boolean; mono?: boolean })
  | (CampoBase & { tipo: 'entero'; min: number; max: number; requerido?: boolean })
  | (CampoBase & { tipo: 'decimal'; decimales: number; min: number; max: number; requerido?: boolean; sufijo?: string })
  | (CampoBase & { tipo: 'fecha'; requerido?: boolean })
  | (CampoBase & { tipo: 'select'; opciones: readonly Opcion[]; /** Etiqueta de la opción vacía (valor nulo). Sin ella, la elección es obligatoria. */ vacio?: string })
  | (CampoBase & { tipo: 'switch' });

export type Valores = Record<string, string | boolean>;
export type Fila = Record<string, unknown>;

/** Un número del JSON (o un string) a texto decimal sin notación científica. */
export function aTexto(valor: unknown): string {
  if (valor === null || valor === undefined) return '';
  if (typeof valor === 'number') {
    const t = String(valor);
    return t.includes('e') ? valor.toFixed(6).replace(/\.?0+$/, '') : t;
  }
  return String(valor);
}

/**
 * Texto escrito por la persona -> decimal canónico con punto, o `null` si no es válido o tiene más
 * decimales de los permitidos. Acepta coma decimal. Sin aritmética: solo reglas sobre el texto.
 */
export function normalizarDecimalEntrada(texto: string, maxDecimales: number): string | null {
  const t = texto.trim().replace(',', '.');
  const m = /^(\d+)(?:\.(\d+))?$/.exec(t);
  if (!m) return null;
  if ((m[2]?.length ?? 0) > maxDecimales) return null;
  return m[2] ? `${m[1]}.${m[2]}` : m[1]!;
}

/** Compara dos decimales no negativos ya normalizados sin pasar por coma flotante. */
export function compararDecimales(a: string, b: string): number {
  const [ea = '0', da = ''] = a.split('.');
  const [eb = '0', db = ''] = b.split('.');
  const n = Math.max(da.length, db.length);
  const x = BigInt(ea + da.padEnd(n, '0'));
  const y = BigInt(eb + db.padEnd(n, '0'));
  return x < y ? -1 : x > y ? 1 : 0;
}

export function valoresIniciales(campos: readonly Campo[], objeto?: object | null): Valores {
  const fila = objeto as Fila | null | undefined;
  const v: Valores = {};
  for (const c of campos) {
    const original = fila?.[c.clave];
    if (c.tipo === 'switch') v[c.clave] = fila ? original === true : false;
    else if (c.tipo === 'select') v[c.clave] = original === null || original === undefined ? (c.vacio !== undefined ? '' : (c.opciones[0]?.value ?? '')) : String(original);
    else v[c.clave] = aTexto(original);
  }
  return v;
}

export interface TextosValidacion {
  obligatorio: string;
  largo: string;
  entero: string;
  decimal: string;
  rango: string;
  fecha: string;
}

/** Mensaje de error de un campo, o `null` si es válido. */
export function validarCampo(c: Campo, valor: string | boolean | undefined, t: TextosValidacion): string | null {
  if (c.tipo === 'switch') return null;
  const texto = typeof valor === 'string' ? valor.trim() : '';
  const requerido = c.tipo === 'select' ? c.vacio === undefined : c.requerido === true;
  if (texto === '') return requerido ? t.obligatorio : null;
  switch (c.tipo) {
    case 'texto':
      return texto.length > c.max ? t.largo.replace('{max}', String(c.max)) : null;
    case 'entero': {
      if (!/^-?\d+$/.test(texto)) return t.entero;
      const n = Number(texto);
      return n < c.min || n > c.max ? t.rango.replace('{min}', String(c.min)).replace('{max}', String(c.max)) : null;
    }
    case 'decimal': {
      const n = normalizarDecimalEntrada(texto, c.decimales);
      if (n === null) return t.decimal.replace('{n}', String(c.decimales));
      if (compararDecimales(n, String(c.min)) < 0 || compararDecimales(n, String(c.max)) > 0) {
        return t.rango.replace('{min}', String(c.min)).replace('{max}', String(c.max));
      }
      return null;
    }
    case 'fecha':
      return /^\d{4}-\d{2}-\d{2}$/.test(texto) ? null : t.fecha;
    case 'select':
      return c.opciones.some((o) => o.value === texto) || (c.vacio !== undefined && texto === '') ? null : t.obligatorio;
  }
}

/**
 * Cuerpo de la petición. Texto vacío opcional -> `null`; entero -> número; decimal -> string con
 * punto; select vacío -> `null`. En edición, los campos `soloAlta` conservan el valor original.
 */
export function construirCuerpo(campos: readonly Campo[], valores: Valores, original?: object | null): Record<string, unknown> {
  const cuerpo: Record<string, unknown> = {};
  for (const c of campos) {
    const bruto = original && c.soloAlta ? valoresIniciales([c], original)[c.clave] : valores[c.clave];
    if (c.tipo === 'switch') {
      cuerpo[c.clave] = bruto === true;
      continue;
    }
    const texto = typeof bruto === 'string' ? bruto.trim() : '';
    if (texto === '') {
      cuerpo[c.clave] = null;
      continue;
    }
    if (c.tipo === 'entero') cuerpo[c.clave] = Number(texto);
    else if (c.tipo === 'decimal') cuerpo[c.clave] = normalizarDecimalEntrada(texto, c.decimales) ?? texto;
    else cuerpo[c.clave] = texto;
  }
  return cuerpo;
}
