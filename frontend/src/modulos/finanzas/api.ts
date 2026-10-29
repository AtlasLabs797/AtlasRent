import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { clienteHttp } from '../../api/cliente';
import type {
  Cobro,
  CobroPeticion,
  CobroResumen,
  CobroSimulacion,
  ComprobanteRetencion,
  CuentaReceptora,
  FacturaPeticion,
  FacturaLineaPeticion,
  FianzaPeticion,
  ParametrosCobro,
  Subempresa,
  TipoImpuesto,
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
    enabled: id !== '',
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

/** Catálogos de lectura mínima (`/catalogos/*`): cada uno exige solo la acción de quien lo usa. */
export function useFormasPago(activo = true) {
  return useQuery({
    queryKey: ['finanzas', 'formas-pago'],
    enabled: activo,
    retry: false,
    queryFn: async () => (await clienteHttp.get<Pagina<FormaPago>>('/catalogos/formas-pago')).data.data,
  });
}

export function useCuentasReceptoras(activo = true) {
  return useQuery({
    queryKey: ['finanzas', 'cuentas-receptoras'],
    enabled: activo,
    retry: false,
    queryFn: async () => (await clienteHttp.get<Pagina<CuentaReceptora>>('/catalogos/cuentas-receptoras')).data.data,
  });
}

export function useSeries(activo = true) {
  return useQuery({
    queryKey: ['finanzas', 'series'],
    enabled: activo,
    retry: false,
    queryFn: async () => (await clienteHttp.get<Pagina<Serie>>('/catalogos/series')).data.data,
  });
}

export function useSubempresas(activo = true) {
  return useQuery({
    queryKey: ['finanzas', 'subempresas'],
    enabled: activo,
    retry: false,
    queryFn: async () => (await clienteHttp.get<Pagina<Subempresa>>('/catalogos/subempresas')).data.data,
  });
}

/** Tolerancia de diferencia de cambio que aplicará la API al cerrar una factura. */
export function useParametrosCobro(activo = true) {
  return useQuery({
    queryKey: ['finanzas', 'parametros-cobro'],
    enabled: activo,
    retry: false,
    queryFn: async () => (await clienteHttp.get<ParametrosCobro>('/catalogos/parametros-cobro')).data,
  });
}

/** Tipos de ITBIS y de retención activos para elegir en un borrador (`GET /catalogos/tipos-*`, `factura.borrador`). */
export function useTiposImpuesto(tipo: 'itbis' | 'retencion') {
  return useQuery({
    queryKey: ['finanzas', 'tipos', tipo],
    retry: false,
    queryFn: async () => (await clienteHttp.get<Pagina<TipoImpuesto>>(`/catalogos/tipos-${tipo}`, { params: { pageSize: 200 } })).data.data,
  });
}

export function useComprobanteRetencion(facturaId: string, activo: boolean) {
  return useQuery({
    queryKey: ['finanzas', 'comprobante-retencion', facturaId],
    enabled: activo,
    queryFn: async () =>
      (await clienteHttp.get<Pagina<ComprobanteRetencion>>('/comprobantes-retencion', { params: { facturaId, pageSize: 1 } })).data.data[0] ?? null,
  });
}

export interface OpcionRef {
  value: string;
  label: string;
}

interface ClienteRef {
  id: string;
  numero: number;
  nombre: string;
}

const aOpcionCliente = (c: ClienteRef) => ({ value: c.id, label: c.nombre, meta: `CLI-${String(c.numero).padStart(5, '0')}` });

/** Búsqueda remota de clientes por nombre o número (la consulta se debouncea en quien llama). */
export function useBuscarClientes(q: string) {
  return useQuery({
    queryKey: ['finanzas', 'clientes', q],
    placeholderData: (anterior) => anterior,
    queryFn: async () => {
      const { data } = await clienteHttp.get<Pagina<ClienteRef>>('/clientes', { params: limpia({ q: q.trim(), pageSize: 20 }) });
      return data.data.map(aOpcionCliente);
    },
  });
}

/** El cliente ya elegido (p. ej. desde la URL), para poder mostrar su nombre aunque no esté en la búsqueda. */
export function useClienteElegido(id: string) {
  return useQuery({
    queryKey: ['finanzas', 'cliente', id],
    enabled: id !== '',
    queryFn: async () => aOpcionCliente((await clienteHttp.get<ClienteRef>(`/clientes/${id}`)).data),
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

export interface ContratoRef {
  id: string;
  cliente: string | null;
  unidad: string | null;
  moneda: Moneda;
  fechaInicio: string;
}

/** Contratos para elegir al dar de alta una fianza (búsqueda remota por `q`). */
export function useContratos(q: string, activo: boolean) {
  return useQuery({
    queryKey: ['finanzas', 'contratos', q],
    enabled: activo,
    placeholderData: (anterior) => anterior,
    queryFn: async () => (await clienteHttp.get<Pagina<ContratoRef>>('/contratos', { params: limpia({ q: q.trim(), pageSize: 20 }) })).data.data,
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

// ---------------------------------------------------------------- borradores, comprobante de retención y alta de fianza

export function useCrearBorrador() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async ({ cuerpo, clave }: ConClave<FacturaPeticion>) => (await clienteHttp.post<Factura>('/facturas', cuerpo, conClave(clave))).data,
    onSuccess: invalidar,
  });
}

export function useActualizarBorrador(id: string) {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (cuerpo: FacturaPeticion) => (await clienteHttp.put<Factura>(`/facturas/${id}`, cuerpo, conClave(crypto.randomUUID()))).data,
    onSuccess: invalidar,
  });
}

/** Alta, edición (con `lineaId`) y baja de líneas; todas devuelven la factura con los totales recalculados por la API. */
export function useLineaBorrador(id: string) {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (a: { lineaId?: string; cuerpo?: FacturaLineaPeticion; borrar?: boolean }) => {
      const base = `/facturas/${id}/lineas`;
      if (a.borrar && a.lineaId) return (await clienteHttp.delete<Factura>(`${base}/${a.lineaId}`, conClave(crypto.randomUUID()))).data;
      if (a.lineaId) return (await clienteHttp.put<Factura>(`${base}/${a.lineaId}`, a.cuerpo, conClave(crypto.randomUUID()))).data;
      return (await clienteHttp.post<Factura>(base, a.cuerpo, conClave(crypto.randomUUID()))).data;
    },
    onSuccess: invalidar,
  });
}

export function useRecibirComprobanteRetencion(id: string) {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (cuerpo: { numero: string; fechaRecepcion: string }) =>
      (await clienteHttp.put<ComprobanteRetencion>(`/comprobantes-retencion/${id}/recibir`, cuerpo, conClave(crypto.randomUUID()))).data,
    onSuccess: invalidar,
  });
}

export function useCrearFianza() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async ({ cuerpo, clave }: ConClave<FianzaPeticion>) => (await clienteHttp.post<Fianza>('/fianzas', cuerpo, conClave(clave))).data,
    onSuccess: invalidar,
  });
}
