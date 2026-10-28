import { formatearPorcentaje } from './config.ts';
import { construirCuerpo, type Campo, type Fila, type Valores } from './esquema.ts';
import { textosConfiguracion as t } from './textos.ts';

/** Catálogos de `ConfigController` con alta, edición y baja. Cada uno es una ruta, un esquema y unas columnas. */
export interface ColumnaCatalogo {
  key: string;
  etiqueta: string;
  align?: 'right';
  mono?: boolean;
  /** Texto de la celda a partir de la fila. */
  formato: (fila: Fila) => string;
}

export interface DefinicionCatalogo {
  id: string;
  ruta: string;
  titulo: string;
  descripcion?: string;
  nuevo: string;
  editar: (fila: Fila) => string;
  /** Nombre de la propiedad booleana de estado: `activa` o `activo`. */
  campoActivo: 'activa' | 'activo';
  campos: readonly Campo[];
  columnas: readonly ColumnaCatalogo[];
  /** Campos de solo lectura en esta fila y por qué. */
  bloqueo?: (fila: Fila) => { campos: readonly string[]; motivo: string } | null;
}

const texto = (k: string) => (f: Fila) => (f[k] === null || f[k] === undefined || f[k] === '' ? '—' : String(f[k]));
const opciones = (m: Record<string, string>) => Object.entries(m).map(([value, label]) => ({ value, label }));
const MONEDAS = [
  { value: 'USD', label: 'USD' },
  { value: 'DOP', label: 'DOP' },
];

export const SERIES: DefinicionCatalogo = {
  id: 'series',
  ruta: '/config/series',
  titulo: t.series.titulo,
  descripcion: t.series.descripcion,
  nuevo: t.series.nueva,
  editar: (f) => t.series.editar.replace('{numero}', String(f.numero)),
  campoActivo: 'activa',
  campos: [
    { clave: 'numero', etiqueta: t.series.campos.numero, tipo: 'entero', min: 1, max: 999, requerido: true },
    { clave: 'prefijo', etiqueta: t.series.campos.prefijo, hint: t.series.campos.prefijoHint, tipo: 'texto', max: 16, requerido: true, mono: true },
    { clave: 'moneda', etiqueta: t.series.campos.moneda, tipo: 'select', opciones: MONEDAS, vacio: t.series.campos.monedaLibre },
    { clave: 'tipo', etiqueta: t.series.campos.tipo, tipo: 'select', opciones: opciones(t.series.tipos) },
    { clave: 'plantillaPdf', etiqueta: t.series.campos.plantillaPdf, hint: t.series.campos.plantillaPdfHint, tipo: 'texto', max: 120 },
    { clave: 'condicionesPago', etiqueta: t.series.campos.condicionesPago, hint: t.series.campos.condicionesPagoHint, tipo: 'texto', max: 500, multilinea: true },
    { clave: 'sumaAFacturado', etiqueta: t.series.campos.sumaAFacturado, tipo: 'switch' },
    { clave: 'sumaASaldos', etiqueta: t.series.campos.sumaASaldos, tipo: 'switch' },
    { clave: 'generaComprobante', etiqueta: t.series.campos.generaComprobante, tipo: 'switch' },
    { clave: 'activa', etiqueta: t.series.campos.activa, tipo: 'switch' },
  ],
  columnas: [
    { key: 'numero', etiqueta: t.series.columnas.numero, mono: true, formato: texto('numero') },
    { key: 'prefijo', etiqueta: t.series.columnas.prefijo, mono: true, formato: texto('prefijo') },
    { key: 'moneda', etiqueta: t.series.columnas.moneda, formato: (f) => (f.moneda ? String(f.moneda) : t.series.campos.monedaLibre) },
    { key: 'tipo', etiqueta: t.series.columnas.tipo, formato: (f) => (t.series.tipos as Record<string, string>)[String(f.tipo)] ?? String(f.tipo) },
    { key: 'siguiente', etiqueta: t.series.columnas.siguiente, align: 'right', mono: true, formato: texto('siguiente') },
    { key: 'generaComprobante', etiqueta: t.series.columnas.comprobante, formato: (f) => (f.generaComprobante ? t.comun.si : t.comun.no) },
  ],
  // `siguiente` > 1 significa que ya se emitió algún número (el backend además mira las facturas).
  bloqueo: (f) =>
    typeof f.siguiente === 'number' && f.siguiente > 1 ? { campos: ['moneda', 'prefijo', 'tipo'], motivo: t.series.bloqueada } : null,
};

