import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { clienteHttp } from '../../api/cliente';
import type {
  Cobro,
  CobroPeticion,
  CobroResumen,
  CobroSimulacion,
  CuentaReceptora,
  CuentasRespuesta,
  Factura,
  FacturaAnulacionSimulacion,
  FacturaDuplicacionRespuesta,
  Fianza,
  FianzaResumen,
  FacturaResumen,
  FormaPago,
  GeneracionPeriodicaRespuesta,
  Moneda,
  Pagina,
  Serie,
  SimulacionPeriodicaRespuesta,
  TipoCambioActual,
} from './tipos.ts';

type Consulta = Record<string, string | number | boolean | undefined>;

function limpia(q: Consulta): Consulta {
  return Object.fromEntries(Object.entries(q).filter(([, v]) => v !== undefined && v !== ''));
}

const conClave = (clave: string) => ({ headers: { 'Idempotency-Key': clave } });

/** Abre un PDF de la API en otra pestaña (la descarga lleva la cookie de sesión, no una URL pública). */
export async function abrirPdf(ruta: string): Promise<void> {
  const { data } = await clienteHttp.get<Blob>(ruta, { responseType: 'blob' });
  const url = URL.createObjectURL(new Blob([data], { type: 'application/pdf' }));
  window.open(url, '_blank', 'noopener');
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}

// ---------------------------------------------------------------- lecturas

export function useCuentas(consulta: Consulta) {
  return useQuery({
    queryKey: ['finanzas', 'cuentas', consulta],
    queryFn: async () => (await clienteHttp.get<CuentasRespuesta>('/cuentas', { params: limpia(consulta) })).data,
  });
}

export function useFacturas(consulta: Consulta) {
  return useQuery({
    queryKey: ['finanzas', 'facturas', consulta],
    queryFn: async () => (await clienteHttp.get<Pagina<FacturaResumen>>('/facturas', { params: limpia(consulta) })).data,
  });
}

export function useFactura(id: string) {
  return useQuery({
    queryKey: ['finanzas', 'factura', id],
    queryFn: async () => (await clienteHttp.get<Factura>(`/facturas/${id}`)).data,
  });
}

export function useCobros(consulta: Consulta) {
  return useQuery({
    queryKey: ['finanzas', 'cobros', consulta],
    queryFn: async () => (await clienteHttp.get<Pagina<CobroResumen>>('/cobros', { params: limpia(consulta) })).data,
  });
}

export function useCobro(id: string) {
  return useQuery({
    queryKey: ['finanzas', 'cobro', id],
    queryFn: async () => (await clienteHttp.get<Cobro>(`/cobros/${id}`)).data,
  });
}

export function useFianzas(consulta: Consulta) {
  return useQuery({
    queryKey: ['finanzas', 'fianzas', consulta],
    queryFn: async () => (await clienteHttp.get<Pagina<FianzaResumen>>('/fianzas', { params: limpia(consulta) })).data,
  });
}

export function useFianza(id: string) {
  return useQuery({
    queryKey: ['finanzas', 'fianza', id],
    queryFn: async () => (await clienteHttp.get<Fianza>(`/fianzas/${id}`)).data,
  });
}

/** Cambio del día de la moneda recibida (DOP por USD). Solo se pide cuando hace falta. */
export function useTipoCambioActual(moneda: Moneda, activo: boolean) {
  return useQuery({
    queryKey: ['finanzas', 'tipo-cambio', moneda],
    enabled: activo,
    queryFn: async () => (await clienteHttp.get<TipoCambioActual>('/tipos-cambio/actual', { params: { moneda } })).data,
  });
}

// TODO(A#): estos catálogos solo los lee `configuracion.editar` (ConfigController). El gerente, que
// registra cobros y emite facturas, recibe 403: hace falta una lectura de catálogos con `cobro.registrar`
// / `factura.emitir`. Mientras tanto la pantalla lo explica en vez de dejar el selector vacío.
export function useFormasPago(activo = true) {
  return useQuery({
    queryKey: ['finanzas', 'formas-pago'],
    enabled: activo,
    retry: false,
    queryFn: async () => (await clienteHttp.get<Pagina<FormaPago>>('/config/formas-pago', { params: { pageSize: 200 } })).data.data,
  });
}

export function useCuentasReceptoras(activo = true) {
  return useQuery({
    queryKey: ['finanzas', 'cuentas-receptoras'],
    enabled: activo,
    retry: false,
    queryFn: async () => (await clienteHttp.get<Pagina<CuentaReceptora>>('/config/cuentas', { params: { pageSize: 200 } })).data.data,
  });
}

export function useSeries(activo = true) {
  return useQuery({
    queryKey: ['finanzas', 'series'],
    enabled: activo,
    retry: false,
    queryFn: async () => (await clienteHttp.get<Pagina<Serie>>('/config/series', { params: { pageSize: 200 } })).data.data,
  });
}

export interface OpcionRef {
  value: string;
  label: string;
}

