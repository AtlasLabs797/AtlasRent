import { ZONA_NEGOCIO } from '../../dominio/fechas.ts';

/**
 * Aritmética de fechas de negocio sobre texto `yyyy-MM-dd`. Todo pasa por `Date.UTC` y nunca por la
 * zona del navegador: un día es un día, no un instante (AGENTS §6).
 */
const ISO = /^(\d{4})-(\d{2})-(\d{2})$/;

function partes(iso: string): [number, number, number] {
  const m = ISO.exec(iso);
  if (!m) throw new Error(`Fecha no válida: "${iso}"`);
  return [Number(m[1]), Number(m[2]), Number(m[3])];
}

function aIso(ms: number): string {
  return new Date(ms).toISOString().slice(0, 10);
}

/** Hoy en la zona de negocio (America/Santo_Domingo), no en la del navegador. */
export function hoyNegocio(ahora: Date = new Date()): string {
  const p = Object.fromEntries(
    new Intl.DateTimeFormat('en-CA', { timeZone: ZONA_NEGOCIO, year: 'numeric', month: '2-digit', day: '2-digit' })
      .formatToParts(ahora)
      .map((x) => [x.type, x.value]),
  );
  return `${p.year}-${p.month}-${p.day}`;
}

export function sumarDias(iso: string, dias: number): string {
  const [a, m, d] = partes(iso);
  return aIso(Date.UTC(a, m - 1, d + dias));
}

/** Días naturales entre dos fechas (`hasta - desde`). */
export function diasEntre(desde: string, hasta: string): number {
  const [a1, m1, d1] = partes(desde);
  const [a2, m2, d2] = partes(hasta);
  return Math.round((Date.UTC(a2, m2 - 1, d2) - Date.UTC(a1, m1 - 1, d1)) / 86_400_000);
}

export interface Mes {
  anio: number;
  /** 1 a 12. */
  mes: number;
}

export function mesDe(iso: string): Mes {
  const [anio, mes] = partes(iso);
  return { anio, mes };
}

export function desplazarMes({ anio, mes }: Mes, delta: number): Mes {
  const indice = anio * 12 + (mes - 1) + delta;
  return { anio: Math.floor(indice / 12), mes: (indice % 12) + 1 };
}

export function diasDelMes({ anio, mes }: Mes): number {
  return new Date(Date.UTC(anio, mes, 0)).getUTCDate();
}

/** Primer y último día del mes (los dos incluidos, como pide `GET /calendario`). */
export function rangoDelMes(m: Mes): { desde: string; hasta: string } {
  const p2 = (n: number) => String(n).padStart(2, '0');
  return { desde: `${m.anio}-${p2(m.mes)}-01`, hasta: `${m.anio}-${p2(m.mes)}-${p2(diasDelMes(m))}` };
}

export function fechasDelMes(m: Mes): string[] {
  const { desde } = rangoDelMes(m);
  return Array.from({ length: diasDelMes(m) }, (_, i) => sumarDias(desde, i));
}

const MESES = ['enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio', 'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre'];

export function etiquetaMes({ anio, mes }: Mes): string {
  return `${MESES[mes - 1]} ${anio}`;
}

const DIAS_SEMANA = ['D', 'L', 'M', 'X', 'J', 'V', 'S'];

/** Inicial del día de la semana ("L", "M", "X"…). */
export function inicialDiaSemana(iso: string): string {
  const [a, m, d] = partes(iso);
  return DIAS_SEMANA[new Date(Date.UTC(a, m - 1, d)).getUTCDay()]!;
}

export function esFinDeSemana(iso: string): boolean {
  const [a, m, d] = partes(iso);
  const dia = new Date(Date.UTC(a, m - 1, d)).getUTCDay();
  return dia === 0 || dia === 6;
}
