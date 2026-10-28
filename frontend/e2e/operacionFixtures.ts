import { ACCIONES } from '../src/permisos';
import type { ApiFalsa } from './apiFalsa';
import { identidad } from './fixtures';

/** Fixtures con la forma EXACTA de los DTO del backend (CalendarioDtos, CotizacionDtos, ReservaDtos, ContratoDtos, InventarioDtos, PlantillaDtos). Datos inventados. */

export const INMUEBLE_ID = 'aaaaaaaa-0000-0000-0000-000000000001';
export const UNIDAD_ID = 'bbbbbbbb-0000-0000-0000-000000000001';
export const CLIENTE_ID = 'cccccccc-0000-0000-0000-000000000001';
export const FACTURADORA_ID = 'dddddddd-0000-0000-0000-000000000001';

/** Empleado real de la matriz: cliente.ver/editar, contrato.borrador, factura.ver/borrador, cotizacion.enviar; sin contrato.estado ni factura.emitir. */
export const EMPLEADO_OPERACION = identidad({
  nombreAcceso: 'empleado',
  nombre: 'Luis Rosario',
  rol: 'empleado',
  permisosEfectivos: [
    ACCIONES.clienteVer,
    ACCIONES.clienteEditar,
    ACCIONES.contratoBorrador,
    ACCIONES.facturaVer,
    ACCIONES.facturaBorrador,
    ACCIONES.cotizacionEnviar,
    ACCIONES.sesionVer,
    ACCIONES.sesionCerrar,
    ACCIONES.sesionPassword,
  ],
});

export function pagina<T>(data: T[]) {
  return { data, total: data.length, page: 1, pageSize: 25, totalPages: 1 };
}

export const inmueble = {
  id: INMUEBLE_ID,
  nombre: 'Torre Marina',
  direccion: 'Av. del Puerto 12',
  tipo: 'Edificio',
  propietariaId: null,
  plantillaContratoPorDefecto: 'ALQ-MENSUAL',
  renovacionDefectoMeses: 12,
  activo: true,
  createdAt: '2026-01-10T15:00:00Z',
};

export const unidad = {
  id: UNIDAD_ID,
  inmuebleId: INMUEBLE_ID,
  nombre: 'A-101',
  tipo: 'Apartamento',
  propietariaId: null,
  facturadoraPorDefectoId: FACTURADORA_ID,
  estadoManual: 'Disponible',
  modalidades: ['Mensual', 'MedioDia', 'DiaCompleto'],
  tarifas: [
    { modalidad: 'Mensual', moneda: 'USD', importe: '1250.50' },
    { modalidad: 'DiaCompleto', moneda: 'USD', importe: '120.00' },
  ],
  metrosCuadrados: '62.00',
  capacidad: 4,
  habitaciones: 2,
  banos: '1.00',
  medidor: null,
  notas: null,
  activo: true,
  createdAt: '2026-01-10T15:00:00Z',
};

export const cliente = {
  id: CLIENTE_ID,
  numero: 1873,
  nombre: 'Ana Martínez',
  tipo: 'Persona',
  email: 'ana@example.com',
  telefono: null,
  agenteRetencion: false,
  enGestionCobro: false,
  recordatoriosPausados: false,
  activo: true,
  createdAt: '2026-01-10T15:00:00Z',
};

/** Respuestas de apoyo que piden los selectores de cliente, prospecto, inmueble y unidad. */
export function responderSelectores(api: ApiFalsa): void {
  api.responder('GET', '/clientes', { body: pagina([cliente]) });
  api.responder('GET', '/prospectos', { body: pagina([]) });
  api.responder('GET', '/inmuebles', { body: pagina([inmueble]) });
  api.responder('GET', `/inmuebles/${INMUEBLE_ID}/unidades`, { body: pagina([unidad]) });
}

// ---------------------------------------------------------------- calendario

const TIPOS = ['Libre', 'Contrato', 'ReservaMensual', 'ReservaCorta', 'Mantenimiento'] as const;

