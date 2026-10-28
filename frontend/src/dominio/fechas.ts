/**
 * Fechas de negocio (AGENTS §6): `yyyy-MM-dd` es una fecha, no un instante. Se reordena el texto y
 * nunca se pasa por `new Date()`, que la interpretaría en UTC y daría el día anterior en
 * America/Santo_Domingo (UTC-4).
 */
const FECHA = /^(\d{4})-(\d{2})-(\d{2})$/;

export const ZONA_NEGOCIO = 'America/Santo_Domingo';

/** "2026-03-05" -> "05/03/2026". Una fecha imposible ("2026-02-30") lanza. */
export function formatearFecha(fecha: string): string {
  const m = FECHA.exec(fecha);
  if (!m) throw new Error(`Fecha no válida: "${fecha}"`);
  const [a, mes, dia] = [Number(m[1]), Number(m[2]), Number(m[3])];
  const diasMes = new Date(Date.UTC(a, mes, 0)).getUTCDate();
  if (mes < 1 || mes > 12 || dia < 1 || dia > diasMes) throw new Error(`Fecha no válida: "${fecha}"`);
  return `${m[3]}/${m[2]}/${m[1]}`;
}

/** Marca de tiempo UTC (`timestamptz`) -> "05/03/2026 14:30" en la zona de negocio. */
export function formatearMarca(iso: string): string {
  const instante = new Date(iso);
  if (Number.isNaN(instante.getTime())) throw new Error(`Marca de tiempo no válida: "${iso}"`);
  const partes = Object.fromEntries(
    new Intl.DateTimeFormat('es-DO', {
      timeZone: ZONA_NEGOCIO,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      hourCycle: 'h23',
    })
      .formatToParts(instante)
      .map((p) => [p.type, p.value]),
  );
  return `${partes.day}/${partes.month}/${partes.year} ${partes.hour}:${partes.minute}`;
}
