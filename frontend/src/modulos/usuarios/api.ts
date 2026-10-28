import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { clienteHttp } from '../../api/cliente';
import type { CuerpoAlta } from './usuarios.ts';

/** Contratos de `UsuariosController` (DTOs de `UsuarioDtos.cs`). Todas las rutas piden `configuracion.editar`. */
export interface UsuarioResumen {
  id: string;
  nombreAcceso: string;
  nombre: string;
  email: string | null;
  activo: boolean;
  rol: string;
}

export interface UsuarioPermisos {
  id: string;
  rol: string;
  permisos: string[];
}

export interface UsuarioCreado {
  id: string;
  nombreAcceso: string;
  rol: string;
  contrasenaComprobada: boolean;
}

export const CLAVE_USUARIOS = ['usuarios'] as const;
const clavePermisos = (id: string) => ['usuarios', id, 'permisos'] as const;

export function useUsuarios() {
  return useQuery({
    queryKey: CLAVE_USUARIOS,
    queryFn: async () => (await clienteHttp.get<{ data: UsuarioResumen[] }>('/usuarios')).data.data,
  });
}

export function usePermisosUsuario(id: string | null) {
  return useQuery({
    queryKey: clavePermisos(id ?? ''),
    enabled: id !== null,
    queryFn: async () => (await clienteHttp.get<UsuarioPermisos>(`/usuarios/${id}/permisos`)).data,
  });
}

export function useCrearUsuario() {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (cuerpo: CuerpoAlta) => (await clienteHttp.post<UsuarioCreado>('/usuarios', cuerpo)).data,
    onSuccess: () => cliente.invalidateQueries({ queryKey: CLAVE_USUARIOS }),
  });
}

export function useCambiarRol() {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (p: { id: string; rol: string }) => {
      await clienteHttp.put(`/usuarios/${p.id}/rol`, { rol: p.rol });
    },
    onSuccess: (_, p) => {
      void cliente.invalidateQueries({ queryKey: CLAVE_USUARIOS });
      void cliente.invalidateQueries({ queryKey: clavePermisos(p.id) });
    },
  });
}

export function useFijarPermiso() {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (p: { id: string; accion: string; permitido: boolean }) => {
      await clienteHttp.put(`/usuarios/${p.id}/permisos`, { accion: p.accion, permitido: p.permitido });
    },
    onSettled: (_d, _e, p) => cliente.invalidateQueries({ queryKey: clavePermisos(p.id) }),
  });
}

export function useFijarActivo() {
  const cliente = useQueryClient();
  return useMutation({
    mutationFn: async (p: { id: string; activo: boolean }) => {
      await clienteHttp.put(`/usuarios/${p.id}/activo`, { activo: p.activo });
    },
    onSuccess: () => cliente.invalidateQueries({ queryKey: CLAVE_USUARIOS }),
  });
}

export function useRestablecerPassword() {
  return useMutation({
    mutationFn: async (p: { id: string; contrasena: string }) =>
      (await clienteHttp.put<{ contrasenaComprobada: boolean }>(`/usuarios/${p.id}/password`, { contrasena: p.contrasena })).data,
  });
}
