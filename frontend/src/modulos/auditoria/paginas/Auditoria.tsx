import { useState, type FormEvent, type JSX } from 'react';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { TablaVirtual } from '../../../componentes/TablaVirtual';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { Input } from '../../../design-system/components/forms/Input';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { Pagination } from '../../../design-system/components/navigation/Pagination';
import { formatearMarca } from '../../../dominio/fechas';
import { textos } from '../../../i18n/textos';
import { useAuditoria } from '../api';
import { datosComoTexto, FILTROS_VACIOS, type AuditoriaFila, type FiltrosAuditoria } from '../modelo';
import { textosAuditoria as t } from '../textos';

/**
 * Lectura de la auditoría (`auditoria.ver`). Solo lectura. `datos` se enseña como JSON en texto
 * plano dentro de un `<pre>`: es contenido de la base de datos, nunca HTML.
 */
export default function Auditoria(): JSX.Element {
  const [borrador, setBorrador] = useState<FiltrosAuditoria>(FILTROS_VACIOS);
  const [filtros, setFiltros] = useState<FiltrosAuditoria>(FILTROS_VACIOS);
  const [pagina, setPagina] = useState(1);
  const [elegida, setElegida] = useState<AuditoriaFila | null>(null);
  const consulta = useAuditoria(pagina, filtros);
  const f = t.filtros;

  function aplicar(ev: FormEvent) {
    ev.preventDefault();
    setFiltros(borrador);
    setPagina(1);
    setElegida(null);
  }

  function limpiar() {
    setBorrador(FILTROS_VACIOS);
    setFiltros(FILTROS_VACIOS);
    setPagina(1);
    setElegida(null);
  }

  const campo = (clave: keyof FiltrosAuditoria) => ({
    value: borrador[clave],
    onChange: (ev: { target: { value: string } }) => setBorrador({ ...borrador, [clave]: ev.target.value }),
  });

  return (
    <>
      <PageHeader
        eyebrow={textos.navegacion.grupos.control}
        title={t.titulo}
        description={t.descripcion}
        breadcrumbs={[{ label: textos.navegacion.grupos.control }, { label: textos.navegacion.modulos.auditoria.etiqueta }]}
      />
      <Card>
        <form
          onSubmit={aplicar}
          style={{ display: 'grid', gap: 'var(--space-6)', gridTemplateColumns: 'repeat(auto-fit,minmax(180px,1fr))', alignItems: 'end' }}
        >
          <Input label={f.desde} type="date" {...campo('desde')} />
          <Input label={f.hasta} type="date" {...campo('hasta')} />
          <Input label={f.usuario} {...campo('usuarioId')} />
          <Input label={f.entidad} {...campo('entidad')} />
          <Input label={f.accion} {...campo('accion')} />
          <div style={{ display: 'flex', gap: 'var(--space-4)' }}>
            <Button type="submit" variant="secondary" icon="search">
              {f.aplicar}
            </Button>
            <Button type="button" variant="ghost" onClick={limpiar}>
              {f.limpiar}
            </Button>
          </div>
        </form>
      </Card>
      <div style={{ height: 'var(--space-9)' }} />
      <Card title={t.tabla.titulo} padded={false}>
        {consulta.isPending ? (
          <p style={{ margin: 'var(--space-9)', color: 'var(--text-muted)' }}>{t.cargando}</p>
        ) : consulta.isError ? (
          <div style={{ padding: 'var(--space-9)' }}>
            <ErrorAlerta error={consulta.error} titulo={t.errorCarga} />
          </div>
        ) : (
          <>
            <TablaVirtual<AuditoriaFila>
              rows={consulta.data.data}
              empty={t.tabla.vacio}
              onRowClick={setElegida}
              columns={[
                { key: 'fecha', label: t.tabla.columnas.fecha, render: (x: AuditoriaFila) => <span className="mono">{formatearMarca(x.fecha)}</span> },
                { key: 'usuario', label: t.tabla.columnas.usuario, render: (x: AuditoriaFila) => x.usuarioNombre ?? t.tabla.sistema },
                { key: 'accion', label: t.tabla.columnas.accion, render: (x: AuditoriaFila) => <span className="mono">{x.accion}</span> },
                { key: 'entidad', label: t.tabla.columnas.entidad, render: (x: AuditoriaFila) => x.entidad ?? '—' },
                { key: 'resultado', label: t.tabla.columnas.resultado, render: (x: AuditoriaFila) => x.resultado ?? '—' },
                { key: 'ip', label: t.tabla.columnas.ip, render: (x: AuditoriaFila) => <span className="mono">{x.ip}</span> },
                {
                  key: 'detalle',
                  label: t.tabla.columnas.detalle,
                  align: 'right',
                  render: (x: AuditoriaFila) => (
                    <Button variant="ghost" size="sm" aria-label={t.tabla.verEtiqueta.replace('{accion}', x.accion)} onClick={() => setElegida(x)}>
                      {t.tabla.ver}
                    </Button>
                  ),
                },
              ]}
            />
            <Pagination
              page={pagina}
              pageSize={consulta.data.pageSize}
              total={consulta.data.total}
              onChange={(p) => {
                setPagina(p);
                setElegida(null);
              }}
            />
          </>
        )}
      </Card>
      {elegida && (
        <>
          <div style={{ height: 'var(--space-9)' }} />
          <Card
            title={`${t.detalle.titulo}: ${elegida.accion}`}
            actions={
              <Button variant="ghost" size="sm" onClick={() => setElegida(null)}>
                {t.detalle.cerrar}
              </Button>
            }
          >
            <dl style={{ margin: 0, display: 'grid', gap: 'var(--space-4)' }}>
              {elegida.origen && <Dato etiqueta={t.detalle.origen} valor={elegida.origen} />}
              {elegida.entidadId && <Dato etiqueta={t.detalle.entidadId} valor={elegida.entidadId} />}
            </dl>
            <p style={{ color: 'var(--text-muted)' }}>{t.detalle.nota}</p>
            {elegida.datos === null || elegida.datos === undefined ? (
              <p style={{ color: 'var(--text-muted)' }}>{t.detalle.sinDatos}</p>
            ) : (
              <pre
                className="mono"
                style={{
                  margin: 0,
                  padding: 'var(--space-6)',
                  overflow: 'auto',
                  maxHeight: 360,
                  background: 'var(--surface-sunken)',
                  border: '1px solid var(--border-default)',
                  borderRadius: 'var(--radius-md)',
                  whiteSpace: 'pre-wrap',
                  wordBreak: 'break-word',
                }}
              >
                {datosComoTexto(elegida.datos)}
              </pre>
            )}
          </Card>
        </>
      )}
    </>
  );
}

function Dato({ etiqueta, valor }: { etiqueta: string; valor: string }) {
  return (
    <div style={{ display: 'flex', gap: 'var(--space-6)', justifyContent: 'space-between' }}>
      <dt style={{ color: 'var(--text-muted)' }}>{etiqueta}</dt>
      <dd className="mono" style={{ margin: 0 }}>
        {valor}
      </dd>
    </div>
  );
}
