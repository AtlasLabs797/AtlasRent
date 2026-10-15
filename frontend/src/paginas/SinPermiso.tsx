import { useNavigate } from 'react-router';
import { Button } from '../design-system/components/core/Button';
import { EmptyState } from '../design-system/components/feedback/EmptyState';
import { PageHeader } from '../design-system/components/navigation/PageHeader';
import { textos } from '../i18n/textos';

/**
 * Ruta a la que llega alguien por URL directa sin el permiso. No enseña el 403 del backend: explica
 * que la falta es de permisos y devuelve al panel (AGENTS.md §6: la validación real es del backend,
 * esta pantalla solo informa).
 */
export default function SinPermiso() {
  const navegar = useNavigate();
  const { sinPermiso, app } = textos;
  return (
    <>
      <PageHeader eyebrow={app.nombre} title={sinPermiso.titulo} description={sinPermiso.descripcion} />
      <EmptyState
        icon="shield-alert"
        title={sinPermiso.titulo}
        action={<Button onClick={() => navegar('/')}>{sinPermiso.volver}</Button>}
      />
    </>
  );
}