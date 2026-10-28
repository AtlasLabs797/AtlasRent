import { normalizarDecimal } from '../../dominio/dinero.ts';
import type {
  CuerpoInmueble,
  CuerpoTarifa,
  CuerpoUnidad,
  EstadoUnidad,
  InmuebleFicha,
  ModalidadAlquiler,
  Moneda,
  TipoInmueble,
  TipoUnidad,
  Unidad,
} from './tipos.ts';

/**
 * Lógica pura del módulo (sin React). Reglas de `InmuebleDtos.cs` y `AlmacenDocumentos.cs`: la API
 * las vuelve a validar. El dinero nunca pasa por coma flotante: el texto del campo se normaliza con
 * `normalizarDecimal` (solo reordena dígitos) y viaja como string.
 */
export const TIPOS_INMUEBLE: readonly TipoInmueble[] = ['Edificio', 'Villa', 'Local', 'Terreno', 'Otro'];
export const TIPOS_UNIDAD: readonly TipoUnidad[] = ['Oficina', 'Sala', 'Apartamento', 'Local', 'Bodega', 'Terreno', 'Otro'];
export const ESTADOS_UNIDAD: readonly EstadoUnidad[] = ['Disponible', 'Mantenimiento', 'FueraDeServicio'];
export const MODALIDADES: readonly ModalidadAlquiler[] = ['Mensual', 'MedioDia', 'DiaCompleto'];
export const MONEDAS: readonly Moneda[] = ['USD', 'DOP'];

/** 25 MB, el tope de `AlmacenDocumentos.TamanoMaximoBytes`. */
export const TAMANO_MAXIMO_BYTES = 25 * 1024 * 1024;

export function textoONulo(valor: string | null | undefined): string | null {
  const t = (valor ?? '').trim();
  return t === '' ? null : t;
}

// ---------------------------------------------------------------- inmueble

export interface ValoresInmueble {
  nombre: string;
  direccion: string;
  tipo: TipoInmueble;
  notas: string;
  plantillaContratoPorDefecto: string;
  renovacionDefectoMeses: string;
}

export function valoresInmueble(i?: Partial<InmuebleFicha> | null): ValoresInmueble {
  return {
    nombre: i?.nombre ?? '',
    direccion: i?.direccion ?? '',
    tipo: i?.tipo ?? 'Villa',
    notas: i?.notas ?? '',
    plantillaContratoPorDefecto: i?.plantillaContratoPorDefecto ?? '',
    renovacionDefectoMeses: i?.renovacionDefectoMeses != null ? String(i.renovacionDefectoMeses) : '',
  };
}

/** Entero de 1 a 600 (`[Range(1, 600)]`) o vacío. */
export function mesesValidos(texto: string): boolean {
  const t = texto.trim();
  if (t === '') return true;
  return /^\d{1,3}$/.test(t) && Number(t) >= 1 && Number(t) <= 600;
}

/**
 * La propietaria no se edita en esta pantalla (no hay listado de subempresas en la API de esta
 * tarea): se reenvía la que ya tenía el inmueble para que un PUT no la borre.
 */
export function cuerpoInmueble(v: ValoresInmueble, original?: Pick<InmuebleFicha, 'propietariaId'> | null): CuerpoInmueble {
  return {
    nombre: v.nombre.trim(),
    direccion: textoONulo(v.direccion),
    tipo: v.tipo,
    propietariaId: original?.propietariaId ?? null,
    notas: textoONulo(v.notas),
    plantillaContratoPorDefecto: textoONulo(v.plantillaContratoPorDefecto),
    renovacionDefectoMeses: v.renovacionDefectoMeses.trim() === '' ? null : Number(v.renovacionDefectoMeses),
  };
}

// ---------------------------------------------------------------- unidad

export interface ValoresUnidad {
  nombre: string;
  tipo: TipoUnidad;
  estadoManual: EstadoUnidad;
  modalidades: ModalidadAlquiler[];
  /** Texto de cada tarifa por `Modalidad|Moneda`; vacío = sin tarifa. */
  tarifas: Record<string, string>;
  metrosCuadrados: string;
  capacidad: string;
  habitaciones: string;
  banos: string;
  medidor: string;
  notas: string;
}

export const claveTarifa = (m: ModalidadAlquiler, moneda: Moneda): string => `${m}|${moneda}`;

export function valoresUnidad(u?: Partial<Unidad> | null): ValoresUnidad {
  const tarifas: Record<string, string> = {};
  for (const t of u?.tarifas ?? []) tarifas[claveTarifa(t.modalidad, t.moneda)] = normalizarDecimal(t.importe);
  const num = (n: number | null | undefined) => (n == null ? '' : String(n));
  return {
    nombre: u?.nombre ?? '',
    tipo: u?.tipo ?? 'Apartamento',
    estadoManual: u?.estadoManual ?? 'Disponible',
    modalidades: u?.modalidades ? [...u.modalidades] : ['Mensual'],
    tarifas,
    metrosCuadrados: num(u?.metrosCuadrados),
    capacidad: num(u?.capacidad),
    habitaciones: num(u?.habitaciones),
    banos: num(u?.banos),
    medidor: u?.medidor ?? '',
    notas: u?.notas ?? '',
  };
}

/** Importe de tarifa: hasta 16 enteros y 2 decimales, con punto o coma. Devuelve el motivo o `null`. */
export function errorImporte(texto: string): 'formato' | null {
  const t = texto.trim();
  if (t === '') return null;
  return /^\d{1,16}([.,]\d{1,2})?$/.test(t) ? null : 'formato';
}

