import { useState, type JSX } from 'react';
import { useForm } from 'react-hook-form';
import { aErrorApi } from '../../../api/errores';
import { aplicarErroresApi } from '../../../api/formularios';
import { ConfirmarAccion } from '../../../componentes/ConfirmarAccion';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Badge } from '../../../design-system/components/core/Badge';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { DataTable } from '../../../design-system/components/data/DataTable';
import { KeyValueList } from '../../../design-system/components/data/KeyValueList';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { Dialog } from '../../../design-system/components/feedback/Dialog';
import { Drawer } from '../../../design-system/components/feedback/Drawer';
import { EmptyState } from '../../../design-system/components/feedback/EmptyState';
import { Skeleton } from '../../../design-system/components/feedback/Skeleton';
import { Input } from '../../../design-system/components/forms/Input';
import { Select } from '../../../design-system/components/forms/Select';
import { Switch } from '../../../design-system/components/forms/Switch';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { textos } from '../../../i18n/textos';
import { useSesion } from '../../../sesion/store';
import {
  useCambiarRol,
  useCrearUsuario,
  useFijarActivo,
  useFijarPermiso,
  usePermisosUsuario,
  useRestablecerPassword,
  useUsuarios,
  type UsuarioResumen,
} from '../api';
import { textosUsuarios as t } from '../textos';
import {
  CONTRASENA_MAXIMA,
  CONTRASENA_MINIMA,
  ROLES,
  cuerpoAlta,
  emailValido,
  errorContrasena,
  gruposDelTecho,
  permisoBloqueado,
  recortadas,
  type ValoresAlta,
} from '../usuarios';

const OPCIONES_ROL = ROLES.map((r) => ({ value: r, label: t.roles[r] ?? r }));

function EstadoBadge({ activo }: { activo: boolean }): JSX.Element {
  return (
    <Badge tone={activo ? 'success' : 'neutral'} dot>
      {activo ? t.activo : t.inactivo}
    </Badge>
  );
}

/** Usuarios y permisos (spec §2, acción 1). Solo administrador: `configuracion.editar` en la ruta y en cada endpoint. */
export default function Usuarios(): JSX.Element {
  const lista = useUsuarios();
  const [alta, setAlta] = useState(false);
  const [seleccionado, setSeleccionado] = useState<string | null>(null);
  const filas = lista.data ?? [];
  const actual = filas.find((u) => u.id === seleccionado) ?? null;

  return (
    <>
      <PageHeader
        eyebrow={textos.navegacion.grupos.control}
        title={t.titulo}
        description={t.descripcion}
        breadcrumbs={[{ label: textos.navegacion.grupos.control }, { label: t.titulo }]}
      >
        <Button icon="plus" onClick={() => setAlta(true)}>
          {t.nuevo}
        </Button>
      </PageHeader>

      {lista.isError && <ErrorAlerta error={lista.error} titulo={t.errorLista} />}
      <Card padded={false}>
        {lista.isPending ? (
          <div style={{ padding: 'var(--space-9)' }} aria-label={t.cargando}>
            <Skeleton lines={5} height={20} />
          </div>
        ) : filas.length === 0 && !lista.isError ? (
          <EmptyState icon="user-cog" title={t.vacio.titulo}>
            {t.vacio.cuerpo}
          </EmptyState>
        ) : (
          <DataTable
            rows={filas}
            selectedKey={seleccionado}
            onRowClick={(u: UsuarioResumen) => setSeleccionado(u.id)}
            columns={[
              { key: 'nombreAcceso', label: t.columnas.usuario, render: (u: UsuarioResumen) => <span className="mono">{u.nombreAcceso}</span> },
              { key: 'nombre', label: t.columnas.nombre },
              { key: 'email', label: t.columnas.email, render: (u: UsuarioResumen) => u.email ?? '—' },
              { key: 'rol', label: t.columnas.rol, render: (u: UsuarioResumen) => <Badge tone={u.rol === 'admin' ? 'accent' : 'neutral'}>{t.roles[u.rol] ?? u.rol}</Badge> },
              { key: 'activo', label: t.columnas.estado, render: (u: UsuarioResumen) => <EstadoBadge activo={u.activo} /> },
            ]}
          />
        )}
      </Card>

      {alta && <DrawerAlta onCerrar={() => setAlta(false)} />}
      {actual && <DrawerDetalle usuario={actual} onCerrar={() => setSeleccionado(null)} />}
    </>
  );
}

