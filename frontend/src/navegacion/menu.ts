// Las extensiones van explícitas en los módulos puros que carga el runner de Node (tests/*.test.ts);
// `allowImportingTsExtensions` lo permite y Vite las acepta igual.
import { textos } from '../i18n/textos.ts';
import { ACCIONES, type Accion } from '../permisos.ts';

/**
 * Módulos de Atlas Rent y su entrada de menú. Cada módulo declara el identificador de acción del
 * backend que lo habilita: el menú se filtra con el MISMO identificador que exige la API, así que
 * una entrada visible nunca es un 403 garantizado.
 *
 * Un módulo sin pantalla todavía se declara aquí, con `ruta: null` y su `TODO(A#)` al lado. La
 * `SideNav` solo pinta los que tienen ruta: enlazar a una ruta inexistente daría un 404, que es
 * peor que una entrada sin destino.
 */
export interface EntradaMenu {
  /** Clave estable: la usa `SideNav` como `key` y para resaltar la activa. */
  clave: keyof typeof textos.navegacion.modulos;
  etiqueta: string;
  icono: string;
  /** Acción del backend que habilita el módulo. Un usuario sin ella no lo ve. */
  accion: Accion;
  /** Ruta real de la pantalla. `null` mientras el módulo no tenga pantalla. */
  ruta: string | null;
  /** `TODO(A#)`: qué pantalla falta. Se borra cuando exista. */
  pendiente?: string;
}

export interface GrupoMenu {
  etiqueta: string;
  entradas: EntradaMenu[];
}

type Clave = keyof typeof textos.navegacion.modulos;

function modulo(clave: Clave, accion: Accion, ruta: string | null, pendiente?: string): EntradaMenu {
  const texto = textos.navegacion.modulos[clave];
  return pendiente === undefined
    ? { clave, etiqueta: texto.etiqueta, icono: texto.icono, accion, ruta }
    : { clave, etiqueta: texto.etiqueta, icono: texto.icono, accion, ruta, pendiente };
}

export const GRUPOS_MENU: GrupoMenu[] = [
  {
    etiqueta: textos.navegacion.grupos.operacion,
    entradas: [
      modulo('panel', ACCIONES.sesionVer, '/'),
      // TODO(A#): la API de clientes es de F1-09, pero la pantalla todavía no existe.
      modulo('clientes', ACCIONES.clienteVer, null, 'pantalla de clientes'),
      // TODO(A#): `cliente.ver` es la acción que cubre ver unidades (Acciones.cs).
      modulo('unidades', ACCIONES.clienteVer, null, 'pantalla de unidades e inmuebles'),
      modulo('contratos', ACCIONES.clienteVer, null, 'pantalla de contratos'),
    ],
  },
  {
    etiqueta: textos.navegacion.grupos.finanzas,
    entradas: [
      modulo('facturas', ACCIONES.facturaVer, null, 'pantalla de facturas'),
      modulo('cobros', ACCIONES.cobroRegistrar, null, 'pantalla de cobros'),
      modulo('fianzas', ACCIONES.fianzaMover, null, 'pantalla de fianzas'),
      modulo('informes', ACCIONES.informeVer, '/informes'),
    ],
  },
  {
    etiqueta: textos.navegacion.grupos.control,
    entradas: [
      modulo('auditoria', ACCIONES.auditoriaVer, null, 'pantalla de auditoría'),
      modulo('bloqueos', ACCIONES.ipDesbloquear, null, 'pantalla de desbloqueos'),
      // La matriz de la especificación mete usuarios y roles en la misma fila que configuración:
      // `configuracion.editar`, solo admin.
      modulo('usuarios', ACCIONES.configuracionEditar, null, 'pantalla de usuarios y roles'),
      modulo('configuracion', ACCIONES.configuracionEditar, null, 'pantalla de configuración'),
    ],
  },
  {
    etiqueta: textos.navegacion.grupos.herramientas,
    entradas: [
      modulo('asistente', ACCIONES.iaConsultar, '/asistente'),
      // Muestra de componentes del design system (F1-13). Es una herramienta de quien la mantiene,
      // así que se reserva a quien edita la configuración.
      modulo('componentes', ACCIONES.configuracionEditar, '/componentes'),
    ],
  },
];

/** Todas las entradas, sin filtrar. */
export const ENTRADAS_MENU: EntradaMenu[] = GRUPOS_MENU.flatMap((grupo) => grupo.entradas);

/** Entrada del menú que corresponde a una ruta, si la hay. */
export function entradaDeRuta(ruta: string): EntradaMenu | undefined {
  return ENTRADAS_MENU.find((entrada) => entrada.ruta === ruta);
}

/**
 * Módulos que un usuario puede usar: los que tienen la acción del backend. No dice si tienen
 * pantalla: eso lo filtra `entradasNavegables`, porque un módulo sin pantalla se declara pero no se
 * enlaza. Un grupo sin ninguna entrada visible no se devuelve.
 */
export function menuPermitido(permisos: readonly string[]): GrupoMenu[] {
  const concedidas = new Set(permisos);
  return GRUPOS_MENU.map((grupo) => ({
    etiqueta: grupo.etiqueta,
    entradas: grupo.entradas.filter((entrada) => concedidas.has(entrada.accion)),
  })).filter((grupo) => grupo.entradas.length > 0);
}

/** Entradas que se pueden pulsar: las que tienen pantalla detrás. */
export function entradasNavegables(grupo: GrupoMenu): EntradaMenu[] {
  return grupo.entradas.filter((entrada) => entrada.ruta !== null);
}

/** Acción que habilita una ruta, o `null` si esa ruta no pertenece a ningún módulo. */
export function accionDeRuta(ruta: string): Accion | null {
  return entradaDeRuta(ruta)?.accion ?? null;
}

/**
 * Si el usuario puede entrar a una ruta. Lo que no pertenece a ningún módulo no se filtra aquí:
 * eso no es "sin permiso", es "no existe", y lo lleva a la pantalla de página no encontrada.
 */
export function puedeVerRuta(permisos: readonly string[], ruta: string): boolean {
  const accion = accionDeRuta(ruta);
  return accion === null || permisos.includes(accion);
}

/** Nombre del rol para la `SideNav`. Un rol desconocido se muestra tal cual, sin inventar nada. */
export function nombreDelRol(rol: string): string {
  const nombres = textos.navegacion.roles as Record<string, string>;
  return nombres[rol] ?? rol;
}