import { aDecimal, compararImportes, entradaADecimal, esPositivo, sumarImportes, type Importe } from './decimal.ts';
import type { CobroAsignacionSimulada, CobroPeticion, CuentaFila, Moneda } from './tipos.ts';

/** Lógica pura de la vista de cuentas y del panel de cobro (sin React, para el Node Test Runner). */

// ---------------------------------------------------------------- filtros en la URL

export interface FiltrosCuentas {
  page: number;
  anio: string;
  moneda: '' | Moneda;
  /** '' | Pendiente | Parcial | Pagada | Anulada */
  estado: string;
  clienteId: string;
  contratoId: string;
  unidadId: string;
  inmuebleId: string;
  propietariaId: string;
  facturadoraId: string;
  desde: string;
  hasta: string;
  vencidas: boolean;
  noEnviadas: boolean;
  envioFallido: boolean;
  modificadasTrasEnvio: boolean;
  incluirProformas: boolean;
  incluirFianzas: boolean;
}

export const FILTROS_VACIOS: FiltrosCuentas = {
  page: 1,
  anio: '',
  moneda: '',
  estado: '',
  clienteId: '',
  contratoId: '',
  unidadId: '',
  inmuebleId: '',
  propietariaId: '',
  facturadoraId: '',
  desde: '',
  hasta: '',
  vencidas: false,
  noEnviadas: false,
  envioFallido: false,
  modificadasTrasEnvio: false,
  incluirProformas: false,
  incluirFianzas: true,
};

const TEXTOS = ['anio', 'estado', 'clienteId', 'contratoId', 'unidadId', 'inmuebleId', 'propietariaId', 'facturadoraId', 'desde', 'hasta'] as const;
const BOOLEANOS = ['vencidas', 'noEnviadas', 'envioFallido', 'modificadasTrasEnvio', 'incluirProformas'] as const;

export function filtrosDesdeParametros(p: URLSearchParams): FiltrosCuentas {
  const f: FiltrosCuentas = { ...FILTROS_VACIOS };
  for (const k of TEXTOS) f[k] = p.get(k) ?? '';
  const moneda = p.get('moneda');
  f.moneda = moneda === 'USD' || moneda === 'DOP' ? moneda : '';
  for (const k of BOOLEANOS) f[k] = p.get(k) === 'true';
  f.incluirFianzas = p.get('incluirFianzas') !== 'false';
  const pagina = Number(p.get('page'));
  f.page = Number.isInteger(pagina) && pagina > 0 ? pagina : 1;
  return f;
}

/** Solo lo que difiere del valor por defecto: la URL se queda corta y se puede enlazar. */
export function parametrosDeFiltros(f: FiltrosCuentas): URLSearchParams {
  const p = new URLSearchParams();
  for (const k of TEXTOS) if (f[k]) p.set(k, f[k]);
  if (f.moneda) p.set('moneda', f.moneda);
  for (const k of BOOLEANOS) if (f[k]) p.set(k, 'true');
  if (!f.incluirFianzas) p.set('incluirFianzas', 'false');
  if (f.page > 1) p.set('page', String(f.page));
  return p;
}

/** Query de `GET /cuentas`. "Anulada" es estado de la factura; el resto, estado de cobro derivado. */
export function consultaCuentas(f: FiltrosCuentas, pageSize: number): Record<string, string | number | boolean> {
  const q: Record<string, string | number | boolean> = { page: f.page, pageSize };
  for (const k of ['anio', 'clienteId', 'contratoId', 'unidadId', 'inmuebleId', 'propietariaId', 'facturadoraId', 'desde', 'hasta'] as const) {
    if (f[k]) q[k] = f[k];
  }
  if (f.moneda) q.moneda = f.moneda;
  if (f.estado === 'Anulada') q.estado = 'Anulada';
  else if (f.estado) q.estadoCobro = f.estado;
  for (const k of BOOLEANOS) if (f[k]) q[k] = true;
  q.incluirFianzas = f.incluirFianzas;
  return q;
}

// ---------------------------------------------------------------- selección para cobrar

export type MotivoSeleccion = 'vacia' | 'noCobrable' | 'variosClientes' | 'sinContrato' | 'variosContratos';

