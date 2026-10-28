// Textos del módulo Asistente. Los del menú (etiqueta e icono) viven en `i18n/textos.ts`.
/**
 * Asistente de IA (F6-03). El backend es sin estado: cada pregunta va sola y la respuesta no se
 * guarda en el navegador, así que aquí no hay historial ni conversación que restaurar.
 */
export const textosAsistente = {
  titulo: 'Asistente',
  descripcion: 'Pregunta sobre los datos de Atlas Rent. Elige sobre qué quieres la respuesta.',
  formulario: {
    titulo: 'Pregunta',
    descripcion:
      'La pregunta se manda junto con el alcance elegido. Cada pregunta es independiente: no se guarda la conversación en este navegador.',
    pregunta: 'Pregunta',
    pista: 'Por ejemplo: cuántos contratos vencen este mes',
    alcance: 'Alcance',
    alcances: {
      clientes: 'Clientes',
      cuentas: 'Cuentas',
      facturas: 'Facturas',
      contratos: 'Contratos',
      informes: 'Informes',
    },
    enviar: 'Consultar',
    enviando: 'Consultando…',
    preguntaVacia: 'Escribe una pregunta antes de consultar.',
    alcanceInvalido: 'Elige un alcance de los de la lista.',
  },
  respuesta: {
    titulo: 'Respuesta',
    vacioTitulo: 'Todavía no hay respuesta',
    vacioCuerpo: 'Escribe una pregunta y elige el alcance para ver aquí lo que responde la IA.',
    fuente: 'Respuesta de {proveedor} · {modelo}',
  },
  /**
   * Aviso de privacidad de F6-04. Es permanente y visible antes de escribir nada, no un modal: lo
   * que protege no es una acción puntual sino un envío que va a pasar cada vez que se consulte.
   * Y termina en la instrucción que de verdad sirve, que es la que el sistema no puede imponer:
   * la seudonimización cubre los datos que carga el servicio, no lo que escribe la persona.
   */
  privacidad: {
    titulo: 'Tu pregunta sale de Atlas Rent',
    cuerpo:
      'La pregunta y los datos del alcance elegido se envían al proveedor de IA configurado en esta instalación, fuera de Atlas Rent. Los nombres, teléfonos y documentos se sustituyen por seudónimos antes de salir, pero lo que escribas tú va tal cual: no pongas datos personales en la pregunta.',
  },
  /** Fallbacks: si la API manda su `detail` de `ProblemDetails`, ese texto es el que se enseña. */
  errores: {
    sesionCaducada: {
      titulo: 'La sesión ha caducado',
      cuerpo: 'Vuelve a entrar en Atlas Rent para poder seguir preguntando.',
    },
    sinPermiso: {
      titulo: 'Tu usuario no puede consultar la IA',
      cuerpo: 'Pídeselo a quien administre Atlas Rent.',
    },
    limite: {
      titulo: 'Se ha alcanzado el límite de consultas',
      cuerpo: 'Espera unos minutos antes de volver a intentarlo.',
    },
    noDisponible: {
      titulo: 'La IA no está disponible',
      cuerpo: 'No está habilitada en esta instalación o no tiene clave configurada.',
    },
    generico: {
      titulo: 'No se ha podido consultar la IA',
      cuerpo: 'Vuelve a intentarlo en un momento.',
    },
    reintentar: 'Reintentar',
  },
};
