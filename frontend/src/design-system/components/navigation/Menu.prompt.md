Dropdown of actions for one object. Trigger is an `IconButton icon="ellipsis"` on table rows and cards, or a secondary `Button iconRight="chevron-down"` in page headers. Destructive items go last after a separator with `danger`.
```jsx
<Menu align="right" trigger={<IconButton icon="ellipsis" label="Más acciones" />} items={[
  { label: 'Duplicar factura', icon: 'copy' },
  { label: 'Enviar por WhatsApp', icon: 'message-circle' },
  { separator: true },
  { label: 'Anular factura', icon: 'ban', danger: true },
]} onSelect={v => …} />
```
Labels are verb-first, sentence case. Max ~8 items; more means it should be a page.
