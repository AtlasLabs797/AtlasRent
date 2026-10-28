import type { JSX } from 'react';
import { FormularioPassword } from '../componentes/FormularioPassword';
import { PantallaCentrada } from '../componentes/PantallaCentrada';
import { Button } from '../design-system/components/core/Button';
import { textos } from '../i18n/textos';
import { useSesion } from '../sesion/store';

const { forzado } = textos.cuenta;

/**
 * Cambio de contraseña obligatorio (`debeCambiarPassword` o 403 SEG-010). Mientras esté pendiente la
 * API solo deja ver la identidad, cerrar sesión y cambiarla, así que esta pantalla sustituye al
 * `Layout`: un menú con módulos que siempre darían 403 sería mentir.
 */
export default function CambioPasswordForzado(): JSX.Element {
  const salir = useSesion((estado) => estado.salir);
  return (
    <PantallaCentrada titulo={forzado.titulo} ancho={440}>
      <p style={{ margin: 0, color: 'var(--text-muted)' }}>{forzado.descripcion}</p>
      <FormularioPassword />
      <Button variant="ghost" onClick={() => void salir()}>
        {forzado.cerrarSesion}
      </Button>
    </PantallaCentrada>
  );
}
