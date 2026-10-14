window.AR_DATA={
user:{name:'Carmen Reyes',role:'Gerente',initials:'CR'},
units:[
{id:'U-001',name:'Apto 4B',prop:'Torre Coral',type:'Apartamento',status:'ocupado',client:'Inversiones Marlin SRL',rent:1250,cur:'USD',modes:'Mensual'},
{id:'U-002',name:'Apto 5A',prop:'Torre Coral',type:'Apartamento',status:'disponible',client:'—',rent:1100,cur:'USD',modes:'Mensual'},
{id:'U-003',name:'Apto 2C',prop:'Torre Coral',type:'Apartamento',status:'reservado',client:'Ana Lucía Méndez',rent:980,cur:'USD',modes:'Mensual',alert:'Reserva desde hace 9 días'},
{id:'U-010',name:'Villa Palmeras',prop:'—',type:'Villa',status:'ocupado',client:'María José Peña',rent:2800,cur:'USD',modes:'Mensual'},
{id:'U-021',name:'Oficina 2',prop:'Plaza Este',type:'Oficina',status:'ocupado',client:'Luis Ortega',rent:27900,cur:'DOP',modes:'Mensual · Diario'},
{id:'U-022',name:'Sala de reuniones A',prop:'Plaza Este',type:'Sala',status:'disponible',client:'—',rent:35,cur:'USD',modes:'Por horas'},
{id:'U-030',name:'Nave 3',prop:'Parque Industrial Norte',type:'Nave',status:'mantenimiento',client:'—',rent:3400,cur:'USD',modes:'Mensual'},
{id:'U-031',name:'Local 1',prop:'Plaza Este',type:'Local',status:'fuera',client:'—',rent:1500,cur:'USD',modes:'Mensual'}
],
invoices:[
{id:'FAC-A-2026-00412',client:'Inversiones Marlin SRL',unit:'Torre Coral · 4B',concept:'Renta octubre',due:'05/10/2026',total:1250,paid:1250,cur:'USD',rate:62,status:'pagado'},
{id:'FAC-A-2026-00413',client:'María José Peña',unit:'Villa Palmeras',concept:'Renta octubre',due:'01/10/2026',total:2800,paid:1400,cur:'USD',rate:62,status:'parcial',overdue:true},
{id:'FAC-B-2026-00088',client:'Luis Ortega',unit:'Plaza Este · Oficina 2',concept:'Renta octubre',due:'10/10/2026',total:27900,paid:0,cur:'DOP',status:'pendiente'},
{id:'FAC-C-2026-00051',client:'Inversiones Marlin SRL',unit:'Torre Coral · 4B',concept:'Electricidad septiembre',due:'05/10/2026',total:86.4,paid:0,cur:'USD',rate:62,status:'pendiente',overdue:true},
{id:'FAC-A-2026-00398',client:'Grupo Solimar SAS',unit:'Torre Coral · 6A',concept:'Renta septiembre',due:'05/09/2026',total:1450,paid:0,cur:'USD',rate:61.8,status:'pendiente',overdue:true},
{id:'FAC-A-2026-00377',client:'Pedro Almonte',unit:'Torre Coral · 1D',concept:'Renta septiembre',due:'01/09/2026',total:900,paid:0,cur:'USD',status:'anulada'}
],
alerts:[
{tone:'danger',title:'4 facturas vencidas',body:'5.736,40 USD pendiente · más antigua 05/09'},
{tone:'warning',title:'Reserva con más de 7 días',body:'Torre Coral · 2C · Ana Lucía Méndez'},
{tone:'warning',title:'Fianza pendiente de devolución',body:'CT-0192 · cerrado el 30/09 · 2.200,00 USD'},
{tone:'info',title:'2 unidades quedan libres en 30 días',body:'Torre Coral · 3A, Plaza Este · Local 4'},
{tone:'ai',title:'3 autorizaciones IA pendientes',body:'Agente de cobros (2), Agente de contratos (1)'}
],
approvals:[
{id:1,agent:'Agente de cobros',action:'Registrar cobro',requestedAt:'hace 12 min',record:'FAC-A-2026-00413',origin:'Revisión de extracto',current:'Parcial · pendiente 1.400,00 USD',proposed:'Pagado · cobro 1.400,00 USD',reason:'Transferencia Banco Popular ref. 88231 del 07/10 coincide con cliente e importe pendiente.',impact:'Saldo del contrato CT-0217 pasa a 0,00 USD',risk:'medio'},
{id:2,agent:'Agente de contratos',action:'Activar contrato',requestedAt:'hace 1 h',record:'CT-0231',origin:'Borrador de Lucía Pérez',current:'Borrador',proposed:'Activo desde 15/10/2026',reason:'Revisión completa: sin solapes con reserva RS-0088, fianza cobrada, plantilla Apartamento.',impact:'Torre Coral · 2C pasa a Ocupado el 15/10',risk:'bajo'},
{id:3,agent:'Agente de cobros',action:'Ejecutar devolución de fianza',requestedAt:'ayer',record:'FZ-0192',origin:'Cierre de contrato CT-0192',current:'Retenida 2.200,00 USD',proposed:'Devolver 1.950,00 USD',reason:'Inventario de salida: falta 1 silla de comedor y daño en persiana (250,00 USD).',impact:'Descuento 250,00 USD · transferencia',risk:'alto'}
]};