export const ITBIS: DefinicionCatalogo = {
  id: 'itbis',
  ruta: '/config/tipos-itbis',
  titulo: t.impuestos.itbis.titulo,
  descripcion: t.impuestos.itbis.descripcion,
  nuevo: t.impuestos.itbis.nuevo,
  editar: () => t.impuestos.itbis.editar,
  campoActivo: 'activo',
  campos: [
    { clave: 'nombre', etiqueta: t.impuestos.campos.nombre, tipo: 'texto', max: 120, requerido: true },
    { clave: 'porcentaje', etiqueta: t.impuestos.campos.porcentaje, tipo: 'decimal', decimales: 2, min: 0, max: 100, requerido: true, sufijo: '%' },
    { clave: 'activo', etiqueta: t.impuestos.campos.activo, tipo: 'switch' },
  ],
  columnas: [
    { key: 'nombre', etiqueta: t.impuestos.itbis.columnas.nombre, formato: texto('nombre') },
    { key: 'porcentaje', etiqueta: t.impuestos.itbis.columnas.porcentaje, align: 'right', mono: true, formato: (f) => formatearPorcentaje(f.porcentaje as number) },
  ],
};

export const RETENCIONES: DefinicionCatalogo = {
  id: 'retenciones',
  ruta: '/config/tipos-retencion',
  titulo: t.impuestos.retencion.titulo,
  descripcion: t.impuestos.retencion.descripcion,
  nuevo: t.impuestos.retencion.nuevo,
  editar: () => t.impuestos.retencion.editar,
  campoActivo: 'activo',
  campos: [
    { clave: 'nombre', etiqueta: t.impuestos.campos.nombre, tipo: 'texto', max: 120, requerido: true },
    { clave: 'porcentaje', etiqueta: t.impuestos.campos.porcentaje, tipo: 'decimal', decimales: 2, min: 0, max: 100, requerido: true, sufijo: '%' },
    { clave: 'base', etiqueta: t.impuestos.campos.base, tipo: 'select', opciones: opciones(t.impuestos.bases) },
    { clave: 'esDefecto', etiqueta: t.impuestos.campos.esDefecto, hint: t.impuestos.campos.esDefectoHint, tipo: 'switch' },
    { clave: 'activo', etiqueta: t.impuestos.campos.activo, tipo: 'switch' },
  ],
  columnas: [
    { key: 'nombre', etiqueta: t.impuestos.retencion.columnas.nombre, formato: texto('nombre') },
    { key: 'porcentaje', etiqueta: t.impuestos.retencion.columnas.porcentaje, align: 'right', mono: true, formato: (f) => formatearPorcentaje(f.porcentaje as number) },
    { key: 'base', etiqueta: t.impuestos.retencion.columnas.base, formato: (f) => (t.impuestos.bases as Record<string, string>)[String(f.base)] ?? String(f.base) },
    { key: 'esDefecto', etiqueta: t.impuestos.retencion.columnas.defecto, formato: (f) => (f.esDefecto ? t.comun.si : t.comun.no) },
  ],
};

export const FORMAS_PAGO: DefinicionCatalogo = {
  id: 'formas-pago',
  ruta: '/config/formas-pago',
  titulo: t.pagos.formas.titulo,
  nuevo: t.pagos.formas.nuevo,
  editar: () => t.pagos.formas.editar,
  campoActivo: 'activa',
  campos: [
    { clave: 'nombre', etiqueta: t.pagos.campos.nombre, tipo: 'texto', max: 120, requerido: true },
    { clave: 'activa', etiqueta: t.pagos.campos.activa, tipo: 'switch' },
  ],
  columnas: [{ key: 'nombre', etiqueta: t.pagos.formas.columnas.nombre, formato: texto('nombre') }],
};

export const CUENTAS: DefinicionCatalogo = {
  id: 'cuentas',
  ruta: '/config/cuentas',
  titulo: t.pagos.cuentas.titulo,
  nuevo: t.pagos.cuentas.nuevo,
  editar: () => t.pagos.cuentas.editar,
  campoActivo: 'activa',
  campos: [
    { clave: 'nombre', etiqueta: t.pagos.campos.nombre, tipo: 'texto', max: 120, requerido: true },
    { clave: 'banco', etiqueta: t.pagos.campos.banco, tipo: 'texto', max: 120 },
    { clave: 'moneda', etiqueta: t.pagos.campos.moneda, tipo: 'select', opciones: MONEDAS },
    { clave: 'activa', etiqueta: t.pagos.campos.activa, tipo: 'switch' },
  ],
  columnas: [
    { key: 'nombre', etiqueta: t.pagos.cuentas.columnas.nombre, formato: texto('nombre') },
    { key: 'banco', etiqueta: t.pagos.cuentas.columnas.banco, formato: texto('banco') },
    { key: 'moneda', etiqueta: t.pagos.cuentas.columnas.moneda, formato: texto('moneda') },
  ],
};

