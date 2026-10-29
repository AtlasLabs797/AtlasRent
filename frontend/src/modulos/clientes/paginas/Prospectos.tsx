import { useQuery } from '@tanstack/react-query';
import { useEffect, useMemo, useState } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate } from 'react-router';
import { aErrorApi } from '../../../api/errores';
import { aplicarErroresApi } from '../../../api/formularios';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Badge } from '../../../design-system/components/core/Badge';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { DataTable } from '../../../design-system/components/data/DataTable';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { Dialog } from '../../../design-system/components/feedback/Dialog';
import { Drawer } from '../../../design-system/components/feedback/Drawer';
import { EmptyState } from '../../../design-system/components/feedback/EmptyState';
import { Skeleton } from '../../../design-system/components/feedback/Skeleton';
import { Input } from '../../../design-system/components/forms/Input';
import { Select } from '../../../design-system/components/forms/Select';
import { Textarea } from '../../../design-system/components/forms/Textarea';
import { FilterBar } from '../../../design-system/components/navigation/FilterBar';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { Pagination } from '../../../design-system/components/navigation/Pagination';
import { Tabs } from '../../../design-system/components/navigation/Tabs';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { leerProspecto, useDesactivarProspecto, useGuardarProspecto, useListaProspectos, useReactivarProspecto } from '../api';
import { FormularioCliente } from '../FormularioCliente';
import {
  cuerpoProspecto,
  emailPlausible,
  TIPOS,
  valoresClienteDesdeProspecto,
  valoresProspecto,
  type ValoresProspecto,
} from '../logica';
import { textosClientes } from '../textos';
import type { ProspectoResumen, TipoCliente } from '../tipos';
import { useRetardo } from '../useRetardo';

const t = textosClientes;
const p = t.prospectos;
const POR_PAGINA = 25;

export default function Prospectos() {
  const navegar = useNavigate();
  const identidad = useSesion((s) => s.identidad);
  const editar = puede(identidad, ACCIONES.clienteEditar);
  const [q, setQ] = useState('');
  const [page, setPage] = useState(1);
  const [seleccion, setSeleccion] = useState<string | 'nuevo' | null>(null);
  const qRetardada = useRetardo(q, 300);
  useEffect(() => setPage(1), [qRetardada]);

  const lista = useListaProspectos({ q: qRetardada, page, pageSize: POR_PAGINA });
  const filas = lista.data?.data ?? [];

  return (
    <>
      <PageHeader eyebrow={t.eyebrow} title={p.titulo} description={p.descripcion} breadcrumbs={[{ label: t.eyebrow }, { label: t.titulo, onClick: () => navegar('/clientes') }, { label: p.titulo }]}>
        {editar && (
          <Button icon="plus" onClick={() => setSeleccion('nuevo')}>
            {p.nuevo}
          </Button>
        )}
      </PageHeader>
      <Tabs
        tabs={[{ label: t.pestanas.clientes, value: 'clientes' }, { label: t.pestanas.prospectos, value: 'prospectos' }]}
        value="prospectos"
        onChange={(v) => v === 'clientes' && navegar('/clientes')}
      />
      <div style={{ height: 16 }} />
      <Card padded={false}>
        <FilterBar searchPlaceholder={p.buscar} search={q} onSearch={setQ} />
        {lista.isPending ? (
          <div style={{ padding: 'var(--space-9)' }}>
            <Skeleton lines={5} height={20} />
          </div>
        ) : lista.isError ? (
          <div style={{ padding: 'var(--space-9)' }}>
            <ErrorAlerta error={lista.error} titulo={p.errorTitulo} />
          </div>
        ) : filas.length === 0 ? (
          <EmptyState icon="user-plus" title={p.vacioTitulo}>
            {p.vacioCuerpo}
          </EmptyState>
        ) : (
          <>
            <DataTable
              rows={filas}
              onRowClick={(f: ProspectoResumen) => setSeleccion(f.id)}
              columns={[
                { key: 'nombre', label: p.columnas.nombre, render: (f: ProspectoResumen) => <strong style={{ color: 'var(--text-strong)', fontWeight: 600 }}>{f.nombre}</strong> },
                { key: 'tipo', label: p.columnas.tipo, render: (f: ProspectoResumen) => t.tipos[f.tipo] },
                { key: 'email', label: p.columnas.email, render: (f: ProspectoResumen) => f.email ?? '—' },
                { key: 'telefono', label: p.columnas.telefono, render: (f: ProspectoResumen) => <span className="mono">{f.telefono ?? '—'}</span> },
                { key: 'estado', label: p.columnas.estado, render: (f: ProspectoResumen) => (f.activo ? <Badge status="activo" /> : <Badge tone="neutral">{t.estados.inactivo}</Badge>) },
              ]}
            />
            <Pagination page={lista.data?.page ?? page} pageSize={POR_PAGINA} total={lista.data?.total ?? 0} onChange={setPage} />
          </>
        )}
      </Card>
      <PanelProspecto seleccion={seleccion} editar={editar} onClose={() => setSeleccion(null)} />
    </>
  );
}

