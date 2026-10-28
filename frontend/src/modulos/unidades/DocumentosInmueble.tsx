import { useRef, useState, type FormEvent, type JSX } from 'react';
import { aErrorApi } from '../../api/errores';
import { ErrorAlerta } from '../../componentes/ErrorAlerta';
import { Button } from '../../design-system/components/core/Button';
import { Card } from '../../design-system/components/core/Card';
import { IconButton } from '../../design-system/components/core/IconButton';
import { DataTable } from '../../design-system/components/data/DataTable';
import { Alert } from '../../design-system/components/feedback/Alert';
import { EmptyState } from '../../design-system/components/feedback/EmptyState';
import { Skeleton } from '../../design-system/components/feedback/Skeleton';
import { Select } from '../../design-system/components/forms/Select';
import { formatearMarca } from '../../dominio/fechas';
import { descargarDocumento, useDocumentos, useSubirDocumento } from './api';
import { claveErrorSubida, formatearTamano, validarPdf } from './logica';
import { textosUnidades } from './textos';
import type { CategoriaDocumento, DocumentoInmueble } from './tipos';

const t = textosUnidades.documentos;
const CATEGORIAS: CategoriaDocumento[] = ['Compraventa', 'Titulo', 'Otros'];

/**
 * Documentos PDF de propiedad. Solo se monta con `inmueble.documento`: sin la acción ni se pide la
 * lista (sería un 403) ni se enseña la pestaña.
 */
export function DocumentosInmueble({ inmuebleId }: { inmuebleId: string }): JSX.Element {
  const lista = useDocumentos(inmuebleId, true);
  const subir = useSubirDocumento(inmuebleId);
  const entrada = useRef<HTMLInputElement>(null);
  const [categoria, setCategoria] = useState<CategoriaDocumento>('Compraventa');
  const [archivo, setArchivo] = useState<File | null>(null);
  const [aviso, setAviso] = useState<{ tono: 'danger' | 'success'; texto: string } | null>(null);

  async function enviar(e: FormEvent) {
    e.preventDefault();
    if (!archivo) {
      setAviso({ tono: 'danger', texto: t.sinArchivo });
      return;
    }
    const motivo = validarPdf(archivo);
    if (motivo) {
      setAviso({ tono: 'danger', texto: t.errores[motivo] });
      return;
    }
    setAviso(null);
    try {
      await subir.mutateAsync({ archivo, categoria });
      setArchivo(null);
      if (entrada.current) entrada.current.value = '';
      setAviso({ tono: 'success', texto: t.subidoOk });
    } catch (err) {
      const api = aErrorApi(err);
      const clave = claveErrorSubida(api.status, api.detail);
      setAviso({ tono: 'danger', texto: clave === 'detalle' ? (api.detail ?? t.errores.generico) : t.errores[clave] });
    }
  }

  async function descargar(doc: DocumentoInmueble) {
    try {
      await descargarDocumento(inmuebleId, doc);
    } catch (err) {
      setAviso({ tono: 'danger', texto: aErrorApi(err).detail ?? t.errorDescarga });
    }
  }

  return (
    <div style={{ display: 'grid', gap: 24 }}>
      <Card title={t.titulo}>
        <p style={{ margin: '0 0 var(--space-7)', color: 'var(--text-muted)' }}>{t.subtitulo}</p>
        <form onSubmit={enviar} noValidate style={{ display: 'flex', gap: 'var(--space-7)', alignItems: 'flex-end', flexWrap: 'wrap' }}>
          <Select
            label={t.categoria}
            value={categoria}
            options={CATEGORIAS.map((c) => ({ value: c, label: textosUnidades.categorias[c] }))}
            onChange={(e) => setCategoria(e.target.value as CategoriaDocumento)}
            style={{ minWidth: 180 }}
          />
          <div className="ar-field" style={{ flex: '1 1 260px' }}>
            <label className="ar-label" htmlFor="documento-archivo">
              {t.archivo}
            </label>
            <input
              id="documento-archivo"
              ref={entrada}
              type="file"
              accept="application/pdf,.pdf"
              onChange={(e) => {
                setArchivo(e.target.files?.[0] ?? null);
                setAviso(null);
              }}
            />
            <span className="ar-hint">{t.archivoPista}</span>
          </div>
          <Button type="submit" icon="upload" disabled={subir.isPending}>
            {subir.isPending ? t.subiendo : t.subir}
          </Button>
        </form>
        {aviso && (
          <Alert tone={aviso.tono} title={aviso.tono === 'danger' ? t.errores.titulo : undefined} style={{ marginTop: 'var(--space-7)' }}>
            {aviso.texto}
          </Alert>
        )}
      </Card>

      <Card padded={false}>
        {lista.isPending ? (
          <div style={{ padding: 'var(--space-9)' }}>
            <Skeleton lines={3} height={20} />
          </div>
        ) : lista.isError ? (
          <div style={{ padding: 'var(--space-9)' }}>
            <ErrorAlerta error={lista.error} titulo={t.errorTitulo} />
          </div>
        ) : lista.data.data.length === 0 ? (
          <EmptyState icon="file-text" title={t.vacioTitulo}>
            {t.vacioCuerpo}
          </EmptyState>
        ) : (
          <DataTable
            rows={lista.data.data}
            columns={[
              { key: 'categoria', label: t.columnas.categoria, render: (d: DocumentoInmueble) => textosUnidades.categorias[d.categoria] },
              { key: 'nombre', label: t.columnas.nombre, render: (d: DocumentoInmueble) => <span className="mono">{d.nombreOriginal}</span> },
              { key: 'version', label: t.columnas.version, align: 'right', render: (d: DocumentoInmueble) => <span className="mono">v{d.version}</span> },
              { key: 'tamano', label: t.columnas.tamano, align: 'right', render: (d: DocumentoInmueble) => <span className="mono">{formatearTamano(d.tamanoBytes)}</span> },
              { key: 'fecha', label: t.columnas.fecha, render: (d: DocumentoInmueble) => <span className="mono">{formatearMarca(d.createdAt)}</span> },
              {
                key: 'acciones',
                label: '',
                align: 'right',
                width: 56,
                render: (d: DocumentoInmueble) => <IconButton size="sm" icon="download" label={t.descargar} onClick={() => void descargar(d)} />,
              },
            ]}
          />
        )}
      </Card>
    </div>
  );
}