export const SUBEMPRESAS: DefinicionCatalogo = {
  id: 'subempresas',
  ruta: '/config/subempresas',
  titulo: t.subempresas.titulo,
  descripcion: t.subempresas.descripcion,
  nuevo: t.subempresas.nueva,
  editar: () => t.subempresas.editar,
  campoActivo: 'activa',
  campos: [
    { clave: 'nombre', etiqueta: t.subempresas.campos.nombre, tipo: 'texto', max: 120, requerido: true },
    { clave: 'rnc', etiqueta: t.subempresas.campos.rnc, tipo: 'texto', max: 32, mono: true },
    { clave: 'direccion', etiqueta: t.subempresas.campos.direccion, tipo: 'texto', max: 300 },
    { clave: 'telefono', etiqueta: t.subempresas.campos.telefono, tipo: 'texto', max: 40 },
    { clave: 'email', etiqueta: t.subempresas.campos.email, tipo: 'texto', max: 200 },
    { clave: 'propietaria', etiqueta: t.subempresas.campos.propietaria, tipo: 'switch' },
    { clave: 'facturadora', etiqueta: t.subempresas.campos.facturadora, tipo: 'switch' },
    { clave: 'activa', etiqueta: t.subempresas.campos.activa, tipo: 'switch' },
  ],
  columnas: [
    { key: 'nombre', etiqueta: t.subempresas.columnas.nombre, formato: texto('nombre') },
    { key: 'rnc', etiqueta: t.subempresas.columnas.rnc, mono: true, formato: texto('rnc') },
    {
      key: 'funcion',
      etiqueta: t.subempresas.columnas.funcion,
      formato: (f) => [f.propietaria ? t.subempresas.propietaria : '', f.facturadora ? t.subempresas.facturadora : ''].filter(Boolean).join(' · ') || '—',
    },
  ],
};

/** Esquema del numerador fiscal (`PUT /config/numeradores-fiscales/{serieId}`). */
export const CAMPOS_NUMERADOR: readonly Campo[] = [
  { clave: 'tipoPrefijo', etiqueta: t.fiscal.campos.tipoPrefijo, hint: t.fiscal.campos.tipoPrefijoHint, tipo: 'texto', max: 16, requerido: true, mono: true },
  { clave: 'rangoDesde', etiqueta: t.fiscal.campos.rangoDesde, tipo: 'entero', min: 1, max: 2147483646, requerido: true },
  { clave: 'rangoHasta', etiqueta: t.fiscal.campos.rangoHasta, tipo: 'entero', min: 1, max: 2147483646, requerido: true },
  { clave: 'fechaVencimiento', etiqueta: t.fiscal.campos.fechaVencimiento, hint: t.fiscal.campos.fechaVencimientoHint, tipo: 'fecha', requerido: true },
  { clave: 'siguienteNumero', etiqueta: t.fiscal.campos.siguienteNumero, hint: t.fiscal.campos.siguienteNumeroHint, tipo: 'entero', min: 1, max: 2147483647, requerido: true },
  { clave: 'umbralAlerta', etiqueta: t.fiscal.campos.umbralAlerta, hint: t.fiscal.campos.umbralAlertaHint, tipo: 'entero', min: 0, max: 2147483647, requerido: true },
];

/** Esquema de la plantilla de mensaje (`PUT /config/plantillas-mensaje`). Tipo e idioma son la clave: solo al crear. */
export const CAMPOS_PLANTILLA: readonly Campo[] = [
  { clave: 'tipo', etiqueta: t.mensajes.campos.tipo, tipo: 'select', opciones: opciones(t.mensajes.tipos), soloAlta: true },
  { clave: 'idioma', etiqueta: t.mensajes.campos.idioma, hint: t.mensajes.campos.idiomaHint, tipo: 'texto', max: 10, requerido: true, soloAlta: true, mono: true },
  { clave: 'asunto', etiqueta: t.mensajes.campos.asunto, tipo: 'texto', max: 300, requerido: true },
  { clave: 'cuerpo', etiqueta: t.mensajes.campos.cuerpo, tipo: 'texto', max: 8000, requerido: true, multilinea: true },
  { clave: 'activa', etiqueta: t.mensajes.campos.activa, hint: t.mensajes.campos.activaHint, tipo: 'switch' },
];

/** Esquema de `PUT /config/ajustes`. */
const a = t.ajustes;
export const CAMPOS_AJUSTES: readonly Campo[] = [
  { clave: 'toleranciaCambioUsd', etiqueta: a.toleranciaUsd, tipo: 'decimal', decimales: 2, min: 0, max: 1000000, sufijo: 'USD' },
  { clave: 'toleranciaCambioDop', etiqueta: a.toleranciaDop, tipo: 'decimal', decimales: 2, min: 0, max: 1000000, sufijo: 'DOP' },
  { clave: 'modoRecordatorios', etiqueta: a.modo, hint: a.modoHint, tipo: 'select', opciones: Object.entries(a.modos).map(([value, label]) => ({ value, label })) },
  { clave: 'diasPrimerRecordatorio', etiqueta: a.diasPrimero, hint: a.diasPrimeroHint, tipo: 'entero', min: 1, max: 365 },
  { clave: 'diasEntreRecordatorios', etiqueta: a.diasEntre, hint: a.diasEntreHint, tipo: 'entero', min: 1, max: 365 },
  { clave: 'mfaGerente', etiqueta: a.mfaGerente, hint: a.mfaGerenteHint, tipo: 'switch' },
];


/** Cuerpo de `PUT /config/ajustes`: decimales como string, vacío = null. */
export function cuerpoAjustes(valores: Valores): Record<string, unknown> {
  return construirCuerpo(CAMPOS_AJUSTES, valores);
}
