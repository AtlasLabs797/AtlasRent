/**
 * Textos de interfaz centralizados (AGENTS.md: español; inglés activable después).
 * El inglés se añade más adelante como otro idioma, no se mezcla en los componentes.
 */
export const textos = {
  app: {
    nombre: 'Atlas Rent',
    cargando: 'Cargando…',
    noEncontradaTitulo: 'Página no encontrada',
    noEncontradaCuerpo: 'La dirección no corresponde a ninguna pantalla de Atlas Rent.',
    irAlInicio: 'Volver al inicio',
  },
  navegacion: {
    grupos: [
      {
        etiqueta: 'Operación',
        items: [
          { clave: 'panel', icono: 'layout-dashboard', etiqueta: 'Panel' },
          { clave: 'componentes', icono: 'swatch-book', etiqueta: 'Design system' },
        ],
      },
    ],
    usuario: { name: 'Andi Batista', role: 'Administrador' },
  },
  barraSuperior: {
    buscar: 'Buscar cliente, unidad, factura o contrato',
    cerrar: 'Cerrar sesión',
  },
  tema: {
    etiqueta: 'Tema',
    opciones: [
      { valor: 'claro', etiqueta: 'Claro' },
      { valor: 'oscuro', etiqueta: 'Oscuro' },
      { valor: 'sistema', etiqueta: 'Sistema' },
    ],
  },
  panel: {
    sobrelinea: 'Atlas Rent',
    titulo: 'Facturación y alquileres',
    descripcion: 'Unidades, contratos, facturas y cobros en un solo lugar. Base de la interfaz en construcción.',
    accionesPrimaria: 'Registrar cobro',
  },
  designSystem: {
    sobrelinea: 'Design system',
    titulo: 'Componentes',
    descripcion: 'Muestra de los componentes del design system y de los estados de unidad, en tema claro y oscuro.',
    temaClaro: 'Tema claro',
    temaOscuro: 'Tema oscuro',
    temaActivo: 'Tema activo',
    botones: {
      titulo: 'Botones',
      descripcion: 'Un solo primario por vista. Secundario con borde de acento, terciario ghost.',
      primaria: 'Registrar cobro',
      primariaIcono: 'plus',
      primariaAccion: 'Registrar cobro',
      secundaria: 'Emitir factura',
      terciaria: 'Ver detalle',
      peligro: 'Anular factura',
      ia: 'Proponer con IA',
      pequeno: 'Pequeño',
      mediano: 'Mediano',
      grande: 'Grande',
    },
    badges: {
      titulo: 'Badges de estado',
      descripcion: 'Punto de estado de unidad y de pago; nunca una fila coloreada.',
      etiqueta: 'Etiqueta personalizada',
    },
    formularios: {
      titulo: 'Campos',
      descripcion: 'Etiqueta, pista y error con los tokens del design system.',
      etiqueta: 'Cliente',
      pista: 'Nombre o número de cliente',
      conError: 'Cédula',
      error: 'Documento de identidad no válido',
      monto: 'Monto',
      montoSufijo: 'USD',
    },
    alertas: {
      titulo: 'Avisos',
      descripcion: 'Avisos que cambian el estado de una operación.',
      infoTitulo: 'Tipo de cambio congelado',
      infoCuerpo: 'La factura FAC-A-2026-00412 usa la tasa del día de emisión.',
      avisoTitulo: 'Fianza pendiente de devolver',
      avisoCuerpo: 'El contrato CT-0217 tiene fianza retenida sin devolver.',
      peligroTitulo: 'Falta actualizar el tipo de cambio',
      peligroCuerpo: 'La tasa de hoy aún no está disponible.',
    },
    kpi: {
      titulo: 'Indicadores',
      descripcion: 'Máximo cuatro por fila y uno destacado. Toda métrica lleva comparación.',
      destacado: 'Cobrado este mes',
      destacadoMeta: 'vs 980,00 USD del mes anterior',
      pendiente: 'Por cobrar',
      vencido: 'Vencido',
      unidades: 'Unidades ocupadas',
      unidadesMeta: 'vs 104 el mes anterior',
    },
    tabla: {
      titulo: 'Tabla',
      descripcion: 'Sin banda gris en la cabecera; números a la derecha en mono.',
      columnas: { unidad: 'Unidad', cliente: 'Cliente', contrato: 'Contrato', saldo: 'Saldo' },
      vacia: 'Sin facturas para este cliente',
    },
    unidades: {
      titulo: 'Unidades',
      descripcion: 'Estados de unidad con sus tokens: disponible, reservado, ocupado, mantenimiento y fuera de servicio.',
      alquiler: 'Mensual',
      porHoras: 'Por horas',
      diario: 'Diario',
      mantenimiento: 'Revisión de aire acondicionado',
    },
    vacio: {
      titulo: 'Estados vacíos y atajos',
      descripcion: 'Un estado vacío dice para qué sirve la superficie y cuál es la siguiente acción útil.',
      facturaTitulo: 'Todavía no hay facturas de esta serie',
      facturaCuerpo: 'Cuando se emita la primera factura aparecerá aquí con su saldo y su estado de pago.',
      atajo: '⌘ K abre la búsqueda global desde cualquier pantalla.',
    },
  },
};