import { Alert } from '../design-system/components/feedback/Alert';
import { Button } from '../design-system/components/core/Button';
import { Card } from '../design-system/components/core/Card';
import { PageHeader } from '../design-system/components/navigation/PageHeader';
import { textos } from '../i18n/textos';
import { ACCIONES } from '../permisos';
import { puede, useSesion } from '../sesion/store';

export default function Inicio() {
  const { panel } = textos;
  const identidad = useSesion((estado) => estado.identidad);

  return (
    <>
      <PageHeader
        eyebrow={panel.sobrelinea}
        title={panel.titulo}
        description={panel.descripcion}
        breadcrumbs={[{ label: panel.sobrelinea }, { label: 'Panel' }]}
      >
        {/* "Registrar cobro" es `cobro.registrar`, que en la matriz de roles es de gerente y
            administrador. Mostrárselo al empleado sería ofrecerle una acción que siempre va a
            recibir un 403. El backend seguiría denegando: esto es para no prometer lo que no hay. */}
        {puede(identidad, ACCIONES.cobroRegistrar) && (
          <Button icon={textos.designSystem.botones.primariaIcono}>{panel.accionesPrimaria}</Button>
        )}
      </PageHeader>
      <Alert tone="info" title={textos.designSystem.alertas.infoTitulo}>
        {textos.designSystem.alertas.infoCuerpo}
      </Alert>
      <div style={{ height: 24 }} />
      <Card title={panel.titulo}>
        <p style={{ margin: 0, color: 'var(--text-muted)' }}>{panel.descripcion}</p>
      </Card>
    </>
  );
}