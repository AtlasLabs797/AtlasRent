Use in the Reservas screen for salas/oficinas and inside the "Nueva reserva por horas" dialog. A conflicting `proposed` slot must also disable the save button — no real overlap is allowed.
```jsx
<HourSlots resources={[{name:'Sala Coral',meta:'8 pax',bookings:[{start:'09:00',end:'11:00',label:'Marlin SRL'}]}]} proposed={{resource:'Sala Coral',start:'10:00',end:'12:00'}}/>
```