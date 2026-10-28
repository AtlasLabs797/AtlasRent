import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Badge } from '../../../design-system/components/core/Badge';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { DataTable } from '../../../design-system/components/data/DataTable';
import { Drawer } from '../../../design-system/components/feedback/Drawer';
import { EmptyState } from '../../../design-system/components/feedback/EmptyState';
import { Checkbox } from '../../../design-system/components/forms/Checkbox';
import { Input } from '../../../design-system/components/forms/Input';
import { Select } from '../../../design-system/components/forms/Select';
import { Textarea } from '../../../design-system/components/forms/Textarea';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { Tabs } from '../../../design-system/components/navigation/Tabs';
import { textos } from '../../../i18n/textos';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { CargandoFilas } from '../../operacion/Comunes';
import { etiquetaTipoAnexo, type TipoAnexoApi } from '../../operacion/etiquetas';
import { AvisoBloqueo } from '../../reservas/AvisoBloqueo';
import { useAnexos, useDesactivarPlantilla, useGuardarPlantilla, usePlantillas, type PlantillaAnexo, type PlantillaContrato, type Seccion } from '../api';
import { DialogoAccion } from '../DialogoAccion';
import { textosContratos } from '../textos';

const p = textosContratos.plantillasPagina;
const TIPOS = Object.keys(etiquetaTipoAnexo) as TipoAnexoApi[];

type Elemento = PlantillaContrato | PlantillaAnexo;

/**
 * Plantillas de contrato y catálogo de anexos (§3.5). Leer exige `cliente.ver`; crear y editar,
 * `contrato.borrador`; desactivar, `contrato.estado`. El texto es plano: nunca se interpreta como formato.
 */
export default function Plantillas() {
  const navegar = useNavigate();
  const identidad = useSesion((s) => s.identidad);
  const [pestana, setPestana] = useState<'contratos' | 'anexos'>('contratos');
  const [editando, setEditando] = useState<Elemento | 'nuevo' | null>(null);
  const [desactivando, setDesactivando] = useState<Elemento | null>(null);
  const plantillas = usePlantillas();
  const anexos = useAnexos();
  const desactivarContrato = useDesactivarPlantilla('contrato');
  const desactivarAnexo = useDesactivarPlantilla('anexo');
  const escribe = puede(identidad, ACCIONES.contratoBorrador);
  const gestiona = puede(identidad, ACCIONES.contratoEstado);
  const enContratos = pestana === 'contratos';
  const consulta = enContratos ? plantillas : anexos;
  const filas: Elemento[] = (consulta.data?.data ?? []) as Elemento[];

  return (
    <>
      <PageHeader
        eyebrow={textos.navegacion.modulos.contratos.etiqueta}
        title={p.titulo}
        description={p.descripcion}
        breadcrumbs={[{ label: textos.navegacion.modulos.contratos.etiqueta, onClick: () => navegar('/contratos') }, { label: p.titulo }]}
      >
        {escribe && (
          <Button icon="plus" onClick={() => setEditando('nuevo')}>
            {enContratos ? p.nueva : p.nuevoAnexo}
          </Button>
        )}
      </PageHeader>

      <Tabs
        value={pestana}
        onChange={(v) => setPestana(v as 'contratos' | 'anexos')}
        tabs={[
          { value: 'contratos', label: p.pestanas.contratos },
          { value: 'anexos', label: p.pestanas.anexos },
        ]}
      />

      <div style={{ marginTop: 'var(--space-9)' }}>
        {!enContratos && <p style={{ margin: '0 0 var(--space-6)', color: 'var(--text-muted)' }}>{p.anexosAyuda}</p>}
        {consulta.isError && <ErrorAlerta error={consulta.error} titulo={p.errorTitulo} />}
        <Card padded={false}>
          {consulta.isPending ? (
            <CargandoFilas />
          ) : filas.length === 0 ? (
            <EmptyState icon="file-text" title={enContratos ? p.vacio : p.vacioAnexos} />
          ) : (
            <DataTable
              rows={filas}
              columns={[
                ...(enContratos ? [{ key: 'codigo', label: p.columnas.codigo, render: (x: PlantillaContrato) => <span className="mono">{x.codigo}</span> }] : [{ key: 'tipo', label: p.columnas.tipo, render: (x: PlantillaAnexo) => etiquetaTipoAnexo[x.tipo] }]),
                { key: 'nombre', label: p.columnas.nombre },
                { key: 'secciones', label: p.columnas.secciones, align: 'right' as const, render: (x: Elemento) => <span className="mono">{x.secciones.length}</span> },
                ...(enContratos ? [{ key: 'anexos', label: p.columnas.anexos, render: (x: PlantillaContrato) => x.anexosTipos.map((a) => etiquetaTipoAnexo[a]).join(', ') || '—' }] : []),
                {
                  key: 'estado',
                  label: p.columnas.estado,
                  render: (x: Elemento) => (
                    <Badge tone={x.activa ? 'success' : 'neutral'} dot>
                      {x.activa ? p.activa : p.inactiva}
                    </Badge>
                  ),
                },
                {
                  key: 'acciones',
                  label: '',
                  align: 'right' as const,
                  render: (x: Elemento) => (
                    <span style={{ display: 'inline-flex', gap: 'var(--space-2)' }}>
                      {escribe && (
                        <Button size="sm" variant="ghost" onClick={() => setEditando(x)}>
                          {p.editar}
                        </Button>
                      )}
                      {gestiona && x.activa && (
                        <Button size="sm" variant="ghost" onClick={() => setDesactivando(x)}>
                          {p.desactivar}
                        </Button>
                      )}
                    </span>
                  ),
                },
              ]}
            />
          )}
        </Card>
      </div>

      <FormularioPlantilla clase={pestana === 'contratos' ? 'contrato' : 'anexo'} elemento={editando === 'nuevo' ? null : editando} abierto={editando !== null} onCerrar={() => setEditando(null)} />

      <DialogoAccion
        abierto={desactivando !== null}
        titulo={`${p.desactivarTitulo} «${desactivando?.nombre ?? ''}»`}
        descripcion={p.desactivarConsecuencia}
        confirmar={p.desactivar}
        tono="danger"
        onConfirmar={() => (enContratos ? desactivarContrato : desactivarAnexo).mutateAsync(desactivando!.id)}
        onCerrar={() => setDesactivando(null)}
      />
    </>
  );
}

