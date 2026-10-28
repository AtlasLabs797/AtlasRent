import { ACCIONES, type Accion } from '../../permisos.ts';

/** Lógica pura de la pantalla de usuarios: techo de cada rol, validaciones y cuerpos de petición. */

export const ROLES = ['admin', 'gerente', 'empleado'] as const;
export type Rol = (typeof ROLES)[number];

export function esRol(valor: string): valor is Rol {
  return (ROLES as readonly string[]).includes(valor);
}

/** Mismos límites que `ServicioContrasenas` del backend; la API vuelve a validar y comprueba filtraciones. */
export const CONTRASENA_MINIMA = 12;
export const CONTRASENA_MAXIMA = 72;

const A = ACCIONES;

/**
 * Techo de cada rol: copia de `MatrizPermisos.PorRol`. La API no lo expone, así que se replica aquí
 * para enseñar qué se puede quitar; la protección real es el backend, que rechaza con 400 activar algo
 * fuera del techo.
 * TODO(A#): exponer el techo (por ejemplo en `GET /usuarios/{id}/permisos`) para no duplicar la matriz.
 */
export const TECHO_ROL: Readonly<Record<Rol, readonly Accion[]>> = {
  admin: Object.values(A),
  gerente: [
    A.clienteVer, A.clienteEditar, A.inmuebleDocumento, A.contratoBorrador, A.contratoEstado, A.facturaVer,
    A.facturaBorrador, A.facturaEmitir, A.facturaAnular, A.cobroRegistrar, A.fianzaMover, A.documentoEnviar,
    A.cotizacionEnviar, A.mensajeriaMasivo, A.informeVer, A.auditoriaVer, A.sesionVer, A.sesionCerrar,
    A.sesionPassword, A.iaConsultar, A.iaProponer,
  ],
  empleado: [
    A.clienteVer, A.clienteEditar, A.contratoBorrador, A.facturaVer, A.facturaBorrador, A.cotizacionEnviar,
    A.sesionVer, A.sesionCerrar, A.sesionPassword,
  ],
};

/** Acciones básicas de sesión: todo usuario las necesita para entrar, no se ofrecen para recortar. */
const BASICAS: readonly Accion[] = [A.sesionVer, A.sesionCerrar, A.sesionPassword];

/** A un administrador la API no le deja quitar estas dos (SEC-025). */
const SALVAGUARDAS_ADMIN: readonly Accion[] = [A.mfaObligatorio, A.seguridadAccesoEmergencia];

export interface GrupoAcciones {
  clave: string;
  acciones: readonly Accion[];
}

/** Cómo se agrupan las acciones en el editor de permisos. */
export const GRUPOS_ACCIONES: readonly GrupoAcciones[] = [
  { clave: 'clientes', acciones: [A.clienteVer, A.clienteEditar, A.inmuebleDocumento, A.contratoBorrador, A.contratoEstado] },
  { clave: 'facturacion', acciones: [A.facturaVer, A.facturaBorrador, A.facturaEmitir, A.facturaAnular, A.cobroRegistrar, A.fianzaMover] },
  { clave: 'mensajeria', acciones: [A.documentoEnviar, A.cotizacionEnviar, A.mensajeriaMasivo, A.plantillaEditar] },
  { clave: 'informes', acciones: [A.informeVer, A.auditoriaVer] },
  { clave: 'seguridad', acciones: [A.ipDesbloquear, A.cuentaDesbloquear, A.mfaObligatorio, A.seguridadAccesoEmergencia] },
  { clave: 'configuracion', acciones: [A.configuracionEditar, A.agenteToken] },
  { clave: 'ia', acciones: [A.iaConsultar, A.iaProponer] },
];

/** Grupos con solo las acciones que el rol trae (su techo) y que se pueden recortar. Un grupo vacío no sale. */
export function gruposDelRol(rol: string): GrupoAcciones[] {
  if (!esRol(rol)) return [];
  const techo = TECHO_ROL[rol];
  return GRUPOS_ACCIONES.map((g) => ({
    clave: g.clave,
    acciones: g.acciones.filter((a) => techo.includes(a) && !BASICAS.includes(a)),
  })).filter((g) => g.acciones.length > 0);
}

/** Si el permiso no se puede quitar: las salvaguardas de un administrador. */
export function permisoBloqueado(rol: string, accion: string): boolean {
  return rol === 'admin' && (SALVAGUARDAS_ADMIN as readonly string[]).includes(accion);
}

/** Cuerpo de `PUT /usuarios/{id}/permisos`. */
export function cuerpoPermiso(accion: string, permitido: boolean): { accion: string; permitido: boolean } {
  return { accion, permitido };
}

/** Cuántas acciones del techo tiene recortadas este usuario. */
export function recortadas(rol: string, efectivos: readonly string[]): number {
  return gruposDelRol(rol).reduce((n, g) => n + g.acciones.filter((a) => !efectivos.includes(a)).length, 0);
}

/** Error de contraseña por longitud, o `null` si cumple. */
export function errorContrasena(valor: string): 'corta' | 'larga' | null {
  if (valor.length < CONTRASENA_MINIMA) return 'corta';
  if (valor.length > CONTRASENA_MAXIMA) return 'larga';
  return null;
}

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export function emailValido(valor: string): boolean {
  const e = valor.trim();
  return e === '' || (EMAIL.test(e) && e.length <= 200);
}

export interface ValoresAlta {
  nombreAcceso: string;
  nombre: string;
  email: string;
  contrasena: string;
  rol: string;
}

export interface CuerpoAlta {
  nombreAcceso: string;
  nombre: string;
  email?: string;
  contrasena: string;
  rol: Rol;
}

/**
 * Cuerpo de `POST /usuarios`: recorta los textos, omite el email vacío y comprueba el rol. La
 * contraseña va tal cual (los espacios cuentan). Lanza si falta algo; la pantalla valida antes y esto
 * es la red de seguridad.
 */
export function cuerpoAlta(v: ValoresAlta): CuerpoAlta {
  const nombreAcceso = v.nombreAcceso.trim();
  const nombre = v.nombre.trim();
  const email = v.email.trim();
  if (nombreAcceso.length < 1 || nombreAcceso.length > 64) throw new Error('El nombre de acceso va de 1 a 64 caracteres.');
  if (nombre.length < 1 || nombre.length > 200) throw new Error('El nombre va de 1 a 200 caracteres.');
  if (!emailValido(email)) throw new Error('El email no es válido.');
  if (errorContrasena(v.contrasena) !== null) throw new Error('La contraseña no cumple la longitud.');
  if (!esRol(v.rol)) throw new Error('El rol no es válido.');
  return { nombreAcceso, nombre, ...(email ? { email } : {}), contrasena: v.contrasena, rol: v.rol };
}
