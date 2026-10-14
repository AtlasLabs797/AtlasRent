function Dashboard({go}){
  const {OccupancyBar,StatCard,Card,Alert,Button,AIActionButton,Badge,Money,DataTable,SegmentedControl,SectionHeader}=window.AtlasRentDesignSystem_75bc92;
  const d=window.AR_DATA;const [cur,setCur]=React.useState('USD');
  const occ={disponible:2,reservado:6,ocupado:128,mantenimiento:9,fuera:5};
  const tot=150;
  return <div>
    <PageHead eyebrow="Inicio" title="Buenos días, Carmen" sub="Miércoles 8 de octubre de 2026 · 150 unidades">
      <AIActionButton label="Preguntar a IA" actions={['Revisar pendientes','Detectar anomalías','Priorizar tareas','Generar resumen']}/>
      <Button variant="secondary" icon="refresh-cw" onClick={()=>go('facturas')}>Generar facturas periódicas</Button>
    </PageHead>
    <SectionHeader eyebrow="Resumen · octubre 2026"><SegmentedControl options={['USD','DOP','Consolidado']} value={cur} onChange={setCur}/></SectionHeader>
    <div style={{display:'grid',gridTemplateColumns:'repeat(auto-fit,minmax(200px,1fr))',gap:17,marginBottom:40}}>
      {cur==='DOP'?<>
        <StatCard featured style={{gridColumn:'span 2'}} label="Cobrado" value="356.800 DOP" meta="86,5% de 412.600 DOP facturado · +2,1% vs. septiembre"/><StatCard label="Pendiente" value="55.800 DOP" meta="2 facturas"/><StatCard label="Vencido" value="0 DOP" meta="0 facturas"/><StatCard label="Fianzas" value="83.700 DOP" meta="3 contratos"/>
      </>:<>
        <StatCard featured style={{gridColumn:'span 2'}} label="Cobrado" value="162.075 USD" meta="87,9% de 184.320 USD facturado · +3,4% vs. septiembre"/><StatCard label="Pendiente" value="22.245 USD" meta="19 facturas"/><StatCard label="Vencido" value="9.480 USD" tone="danger" meta="23 facturas"/><StatCard label="Fianzas" value="214.900 USD" meta="121 contratos"/>
      </>}
    </div>
    <div style={{display:'grid',gridTemplateColumns:'repeat(auto-fit,minmax(min(100%,420px),1fr))',gap:24,alignItems:'start'}}>
      <div style={{display:'flex',flexDirection:'column',gap:24}}>
        <Card title="Ocupación" actions={<Button variant="ghost" size="sm" iconRight="arrow-right" onClick={()=>go('unidades')}>Ver unidades</Button>}>
          <OccupancyBar counts={occ}/>
        </Card>
        <Card title="Facturas vencidas" padded={false} actions={<Button variant="ghost" size="sm" iconRight="arrow-right" onClick={()=>go('facturas')}>Ver todas</Button>}>
          <DataTable rows={d.invoices.filter(i=>i.overdue)} columns={[{key:'id',label:'Factura',render:r=><span style={{font:'var(--type-mono)',color:'var(--text-strong)'}}>{r.id}</span>},{key:'client',label:'Cliente'},{key:'due',label:'Vencimiento'},{key:'s',label:'Estado',render:r=><div style={{display:'flex',gap:4}}><Badge status={r.status}/><Badge status="vencida"/></div>},{key:'p',label:'Pendiente',align:'right',render:r=><Money amount={r.total-r.paid} currency={r.cur} tone="danger"/>}]}/>
        </Card>
      </div>
      <Card title="Alertas" actions={<Badge>{d.alerts.length}</Badge>}>
        <div style={{display:'flex',flexDirection:'column',gap:10}}>{d.alerts.map((a,i)=><Alert key={i} tone={a.tone} title={a.title} action={a.tone==='ai'?<Button size="sm" variant="ai" onClick={()=>go('aprobaciones')}>Revisar</Button>:null}>{a.body}</Alert>)}</div>
      </Card>
    </div>
  </div>;
}
window.Dashboard=Dashboard;