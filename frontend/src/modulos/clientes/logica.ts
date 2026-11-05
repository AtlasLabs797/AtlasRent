import type {
  ClienteFicha,
  Contacto,
  CuerpoBusquedaDocumento,
  CuerpoCliente,
  CuerpoContacto,
  CuerpoProspecto,
  ProspectoFicha,
  TipoCliente,
} from './tipos.ts';

/**
 * Lógica pura del módulo (sin React): valores de formulario, cuerpos de petición y validaciones.
 * Las reglas son las de los DTO del backend (`ClienteDtos.cs`); la API vuelve a validarlas.
 */
export const NUMERO_MAXIMO = 99_999_999;
export const TIPOS: readonly TipoCliente[] = ['PersonaFisica', 'Empresa'];

export interface ValoresCliente {
  nombre: string;
  tipo: TipoCliente;
  documento: string;
  email: string;
  telefono: string;
  direccion: string;
  notas: string;
  agenteRetencion: boolean;
  enGestionCobro: boolean;
  recordatoriosPausados: boolean;
  numero: string;
}

export interface ValoresProspecto {
  nombre: string;
  tipo: TipoCliente;
  documento: string;
  email: string;
  telefono: string;
  notas: string;
}

export interface ValoresContacto {
  nombre: string;
  relacion: string;
  email: string;
  telefono: string;
  esPrincipal: boolean;
}

/** Texto recortado, o `null` si queda vacío: la API distingue "sin valor" de "cadena vacía". */
export function textoONulo(valor: string | null | undefined): string | null {
  const t = (valor ?? '').trim();
  return t === '' ? null : t;
}

export function valoresCliente(ficha?: Partial<ClienteFicha> | null): ValoresCliente {
  return {
    nombre: ficha?.nombre ?? '',
    tipo: ficha?.tipo ?? 'PersonaFisica',
    documento: ficha?.documento ?? '',
    email: ficha?.email ?? '',
    telefono: ficha?.telefono ?? '',
    direccion: ficha?.direccion ?? '',
    notas: ficha?.notas ?? '',
    agenteRetencion: ficha?.agenteRetencion ?? false,
    enGestionCobro: ficha?.enGestionCobro ?? false,
    recordatoriosPausados: ficha?.recordatoriosPausados ?? false,
    numero: '',
  };
}

/** Valores de un cliente nuevo a partir de un prospecto: solo lo que comparten. */
export function valoresClienteDesdeProspecto(p: ProspectoFicha): ValoresCliente {
  return {
    ...valoresCliente(),
    nombre: p.nombre,
    tipo: p.tipo,
    documento: p.documento ?? '',
    email: p.email ?? '',
    telefono: p.telefono ?? '',
    notas: p.notas ?? '',
  };
}

/** `true` si el texto es un entero de 1 a `NUMERO_MAXIMO`. */
export function numeroValido(texto: string): boolean {
  if (!/^\d{1,8}$/.test(texto.trim())) return false;
  const n = Number(texto);
  return n >= 1 && n <= NUMERO_MAXIMO;
}

/**
 * Cuerpo de `POST/PUT /clientes`. El número solo viaja en el alta y solo si quien guarda puede
 * fijarlo (`configuracion.editar`): la API responde 403 a un número de quien no puede, así que no
 * se manda.
 */
export function cuerpoCliente(v: ValoresCliente, opciones: { alta: boolean; puedeFijarNumero: boolean }): CuerpoCliente {
  const cuerpo: CuerpoCliente = {
    nombre: v.nombre.trim(),
    tipo: v.tipo,
    documento: textoONulo(v.documento),
    email: textoONulo(v.email),
    telefono: textoONulo(v.telefono),
    direccion: textoONulo(v.direccion),
    notas: textoONulo(v.notas),
    agenteRetencion: v.agenteRetencion,
    enGestionCobro: v.enGestionCobro,
    recordatoriosPausados: v.recordatoriosPausados,
  };
  if (opciones.alta && opciones.puedeFijarNumero && v.numero.trim() !== '') cuerpo.numero = Number(v.numero);
  return cuerpo;
}

