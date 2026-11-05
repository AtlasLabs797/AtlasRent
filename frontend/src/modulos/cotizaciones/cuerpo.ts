import { centimosDeLinea, type LineaEditable } from './calculo.ts';
import type { EstadoCotizacionApi } from '../operacion/etiquetas.ts';
import type { MonedaApi } from '../operacion/tipos.ts';

export interface FormularioCotizacion {
  destinatarioTipo: 'cliente' | 'prospecto';
  destinatarioId: string | null;
  unidadId: string | null;
  moneda: MonedaApi;
  lineas: LineaEditable[];
}

export const LINEA_VACIA: LineaEditable = { descripcion: '', cantidad: '1', precioUnitario: '' };

/** Cuerpo de `POST /cotizaciones` y `PUT /cotizaciones/{id}`. Importes y cantidades como texto decimal. */
export function cuerpoCotizacion(f: FormularioCotizacion) {
  return {
    clienteId: f.destinatarioTipo === 'cliente' ? f.destinatarioId : null,
    prospectoId: f.destinatarioTipo === 'prospecto' ? f.destinatarioId : null,
    unidadId: f.unidadId,
    moneda: f.moneda,
    lineas: f.lineas.map((l) => ({
      descripcion: l.descripcion.trim(),
      cantidad: l.cantidad.trim().replace(',', '.'),
      precioUnitario: l.precioUnitario.trim().replace(',', '.'),
    })),
  };
}

export interface ErroresCotizacion {
  destinatario?: string;
  lineas?: string;
  /** Mensaje por índice de línea. */
  linea: Record<number, string>;
}

/** Validación previa al envío; las reglas de verdad las vuelve a aplicar la API (409). */
export function validarCotizacion(f: FormularioCotizacion): ErroresCotizacion {
  const errores: ErroresCotizacion = { linea: {} };
  if (!f.destinatarioId) errores.destinatario = 'Elige un cliente o un prospecto.';
  if (f.lineas.length === 0) errores.lineas = 'Añade al menos una línea.';
  f.lineas.forEach((l, i) => {
    if (l.descripcion.trim() === '') errores.linea[i] = 'Escribe qué se ofrece.';
    else if (centimosDeLinea(l.cantidad, l.precioUnitario) === null) errores.linea[i] = 'Cantidad y precio deben ser números no negativos.';
  });
  return errores;
}

export function hayErrores(e: ErroresCotizacion): boolean {
  return Boolean(e.destinatario || e.lineas || Object.keys(e.linea).length > 0);
}

/** D18: borrador → enviada → aceptada o rechazada. */
export function transicionesCotizacion(estado: EstadoCotizacionApi): EstadoCotizacionApi[] {
  switch (estado) {
    case 'Borrador':
      return ['Enviada'];
    case 'Enviada':
      return ['Aceptada', 'Rechazada'];
    default:
      return [];
  }
}

/** Solo se edita y se da de baja en borrador: lo que ya salió no se toca. */
export function cotizacionEditable(estado: EstadoCotizacionApi): boolean {
  return estado === 'Borrador';
}

/** Solo una cotización aceptada se copia a una reserva o a un contrato. */
export function puedeConvertir(estado: EstadoCotizacionApi): boolean {
  return estado === 'Aceptada';
}
