import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { Link, useNavigate, useParams } from 'react-router';
import { aErrorApi } from '../../../api/errores';
import { aplicarErroresApi } from '../../../api/formularios';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Badge } from '../../../design-system/components/core/Badge';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { IconButton } from '../../../design-system/components/core/IconButton';
import { DataTable } from '../../../design-system/components/data/DataTable';
import { KeyValueList } from '../../../design-system/components/data/KeyValueList';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { Dialog } from '../../../design-system/components/feedback/Dialog';
import { Drawer } from '../../../design-system/components/feedback/Drawer';
import { EmptyState } from '../../../design-system/components/feedback/EmptyState';
import { Skeleton } from '../../../design-system/components/feedback/Skeleton';
import { Checkbox } from '../../../design-system/components/forms/Checkbox';
import { Input } from '../../../design-system/components/forms/Input';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { formatearMarca } from '../../../dominio/fechas';
import { puedeVerRuta } from '../../../navegacion/menu';
import { ACCIONES } from '../../../permisos';
import { permisosDe, puede, useSesion } from '../../../sesion/store';
import { MODULOS } from '../../lista';
import {
  descargarArchivo,
  useCliente,
  useContactos,
  useDesactivarCliente,
  useEliminarContacto,
  useGuardarContacto,
} from '../api';
import { FormularioCliente } from '../FormularioCliente';
import { cuerpoContacto, emailPlausible, enlacesDeCliente, valoresContacto, type ValoresContacto } from '../logica';
import { textosClientes } from '../textos';
import type { Contacto } from '../tipos';

const t = textosClientes;

