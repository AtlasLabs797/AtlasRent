import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { clienteHttp } from '../../api/cliente';
import { cuerpoAprobacion, cuerpoIp, cuerpoSolicitudEmergencia } from './bloqueos.ts';

/** Contratos de `BloqueosController` y `EmergenciaController` (DTOs de `UsuarioDtos.cs`). */
export interface IpBloqueada {
  ip: string;
  nivel: number;
  fallos: number;
  desde: string;
  hasta: string | null;
  indefinido: boolean;
}

export interface CuentaBloqueada {
  usuarioId: string;
  nombreAcceso: string;
  nombre: string;
  desde: string;
  hasta: string;
}

export const ESTADOS_SOLICITUD = ['pendiente', 'aprobada', 'caducada', 'usada', 'todas'] as const;
export type EstadoSolicitud = (typeof ESTADOS_SOLICITUD)[number];

/** Nunca lleva el código ni su hash. */
export interface SolicitudEmergencia {
  id: string;
  usuarioId: string;
  usuarioNombreAcceso: string;
  solicitanteId: string;
  solicitanteNombreAcceso: string;
  motivo: string;
  creadoAt: string;
  aprobadoPorId: string | null;
  aprobadoPorNombreAcceso: string | null;
  aprobadoAt: string | null;
  caducaAt: string | null;
  usadoAt: string | null;
  estado: string;
}

const CLAVE_BLOQUEOS = ['bloqueos'] as const;

export function useIpsBloqueadas() {
  return useQuery({
    queryKey: [...CLAVE_BLOQUEOS, 'ips'],
    queryFn: async () => (await clienteHttp.get<{ data: IpBloqueada[] }>('/seguridad/bloqueos/ips')).data.data,
  });
}

export function useCuentasBloqueadas() {
  return useQuery({
    queryKey: [...CLAVE_BLOQUEOS, 'cuentas'],
    queryFn: async () => (await clienteHttp.get<{ data: CuentaBloqueada[] }>('/seguridad/bloqueos/cuentas')).data.data,
  });
}

export function useListaBlanca() {
  return useQuery({
    queryKey: [...CLAVE_BLOQUEOS, 'lista-blanca'],
    queryFn: async () => (await clienteHttp.get<{ data: string[] }>('/seguridad/bloqueos/lista-blanca')).data.data,
  });
}

export function useSolicitudesEmergencia(estado: EstadoSolicitud) {
  return useQuery({
    queryKey: ['emergencia', 'solicitudes', estado],
    queryFn: async () =>
      (await clienteHttp.get<{ data: SolicitudEmergencia[] }>('/auth/emergencia/solicitudes', { params: { estado } })).data.data,
  });
}

export function useDesbloquearIp() {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (ip: string) => (await clienteHttp.post<{ ip: string; desbloqueada: boolean }>('/seguridad/bloqueos/ip/desbloquear', cuerpoIp(ip))).data,
    onSettled: () => cliente.invalidateQueries({ queryKey: CLAVE_BLOQUEOS }),
  });
}

export function useDesbloquearCuenta() {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (usuarioId: string) =>
      (await clienteHttp.post<{ usuarioId: string; desbloqueada: boolean }>('/seguridad/bloqueos/cuenta/desbloquear', { usuarioId })).data,
    onSettled: () => cliente.invalidateQueries({ queryKey: CLAVE_BLOQUEOS }),
  });
}

export interface RespuestaEmergencia {
  id: string;
  caduca: string | null;
  codigoAcceso: string | null;
}

export function useSolicitarEmergencia() {
  const cliente = useQueryClient();
  return useMutation({
    onSuccess: () => cliente.invalidateQueries({ queryKey: ['emergencia'] }),
    mutationFn: async (p: { usuarioObjetivoId: string; motivo: string }) =>
      (await clienteHttp.post<RespuestaEmergencia>('/auth/emergencia/solicitar', cuerpoSolicitudEmergencia(p.usuarioObjetivoId, p.motivo))).data,
  });
}

export function useAprobarEmergencia() {
  const cliente = useQueryClient();
  return useMutation({
    onSettled: () => cliente.invalidateQueries({ queryKey: ['emergencia'] }),
    mutationFn: async (solicitudId: string) =>
      (await clienteHttp.post<RespuestaEmergencia>('/auth/emergencia/aprobar', cuerpoAprobacion(solicitudId))).data,
  });
}
