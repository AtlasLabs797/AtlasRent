const AR=window.AtlasRentDesignSystem_75bc92;
const navGroups=counts=>[{label:'Operación',items:[{key:'dashboard',icon:'layout-dashboard',label:'Inicio'},{key:'unidades',icon:'building-2',label:'Inmuebles y unidades'},{key:'clientes',icon:'users',label:'Clientes'},{key:'reservas',icon:'calendar-days',label:'Reservas'},{key:'contratos',icon:'scroll-text',label:'Contratos'}]},{label:'Finanzas',items:[{key:'facturas',icon:'file-text',label:'Facturas'},{key:'cobros',icon:'wallet',label:'Cobros'},{key:'fianzas',icon:'shield-check',label:'Fianzas'},{key:'informes',icon:'chart-column',label:'Informes'}]},{label:'Control',items:[{key:'aprobaciones',icon:'bot',label:'Aprobaciones IA',count:counts.ai,countTone:'ai'},{key:'revision',icon:'inbox',label:'Bandeja de revisión',count:2},{key:'auditoria',icon:'history',label:'Auditoría'},{key:'migracion',icon:'database',label:'Migración'}]}];
const cmdGroups=d=>[
 {label:'Unidades',items:d.units.slice(0,4).map(u=>({label:u.prop+' · '+u.name,sub:u.type+' · '+u.client,meta:u.id,icon:'building-2',value:'unidades'}))},
 {label:'Facturas',items:d.invoices.slice(0,3).map(i=>({label:i.id,sub:i.client,meta:i.due,icon:'file-text',value:'facturas'}))},
 {label:'Acciones',items:[{label:'Registrar cobro',icon:'wallet',value:'facturas'},{label:'Revisar aprobaciones IA',icon:'sparkles',tone:'ai',value:'aprobaciones'}]}];
function PageHead({eyebrow,title,sub,children}){
  const {PageHeader}=AR;
  return <PageHeader eyebrow={eyebrow} title={title} description={sub}>{children}</PageHeader>;
}
Object.assign(window,{navGroups,cmdGroups,PageHead});