import { useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { Button } from '../design-system/components/core/Button';
import { Card } from '../design-system/components/core/Card';
import { Alert } from '../design-system/components/feedback/Alert';
import { EmptyState } from '../design-system/components/feedback/EmptyState';
import { PageHeader } from '../design-system/components/navigation/PageHeader';
import { textos } from '../i18n/textos';
import { ACCIONES } from '../permisos';
import { puede, useSesion } from '../sesion/store';
import { preguntarIa } from '../ia/api';
import {
  ALCANCES,
  avisoDeError,
  cuerpoConsulta,
  type AlcanceConsulta,
  type CuerpoConsultaIa,
} from '../ia/consulta';
import SinPermiso from './SinPermiso';

/**
 * Asistente de IA (F6-03). Una pregunta, un alcance y una respuesta: el backend es sin estado, así
 * que aquí no hay conversación que guardar ni historial que restaurar (cada envío es una petición
 * nueva). Solo se ve lo que responde la última pregunta.
 *
 * `ia.consultar` se comprueba aquí y en la ruta, con el mismo identificador que declara el endpoint:
 * una pantalla que promete respuestas y solo puede dar un 403 miente (AGENTS.md §6).
 */
export default function Asistente() {
  const identidad = useSesion((estado) => estado.identidad);
  const [pregunta, setPregunta] = useState('');
  const [alcance, setAlcance] = useState<AlcanceConsulta>(ALCANCES[0]);
  const [falloLocal, setFalloLocal] = useState<string | null>(null);

  const consulta = useMutation({
    mutationFn: (cuerpo: CuerpoConsultaIa) => preguntarIa(cuerpo),
  });

  const enVuelo = consulta.isPending;

  function enviar() {
    if (enVuelo) return;
    let cuerpo: CuerpoConsultaIa;
    try {
      cuerpo = cuerpoConsulta(pregunta, alcance);
    } catch (e) {
      // El botón se deshabilita sin pregunta, así que esto es el camino de seguridad, no el normal.
      setFalloLocal(e instanceof Error ? e.message : textos.asistente.errores.generico.cuerpo);
      return;
    }
    setFalloLocal(null);
    consulta.reset();
    consulta.mutate(cuerpo);
  }

  if (!puede(identidad, ACCIONES.iaConsultar)) return <SinPermiso />;

  const t = textos.asistente;
  const respuesta = consulta.data ?? null;
  const aviso = consulta.isError ? avisoDeError(consulta.error) : null;

  return (
    <>
      <PageHeader
        eyebrow={textos.navegacion.grupos.herramientas}
        title={t.titulo}
        description={t.descripcion}
        breadcrumbs={[
          { label: textos.navegacion.grupos.herramientas },
          { label: textos.navegacion.modulos.asistente.etiqueta },
        ]}
      />

      <Card title={t.formulario.titulo}>
        <p style={{ margin: '0 0 var(--space-9)', color: 'var(--text-muted)' }}>{t.formulario.descripcion}</p>

        <div className="ar-field">
          <label className="ar-label" htmlFor="ia-pregunta">
            {t.formulario.pregunta}
          </label>
          {/* El design system trae `Textarea` pero todavía no está portado al frontend: es el
              `<textarea>` nativo con la clase del DS (`.ar-textarea`), como el `<select>` de los
              filtros de `Informes`. Pendiente de portarlo junto con `Select`. */}
          <textarea
            id="ia-pregunta"
            className="ar-textarea"
            placeholder={t.formulario.pista}
            rows={3}
            value={pregunta}
            disabled={enVuelo}
            onChange={(e) => setPregunta(e.target.value)}
          />
        </div>

        <div className="ar-field" style={{ marginTop: 'var(--space-6)' }}>
          <label className="ar-label" htmlFor="ia-alcance">
            {t.formulario.alcance}
          </label>
          <div className="ar-control">
            <select
              id="ia-alcance"
              value={alcance}
              disabled={enVuelo}
              onChange={(e) => setAlcance(e.target.value as AlcanceConsulta)}
            >
              {ALCANCES.map((valor) => (
                <option key={valor} value={valor}>
                  {t.formulario.alcances[valor]}
                </option>
              ))}
            </select>
          </div>
        </div>

        <div style={{ marginTop: 'var(--space-9)' }}>
          <Button
            variant="ai"
            icon="sparkles"
            disabled={enVuelo || pregunta.trim().length === 0}
            onClick={enviar}
          >
            {enVuelo ? t.formulario.enviando : t.formulario.enviar}
          </Button>
        </div>
      </Card>

      <div style={{ height: 24 }} />

      {falloLocal !== null && (
        <Alert tone="danger" title={t.errores.generico.titulo}>
          {falloLocal}
        </Alert>
      )}

      {aviso && (
        <Alert
          tone={aviso.tono}
          title={aviso.titulo}
          action={
            aviso.reintentable ? (
              <Button size="sm" variant="secondary" disabled={enVuelo} onClick={enviar}>
                {t.errores.reintentar}
              </Button>
            ) : undefined
          }
        >
          {aviso.mensaje}
        </Alert>
      )}

      <div style={{ height: 24 }} />

      <Card title={t.respuesta.titulo}>
        {respuesta === null ? (
          <EmptyState icon="sparkles" title={t.respuesta.vacioTitulo}>
            {t.respuesta.vacioCuerpo}
          </EmptyState>
        ) : (
          <>
            {/* La respuesta llega como texto con saltos de línea: se respeta tal cual. */}
            <p style={{ margin: 0, whiteSpace: 'pre-wrap' }}>{respuesta.respuesta}</p>
            <p style={{ margin: 'var(--space-9) 0 0', color: 'var(--text-subtle)' }}>
              {t.respuesta.fuente
                .replace('{proveedor}', respuesta.proveedor)
                .replace('{modelo}', respuesta.modelo)}
            </p>
          </>
        )}
      </Card>
    </>
  );
}