// La búsqueda del Combobox es local: se traen los 200 primeros (tope de la API). TODO(A#): búsqueda remota si hay más.
export function useClientes() {
  return useQuery({
    queryKey: ['finanzas', 'clientes'],
    queryFn: async () => {
      const { data } = await clienteHttp.get<Pagina<{ id: string; numero: number; nombre: string }>>('/clientes', { params: { pageSize: 200 } });
      return data.data.map((c) => ({ value: c.id, label: c.nombre, meta: `CLI-${String(c.numero).padStart(5, '0')}` }));
    },
  });
}

export function useInmuebles() {
  return useQuery({
    queryKey: ['finanzas', 'inmuebles'],
    queryFn: async () => (await clienteHttp.get<Pagina<{ id: string; nombre: string }>>('/inmuebles', { params: { pageSize: 200 } })).data.data,
  });
}

export function useUnidadesDeInmueble(inmuebleId: string) {
  return useQuery({
    queryKey: ['finanzas', 'unidades', inmuebleId],
    enabled: inmuebleId !== '',
    queryFn: async () => (await clienteHttp.get<Pagina<{ id: string; nombre: string }>>(`/inmuebles/${inmuebleId}/unidades`, { params: { pageSize: 200 } })).data.data,
  });
}

// ---------------------------------------------------------------- escrituras

function useInvalidar() {
  const cliente = useQueryClient();
  return () => cliente.invalidateQueries({ queryKey: ['finanzas'] });
}

export interface ConClave<T> {
  cuerpo: T;
  clave: string;
}

export function useSimularCobro() {
  return useMutation({
    mutationFn: async (cuerpo: CobroPeticion) => (await clienteHttp.post<CobroSimulacion>('/cobros/simular', cuerpo)).data,
  });
}

export function useRegistrarCobro() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async ({ cuerpo, clave }: ConClave<CobroPeticion>) => (await clienteHttp.post<Cobro>('/cobros', cuerpo, conClave(clave))).data,
    onSuccess: invalidar,
  });
}

export function useAnularCobro(id: string) {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (motivo: string) => (await clienteHttp.post<Cobro>(`/cobros/${id}/anular`, { motivo }, conClave(crypto.randomUUID()))).data,
    onSuccess: invalidar,
  });
}

export function useAjusteCambio() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (c: { facturaId: string; tipoCambioValor: string; motivo: string }) =>
      (await clienteHttp.post('/cobros/ajustes-diferencia-cambio', c, conClave(crypto.randomUUID()))).data,
    onSuccess: invalidar,
  });
}

/** Acción sobre una factura (`emitir`, `duplicar`, …): una clave nueva por intento del usuario. */
export function useAccionFactura<R>(id: string, accion: string) {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (cuerpo: unknown = {}) => (await clienteHttp.post<R>(`/facturas/${id}/${accion}`, cuerpo, conClave(crypto.randomUUID()))).data,
    onSuccess: invalidar,
  });
}

export const useEmitirFactura = (id: string) => useAccionFactura<Factura>(id, 'emitir');
export const useAnularFactura = (id: string) => useAccionFactura<Factura>(id, 'anular');
export const useDuplicarFactura = (id: string) => useAccionFactura<FacturaDuplicacionRespuesta>(id, 'duplicar');
export const useConvertirProforma = (id: string) => useAccionFactura<{ factura: Factura }>(id, 'convertir-proforma');
export const useRehacerMoneda = (id: string) => useAccionFactura<{ nueva: FacturaResumen }>(id, 'rehacer-moneda');

export function useSimularAnulacion(id: string, activo: boolean) {
  return useQuery({
    queryKey: ['finanzas', 'simular-anulacion', id],
    enabled: activo,
    gcTime: 0,
    retry: false,
    queryFn: async () => (await clienteHttp.post<FacturaAnulacionSimulacion>(`/facturas/${id}/simular-anulacion`)).data,
  });
}

export function useModificarFactura(id: string) {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (lineas: Array<{ lineaId: string; tipo: string; concepto: string; cantidad: string; precioUnitario: string; tipoItbisId: string | null; tipoRetencionId: string | null }>) =>
      (await clienteHttp.put<Factura>(`/facturas/${id}/modificar`, { lineas }, conClave(crypto.randomUUID()))).data,
    onSuccess: invalidar,
  });
}

export function useReferenciaEfactura(id: string) {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (referencia: string) => (await clienteHttp.put<Factura>(`/facturas/${id}/referencia-efactura`, { referencia }, conClave(crypto.randomUUID()))).data,
    onSuccess: invalidar,
  });
}

export function useSimularPeriodica() {
  return useMutation({
    mutationFn: async (c: { periodo: string; serieId: string }) =>
      (await clienteHttp.post<SimulacionPeriodicaRespuesta>('/facturas/periodicas/simular', c)).data,
  });
}

export function useGenerarPeriodica() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async ({ cuerpo, clave }: ConClave<{ periodo: string; serieId: string; contratosIds: string[] }>) =>
      (await clienteHttp.post<GeneracionPeriodicaRespuesta>('/facturas/periodicas/generar', cuerpo, conClave(clave))).data,
    onSuccess: invalidar,
  });
}

export function useAccionFianza(id: string, accion: 'devolver' | 'retener' | 'liquidar') {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (cuerpo: unknown = undefined) => (await clienteHttp.post<Fianza>(`/fianzas/${id}/${accion}`, cuerpo, conClave(crypto.randomUUID()))).data,
    onSuccess: invalidar,
  });
}