/** "1250,5" -> "1250.50". Lanza si el texto no es un importe válido. */
export function importeATexto(texto: string): string {
  if (errorImporte(texto) !== null || texto.trim() === '') throw new Error(`Importe no válido: "${texto}"`);
  return normalizarDecimal(texto.trim().replace(',', '.'));
}

/** Tarifas rellenas de las modalidades marcadas; una tarifa de una modalidad desmarcada se descarta. */
export function construirTarifas(v: Pick<ValoresUnidad, 'modalidades' | 'tarifas'>): CuerpoTarifa[] {
  const salida: CuerpoTarifa[] = [];
  for (const modalidad of MODALIDADES) {
    if (!v.modalidades.includes(modalidad)) continue;
    for (const moneda of MONEDAS) {
      const texto = v.tarifas[claveTarifa(modalidad, moneda)] ?? '';
      if (texto.trim() !== '') salida.push({ modalidad, moneda, importe: importeATexto(texto) });
    }
  }
  return salida;
}

/** Decimal no negativo con hasta 2 decimales (m², baños con medios) o vacío. */
export function decimalSimpleValido(texto: string): boolean {
  const t = texto.trim();
  return t === '' || /^\d{1,8}([.,]\d{1,2})?$/.test(t);
}

export function enteroValido(texto: string): boolean {
  const t = texto.trim();
  return t === '' || /^\d{1,9}$/.test(t);
}

const aNumeroONulo = (t: string): number | null => (t.trim() === '' ? null : Number(t.trim().replace(',', '.')));

/**
 * Cuerpo de `PUT/POST` de unidad. La propietaria y la facturadora por defecto no se editan aquí: se
 * reenvían las de la unidad existente para que el PUT no las borre.
 */
export function cuerpoUnidad(
  v: ValoresUnidad,
  original?: Pick<Unidad, 'propietariaId' | 'facturadoraPorDefectoId'> | null,
): CuerpoUnidad {
  return {
    nombre: v.nombre.trim(),
    tipo: v.tipo,
    propietariaId: original?.propietariaId ?? null,
    facturadoraPorDefectoId: original?.facturadoraPorDefectoId ?? null,
    estadoManual: v.estadoManual,
    modalidades: MODALIDADES.filter((m) => v.modalidades.includes(m)),
    tarifas: construirTarifas(v),
    metrosCuadrados: aNumeroONulo(v.metrosCuadrados),
    capacidad: aNumeroONulo(v.capacidad),
    habitaciones: aNumeroONulo(v.habitaciones),
    banos: aNumeroONulo(v.banos),
    medidor: textoONulo(v.medidor),
    notas: textoONulo(v.notas),
  };
}

/** Recuento por estado manual para `OccupancyBar`. La ocupación real se calcula por fechas, no aquí. */
export function contarEstados(unidades: readonly Pick<Unidad, 'estadoManual' | 'activo'>[]): {
  disponible: number;
  mantenimiento: number;
  fuera: number;
} {
  const cuenta = { disponible: 0, mantenimiento: 0, fuera: 0 };
  for (const u of unidades) {
    if (!u.activo) continue;
    if (u.estadoManual === 'Disponible') cuenta.disponible++;
    else if (u.estadoManual === 'Mantenimiento') cuenta.mantenimiento++;
    else cuenta.fuera++;
  }
  return cuenta;
}

/** Estado de `Badge`/`UnitCard` para un estado manual. */
export function estadoBadge(e: EstadoUnidad): 'disponible' | 'mantenimiento' | 'fuera' {
  return e === 'Disponible' ? 'disponible' : e === 'Mantenimiento' ? 'mantenimiento' : 'fuera';
}

// ---------------------------------------------------------------- documentos PDF

export type MotivoPdf = 'vacio' | 'noPdf' | 'grande';

/**
 * Validación previa a la subida, con las mismas reglas que `AlmacenDocumentos`: solo PDF y de 1 byte
 * a 25 MB. Es una cortesía para no subir 25 MB y recibir un 400: la API comprueba el contenido real
 * (firma `%PDF`), que aquí no se puede.
 */
export function validarPdf(archivo: { name: string; type: string; size: number }): MotivoPdf | null {
  const porNombre = /\.pdf$/i.test(archivo.name);
  const porTipo = archivo.type === '' || archivo.type === 'application/pdf';
  if (!porNombre || !porTipo) return 'noPdf';
  if (archivo.size <= 0) return 'vacio';
  if (archivo.size > TAMANO_MAXIMO_BYTES) return 'grande';
  return null;
}

/** 1536 -> "1,5 KB"; 3 MB -> "3,0 MB". */
export function formatearTamano(bytes: number): string {
  const u = bytes >= 1024 * 1024 ? [1024 * 1024, 'MB'] : bytes >= 1024 ? [1024, 'KB'] : [1, 'B'];
  const valor = bytes / (u[0] as number);
  return `${u[1] === 'B' ? String(bytes) : valor.toFixed(1).replace('.', ',')} ${u[1]}`;
}

export type ClaveErrorSubida = 'grande' | 'noPdf' | 'detalle' | 'generico';

/** Qué mensaje enseñar ante el fallo de la API al subir: 413 y 415 son claros por sí solos; el 400 trae el motivo. */
export function claveErrorSubida(status: number | undefined, detalle: string | undefined): ClaveErrorSubida {
  if (status === 413) return 'grande';
  if (status === 415) return 'noPdf';
  if (status === 400 && detalle) return 'detalle';
  return 'generico';
}
