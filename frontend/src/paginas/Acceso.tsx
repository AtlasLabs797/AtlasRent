import { useState, type FormEvent, type JSX } from 'react';
import { Button } from '../design-system/components/core/Button';
import { Card } from '../design-system/components/core/Card';
import { Alert } from '../design-system/components/feedback/Alert';
import { Input } from '../design-system/components/forms/Input';
import { textos } from '../i18n/textos';
import { RequiereSegundoFactor, useSesion } from '../sesion/store';

const { acceso } = textos;

/** Pantalla de acceso: sin sesión no hay menú ni datos, solo entrar. */
export default function Acceso(): JSX.Element {
  const entrar = useSesion((estado) => estado.entrar);
  const [nombreAcceso, setNombreAcceso] = useState('');
  const [contrasena, setContrasena] = useState('');
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function enviar(evento: FormEvent) {
    evento.preventDefault();
    setEnviando(true);
    setError(null);
    try {
      const resultado = await entrar(nombreAcceso, contrasena);
      if (resultado === 'credenciales-invalidas') setError(acceso.credencialesInvalidas);
    } catch (e) {
      // El backend exige segundo factor y todavía no hay pantalla para introducirlo (F1-06).
      setError(e instanceof RequiereSegundoFactor ? acceso.segundoFactor : acceso.credencialesInvalidas);
    } finally {
      setEnviando(false);
    }
  }

  return (
    <div
      style={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 24,
        background: 'var(--surface-app)',
      }}
    >
      <Card title={acceso.titulo} style={{ width: '100%', maxWidth: 380 }}>
        <form onSubmit={enviar} style={{ display: 'grid', gap: 16 }}>
          <p style={{ margin: 0, color: 'var(--text-muted)' }}>{acceso.descripcion}</p>
          <Input
            label={acceso.nombreAcceso}
            name="nombreAcceso"
            autoComplete="username"
            value={nombreAcceso}
            onChange={(e) => setNombreAcceso(e.target.value)}
            required
          />
          <Input
            label={acceso.contrasena}
            name="contrasena"
            type="password"
            autoComplete="current-password"
            value={contrasena}
            onChange={(e) => setContrasena(e.target.value)}
            required
          />
          {error && (
            <Alert tone="danger" title={acceso.error}>
              {error}
            </Alert>
          )}
          <Button type="submit" disabled={enviando}>
            {enviando ? acceso.cambiando : acceso.entrar}
          </Button>
        </form>
      </Card>
    </div>
  );
}