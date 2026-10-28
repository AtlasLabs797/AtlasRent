import { useState, type JSX } from 'react';
import { AltaMfa } from '../../../componentes/AltaMfa';
import { FormularioPassword } from '../../../componentes/FormularioPassword';
import { Badge } from '../../../design-system/components/core/Badge';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { KeyValueList } from '../../../design-system/components/data/KeyValueList';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { formatearMarca } from '../../../dominio/fechas';
import { textos } from '../../../i18n/textos';
import { nombreDelRol } from '../../../navegacion/menu';
import { useSesion } from '../../../sesion/store';

const t = textos.cuenta;

/** "Mi cuenta": datos de acceso, cambio voluntario de contraseña y alta del MFA (opcional salvo para el administrador). */
export default function MiCuenta(): JSX.Element {
  const identidad = useSesion((estado) => estado.identidad);
  const mfaActivado = useSesion((estado) => estado.mfaActivado);
  const [altaMfa, setAltaMfa] = useState(false);
  const [recienActivado, setRecienActivado] = useState(false);

  if (!identidad) return <></>;

  return (
    <>
      <PageHeader eyebrow={textos.app.nombre} title={t.titulo} description={t.descripcion} />
      <div style={{ display: 'grid', gap: 'var(--space-9)', maxWidth: 640 }}>
        <Card title={t.datos.titulo}>
          <KeyValueList
            columns={2}
            items={[
              { label: t.datos.usuario, value: identidad.nombreAcceso, mono: true },
              { label: t.datos.nombre, value: identidad.nombre },
              { label: t.datos.rol, value: nombreDelRol(identidad.rol) },
              {
                label: t.datos.ultimoAcceso,
                value: identidad.ultimoAcceso ? formatearMarca(identidad.ultimoAcceso) : t.datos.primerAcceso,
                mono: true,
              },
            ]}
          />
        </Card>

        <Card title={t.password.titulo}>
          <FormularioPassword />
        </Card>

        <Card title={t.mfa.titulo} actions={<Badge tone={identidad.mfaActivado ? 'success' : 'neutral'}>{identidad.mfaActivado ? t.mfa.activada : t.mfa.noActivada}</Badge>}>
          {recienActivado ? (
            <Alert tone="success" title={t.mfa.activadaAviso}>
              {t.mfa.activadaCuerpo}
            </Alert>
          ) : identidad.mfaActivado ? (
            <p style={{ margin: 0, color: 'var(--text-muted)' }}>{t.mfa.descripcion}</p>
          ) : altaMfa ? (
            <AltaMfa
              alTerminar={() => {
                mfaActivado();
                setAltaMfa(false);
                setRecienActivado(true);
              }}
            />
          ) : (
            <div style={{ display: 'grid', gap: 'var(--space-7)', justifyItems: 'start' }}>
              <p style={{ margin: 0, color: 'var(--text-muted)' }}>{t.mfa.descripcion}</p>
              <Button variant="secondary" onClick={() => setAltaMfa(true)}>
                {t.mfa.activar}
              </Button>
            </div>
          )}
        </Card>
      </div>
    </>
  );
}