export default function ClienteFicha() {
  const { id = '' } = useParams();
  const navegar = useNavigate();
  const identidad = useSesion((s) => s.identidad);
  const editar = puede(identidad, ACCIONES.clienteEditar);
  const verInformes = puede(identidad, ACCIONES.informeVer);
  const permisos = permisosDe(identidad);

  const cliente = useCliente(id);
  const contactos = useContactos(id);
  const desactivar = useDesactivarCliente();
  const eliminarContacto = useEliminarContacto(id);

  const [editando, setEditando] = useState(false);
  const [confirmando, setConfirmando] = useState(false);
  const [contacto, setContacto] = useState<Contacto | 'nuevo' | null>(null);
  const [quitando, setQuitando] = useState<Contacto | null>(null);
  const [errorAccion, setErrorAccion] = useState<string | null>(null);
  const [descargando, setDescargando] = useState(false);

  if (cliente.isPending) {
    return (
      <div style={{ padding: 'var(--space-9)' }}>
        <Skeleton lines={6} height={20} />
      </div>
    );
  }
  if (cliente.isError) {
    const noExiste = aErrorApi(cliente.error).status === 404;
    return (
      <>
        <PageHeader eyebrow={t.ficha.eyebrow} title={t.ficha.errorTitulo} breadcrumbs={[{ label: t.ficha.migas, onClick: () => navegar('/clientes') }]} />
        {noExiste ? (
          <EmptyState icon="user-x" title={t.ficha.errorTitulo}>
            {t.ficha.noEncontrado}
          </EmptyState>
        ) : (
          <ErrorAlerta error={cliente.error} titulo={t.ficha.errorTitulo} />
        )}
      </>
    );
  }

  const c = cliente.data;
  const enlaces = enlacesDeCliente(c.id, (base) => MODULOS.some((m) => m.ruta === base) && puedeVerRuta(permisos, base));
  const marcas = [
    c.agenteRetencion && t.marcas.agenteRetencion,
    c.enGestionCobro && t.marcas.enGestionCobro,
    c.recordatoriosPausados && t.marcas.recordatoriosPausados,
  ].filter(Boolean) as string[];

  async function bajarEstadoCuenta() {
    setDescargando(true);
    setErrorAccion(null);
    try {
      await descargarArchivo(`/clientes/${c.id}/estado-de-cuenta.pdf`, `estado-de-cuenta-${c.numero}.pdf`);
    } catch (e) {
      setErrorAccion(aErrorApi(e).detail ?? t.ficha.errorDescarga);
    } finally {
      setDescargando(false);
    }
  }

  return (
    <>
      <PageHeader
        eyebrow={`${t.ficha.eyebrow} nº ${c.numero}`}
        title={c.nombre}
        breadcrumbs={[{ label: t.ficha.migas, onClick: () => navegar('/clientes') }, { label: c.nombre }]}
      >
        {verInformes && (
          <Button variant="secondary" icon="download" disabled={descargando} onClick={() => void bajarEstadoCuenta()}>
            {descargando ? t.ficha.descargando : t.ficha.estadoCuenta}
          </Button>
        )}
        {editar && c.activo && (
          <Button variant="ghost" onClick={() => setConfirmando(true)}>
            {t.ficha.desactivar}
          </Button>
        )}
        {editar && (
          <Button icon="pencil" onClick={() => setEditando(true)}>
            {t.ficha.editar}
          </Button>
        )}
      </PageHeader>

      {!c.activo && (
        <Alert tone="warning" style={{ marginBottom: 16 }}>
          {t.ficha.inactivoAviso}
        </Alert>
      )}
      {errorAccion && (
        <Alert tone="danger" style={{ marginBottom: 16 }}>
          {errorAccion}
        </Alert>
      )}

      <div style={{ display: 'grid', gap: 24 }}>
        <Card title={t.ficha.datos}>
          <KeyValueList
            columns={3}
            items={[
              { label: t.ficha.campos.tipo, value: t.tipos[c.tipo] },
              { label: t.ficha.campos.documento, value: c.documento, mono: true },
              { label: t.ficha.campos.estado, value: c.activo ? <Badge status="activo" /> : <Badge tone="neutral">{t.estados.inactivo}</Badge> },
              { label: t.ficha.campos.email, value: c.email },
              { label: t.ficha.campos.telefono, value: c.telefono, mono: true },
              { label: t.ficha.campos.alta, value: formatearMarca(c.createdAt), mono: true },
              { label: t.ficha.campos.direccion, value: c.direccion, span: true },
              {
                label: t.ficha.campos.marcas,
                value: marcas.length ? (
                  <span style={{ display: 'inline-flex', gap: 6, flexWrap: 'wrap' }}>
                    {marcas.map((m) => (
                      <Badge key={m} tone="info">
                        {m}
                      </Badge>
                    ))}
                  </span>
                ) : (
                  t.ficha.campos.sinMarcas
                ),
                span: true,
              },
              { label: t.ficha.campos.notas, value: c.notas, span: true },
            ]}
          />
        </Card>

        <Card
          title={t.ficha.contactos}
          padded={false}
          actions={
            editar ? (
              <Button variant="secondary" size="sm" icon="plus" onClick={() => setContacto('nuevo')}>
                {t.contacto.nuevo}
              </Button>
            ) : undefined
          }
        >
          {contactos.isPending ? (
            <div style={{ padding: 'var(--space-9)' }}>
              <Skeleton lines={3} height={20} />
            </div>
          ) : contactos.isError ? (
            <div style={{ padding: 'var(--space-9)' }}>
              <ErrorAlerta error={contactos.error} titulo={t.ficha.errorTitulo} />
            </div>
          ) : contactos.data.length === 0 ? (
            <EmptyState icon="contact" title={t.ficha.contactoVacioTitulo}>
              {t.ficha.contactoVacioCuerpo}
            </EmptyState>
          ) : (
            <DataTable
              rows={contactos.data}
              columns={[
                {
                  key: 'nombre',
                  label: t.contacto.columnas.nombre,
                  render: (f: Contacto) => (
                    <span style={{ display: 'inline-flex', gap: 8, alignItems: 'center' }}>
                      <strong style={{ color: 'var(--text-strong)', fontWeight: 600 }}>{f.nombre}</strong>
                      {f.esPrincipal && <Badge tone="accent">{t.contacto.principalBadge}</Badge>}
                    </span>
                  ),
                },
                { key: 'relacion', label: t.contacto.columnas.relacion, render: (f: Contacto) => f.relacion ?? '—' },
                { key: 'email', label: t.contacto.columnas.email, render: (f: Contacto) => f.email ?? '—' },
                { key: 'telefono', label: t.contacto.columnas.telefono, render: (f: Contacto) => <span className="mono">{f.telefono ?? '—'}</span> },
                {
                  key: 'acciones',
                  label: '',
                  align: 'right',
                  width: 96,
                  render: (f: Contacto) =>
                    editar ? (
                      <span style={{ display: 'inline-flex', gap: 4 }}>
                        <IconButton size="sm" icon="pencil" label={t.contacto.editar} onClick={() => setContacto(f)} />
                        <IconButton size="sm" icon="trash-2" label={t.contacto.eliminar} onClick={() => setQuitando(f)} />
                      </span>
                    ) : null,
                },
              ]}
            />
          )}
        </Card>

        {enlaces.length > 0 && (
          <Card title={t.ficha.enlaces}>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
              {enlaces.map((e) => (
                <Link key={e.clave} to={e.destino} className="ar-btn ar-btn--secondary ar-btn--md">
                  {t.ficha.enlace[e.clave]}
                </Link>
              ))}
            </div>
          </Card>
        )}
      </div>

      {editar && <FormularioCliente open={editando} ficha={c} onClose={() => setEditando(false)} onGuardado={() => setEditando(false)} />}

      {editar && (
        <FormularioContacto
          clienteId={c.id}
          contacto={contacto}
          onClose={() => setContacto(null)}
        />
      )}

      <Dialog
        open={confirmando}
        title={t.ficha.desactivarTitulo}
        onClose={() => setConfirmando(false)}
        footer={
          <>
            <Button variant="ghost" onClick={() => setConfirmando(false)}>
              {t.formulario.cancelar}
            </Button>
            <Button
              variant="danger"
              disabled={desactivar.isPending}
              onClick={async () => {
                try {
                  await desactivar.mutateAsync(c.id);
                  setConfirmando(false);
                } catch (e) {
                  setConfirmando(false);
                  setErrorAccion(aErrorApi(e).detail ?? t.formulario.errorGeneral);
                }
              }}
            >
              {t.ficha.desactivarConfirmar}
            </Button>
          </>
        }
      >
        <p style={{ margin: 0, color: 'var(--text-muted)' }}>{t.ficha.desactivarCuerpo}</p>
      </Dialog>

      <Dialog
        open={quitando !== null}
        title={t.contacto.eliminarTitulo}
        onClose={() => setQuitando(null)}
        footer={
          <>
            <Button variant="ghost" onClick={() => setQuitando(null)}>
              {t.formulario.cancelar}
            </Button>
            <Button
              variant="danger"
              disabled={eliminarContacto.isPending}
              onClick={async () => {
                const objetivo = quitando;
                setQuitando(null);
                if (!objetivo) return;
                try {
                  await eliminarContacto.mutateAsync(objetivo.id);
                } catch (e) {
                  setErrorAccion(aErrorApi(e).detail ?? t.formulario.errorGeneral);
                }
              }}
            >
              {t.contacto.eliminarConfirmar}
            </Button>
          </>
        }
      >
        <p style={{ margin: 0, color: 'var(--text-muted)' }}>{t.contacto.eliminarCuerpo}</p>
      </Dialog>
    </>
  );
}