export type Seleccion =
  | { ok: true; clienteId: string; contratoId: string; filas: CuentaFila[] }
  | { ok: false; motivo: MotivoSeleccion };

/** Un cobro es de un cliente y de un contrato (§3.10.1: nunca mueve dinero entre contratos). */
export function evaluarSeleccion(filas: readonly CuentaFila[]): Seleccion {
  const primera = filas[0];
  if (!primera) return { ok: false, motivo: 'vacia' };
  if (filas.some((f) => !f.cobrable)) return { ok: false, motivo: 'noCobrable' };
  if (filas.some((f) => f.clienteId !== primera.clienteId)) return { ok: false, motivo: 'variosClientes' };
  if (!primera.contratoId) return { ok: false, motivo: 'sinContrato' };
  if (filas.some((f) => f.contratoId !== primera.contratoId)) return { ok: false, motivo: 'variosContratos' };
  return { ok: true, clienteId: primera.clienteId, contratoId: primera.contratoId, filas: [...filas] };
}

// ---------------------------------------------------------------- formulario de cobro

export interface FormularioCobro {
  fechaEfectiva: string;
  monedaRecibida: Moneda;
  importeRecibido: string;
  formaPagoId: string;
  cuentaReceptoraId: string;
  referencia: string;
  usarSaldoAFavor: string;
  tipoCambioAplicado: string;
}

export type ModoAsignacion = 'seleccionadas' | 'fifo';

/** Línea de asignación editable: el importe va en la moneda del destino. */
export interface LineaAsignacion {
  facturaId: string | null;
  fianzaId: string | null;
  importe: string;
}

export function lineasDeSimulacion(a: readonly CobroAsignacionSimulada[]): LineaAsignacion[] {
  return a.map((x) => ({ facturaId: x.facturaId, fianzaId: x.fianzaId, importe: aDecimal(x.importe) }));
}

/** ¿Hay que aplicar un cambio? Sí si alguna moneda de destino difiere de la recibida. */
export function necesitaCambio(recibida: Moneda, destinos: readonly Moneda[]): boolean {
  return destinos.some((d) => d !== recibida);
}

export type ErroresCobro = Partial<Record<keyof FormularioCobro, 'obligatorio' | 'invalido' | 'positivo'>>;

export function validarFormulario(f: FormularioCobro, requiereCambio: boolean): ErroresCobro {
  const e: ErroresCobro = {};
  if (!f.fechaEfectiva) e.fechaEfectiva = 'obligatorio';
  if (!f.formaPagoId) e.formaPagoId = 'obligatorio';
  if (!f.cuentaReceptoraId) e.cuentaReceptoraId = 'obligatorio';
  const importe = entradaADecimal(f.importeRecibido);
  if (!f.importeRecibido.trim()) e.importeRecibido = 'obligatorio';
  else if (importe === null) e.importeRecibido = 'invalido';
  else if (!esPositivo(importe)) e.importeRecibido = 'positivo';
  if (f.usarSaldoAFavor.trim() && entradaADecimal(f.usarSaldoAFavor) === null) e.usarSaldoAFavor = 'invalido';
  if (requiereCambio) {
    const tc = entradaADecimal(f.tipoCambioAplicado, 6);
    if (!f.tipoCambioAplicado.trim()) e.tipoCambioAplicado = 'obligatorio';
    else if (tc === null) e.tipoCambioAplicado = 'invalido';
    else if (!esPositivo(tc)) e.tipoCambioAplicado = 'positivo';
  }
  return e;
}

/**
 * Cuerpo de `POST /cobros/simular` y `POST /cobros`. Con `lineas` (ya simuladas y, si hace falta,
 * corregidas a mano) van como asignaciones explícitas, que mandan sobre todo lo demás; sin ellas se
 * pide a la API la propuesta: las facturas marcadas o la FIFO del contrato.
 */