export function valoresProspecto(p?: Partial<ProspectoFicha> | null): ValoresProspecto {
  return {
    nombre: p?.nombre ?? '',
    tipo: p?.tipo ?? 'PersonaFisica',
    documento: p?.documento ?? '',
    email: p?.email ?? '',
    telefono: p?.telefono ?? '',
    notas: p?.notas ?? '',
  };
}

export function cuerpoProspecto(v: ValoresProspecto): CuerpoProspecto {
  return {
    nombre: v.nombre.trim(),
    tipo: v.tipo,
    documento: textoONulo(v.documento),
    email: textoONulo(v.email),
    telefono: textoONulo(v.telefono),
    notas: textoONulo(v.notas),
  };
}

export function valoresContacto(c?: Partial<Contacto> | null): ValoresContacto {
  return {
    nombre: c?.nombre ?? '',
    relacion: c?.relacion ?? '',
    email: c?.email ?? '',
    telefono: c?.telefono ?? '',
    esPrincipal: c?.esPrincipal ?? false,
  };
}

export function cuerpoContacto(v: ValoresContacto): CuerpoContacto {
  return {
    nombre: v.nombre.trim(),
    relacion: textoONulo(v.relacion),
    email: textoONulo(v.email),
    telefono: textoONulo(v.telefono),
    esPrincipal: v.esPrincipal,
  };
}

/** Comprobación ligera de forma; la que manda es `EmailSeguro` en la API. Vacío es válido (opcional). */
export function emailPlausible(texto: string): boolean {
  const t = texto.trim();
  return t === '' || /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(t);
}

/**
 * Petición de la búsqueda por documento (SEC-030): la ruta es fija y el documento va en el cuerpo.
 * Hay un test que garantiza que la ruta nunca lleva el documento.
 */
export function peticionBusquedaDocumento(
  documento: string,
  opciones: { tipo?: TipoCliente | undefined; incluirInactivos: boolean; page: number; pageSize: number },
): { ruta: string; cuerpo: CuerpoBusquedaDocumento } {
  return {
    ruta: '/clientes/busqueda',
    cuerpo: {
      documento: documento.trim(),
      ...(opciones.tipo ? { tipo: opciones.tipo } : {}),
      incluirInactivos: opciones.incluirInactivos,
      page: opciones.page,
      pageSize: opciones.pageSize,
    },
  };
}

/** Parámetros de `GET /clientes`. No hay parámetro de documento: la API lo rechaza con 400. */
export function paramsListaClientes(f: {
  q: string;
  tipo: TipoCliente | '';
  incluirInactivos: boolean;
  page: number;
  pageSize: number;
}): Record<string, string | number | boolean> {
  const params: Record<string, string | number | boolean> = { page: f.page, pageSize: f.pageSize };
  const q = f.q.trim();
  if (q !== '') params.q = q;
  if (f.tipo !== '') params.tipo = f.tipo;
  if (f.incluirInactivos) params.incluirInactivos = true;
  return params;
}

/**
 * Enlaces de la ficha a otros módulos: rutas relativas con `clienteId`. Solo se enseñan los que el
 * enrutador ya monta (`disponible`) y que el usuario puede abrir, así que no hay enlaces a un 404.
 */
export const ENLACES_CLIENTE = [
  { clave: 'contratos', base: '/contratos' },
  { clave: 'reservas', base: '/reservas' },
  { clave: 'cuentas', base: '/cuentas' },
] as const;

export type ClaveEnlace = (typeof ENLACES_CLIENTE)[number]['clave'];

export function enlacesDeCliente(
  clienteId: string,
  disponible: (base: string) => boolean,
): { clave: ClaveEnlace; destino: string }[] {
  return ENLACES_CLIENTE.filter((e) => disponible(e.base)).map((e) => ({
    clave: e.clave,
    destino: `${e.base}?clienteId=${encodeURIComponent(clienteId)}`,
  }));
}
