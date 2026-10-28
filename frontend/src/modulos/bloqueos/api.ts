import { useMutation } from '@tanstack/react-query';
import { clienteHttp } from '../../api/cliente';
import { cuerpoAprobacion, cuerpoIp, cuerpoSolicitudEmergencia } from './bloqueos.ts';

/**
 * Contratos de `BloqueosController` y `EmergenciaController`.
 * TODO(A#): la API no lista cuentas ni IPs bloqueadas ni expone una lista blanca; solo desbloquea por
 * dato. Tampoco lista las solicitudes de emergencia pendientes: el identificador hay que pasarlo
 * entre administradores.
 */
export function useDesbloquearIp() {
  return useMutation({
    mutationFn: async (ip: string) => (await clienteHttp.post<{ ip: string; desbloqueada: boolean }>('/seguridad/bloqueos/ip/desbloquear', cuerpoIp(ip))).data,
  });
}

export function useDesbloquearCuenta() {
  return useMutation({
    mutationFn: async (usuarioId: string) =>
      (await clienteHttp.post<{ usuarioId: string; desbloqueada: boolean }>('/seguridad/bloqueos/cuenta/desbloquear', { usuarioId })).data,
  });
}

export interface RespuestaEmergencia {
  id: string;
  caduca: string | null;
  codigoAcceso: string | null;
}

export function useSolicitarEmergencia() {
  return useMutation({
    mutationFn: async (p: { usuarioObjetivoId: string; motivo: string }) =>
      (await clienteHttp.post<RespuestaEmergencia>('/auth/emergencia/solicitar', cuerpoSolicitudEmergencia(p.usuarioObjetivoId, p.motivo))).data,
  });
}

export function useAprobarEmergencia() {
  return useMutation({
    mutationFn: async (solicitudId: string) =>
      (await clienteHttp.post<RespuestaEmergencia>('/auth/emergencia/aprobar', cuerpoAprobacion(solicitudId))).data,
  });
}