function DrawerAlta({ onCerrar }: { onCerrar: () => void }): JSX.Element {
  const crear = useCrearUsuario();
  const [general, setGeneral] = useState<string | null>(null);
  const [creado, setCreado] = useState<{ comprobada: boolean } | null>(null);
  const { register, handleSubmit, setError, setValue, watch, formState } = useForm<ValoresAlta>({
    defaultValues: { nombreAcceso: '', nombre: '', email: '', contrasena: '', rol: 'empleado' },
  });
  const { errors, isSubmitting } = formState;
  const e = t.alta.errores;

  async function enviar(valores: ValoresAlta) {
    setGeneral(null);
    try {
      const respuesta = await crear.mutateAsync(cuerpoAlta(valores));
      setCreado({ comprobada: respuesta.contrasenaComprobada });
    } catch (fallo) {
      setGeneral(aplicarErroresApi(fallo, setError, ['nombreAcceso', 'nombre', 'email', 'contrasena', 'rol'], t.alta.errorTitulo));
    }
  }

  return (
    <Drawer
      title={t.alta.titulo}
      description={t.alta.descripcion}
      onClose={isSubmitting ? undefined : onCerrar}
      footer={
        creado ? (
          <Button onClick={onCerrar}>{t.detalle.cerrar}</Button>
        ) : (
          <>
            <Button variant="ghost" onClick={onCerrar} disabled={isSubmitting}>
              {t.alta.cancelar}
            </Button>
            <Button type="submit" form="form-alta-usuario" disabled={isSubmitting}>
              {isSubmitting ? t.alta.guardando : t.alta.guardar}
            </Button>
          </>
        )
      }
    >
      {creado ? (
        <Alert tone={creado.comprobada ? 'success' : 'warning'} title={t.alta.creado}>
          {t.alta.creadoCuerpo} {creado.comprobada ? '' : t.alta.creadoNoComprobada}
        </Alert>
      ) : (
        <form id="form-alta-usuario" onSubmit={handleSubmit(enviar)} noValidate style={{ display: 'grid', gap: 'var(--space-7)' }}>
          <Input
            label={t.alta.nombreAcceso}
            required
            autoComplete="off"
            error={errors.nombreAcceso?.message}
            {...register('nombreAcceso', { required: e.obligatorio, maxLength: { value: 64, message: e.largoAcceso }, validate: (v) => v.trim() !== '' || e.obligatorio })}
          />
          <Input
            label={t.alta.nombre}
            required
            error={errors.nombre?.message}
            {...register('nombre', { required: e.obligatorio, maxLength: { value: 200, message: e.largoNombre }, validate: (v) => v.trim() !== '' || e.obligatorio })}
          />
          <Input
            label={t.alta.email}
            type="email"
            hint={t.alta.emailHint}
            error={errors.email?.message}
            {...register('email', { validate: (v) => emailValido(v) || e.email })}
          />
          <Input
            label={t.alta.contrasena}
            type="password"
            required
            autoComplete="new-password"
            hint={t.alta.contrasenaHint}
            error={errors.contrasena?.message}
            {...register('contrasena', {
              required: e.obligatorio,
              validate: (v) => {
                const f = errorContrasena(v);
                return f === null || (f === 'corta' ? e.corta : e.larga);
              },
            })}
          />
          <Select
            label={t.alta.rol}
            value={watch('rol')}
            options={OPCIONES_ROL}
            onChange={(ev) => setValue('rol', ev.target.value)}
          />
          {general && (
            <Alert tone="danger" title={t.alta.errorTitulo}>
              {general}
            </Alert>
          )}
        </form>
      )}
    </Drawer>
  );
}

function tituloDeCambio(error: unknown): string {
  return aErrorApi(error).status === 409 ? t.detalle.cambioConflicto : t.detalle.cambioError;
}

