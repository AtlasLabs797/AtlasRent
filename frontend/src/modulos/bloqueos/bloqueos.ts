/** Lógica pura de desbloqueos y acceso de emergencia. */

/** IPv4 (4 octetos de 0 a 255) o IPv6 (hexadecimales y «:»), con el tope de 45 caracteres de la API. */
export function esIpValida(valor: string): boolean {
  const ip = valor.trim();
  if (ip.length < 3 || ip.length > 45) return false;
  const v4 = /^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$/.exec(ip);
  if (v4) return v4.slice(1).every((o) => Number(o) <= 255);
  return /^[0-9a-fA-F:.]+$/.test(ip) && ip.includes(':') && !/:{3,}/.test(ip) && (ip.match(/::/g) ?? []).length <= 1;
}

export const MOTIVO_MINIMO = 10;
export const MOTIVO_MAXIMO = 500;

const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

export function esGuid(valor: string): boolean {
  return GUID.test(valor.trim());
}

export function cuerpoIp(ip: string): { ip: string } {
  return { ip: ip.trim() };
}

export function cuerpoSolicitudEmergencia(usuarioObjetivoId: string, motivo: string): { usuarioObjetivoId: string; motivo: string } {
  const m = motivo.trim();
  if (m.length < MOTIVO_MINIMO || m.length > MOTIVO_MAXIMO) throw new Error('El motivo va de 10 a 500 caracteres.');
  if (!esGuid(usuarioObjetivoId)) throw new Error('Falta el administrador al que se le da el acceso.');
  return { usuarioObjetivoId, motivo: m };
}

export function cuerpoAprobacion(solicitudId: string): { solicitudId: string } {
  if (!esGuid(solicitudId)) throw new Error('El identificador de la solicitud no es válido.');
  return { solicitudId: solicitudId.trim() };
}