export function cuerpoCobro(
  f: FormularioCobro,
  seleccion: { clienteId: string; contratoId: string; filas: readonly CuentaFila[] },
  modo: ModoAsignacion,
  lineas: readonly LineaAsignacion[] | null,
  requiereCambio: boolean,
): CobroPeticion {
  const cuerpo: CobroPeticion = {
    clienteId: seleccion.clienteId,
    contratoId: seleccion.contratoId,
    fechaEfectiva: f.fechaEfectiva,
    monedaRecibida: f.monedaRecibida,
    importeRecibido: entradaADecimal(f.importeRecibido) ?? '0.00',
    formaPagoId: f.formaPagoId,
    cuentaReceptoraId: f.cuentaReceptoraId,
    usarSaldoAFavor: entradaADecimal(f.usarSaldoAFavor || '0') ?? '0.00',
  };
  if (f.referencia.trim()) cuerpo.referencia = f.referencia.trim();
  if (requiereCambio) {
    const tc = entradaADecimal(f.tipoCambioAplicado, 6);
    if (tc) cuerpo.tipoCambioAplicado = tc;
  }
  if (lineas) {
    cuerpo.asignaciones = lineas
      .filter((l) => esPositivo(l.importe))
      .map((l) => (l.facturaId ? { facturaId: l.facturaId, importe: l.importe } : { fianzaId: l.fianzaId!, importe: l.importe }));
  } else if (modo === 'seleccionadas') {
    const facturas = seleccion.filas.filter((x) => x.tipo === 'Factura').map((x) => x.id);
    const fianzas = seleccion.filas.filter((x) => x.tipo === 'Fianza');
    if (fianzas.length === 0) cuerpo.facturasSeleccionadas = facturas;
    else {
      // La API solo propone importes para facturas: con fianzas se piden explícitas, cada una por su pendiente.
      cuerpo.asignaciones = seleccion.filas.map((x) =>
        x.tipo === 'Fianza'
          ? { fianzaId: x.id, importe: aDecimal(x.pendiente ?? '0') }
          : { facturaId: x.id, importe: aDecimal(x.pendiente ?? '0') },
      );
    }
  }
  return cuerpo;
}

/** Huella del cuerpo: dice si lo simulado sigue siendo lo que se va a registrar. */
export function huellaCuerpo(c: CobroPeticion): string {
  return JSON.stringify(c);
}

/**
 * Idempotency-Key por intento: el mismo cuerpo reintentado (red caída, doble clic) reutiliza la clave
 * y la API devuelve el cobro ya registrado; un cuerpo distinto estrena clave.
 */
export function claveParaIntento(
  previa: { huella: string; clave: string } | null,
  huella: string,
  generar: () => string,
): { huella: string; clave: string } {
  return previa && previa.huella === huella ? previa : { huella, clave: generar() };
}

// ---------------------------------------------------------------- presentación

export function numeroDocumento(serieNumero: number | null, numero: number | null): string {
  return serieNumero === null || numero === null ? '—' : `${serieNumero}-${String(numero).padStart(5, '0')}`;
}

export type InsigniaEstado = { status?: 'pendiente' | 'parcial' | 'pagado' | 'anulada' | 'borrador' | 'retenida' | 'devolucion' | 'devuelta'; tone?: 'info' | 'neutral'; label?: string };

/** Insignia del estado de una fila de cuentas (el backend manda el nombre del enum según el tipo de fila). */
export function insigniaDeFila(tipo: CuentaFila['tipo'], estado: string): InsigniaEstado {
  switch (estado) {
    case 'Pendiente':
      return { status: 'pendiente' };
    case 'Parcial':
      return { status: 'parcial' };
    case 'Pagada':
      return { status: 'pagado', label: 'Pagada' };
    case 'Anulada':
      return { status: 'anulada' };
    case 'Borrador':
      return { status: 'borrador' };
    case 'Retenida':
      return { status: 'retenida' };
    case 'EnLiquidacion':
      return { status: 'devolucion', label: 'En liquidación' };
    case 'Devuelta':
      return { status: 'devuelta' };
    case 'RetenidaParcial':
      return { tone: 'info', label: 'Retenida parcial' };
    case 'Emitida':
      return { tone: 'neutral', label: tipo === 'Proforma' ? 'Proforma' : 'Emitida' };
    case 'Convertida':
      return { tone: 'info', label: 'Convertida' };
    default:
      return { tone: 'neutral', label: estado };
  }
}

/** El motivo de una devolución de fianza es obligatorio si hay descuento o no deja la fianza a cero (§3.11). */
export function motivoObligatorio(importe: string, descontado: string, saldo: Importe): boolean {
  return compararImportes(descontado, '0') > 0 || compararImportes(sumarImportes([importe, descontado]), saldo) < 0;
}
