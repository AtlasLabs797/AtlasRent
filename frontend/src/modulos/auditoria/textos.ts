// Textos del módulo Auditoría. La etiqueta y el icono del menú viven en `i18n/textos.ts`.
export const textosAuditoria = {
  titulo: 'Auditoría',
  descripcion: 'Registro de lo que se ha hecho en la aplicación. Solo lectura.',
  cargando: 'Cargando…',
  errorCarga: 'No se ha podido cargar la auditoría',
  filtros: {
    desde: 'Desde',
    hasta: 'Hasta',
    usuario: 'Usuario (identificador)',
    usuarioHint: 'Identificador del usuario; se ignora si no es válido.',
    entidad: 'Entidad',
    accion: 'Acción',
    aplicar: 'Filtrar',
    limpiar: 'Limpiar',
  },
  tabla: {
    titulo: 'Eventos',
    vacio: 'No hay eventos con estos filtros',
    columnas: { fecha: 'Fecha', usuario: 'Usuario', accion: 'Acción', entidad: 'Entidad', resultado: 'Resultado', ip: 'IP', detalle: 'Detalle' },
    sistema: 'Sistema',
    ver: 'Ver',
    verEtiqueta: 'Ver los datos del evento {accion}',
  },
  detalle: {
    titulo: 'Datos del evento',
    cerrar: 'Cerrar',
    sinDatos: 'Este evento no guarda datos.',
    origen: 'Origen',
    entidadId: 'Identificador de la entidad',
    nota: 'Las claves sensibles aparecen enmascaradas con ***.',
  },
};
