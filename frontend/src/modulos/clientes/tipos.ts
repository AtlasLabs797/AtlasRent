/** Contratos de `ClienteDtos.cs`. Los enums viajan como string; las marcas de tiempo, como ISO 8601. */
export type TipoCliente = 'PersonaFisica' | 'Empresa';

export interface Pagina<T> {
  data: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

/** `ClienteResumen`: lista y respuestas de alta y cambio. Sin documento a propósito. */
export interface ClienteResumen {
  id: string;
  numero: number;
  nombre: string;
  tipo: TipoCliente;
  email: string | null;
  telefono: string | null;
  agenteRetencion: boolean;
  enGestionCobro: boolean;
  recordatoriosPausados: boolean;
  activo: boolean;
  createdAt: string;
}

/** `ClienteRespuesta`: la ficha, con el documento descifrado (decisión D16). */
export interface ClienteFicha extends ClienteResumen {
  documento: string | null;
  direccion: string | null;
  notas: string | null;
}

export interface Contacto {
  id: string;
  clienteId: string;
  nombre: string;
  relacion: string | null;
  email: string | null;
  telefono: string | null;
  esPrincipal: boolean;
}

export interface ProspectoResumen {
  id: string;
  nombre: string;
  tipo: TipoCliente;
  email: string | null;
  telefono: string | null;
  activo: boolean;
  createdAt: string;
}

export interface ProspectoFicha extends ProspectoResumen {
  documento: string | null;
  notas: string | null;
}

/** `ClientePeticion`. `numero` solo lo acepta la API de quien tiene `configuracion.editar`. */
export interface CuerpoCliente {
  nombre: string;
  tipo: TipoCliente;
  documento: string | null;
  email: string | null;
  telefono: string | null;
  direccion: string | null;
  notas: string | null;
  agenteRetencion: boolean;
  enGestionCobro: boolean;
  recordatoriosPausados: boolean;
  numero?: number;
}

export interface CuerpoContacto {
  nombre: string;
  relacion: string | null;
  email: string | null;
  telefono: string | null;
  esPrincipal: boolean;
}

export interface CuerpoProspecto {
  nombre: string;
  tipo: TipoCliente;
  documento: string | null;
  email: string | null;
  telefono: string | null;
  notas: string | null;
}

/** `BusquedaClientePeticion`: el documento va en el cuerpo, nunca en la URL (SEC-030). */
export interface CuerpoBusquedaDocumento {
  documento: string;
  tipo?: TipoCliente;
  incluirInactivos: boolean;
  page: number;
  pageSize: number;
}