function DrawerDetalle({ usuario, onCerrar }: { usuario: UsuarioResumen; onCerrar: () => void }): JSX.Element {
  const yo = useSesion((s) => s.identidad);
  const permisos = usePermisosUsuario(usuario.id);
  const cambiarRol = useCambiarRol();
  const fijarPermiso = useFijarPermiso();
  const fijarActivo = useFijarActivo();
  const [rolElegido, setRolElegido] = useState(usuario.rol);
  const [rolHecho, setRolHecho] = useState(false);
  const [confirmarActivo, setConfirmarActivo] = useState(false);
  const [password, setPassword] = useState(false);

  const esYo = yo?.id === usuario.id;
  const efectivos = permisos.data?.permisos ?? [];
  const rolReal = permisos.data?.rol ?? usuario.rol;
  const techo = permisos.data?.techo ?? [];
  const grupos = gruposDelTecho(techo);
  const nRecortadas = permisos.data ? recortadas(techo, efectivos) : 0;

  return (
    <Drawer title={usuario.nombre} description={<span className="mono">{usuario.nombreAcceso}</span>} onClose={onCerrar} width={600}
      footer={<Button variant="ghost" onClick={onCerrar}>{t.detalle.cerrar}</Button>}
    >
      <div style={{ display: 'grid', gap: 'var(--space-9)' }}>
        <KeyValueList
          columns={2}
          items={[
            { label: t.detalle.usuario, value: usuario.nombreAcceso, mono: true },
            { label: t.detalle.estado, value: <EstadoBadge activo={usuario.activo} /> },
            { label: t.detalle.nombre, value: usuario.nombre },
            { label: t.detalle.email, value: usuario.email ?? t.detalle.sinEmail },
          ]}
        />

        <section style={{ display: 'grid', gap: 'var(--space-6)' }}>
          <h3 className="ar-card__title">{t.detalle.rol}</h3>
          <div style={{ display: 'flex', gap: 'var(--space-6)', alignItems: 'flex-end' }}>
            <Select
              label={t.detalle.rol}
              value={rolElegido}
              options={OPCIONES_ROL}
              hint={t.detalle.rolHint}
              onChange={(ev) => {
                setRolElegido(ev.target.value);
                setRolHecho(false);
              }}
              style={{ flex: 1 }}
            />
            <Button
              variant="secondary"
              disabled={rolElegido === usuario.rol || cambiarRol.isPending}
              onClick={() => cambiarRol.mutate({ id: usuario.id, rol: rolElegido }, { onSuccess: () => setRolHecho(true) })}
            >
              {t.detalle.cambiarRol}
            </Button>
          </div>
          {cambiarRol.isError && <ErrorAlerta error={cambiarRol.error} titulo={tituloDeCambio(cambiarRol.error)} />}
          {rolHecho && <Alert tone="success" title={t.detalle.rolCambiado} />}
        </section>

        <section style={{ display: 'grid', gap: 'var(--space-6)' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <h3 className="ar-card__title">{t.detalle.permisos}</h3>
            {permisos.data && (
              <Badge tone={nRecortadas > 0 ? 'warning' : 'neutral'}>
                {nRecortadas > 0 ? t.detalle.permisosRecortados.replace('{n}', String(nRecortadas)) : t.detalle.permisosSinRecortes}
              </Badge>
            )}
          </div>
          <p style={{ margin: 0, color: 'var(--text-muted)' }}>{t.detalle.permisosDescripcion}</p>
          {permisos.isPending && <Skeleton lines={4} height={18} />}
          {permisos.isError && <ErrorAlerta error={permisos.error} titulo={t.detalle.permisosError} />}
          {fijarPermiso.isError && <ErrorAlerta error={fijarPermiso.error} titulo={tituloDeCambio(fijarPermiso.error)} />}
          {permisos.data &&
            grupos.map((g) => (
              <div key={g.clave} style={{ display: 'grid', gap: 'var(--space-4)' }}>
                <div className="ar-overline">{t.grupos[g.clave] ?? g.clave}</div>
                {g.acciones.map((accion) => {
                  const bloqueado = permisoBloqueado(rolReal, accion);
                  return (
                    <div key={accion} style={{ display: 'grid', gap: 2 }}>
                      <Switch
                        checked={efectivos.includes(accion)}
                        disabled={bloqueado || fijarPermiso.isPending}
                        label={t.acciones[accion] ?? accion}
                        onChange={(permitido) => fijarPermiso.mutate({ id: usuario.id, accion, permitido })}
                      />
                      <div className="mono" style={{ color: 'var(--text-subtle)', font: 'var(--type-caption)', paddingLeft: 56 }}>
                        {accion}
                        {bloqueado ? ` · ${t.detalle.bloqueado}` : ''}
                      </div>
                    </div>
                  );
                })}
              </div>
            ))}
        </section>

        <section style={{ display: 'grid', gap: 'var(--space-6)' }}>
          <h3 className="ar-card__title">{t.detalle.acciones}</h3>
          {fijarActivo.isError && <ErrorAlerta error={fijarActivo.error} titulo={tituloDeCambio(fijarActivo.error)} />}
          <div style={{ display: 'flex', gap: 'var(--space-6)', flexWrap: 'wrap' }}>
            <Button variant="secondary" icon="key-round" onClick={() => setPassword(true)}>
              {t.detalle.restablecer}
            </Button>
            {usuario.activo ? (
              <Button variant="danger" disabled={esYo} onClick={() => setConfirmarActivo(true)}>
                {t.detalle.desactivar}
              </Button>
            ) : (
              <Button variant="secondary" onClick={() => setConfirmarActivo(true)}>
                {t.detalle.activar}
              </Button>
            )}
          </div>
          {esYo && usuario.activo && <p style={{ margin: 0, color: 'var(--text-muted)' }}>{t.detalle.propioUsuario}</p>}
        </section>
      </div>

      <ConfirmarAccion
        open={confirmarActivo}
        accion={usuario.activo ? t.detalle.desactivar : t.detalle.activar}
        peligrosa={usuario.activo}
        consecuencia={usuario.activo ? t.detalle.desactivarConsecuencia : t.detalle.activarConsecuencia}
        onCerrar={() => setConfirmarActivo(false)}
        onConfirmar={async () => {
          await fijarActivo.mutateAsync({ id: usuario.id, activo: !usuario.activo });
        }}
      />
      {password && <DialogPassword usuario={usuario} onCerrar={() => setPassword(false)} />}
    </Drawer>
  );
}

interface ValoresPassword {
  contrasena: string;
  repetir: string;
}

function DialogPassword({ usuario, onCerrar }: { usuario: UsuarioResumen; onCerrar: () => void }): JSX.Element {
  const restablecer = useRestablecerPassword();
  const [general, setGeneral] = useState<string | null>(null);
  const [hecho, setHecho] = useState<{ comprobada: boolean } | null>(null);
  const { register, handleSubmit, setError, getValues, formState } = useForm<ValoresPassword>({ defaultValues: { contrasena: '', repetir: '' } });
  const { errors, isSubmitting } = formState;
  const p = t.password;

  async function enviar(v: ValoresPassword) {
    setGeneral(null);
    try {
      const r = await restablecer.mutateAsync({ id: usuario.id, contrasena: v.contrasena });
      setHecho({ comprobada: r.contrasenaComprobada });
    } catch (fallo) {
      setGeneral(aplicarErroresApi(fallo, setError, ['contrasena'], p.errorTitulo));
    }
  }

  return (
    <Dialog
      title={p.titulo}
      description={`${usuario.nombre} · ${usuario.nombreAcceso}`}
      onClose={isSubmitting ? undefined : onCerrar}
      footer={
        hecho ? (
          <Button onClick={onCerrar}>{t.detalle.cerrar}</Button>
        ) : (
          <>
            <Button variant="ghost" onClick={onCerrar} disabled={isSubmitting}>
              {p.cancelar}
            </Button>
            <Button type="submit" form="form-restablecer" disabled={isSubmitting}>
              {isSubmitting ? p.guardando : p.guardar}
            </Button>
          </>
        )
      }
    >
      {hecho ? (
        <Alert tone={hecho.comprobada ? 'success' : 'warning'} title={p.hecho}>
          {p.hechoCuerpo} {hecho.comprobada ? '' : p.noComprobada}
        </Alert>
      ) : (
        <form id="form-restablecer" onSubmit={handleSubmit(enviar)} noValidate style={{ display: 'grid', gap: 'var(--space-7)' }}>
          <p style={{ margin: 0, color: 'var(--text-muted)' }}>{p.descripcion}</p>
          <Input
            label={p.nueva}
            type="password"
            autoComplete="new-password"
            hint={p.hint}
            error={errors.contrasena?.message}
            {...register('contrasena', {
              required: t.alta.errores.obligatorio,
              minLength: { value: CONTRASENA_MINIMA, message: t.alta.errores.corta },
              maxLength: { value: CONTRASENA_MAXIMA, message: t.alta.errores.larga },
            })}
          />
          <Input
            label={p.repetir}
            type="password"
            autoComplete="new-password"
            error={errors.repetir?.message}
            {...register('repetir', { validate: (v) => v === getValues('contrasena') || p.noCoincide })}
          />
          {general && (
            <Alert tone="danger" title={p.errorTitulo}>
              {general}
            </Alert>
          )}
        </form>
      )}
    </Dialog>
  );
}
