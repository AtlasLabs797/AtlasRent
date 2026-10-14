function PaymentDialog({inv,onClose,onDone}){
  const {Dialog,Button,Input,Select,Money,Alert}=window.AtlasRentDesignSystem_75bc92;
  const pend=inv.total-inv.paid;
  return <Dialog title="Registrar cobro" description={inv.id+' · '+inv.client} onClose={onClose} width={520} footer={<><Button variant="secondary" onClick={onClose}>Cancelar</Button><Button icon="check" onClick={()=>onDone(inv)}>Registrar cobro</Button></>}>
    <div style={{display:'grid',gridTemplateColumns:'1fr 1fr',gap:17}}>
      <div style={{gridColumn:'1/-1',display:'flex',justifyContent:'space-between',alignItems:'center',padding:'14px 17px',background:'var(--surface-sunken)',borderRadius:'var(--radius-md)'}}><span style={{font:'var(--type-body-sm)',color:'var(--text-muted)'}}>Pendiente de esta factura</span><Money amount={pend} currency={inv.cur} rate={inv.rate} strong/></div>
      <Input label="Importe" suffix={inv.cur} defaultValue={pend.toLocaleString('de-DE',{minimumFractionDigits:2})} required/>
      <Input label="Fecha efectiva" defaultValue="08/10/2026" required/>
      <Select label="Forma de pago" options={['Transferencia','Efectivo','Depósito bancario']}/>
      <Input label="Referencia" placeholder="Ej. 88231"/>
      <Select label="Cuenta receptora" options={['Banco Popular · USD ··4471','BHD · DOP ··0912']} style={{gridColumn:'1/-1'}}/>
      <Alert tone="info" style={{gridColumn:'1/-1'}}>Se aplica a la deuda más antigua del contrato. El excedente queda como saldo a favor.</Alert>
    </div>
  </Dialog>;
}
function Invoices({toast}){
  const {Card,DataTable,Badge,Money,Button,IconButton,Input,Select,Tooltip,AIActionButton}=window.AtlasRentDesignSystem_75bc92;
  const [rows,setRows]=React.useState(window.AR_DATA.invoices);const [pay,setPay]=React.useState(null);
  const done=inv=>{setRows(rs=>rs.map(r=>r.id===inv.id?{...r,paid:r.total,status:'pagado',overdue:false}:r));setPay(null);toast('Cobro registrado · '+(inv.total-inv.paid).toLocaleString('de-DE',{minimumFractionDigits:2})+' '+inv.cur);};
  return <div>
    <PageHead eyebrow="Finanzas" title="Facturas" sub="Octubre 2026 · Series A, B, C"><AIActionButton actions={['Revisar facturas','Detectar anomalías','Simular prorrateo']}/><Button variant="secondary" icon="refresh-cw" onClick={()=>toast('18 facturas periódicas generadas · Serie A')}>Generar facturas periódicas</Button><Button icon="plus">Factura manual</Button></PageHead>
    <Card padded={false}>
      <div style={{display:'flex',flexWrap:'wrap',gap:10,padding:'17px 20px',borderBottom:'1px solid var(--border-subtle)'}}><Input icon="search" placeholder="Nº factura, cliente o unidad" style={{flex:'1 1 240px'}}/><Select options={['Todas las series','Serie A · Renta','Serie B · DOP','Serie C · Suministros']}/><Select options={['Estado de cobro','Pendiente','Parcial','Pagado','Vencida']}/><Select options={['Moneda','USD','DOP']}/></div>
      <DataTable rows={rows} columns={[
        {key:'id',label:'Factura',render:r=><span style={{font:'var(--type-mono)',color:r.status==='anulada'?'var(--text-subtle)':'var(--text-strong)'}}>{r.id}</span>},
        {key:'client',label:'Cliente',render:r=><div><div style={{color:'var(--text-strong)'}}>{r.client}</div><div style={{font:'var(--type-caption)',color:'var(--text-muted)'}}>{r.unit}</div></div>},
        {key:'concept',label:'Concepto'},{key:'due',label:'Vence'},
        {key:'s',label:'Cobro',render:r=><div style={{display:'flex',gap:4}}><Badge status={r.status}/>{r.overdue&&<Badge status="vencida"/>}</div>},
        {key:'total',label:'Total',align:'right',render:r=><Money amount={r.total} currency={r.cur} rate={r.rate} tone={r.status==='anulada'?'muted':undefined}/>},
        {key:'pend',label:'Pendiente',align:'right',render:r=>r.status==='anulada'?<span style={{color:'var(--text-subtle)'}}>—</span>:<Money amount={r.total-r.paid} currency={r.cur} tone={r.overdue?'danger':r.total-r.paid===0?'muted':undefined}/>},
        {key:'a',label:'',render:r=><div style={{display:'flex',gap:4,alignItems:'center',justifyContent:'flex-end'}}>{r.status!=='pagado'&&r.status!=='anulada'&&<Button size="sm" variant="secondary" icon="wallet" onClick={()=>setPay(r)}>Cobrar</Button>}<Tooltip content="Enviar por WhatsApp"><IconButton size="sm" icon="message-circle" label="WhatsApp" onClick={()=>toast('PDF enviado por WhatsApp')}/></Tooltip><Tooltip content="Duplicar"><IconButton size="sm" icon="copy" label="Duplicar"/></Tooltip></div>}
      ]}/>
    </Card>
    {pay&&<PaymentDialog inv={pay} onClose={()=>setPay(null)} onDone={done}/>}
  </div>;
}
window.Invoices=Invoices;