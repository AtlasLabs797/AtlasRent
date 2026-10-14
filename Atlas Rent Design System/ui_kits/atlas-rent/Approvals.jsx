function Approvals({toast,setCount}){
  const {ApprovalCard,Alert,Switch,Card,Tabs}=window.AtlasRentDesignSystem_75bc92;
  const [items,setItems]=React.useState(window.AR_DATA.approvals);const [tab,setTab]=React.useState('p');
  const resolve=(id,msg)=>{setItems(i=>{const n=i.filter(x=>x.id!==id);setCount(n.length);return n;});toast(msg);};
  return <div>
    <PageHead eyebrow="Control" title="Aprobaciones IA" sub="Acciones propuestas por agentes que requieren autorización humana"><div style={{padding:'9px 20px',border:'1px solid var(--border-default)',borderRadius:999,background:'var(--surface-card)'}}><Switch tone="ai" defaultChecked label="IA activa"/></div></PageHead>
    <Tabs value={tab} onChange={setTab} style={{marginBottom:24}} tabs={[{label:'Pendientes',value:'p',count:items.length},{label:'Aprobadas',value:'a'},{label:'Rechazadas',value:'r'},{label:'Caducadas',value:'c'}]}/>
    <div style={{display:'flex',flexDirection:'column',gap:17,maxWidth:'var(--content-narrow)'}}>
      <Alert tone="ai">El motor de permisos decide qué requiere aprobación. Si los datos cambian antes de aprobar, la autorización caduca.</Alert>
      {items.length===0?<div style={{padding:40,textAlign:'center',color:'var(--text-muted)',font:'var(--type-body)'}}>No hay nada que aprobar.<br/>Las acciones nuevas de los agentes aparecen aquí en menos de un minuto.</div>:
      items.map(a=><ApprovalCard key={a.id} {...a} onApprove={()=>resolve(a.id,'Aprobado · '+a.action)} onReject={()=>resolve(a.id,'Rechazado · '+a.action)} onEdit={()=>{}}/>)}
    </div>
  </div>;
}
window.Approvals=Approvals;