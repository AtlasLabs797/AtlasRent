// Las extensiones van explícitas en los módulos puros que carga el runner de Node (tests/*.test.ts);
// `allowImportingTsExtensions` lo permite y Vite las acepta igual.
import { textos } from '../i18n/textos.ts';
import { MODULOS } from '../modulos/lista.ts';
import type { DefinicionModulo, GrupoMenuId } from '../modulos/tipos.ts';
import { ACCIONES, type Accion } from '../permisos.ts';

/**
 * Módulos de Atlas Rent y su entrada de menú. Cada módulo declara el identificador de acción del
 * backend que lo habilita: el menú se filtra con el MISMO identificador que exige la API, así que
 * una entrada visible nunca es un 403 garantizado.
 *
 * La ruta y la acción de un módulo con pantalla salen del registro (`src/modulos/registro.ts`): aquí
 * solo está el ORDEN del menú y los módulos que todavía no tienen pantalla, con `ruta: null` y su
 * `TODO(A#)`. La `SideNav` solo pinta los que tienen ruta: enlazar a una ruta inexistente daría un
 * 404, que es peor que una entrada sin destino. Cuando un módulo se registra, deja de ser pendiente
 * sin tocar este archivo.
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

/** Orden del menú: grupos y claves. Una clave registrada fuera de esta lista se añade al final de su grupo. */
const ORDEN: Record<GrupoMenuId, Clave[]> = {
  operacion: ['panel', 'clientes', 'unidades', 'contratos'],
  finanzas: ['cuentas', 'facturas', 'cobros', 'fianzas', 'informes'],
  control: ['auditoria', 'bloqueos', 'usuarios', 'configuracion'],
  herramientas: ['asistente', 'componentes'],
};

/** Módulos sin pantalla todavía: acción del backend que los habilitará y qué falta. */
const PENDIENTES: Partial<Record<Clave, [Accion, string]>> = {
  // TODO(A#): la API de clientes es de F1-09, pero la pantalla todavía no existe.
  clientes: [ACCIONES.clienteVer, 'pantalla de clientes'],
  // TODO(A#): `cliente.ver` es la acción que cubre ver unidades (Acciones.cs).
  unidades: [ACCIONES.clienteVer, 'pantalla de unidades e inmuebles'],
  contratos: [ACCIONES.clienteVer, 'pantalla de contratos'],
  auditoria: [ACCIONES.auditoriaVer, 'pantalla de auditoría'],
  bloqueos: [ACCIONES.ipDesbloquear, 'pantalla de desbloqueos'],
  // La matriz de la especificación mete usuarios y roles en la misma fila que configuración:
  // `configuracion.editar`, solo admin.
  usuarios: [ACCIONES.configuracionEditar, 'pantalla de usuarios y roles'],
  configuracion: [ACCIONES.configuracionEditar, 'pantalla de configuración'],
};

function entrada(clave: Clave, registrado: DefinicionModulo | undefined): EntradaMenu | null {
  const texto = textos.navegacion.modulos[clave];
  if (registrado) {
    return { clave, etiqueta: texto.etiqueta, icono: texto.icono, accion: registrado.accion, ruta: registrado.ruta };
  }
  const pendiente = PENDIENTES[clave];
  if (!pendiente) return null;
  return { clave, etiqueta: texto.etiqueta, icono: texto.icono, accion: pendiente[0], ruta: null, pendiente: pendiente[1] };
}

function construirGrupos(): GrupoMenu[] {
  const registrados = new Map(MODULOS.map((m) => [m.clave, m]));
  return (Object.keys(ORDEN) as GrupoMenuId[]).map((grupo) => {
    const claves = [...ORDEN[grupo]];
    for (const m of MODULOS) {
      if (m.grupo === grupo && !claves.includes(m.clave as Clave)) claves.push(m.clave as Clave);
    }
    return {
      etiqueta: textos.navegacion.grupos[grupo],
      entradas: claves.flatMap((clave) => entrada(clave, registrados.get(clave)) ?? []),
    };
  });
}

export const GRUPOS_MENU: GrupoMenu[] = construirGrupos();

/** Todas las entradas, sin filtrar. */
export const ENTRADAS_MENU: EntradaMenu[] = GRUPOS_MENU.flatMap((grupo) => grupo.entradas);

/** Si `ruta` es la base del módulo o una hija suya (`/clientes` cubre `/clientes/7`). La raíz `/` solo cubre `/`. */
function cubre(base: string, ruta: string): boolean {
  return ruta === base || (base !== '/' && ruta.startsWith(base + '/'));
}

/** Entrada del menú que corresponde a una ruta (o a una ruta hija suya), si la hay. */
export function entradaDeRuta(ruta: string): EntradaMenu | undefined {
  return ENTRADAS_MENU.find((entrada) => entrada.ruta !== null && cubre(entrada.ruta, ruta));
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

/**
 * Acción que habilita una ruta, o `null` si esa ruta no pertenece a ningún módulo. Mira todos los
 * módulos registrados, también los que no salen en el menú ("Mi cuenta").
 */
export function accionDeRuta(ruta: string): Accion | null {
  return MODULOS.find((m) => cubre(m.ruta, ruta))?.accion ?? null;
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