/**
 * Textos de interfaz centralizados (AGENTS.md: español; inglés activable después).
 * El inglés se añade más adelante como otro idioma, no se mezcla en los componentes.
 */
export const textos = {
  app: {
    nombre: 'Atlas Rent',
    cargando: 'Cargando…',
    cargandoSesion: 'Comprobando la sesión…',
    noEncontradaTitulo: 'Página no encontrada',
    noEncontradaCuerpo: 'La dirección no corresponde a ninguna pantalla de Atlas Rent.',
    irAlInicio: 'Volver al inicio',
  },
  acceso: {
    titulo: 'Entrar',
    descripcion: 'Atlas Rent, facturación y alquileres.',
    nombreAcceso: 'Usuario',
    contrasena: 'Contraseña',
    entrar: 'Entrar',
    cambiando: 'Entrando…',
    credencialesInvalidas: 'Usuario o contraseña incorrectos.',
    segundoFactor: 'Esta cuenta tiene un segundo factor pendiente y todavía no hay pantalla para introducirlo.',
    error: 'No se ha podido entrar. Inténtalo de nuevo.',
  },
  sesion: {
    cerrar: 'Cerrar sesión',
    usuario: 'Usuario',
  },
  sinPermiso: {
    titulo: 'Sin permiso',
    descripcion: 'Tu usuario no tiene permiso para esta pantalla. Si debería tenerlo, pídeselo a quien administre Atlas Rent.',
    volver: 'Volver al panel',
  },
  navegacion: {
    grupos: {
      operacion: 'Operación',
      finanzas: 'Finanzas',
      control: 'Control',
      herramientas: 'Herramientas',
    },
    /**
     * Módulos del menú. La etiqueta y el icono viven aquí; qué acción del backend habilita cada uno
     * y si tiene pantalla, en `src/navegacion/menu.ts`.
     */
    modulos: {
      panel: { etiqueta: 'Panel', icono: 'layout-dashboard' },
      clientes: { etiqueta: 'Clientes', icono: 'users' },
      unidades: { etiqueta: 'Unidades e inmuebles', icono: 'building-2' },
      contratos: { etiqueta: 'Contratos', icono: 'file-signature' },
      facturas: { etiqueta: 'Facturas', icono: 'receipt' },
      cobros: { etiqueta: 'Cobros', icono: 'banknote' },
      fianzas: { etiqueta: 'Fianzas', icono: 'landmark' },
      informes: { etiqueta: 'Informes', icono: 'chart-column' },
      auditoria: { etiqueta: 'Auditoría', icono: 'scroll-text' },
      bloqueos: { etiqueta: 'Bloqueos', icono: 'lock' },
      usuarios: { etiqueta: 'Usuarios', icono: 'user-cog' },
      configuracion: { etiqueta: 'Configuración', icono: 'settings' },
      componentes: { etiqueta: 'Design system', icono: 'swatch-book' },
    },
    /** Nombres de los roles de `usuario_rol` para la `SideNav`. */
    roles: {
      admin: 'Administrador',
      gerente: 'Gerente',
      empleado: 'Empleado',
    },
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
  /**
   * Informes y dashboard (F5-01, `docs/ESPECIFICACION.md` §3.15). Los nombres de bloque son los de
   * la especificación; el resto son etiquetas de la pantalla.
   */
  informes: {
    sobrelinea: 'Finanzas',
    titulo: 'Informes',
    descripcion:
      'Bloques económicos de facturación y cobros, y las unidades del día. Todo lo que se ve aquí lo consolida la API.',
    cargando: 'Cargando el informe…',
    filtros: {
      titulo: 'Filtros',
      descripcion: 'El rango de fechas acota los bloques y la serie mensual.',
      desde: 'Desde',
      hasta: 'Hasta',
      moneda: 'Moneda',
      estado: 'Estado',
      todasMonedas: 'Todas',
      todosEstados: 'Todos',
      estados: { Borrador: 'Borrador', Emitida: 'Emitida', Anulada: 'Anulada', Convertida: 'Convertida' },
      sinFechas: 'Sin fechas: todo el histórico.',
    },
    entidades: {
      titulo: 'Filtros que vienen de otra pantalla',
      cuerpo:
        'Esta pantalla todavía no puede elegir cliente, unidad, contrato, inmueble, serie ni empresa: llegan por la dirección y se aplican al informe',
    },
    unidades: {
      titulo: 'Unidades del día',
      descripcion: 'Una unidad está en un solo grupo. Las cifras son del día de corte del dashboard.',
      disponibles: 'Disponibles',
      reservadas: 'Reservadas',
      ocupadas: 'Ocupadas',
      mantenimiento: 'En mantenimiento',
      contratosActivos: 'Contratos activos',
      fecha: 'Corte del {fecha}',
    },
    resumen: {
      titulo: 'Resumen económico',
      descripcion:
        'Cada bloque muestra arriba el consolidado en dólares, con el cambio congelado de cada documento, y abajo las dos monedas de los documentos.',
      proformado: 'Proformado',
      facturado: 'Facturado',
      cobrado: 'Cobrado',
      retenido: 'Retenido',
      pendiente: 'Pendiente',
      vencido: 'Vencido',
      fianzasRetenidas: 'Fianzas retenidas',
      diferenciasCambio: 'Diferencias de cambio',
      fechaCorte: 'Vencido medido contra el {fecha}.',
    },
    sinCambio: {
      titulo: 'Documentos sin tipo de cambio congelado',
      cuerpo:
        '{n} documentos en pesos quedan fuera del consolidado en dólares porque no tienen cambio congelado. El consolidado de arriba no los incluye.',
    },
    saldoFueraDePeriodo: {
      titulo: 'Saldo a favor fuera del periodo',
      cuerpo:
        'El saldo a favor es una foto de hoy, no un movimiento del periodo, así que no suma al total de cobrado cuando hay filtro de fechas. Se sigue enseñando aparte.',
    },
    grafico: {
      titulo: 'Facturado y cobrado por mes',
      descripcion: 'Importes consolidados en dólares, con el cambio congelado de cada documento.',
      facturado: 'Facturado',
      cobrado: 'Cobrado',
      alternativo: 'Gráfico de barras agrupadas: facturado y cobrado por mes, en dólares.',
      vacioTitulo: 'Todavía no hay meses con movimiento',
      vacioCuerpo: 'En cuanto se emita una factura o entre un cobro, los meses aparecerán aquí.',
    },
    exportar: {
      excel: 'Exportar Excel',
      csv: 'Exportar CSV',
      enCurso: 'Exportando…',
      error: 'No se ha podido exportar el informe',
      errorCuerpo: 'La API no ha devuelto el fichero. Vuelve a intentarlo o revisa los filtros.',
    },
    error: {
      titulo: 'No se ha podido cargar el informe',
      cuerpo:
        'Los datos no se han podido pedir a la API, así que no se muestra ninguna cifra. Vuelve a intentarlo en un momento.',
    },
  },
};