function FormularioContacto({ clienteId, contacto, onClose }: { clienteId: string; contacto: Contacto | 'nuevo' | null; onClose: () => void }) {
  const f = t.contacto;
  const guardar = useGuardarContacto(clienteId);
  const existente = contacto !== null && contacto !== 'nuevo' ? contacto : undefined;
  const [general, setGeneral] = useState<string | null>(null);
  const { register, handleSubmit, reset, setError, formState } = useForm<ValoresContacto>({ defaultValues: valoresContacto(existente) });
  const { errors, isSubmitting } = formState;

  useEffect(() => {
    if (contacto === null) return;
    reset(valoresContacto(existente));
    setGeneral(null);
  }, [contacto, existente, reset]);

  async function enviar(v: ValoresContacto) {
    setGeneral(null);
    try {
      await guardar.mutateAsync({ ...(existente ? { id: existente.id } : {}), cuerpo: cuerpoContacto(v) });
      onClose();
    } catch (e) {
      setGeneral(aplicarErroresApi(e, setError, ['nombre', 'relacion', 'email', 'telefono'], t.formulario.errorGeneral));
    }
  }

  return (
    <Drawer
      open={contacto !== null}
      onClose={isSubmitting ? undefined : onClose}
      title={existente ? f.editarTitulo : f.nuevoTitulo}
      footer={
        <>
          <Button variant="ghost" onClick={onClose} disabled={isSubmitting}>
            {t.formulario.cancelar}
          </Button>
          <Button type="submit" form="formulario-contacto" disabled={isSubmitting}>
            {isSubmitting ? t.formulario.guardando : t.formulario.guardar}
          </Button>
        </>
      }
    >
      <form id="formulario-contacto" onSubmit={handleSubmit(enviar)} noValidate style={{ display: 'grid', gap: 'var(--space-7)' }}>
        {general && (
          <Alert tone="danger" title={t.formulario.errorGeneral}>
            {general}
          </Alert>
        )}
        <Input
          label={f.nombre}
          required
          autoComplete="off"
          error={errors.nombre?.message}
          {...register('nombre', { validate: (v) => v.trim() !== '' || t.formulario.errores.obligatorio, maxLength: { value: 200, message: t.formulario.errores.largo } })}
        />
        <Input label={f.relacion} hint={f.relacionPista} autoComplete="off" error={errors.relacion?.message} {...register('relacion', { maxLength: { value: 120, message: t.formulario.errores.largo } })} />
        <Input
          label={f.email}
          type="email"
          autoComplete="off"
          error={errors.email?.message}
          {...register('email', { validate: (v) => emailPlausible(v) || t.formulario.errores.email })}
        />
        <Input label={f.telefono} type="tel" autoComplete="off" error={errors.telefono?.message} {...register('telefono', { maxLength: { value: 40, message: t.formulario.errores.largo } })} />
        <Checkbox label={f.principal} {...register('esPrincipal')} />
      </form>
    </Drawer>
  );
}