function PanelProspecto({ seleccion, editar, onClose }: { seleccion: string | 'nuevo' | null; editar: boolean; onClose: () => void }) {
  const navegar = useNavigate();
  const id = seleccion !== null && seleccion !== 'nuevo' ? seleccion : undefined;
  const ficha = useQuery({ queryKey: ['prospectos', 'ficha', id], enabled: id !== undefined, queryFn: () => leerProspecto(id!) });
  const guardar = useGuardarProspecto();
  const desactivar = useDesactivarProspecto();
  const reactivar = useReactivarProspecto();
  const [convirtiendo, setConvirtiendo] = useState(false);
  const [general, setGeneral] = useState<string | null>(null);
  const [confirmando, setConfirmando] = useState(false);
  const { register, handleSubmit, reset, setError, watch, setValue, formState } = useForm<ValoresProspecto>({ defaultValues: valoresProspecto() });
  const { errors, isSubmitting } = formState;

  useEffect(() => {
    if (seleccion === null) return;
    reset(valoresProspecto(ficha.data));
    setGeneral(null);
  }, [seleccion, ficha.data, reset]);

  async function enviar(v: ValoresProspecto) {
    setGeneral(null);
    try {
      await guardar.mutateAsync({ ...(id ? { id } : {}), cuerpo: cuerpoProspecto(v) });
      onClose();
    } catch (e) {
      setGeneral(aplicarErroresApi(e, setError, ['nombre', 'tipo', 'documento', 'email', 'telefono', 'notas'], t.formulario.errorGeneral));
    }
  }

  const f = t.formulario;
  const cargando = id !== undefined && ficha.isPending;
  const convertido = ficha.data?.clienteId != null;
  const soloLectura = !editar || convertido;
  // Estable entre renders: el formulario de cliente se reinicia cuando cambia `inicial`.
  const valoresConversion = useMemo(() => (ficha.data ? valoresClienteDesdeProspecto(ficha.data) : undefined), [ficha.data]);
  return (
    <>
      <Drawer
        open={seleccion !== null && !convirtiendo}
        onClose={isSubmitting ? undefined : onClose}
        title={id ? p.tituloEdicion : p.tituloAlta}
        footer={
          editar && !convertido ? (
            <>
              {id && ficha.data?.activo && (
                <Button variant="ghost" onClick={() => setConfirmando(true)} style={{ marginRight: 'auto' }}>
                  {p.desactivar}
                </Button>
              )}
              {id && ficha.data && !ficha.data.activo && (
                <Button
                  variant="secondary"
                  disabled={reactivar.isPending}
                  onClick={async () => {
                    setGeneral(null);
                    try {
                      await reactivar.mutateAsync(id);
                      onClose();
                    } catch (e) {
                      setGeneral(aErrorApi(e).detail ?? p.reactivarError);
                    }
                  }}
                  style={{ marginRight: 'auto' }}
                >
                  {p.reactivar}
                </Button>
              )}
              <Button variant="ghost" onClick={onClose} disabled={isSubmitting}>
                {f.cancelar}
              </Button>
              <Button type="submit" form="formulario-prospecto" disabled={isSubmitting || cargando}>
                {isSubmitting ? f.guardando : f.guardar}
              </Button>
            </>
          ) : undefined
        }
      >
        {cargando ? (
          <Skeleton lines={5} height={20} />
        ) : ficha.isError ? (
          <ErrorAlerta error={ficha.error} titulo={p.errorTitulo} />
        ) : (
          <form id="formulario-prospecto" onSubmit={handleSubmit(enviar)} noValidate style={{ display: 'grid', gap: 'var(--space-7)' }}>
            {general && (
              <Alert tone="danger" title={f.errorGeneral}>
                {general}
              </Alert>
            )}
            <fieldset disabled={soloLectura} style={{ border: 0, padding: 0, margin: 0, display: 'grid', gap: 'var(--space-7)' }}>
              <Input label={f.nombre} required autoComplete="off" error={errors.nombre?.message} {...register('nombre', { validate: (v) => v.trim() !== '' || f.errores.obligatorio, maxLength: { value: 200, message: f.errores.largo } })} />
              <Select
                label={f.tipo}
                value={watch('tipo')}
                options={TIPOS.map((v) => ({ value: v, label: t.tipos[v] }))}
                onChange={(e) => setValue('tipo', e.target.value as TipoCliente)}
              />
              <Input label={f.documento} hint={f.documentoPista} autoComplete="off" error={errors.documento?.message} {...register('documento', { maxLength: { value: 64, message: f.errores.largo } })} />
              <Input label={f.email} type="email" autoComplete="off" error={errors.email?.message} {...register('email', { validate: (v) => emailPlausible(v) || f.errores.email })} />
              <Input label={f.telefono} type="tel" autoComplete="off" error={errors.telefono?.message} {...register('telefono', { maxLength: { value: 40, message: f.errores.largo } })} />
              <Textarea label={f.notas} rows={3} error={errors.notas?.message} {...register('notas', { maxLength: { value: 1000, message: f.errores.largo } })} />
            </fieldset>
            {ficha.data && convertido && (
              <Alert
                tone="success"
                action={
                  <Button size="sm" variant="secondary" onClick={() => navegar(`/clientes/${ficha.data.clienteId}`)}>
                    {p.verCliente}
                  </Button>
                }
              >
                {p.convertido}
              </Alert>
            )}
            {ficha.data && !convertido && !ficha.data.activo && <Alert tone="warning">{p.inactivoAviso}</Alert>}
            {editar && ficha.data && !convertido && ficha.data.activo && (
              <Alert
                tone="info"
                action={
                  <Button size="sm" variant="secondary" onClick={() => setConvirtiendo(true)}>
                    {p.convertir}
                  </Button>
                }
              >
                {p.convertirAviso}
              </Alert>
            )}
          </form>
        )}
      </Drawer>
      {editar && id && ficha.data && (
        <FormularioCliente
          open={convirtiendo}
          inicial={valoresConversion}
          convertirProspectoId={id}
          onClose={() => setConvirtiendo(false)}
          onGuardado={(cliente) => {
            setConvirtiendo(false);
            onClose();
            navegar(`/clientes/${cliente.id}`);
          }}
        />
      )}
      <Dialog
        open={confirmando}
        title={p.desactivarTitulo}
        onClose={() => setConfirmando(false)}
        footer={
          <>
            <Button variant="ghost" onClick={() => setConfirmando(false)}>
              {f.cancelar}
            </Button>
            <Button
              variant="danger"
              disabled={desactivar.isPending}
              onClick={async () => {
                try {
                  await desactivar.mutateAsync(id!);
                  setConfirmando(false);
                  onClose();
                } catch (e) {
                  setConfirmando(false);
                  setGeneral(aErrorApi(e).detail ?? f.errorGeneral);
                }
              }}
            >
              {p.desactivarConfirmar}
            </Button>
          </>
        }
      >
        <p style={{ margin: 0, color: 'var(--text-muted)' }}>{p.desactivarCuerpo}</p>
      </Dialog>
    </>
  );
}
