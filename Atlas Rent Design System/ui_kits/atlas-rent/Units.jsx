function Units(){
  const {Card,DataTable,Badge,Money,Button,SegmentedControl,Select,Input,Tabs,AIActionButton,Alert,UnitCard,KeyValueList}=window.AtlasRentDesignSystem_75bc92;
  const d=window.AR_DATA;const [f,setF]=React.useState('todas');const [view,setView]=React.useState('Lista');const [sel,setSel]=React.useState(d.units[2]);
  const counts=d.units.reduce((a,u)=>(a[u.status]=(a[u.status]||0)+1,a),{});
  const rows=f==='todas'?d.units:d.units.filter(u=>u.status===f);
  return <div>
    <PageHead eyebrow="Operación" title="Inmuebles y unidades" sub="12 inmuebles · 150 unidades alquilables"><Button variant="secondary" icon="building">Nuevo inmueble</Button><Button icon="plus">Nueva unidad</Button></PageHead>
    <Tabs value={f} onChange={setF} style={{marginBottom:24}} tabs={[{label:'Todas',value:'todas',count:d.units.length},{label:'Disponibles',value:'disponible',count:counts.disponible},{label:'Reservadas',value:'reservado',count:counts.reservado},{label:'Ocupadas',value:'ocupado',count:counts.ocupado},{label:'Mantenimiento',value:'mantenimiento',count:counts.mantenimiento},{label:'Fuera de servicio',value:'fuera',count:counts.fuera}]}/>
    <div style={{display:'grid',gridTemplateColumns:'minmax(0,1fr) minmax(300px,360px)',gap:24,alignItems:'start'}}>
      <Card padded={false}>
        <div style={{display:'flex',flexWrap:'wrap',gap:10,padding:'17px 20px',borderBottom:'1px solid var(--border-subtle)',alignItems:'center'}}><Input icon="search" placeholder="Filtrar unidades" style={{flex:'1 1 220px'}}/><Select options={['Todos los tipos','Apartamento','Villa','Oficina','Sala','Nave','Local']}/><SegmentedControl options={['Lista','Cuadrícula']} value={view} onChange={setView}/></div>
        {view==='Lista'?<DataTable rows={rows} selectedKey={sel&&sel.id} onRowClick={setSel} columns={[{key:'name',label:'Unidad',render:r=><div><div style={{fontWeight:600,color:'var(--text-strong)'}}>{r.name}</div><div style={{font:'var(--type-caption)',color:'var(--text-muted)'}}>{r.prop}</div></div>},{key:'type',label:'Tipo'},{key:'status',label:'Estado',render:r=><Badge status={r.status}/>},{key:'client',label:'Cliente'},{key:'modes',label:'Modalidad'},{key:'rent',label:'Precio estándar',align:'right',render:r=><Money amount={r.rent} currency={r.cur}/>}]}/>
        :<div style={{display:'grid',gridTemplateColumns:'repeat(auto-fill,minmax(220px,1fr))',gap:12,padding:20}}>{rows.map(x=><UnitCard key={x.id} name={x.name} property={x.prop} type={x.type} status={x.status} rent={x.rent} currency={x.cur} modalities={[x.modes]} client={x.client!=='—'?x.client:undefined} selected={sel&&sel.id===x.id} onClick={()=>setSel(x)}/>)}</div>}
      </Card>
      {sel&&<Card title={sel.name} actions={<AIActionButton actions={['Comprobar disponibilidad','Revisar reservas','Detectar conflictos','Revisar inventario']}/>}>
        <div style={{display:'flex',flexDirection:'column',gap:14}}>
          <Badge status={sel.status} style={{alignSelf:'flex-start'}}/>
          {sel.alert&&<Alert tone="warning" title={sel.alert}>No caduca automáticamente.</Alert>}
          <KeyValueList columns={2} items={[{label:'Inmueble',value:sel.prop},{label:'Tipo',value:sel.type},{label:'Cliente',value:sel.client,span:true},{label:'Modalidades',value:sel.modes},{label:'Precio estándar',value:<Money amount={sel.rent} currency={sel.cur} strong/>},{label:'Empresa facturadora',value:'Atlas Inmuebles SRL',span:true}]}/>
          <div style={{display:'flex',flexWrap:'wrap',gap:8,marginTop:6}}>{sel.status==='reservado'?<Button size="sm" icon="scroll-text">Convertir a contrato</Button>:sel.status==='disponible'?<Button size="sm" icon="calendar-plus">Nueva reserva</Button>:<Button size="sm" variant="secondary" icon="file-plus">Factura manual</Button>}<Button size="sm" variant="ghost">Ver ficha</Button></div>
        </div>
      </Card>}
    </div>
  </div>;
}
window.Units=Units;