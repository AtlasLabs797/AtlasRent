64px sticky topbar. Always show today's rate (mercado + margen → aplicado). `aiEnabled={false}` when the admin has disabled IA for the user or "Pausar IA" is on.
```jsx
<TopBar rate={{market:60.35,margin:1.65}} notifications={4} onChat={openChat}/>
```