/** Mes completo de `unidades` unidades con un patrón de ocupación variado. `mes` es "2026-10". */
export function calendarioDe(mes: string, unidades: number) {
  const [anio, m] = mes.split('-').map(Number) as [number, number];
  const dias = new Date(Date.UTC(anio, m, 0)).getUTCDate();
  return {
    unidades: Array.from({ length: unidades }, (_, u) => ({
      unidadId: `bbbbbbbb-0000-0000-0000-${String(u + 1).padStart(12, '0')}`,
      unidad: `U-${String(u + 1).padStart(3, '0')}`,
      inmuebleId: INMUEBLE_ID,
      inmueble: 'Torre Marina',
      estadoManual: 'Disponible',
      desde: `${mes}-01`,
      hasta: `${mes}-${String(dias).padStart(2, '0')}`,
      dias: Array.from({ length: dias }, (_, d) => {
        const tipo = TIPOS[(u + Math.floor(d / 6)) % 5]!;
        const medio = tipo === 'ReservaCorta' && d % 2 === 0;
        return {
          fecha: `${mes}-${String(d + 1).padStart(2, '0')}`,
          tipo,
          disponible: tipo === 'Libre',
          motivo: tipo === 'Libre' ? 'Libre' : `Ocupada por ${tipo}`,
          ocupaciones:
            tipo === 'Libre'
              ? []
              : [
                  {
                    origen: tipo,
                    origenId: `eeeeeeee-0000-0000-0000-${String(u + 1).padStart(12, '0')}`,
                    tipoDia: tipo === 'ReservaCorta' ? (medio ? 'MedioDia' : 'DiaCompleto') : null,
                    finExclusivo: tipo === 'Contrato' ? `${mes}-28` : null,
                  },
                ],
        };
      }),
    })),
  };
}

// ---------------------------------------------------------------- cotizaciones

export const COTIZACION_ID = '11111111-aaaa-0000-0000-000000000001';

export function cotizacionResumen(sobre: Record<string, unknown> = {}) {
  return {
    id: COTIZACION_ID,
    clienteId: CLIENTE_ID,
    clienteNombre: 'Ana Martínez',
    prospectoId: null,
    prospectoNombre: null,
    unidadId: UNIDAD_ID,
    unidadNombre: 'A-101',
    estado: 'Borrador',
    moneda: 'USD',
    subtotal: '1500.01',
    total: '1500.01',
    lineas: 2,
    activo: true,
    createdAt: '2026-10-20T15:30:00Z',
    ...sobre,
  };
}

export function cotizacionDetalle(sobre: Record<string, unknown> = {}) {
  const resumen: Record<string, unknown> = cotizacionResumen(sobre);
  delete resumen.lineas;
  return {
    ...resumen,
    lineas: [
      { posicion: 1, descripcion: 'Renta mensual', cantidad: '1.00', precioUnitario: '1250.50', importe: '1250.50' },
      { posicion: 2, descripcion: 'Limpieza inicial', cantidad: '2.50', precioUnitario: '99.80', importe: '249.51' },
    ],
  };
}

// ---------------------------------------------------------------- reservas

export const RESERVA_ID = '22222222-aaaa-0000-0000-000000000001';

export function reservaResumen(sobre: Record<string, unknown> = {}) {
  return {
    id: RESERVA_ID,
    unidadId: UNIDAD_ID,
    unidad: 'A-101',
    clienteId: CLIENTE_ID,
    prospectoId: null,
    cliente: 'Ana Martínez',
    tipo: 'Corta',
    estado: 'Activa',
    modalidad: 'MedioDia',
    moneda: 'USD',
    importe: '120.00',
    anticipoImporte: '30.00',
    fechaReserva: '2026-10-20',
    entradaPrevista: '2026-11-03',
    salidaPrevista: '2026-11-03',
    entradaReal: null,
    salidaReal: null,
    activo: true,
    dias: 2,
    tieneAnticipo: true,
    createdAt: '2026-10-20T15:30:00Z',
    ...sobre,
  };
}

export function reservaDetalle(sobre: Record<string, unknown> = {}) {
  const resumen: Record<string, unknown> = reservaResumen(sobre);
  delete resumen.dias;
  delete resumen.tieneAnticipo;
  return {
    ...resumen,
    anticipoNota: 'Se devuelve si cancela con 48 h',
    observaciones: null,
    dias: [
      { id: 'f1', fecha: '2026-11-03', horaOrientativa: 'de 9 a 14 aprox.', tipo: 'MedioDia' },
      { id: 'f2', fecha: '2026-11-04', horaOrientativa: null, tipo: 'MedioDia' },
    ],
    ...sobre,
  };
}

// ---------------------------------------------------------------- contratos

export const CONTRATO_ID = '33333333-aaaa-0000-0000-000000000001';

