/**
 * Identificadores de acción del backend (AtlasRent.Api/Authorization/Acciones.cs). Se copian tal
 * cual para que el menú y la autorización no se desincronicen: si el backend renombra uno, el menú
 * deja de mostrar el módulo en vez de dejar de protegerlo (la protección real es el backend).
 */
export const ACCIONES = {
  configuracionEditar: 'configuracion.editar',
  clienteVer: 'cliente.ver',
  clienteEditar: 'cliente.editar',
  contratoBorrador: 'contrato.borrador',
  contratoEstado: 'contrato.estado',
  facturaVer: 'factura.ver',
  facturaBorrador: 'factura.borrador',
  facturaEmitir: 'factura.emitir',
  facturaAnular: 'factura.anular',
  cobroRegistrar: 'cobro.registrar',
  fianzaMover: 'fianza.mover',
  documentoEnviar: 'documento.enviar',
  cotizacionEnviar: 'cotizacion.enviar',
  mensajeriaMasivo: 'mensajeria.masivo',
  plantillaEditar: 'plantilla.editar',
  informeVer: 'informe.ver',
  // Herramienta de consulta (F6-03). No está en la matriz de la especificación §2 como el resto, pero
  // pasa por `[Accion]` igual que todas: sin ella el endpoint queda denegado.
  iaConsultar: 'ia.consultar',
  // Propuestas del asistente (F6-03): ver la lista. Decidirlas es `mensajeria.masivo`.
  iaProponer: 'ia.proponer',
  auditoriaVer: 'auditoria.ver',
  ipDesbloquear: 'ip.desbloquear',
  cuentaDesbloquear: 'cuenta.desbloquear',
  sesionVer: 'sesion.ver',
  sesionCerrar: 'sesion.cerrar',
  sesionPassword: 'sesion.password',
} as const;

export type Accion = (typeof ACCIONES)[keyof typeof ACCIONES];