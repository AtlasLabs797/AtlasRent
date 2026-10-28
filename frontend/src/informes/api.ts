import { clienteHttp } from '../api/cliente';
import { textos } from '../i18n/textos';
import { parametrosDeFiltros, type FiltrosInformes } from './filtros';

/**
 * Informes económicos y dashboard (F5-01, `docs/ESPECIFICACION.md` §3.15).
 *
 * Los importes llegan como string decimal de la API: el consolidado en dólares lo hace el backend con el
 * tipo de cambio congelado de cada documento. Aquí no se suma, se resta ni se convierte nada
 * (AGENTS.md §6): un informe que recalcula dinero en el navegador deja de cuadrar con la vista única.
 */

/** Bloque de dinero: las dos monedas por separado y el consolidado en dólares (D9). */
export interface ImporteInforme {
  usd: string;
  dop: string;
  consolidadoUsd: string;
}

/** Cobrado por destino: a factura, a fianza o saldo a favor del contrato. */
export interface BloqueCobradoInforme {
  facturas: ImporteInforme;
  fianzas: ImporteInforme;
  saldoAFavor: ImporteInforme;
  total: ImporteInforme;
}

/** Retenciones aplicadas, con y sin comprobante recibido (F3-04). */
export interface BloqueRetenidoInforme {
  conComprobante: ImporteInforme;
  sinComprobante: ImporteInforme;
  total: ImporteInforme;
}

export interface BloquePendienteInforme {
  facturas: ImporteInforme;
  fianzas: ImporteInforme;
  total: ImporteInforme;
}

/** Un mes del gráfico, en `AAAA-MM`, ya consolidado en dólares. */
export interface SerieMensualInforme {
  mes: string;
  facturadoUsd: string;
  cobradoUsd: string;
}

export interface InformeResumenRespuesta {
  desde: string | null;
  hasta: string | null;
  /** Día contra el que se midió lo vencido: `hasta` si se dio, si no el hoy de la aplicación. */
  fechaCorte: string;
  proformado: ImporteInforme;
  facturado: ImporteInforme;
  cobrado: BloqueCobradoInforme;
  retenido: BloqueRetenidoInforme;
  pendiente: BloquePendienteInforme;
  vencido: ImporteInforme;
  fianzasRetenidas: ImporteInforme;
  diferenciasCambio: ImporteInforme;
  serieMensual: SerieMensualInforme[];
  /** Documentos en pesos fuera del consolidado por no tener cambio congelado (§3.12). */
  documentosSinTipoCambioCongelado: number;
  /** El saldo a favor es una foto de hoy: con filtro de fechas no suma al total de cobrado (§3.15). */
  saldoAFavorFueraDePeriodo: boolean;
}

/** Los cuatro grupos de unidades del día son excluyentes: una unidad está en uno. */
export interface IndicadoresUnidadesInforme {
  disponibles: number;
  reservadas: number;
  ocupadas: number;
  mantenimiento: number;
}

export interface DashboardRespuesta {
  fecha: string;
  unidades: IndicadoresUnidadesInforme;
  contratosActivos: number;
  resumen: InformeResumenRespuesta;
}

export async function pedirResumen(filtros: FiltrosInformes): Promise<InformeResumenRespuesta> {
  const { data } = await clienteHttp.get<InformeResumenRespuesta>('/informes/resumen', {
    params: parametrosDeFiltros(filtros),
  });
  return data;
}

export async function pedirDashboard(filtros: FiltrosInformes): Promise<DashboardRespuesta> {
  const { data } = await clienteHttp.get<DashboardRespuesta>('/informes/dashboard', {
    params: parametrosDeFiltros(filtros),
  });
  return data;
}

export type FormatoExportacion = 'xlsx' | 'csv';

/**
 * Descarga la exportación con la misma consulta que muestra la pantalla, para que el fichero y los
 * bloques de arriba sean el mismo informe.
 *
 * Un error de la API llega como JSON (403 sin `informe.ver`, 404 si la ruta no existiera) y con
 * `responseType: 'blob'` se guardaría como si fuera el informe. Se comprueba el tipo de contenido
 * antes de escribir nada en disco.
 */
export async function descargarExportacion(
  filtros: FiltrosInformes,
  formato: FormatoExportacion,
): Promise<void> {
  const respuesta = await clienteHttp.get<Blob>('/informes/exportar', {
    params: { ...parametrosDeFiltros(filtros), formato },
    responseType: 'blob',
  });

  const tipo = String(respuesta.headers['content-type'] ?? '');
  if (tipo.includes('application/json')) {
    throw new Error(textos.informes.exportar.errorCuerpo);
  }

  const sello = filtros.hasta ?? filtros.desde ?? new Date().toISOString().slice(0, 10);
  const url = URL.createObjectURL(respuesta.data);
  const enlace = document.createElement('a');
  enlace.href = url;
  enlace.download = `informes-${sello}.${formato}`;
  document.body.appendChild(enlace);
  enlace.click();
  enlace.remove();
  URL.revokeObjectURL(url);
}