export function contratoResumen(sobre: Record<string, unknown> = {}) {
  return {
    id: CONTRATO_ID,
    clienteId: CLIENTE_ID,
    cliente: 'Ana Martínez',
    unidadId: UNIDAD_ID,
    unidad: 'A-101',
    facturadoraId: FACTURADORA_ID,
    fechaInicio: '2026-01-01',
    fechaFinVigente: '2026-12-31',
    renta: '1250.50',
    moneda: 'USD',
    // El listado manda el enum como número (sin conversor): 4 = Activo.
    estado: 'Activo',
    estadoActualDesde: '2026-01-01',
    fechaBajaProgramada: null,
    createdAt: '2025-12-20T15:30:00Z',
    ...sobre,
  };
}

export function contratoDetalle(estado: string, sobre: Record<string, unknown> = {}) {
  return {
    id: CONTRATO_ID,
    clienteId: CLIENTE_ID,
    cliente: 'Ana Martínez',
    unidadId: UNIDAD_ID,
    unidad: 'A-101',
    facturadoraId: FACTURADORA_ID,
    facturadora: 'Marina Servicios SRL',
    fechaInicio: '2026-01-01',
    duracionInicialMeses: 12,
    fechaFinInicial: '2026-12-31',
    fechaFinVigente: '2026-12-31',
    renta: '1250.50',
    moneda: 'USD',
    diaPago: 5,
    fianza: '2501.00',
    reglaRenovacion: 'Anual',
    renovacionMeses: 12,
    plantillaContratoId: 'ALQ-MENSUAL',
    plantillaContratoCongeladaId: estado === 'Borrador' || estado === 'PendienteAprobacion' ? null : 'ALQ-MENSUAL',
    plantillaCongeladaAt: estado === 'Borrador' || estado === 'PendienteAprobacion' ? null : '2025-12-28T15:00:00Z',
    estado,
    estadoActualDesde: '2026-01-01',
    estadoActualComentario: null,
    fechaBajaProgramada: null,
    fechaFinReal: null,
    observaciones: null,
    versiones: [{ numero: 2, cambio: 'renta 1200.00 -> 1250.50', rentaAnterior: '1200.00', rentaNueva: '1250.50', moneda: 'USD', alcance: 'Facturas futuras', createdAt: '2026-06-01T14:00:00Z' }],
    // Resultado sin conversor: número (2 = Aprobada).
    solicitudes: [{ id: 's1', resultado: 'Aprobada', huellaContenido: 'abc', solicitadoAt: '2025-12-27T15:00:00Z', resueltoPorId: null, resueltoAt: '2025-12-28T15:00:00Z', comentario: 'Conforme', motivoCaducidad: null }],
    createdAt: '2025-12-20T15:30:00Z',
    ...sobre,
  };
}

export const incrementos = [
  { id: 'i1', tipo: 'Porcentaje', porcentaje: '5.00', importe: null, fechaVigencia: '2027-01-01', aplicado: false, aplicadoAt: null, cancelado: false, canceladoAt: null, rentaAntes: null, rentaDespues: null },
  { id: 'i2', tipo: 'ImporteFijo', porcentaje: null, importe: '50.00', fechaVigencia: '2026-06-01', aplicado: true, aplicadoAt: '2026-06-01T04:00:00Z', cancelado: false, canceladoAt: null, rentaAntes: '1200.00', rentaDespues: '1250.50' },
];

export const inventario = [
  { id: 'it1', inventarioBaseId: null, nombre: 'Sofá', cantidad: '1.00', notas: null, orden: 1 },
  { id: 'it2', inventarioBaseId: null, nombre: 'Lámpara de pie', cantidad: '2.00', notas: 'Una con pantalla beige', orden: 2 },
];

export const plantilla = {
  id: 'p1',
  codigo: 'ALQ-MENSUAL',
  nombre: 'Alquiler mensual estándar',
  secciones: [{ titulo: 'Objeto', texto: 'El arrendador cede el uso de la unidad.' }],
  anexosTipos: ['Normas', 'Inventario'],
  activa: true,
  createdAt: '2026-01-10T15:00:00Z',
  updatedAt: null,
};

export const anexo = {
  id: 'x1',
  tipo: 'Normas',
  nombre: 'Normas de convivencia',
  secciones: [{ titulo: '', texto: 'No se permiten fiestas.' }],
  activa: true,
  createdAt: '2026-01-10T15:00:00Z',
  updatedAt: null,
};