function FormularioPlantilla({ clase, elemento, abierto, onCerrar }: { clase: 'contrato' | 'anexo'; elemento: Elemento | null; abierto: boolean; onCerrar: () => void }) {
  const guardar = useGuardarPlantilla(clase, elemento?.id);
  const f = p.formulario;
  const [codigo, setCodigo] = useState('');
  const [nombre, setNombre] = useState('');
  const [tipo, setTipo] = useState<TipoAnexoApi>('Normas');
  const [secciones, setSecciones] = useState<Seccion[]>([{ titulo: '', texto: '' }]);
  const [anexosTipos, setAnexosTipos] = useState<TipoAnexoApi[]>([]);
  const [fallo, setFallo] = useState<unknown>(null);
  const [intentado, setIntentado] = useState(false);

  useEffect(() => {
    if (!abierto) return;
    setCodigo((elemento as PlantillaContrato | null)?.codigo ?? '');
    setNombre(elemento?.nombre ?? '');
    setTipo((elemento as PlantillaAnexo | null)?.tipo ?? 'Normas');
    setSecciones(elemento && elemento.secciones.length > 0 ? elemento.secciones.map((s) => ({ ...s })) : [{ titulo: '', texto: '' }]);
    setAnexosTipos((elemento as PlantillaContrato | null)?.anexosTipos ?? []);
    setFallo(null);
    setIntentado(false);
  }, [abierto, elemento]);

  const sinNombre = nombre.trim() === '';
  const sinCodigo = clase === 'contrato' && !elemento && codigo.trim() === '';
  const sinTexto = secciones.some((s) => s.texto.trim() === '');

  async function enviar() {
    setIntentado(true);
    setFallo(null);
    if (sinNombre || sinCodigo || sinTexto) return;
    try {
      await guardar.mutateAsync({
        ...(clase === 'contrato' && !elemento ? { codigo: codigo.trim() } : {}),
        ...(clase === 'anexo' && !elemento ? { tipo } : {}),
        nombre: nombre.trim(),
        secciones: secciones.map((s) => ({ titulo: s.titulo.trim(), texto: s.texto })),
        ...(clase === 'contrato' ? { anexosTipos } : {}),
      });
      onCerrar();
    } catch (e) {
      setFallo(e);
    }
  }

  return (
    <Drawer
      open={abierto}
      onClose={guardar.isPending ? undefined : onCerrar}
      width={640}
      title={elemento ? `${p.editar}: ${elemento.nombre}` : clase === 'contrato' ? p.nueva : p.nuevoAnexo}
      footer={
        <>
          <Button variant="ghost" onClick={onCerrar} disabled={guardar.isPending}>
            {f.cancelar}
          </Button>
          <Button onClick={() => void enviar()} disabled={guardar.isPending}>
            {guardar.isPending ? f.guardando : f.guardar}
          </Button>
        </>
      }
    >
      <div style={{ display: 'grid', gap: 'var(--space-9)' }}>
        {fallo !== null && <AvisoBloqueo error={fallo} titulo={f.error} />}
        {clase === 'contrato' && (
          <Input id="plantilla-codigo" label={f.codigo} required hint={f.codigoAyuda} disabled={Boolean(elemento)} maxLength={60} value={codigo} onChange={(e) => setCodigo(e.target.value)} error={intentado && sinCodigo ? 'Escribe el código.' : undefined} />
        )}
        {clase === 'anexo' && (
          <Select label={f.tipo} disabled={Boolean(elemento)} value={tipo} onChange={(e) => setTipo(e.target.value as TipoAnexoApi)} options={TIPOS.map((x) => ({ value: x, label: etiquetaTipoAnexo[x] }))} />
        )}
        <Input id="plantilla-nombre" label={f.nombre} required maxLength={120} value={nombre} onChange={(e) => setNombre(e.target.value)} error={intentado && sinNombre ? 'Escribe el nombre.' : undefined} />

        <fieldset style={{ border: 0, padding: 0, margin: 0, display: 'grid', gap: 'var(--space-6)' }}>
          <legend style={{ font: 'var(--type-label)', padding: 0, marginBottom: 'var(--space-4)' }}>{f.secciones}</legend>
          <span className="ar-hint">{f.textoPlano}</span>
          {secciones.map((s, i) => (
            <div key={i} style={{ display: 'grid', gap: 'var(--space-4)', padding: 'var(--space-6)', border: '1px solid var(--border-default)', borderRadius: 'var(--radius-md)' }}>
              <Input id={`seccion-${i}-titulo`} label={f.tituloSeccion} maxLength={200} value={s.titulo} onChange={(e) => setSecciones(secciones.map((x, j) => (j === i ? { ...x, titulo: e.target.value } : x)))} />
              <Textarea id={`seccion-${i}-texto`} label={f.texto} required rows={5} maxLength={20000} value={s.texto} onChange={(e) => setSecciones(secciones.map((x, j) => (j === i ? { ...x, texto: e.target.value } : x)))} error={intentado && s.texto.trim() === '' ? 'Escribe el texto.' : undefined} />
              <div>
                <Button variant="ghost" size="sm" icon="x" disabled={secciones.length === 1} onClick={() => setSecciones(secciones.filter((_, j) => j !== i))}>
                  {f.quitarSeccion}
                </Button>
              </div>
            </div>
          ))}
          <div>
            <Button variant="secondary" size="sm" icon="plus" onClick={() => setSecciones([...secciones, { titulo: '', texto: '' }])}>
              {f.anadirSeccion}
            </Button>
          </div>
        </fieldset>

        {clase === 'contrato' && (
          <fieldset style={{ border: 0, padding: 0, margin: 0, display: 'grid', gap: 'var(--space-4)' }}>
            <legend style={{ font: 'var(--type-label)', padding: 0, marginBottom: 'var(--space-4)' }}>{f.anexosTipos}</legend>
            {TIPOS.map((x) => (
              <Checkbox key={x} label={etiquetaTipoAnexo[x]} checked={anexosTipos.includes(x)} onChange={(e) => setAnexosTipos(e.target.checked ? [...anexosTipos, x] : anexosTipos.filter((y) => y !== x))} />
            ))}
          </fieldset>
        )}
      </div>
    </Drawer>
  );
}
