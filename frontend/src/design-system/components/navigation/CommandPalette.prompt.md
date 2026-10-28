Mount once in the app root. TopBar's search pill opens it (`onOpenSearch`), and ⌘K / Ctrl K toggles it anywhere.
```jsx
const [open, setOpen] = React.useState(false);
<TopBar onOpenSearch={() => setOpen(true)} />
<CommandPalette open={open} onOpenChange={setOpen} onSelect={go} groups={[
  { label: 'Clientes', items: [{ label: 'Inversiones Marlin SRL', sub: 'RNC 1-31-45678-2', meta: 'CLI-01873', icon: 'user' }] },
  { label: 'Acciones', items: [{ label: 'Registrar cobro', icon: 'wallet' }, { label: 'Preguntar a IA', icon: 'sparkles', tone: 'ai' }] },
]} />
```
Groups in this order: records first (Clientes, Unidades, Facturas, Contratos), then Acciones. Max ~6 items per group.
