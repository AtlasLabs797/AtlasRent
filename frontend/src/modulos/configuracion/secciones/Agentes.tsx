import { useState, type JSX } from 'react';
import { useForm } from 'react-hook-form';
import { aplicarErroresApi } from '../../../api/formularios';
import { ConfirmarAccion } from '../../../componentes/ConfirmarAccion';
import { Copiable } from '../../../componentes/Copiable';
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
import { Checkbox } from '../../../design-system/components/forms/Checkbox';
import { Input } from '../../../design-system/components/forms/Input';
import { Select } from '../../../design-system/components/forms/Select';
import { formatearMarca } from '../../../dominio/fechas';
import { useUsuarios } from '../../usuarios/api';
import { useEmitirToken, useRevocarToken, useTokensAgente, type TokenAgente } from '../api';
import { ACCIONES_AGENTE, cuerpoToken, estadoToken, type EstadoToken } from '../config';
import { textosConfiguracion as textos } from '../textos';

const t = textos.agentes;
const TONO: Record<EstadoToken, 'success' | 'neutral' | 'warning'> = { activo: 'success', revocado: 'neutral', caducado: 'warning' };

/** Tokens de agente (`agente.token`): listar, emitir (el secreto sale una sola vez) y revocar. */
export function Agentes(): JSX.Element {
  const tokens = useTokensAgente();
  const usuarios = useUsuarios();
  const revocar = useRevocarToken();
  const [emitiendo, setEmitiendo] = useState(false);
  const [secreto, setSecreto] = useState<string | null>(null);
  const [revocando, setRevocando] = useState<TokenAgente | null>(null);
  const propietario = (id: string) => usuarios.data?.find((u) => u.id === id)?.nombreAcceso ?? id;
  const filas = tokens.data ?? [];

  return (
    <>
      <Card
        title={t.titulo}
        padded={false}
        actions={
          <Button size="sm" icon="plus" variant="secondary" onClick={() => setEmitiendo(true)}>
            {t.emitir}
          </Button>
        }
      >
        <p style={{ margin: 0, padding: '0 var(--space-9) var(--space-6)', color: 'var(--text-muted)' }}>{t.descripcion}</p>
        {tokens.isError && (
          <div style={{ padding: '0 var(--space-9) var(--space-6)' }}>
            <ErrorAlerta error={tokens.error} titulo={textos.comun.errorLista} />
          </div>
        )}
        {tokens.isPending ? (
          <div style={{ padding: 'var(--space-9)' }}>
            <Skeleton lines={3} height={20} />
          </div>
        ) : filas.length === 0 && !tokens.isError ? (
          <EmptyState icon="key-round" title={t.vacioTitulo}>
            {t.vacioCuerpo}
          </EmptyState>
        ) : (
          <DataTable
            rows={filas}
            columns={[
              { key: 'nombre', label: t.columnas.nombre },
              { key: 'usuarioId', label: t.columnas.propietario, render: (r: TokenAgente) => <span className="mono">{propietario(r.usuarioId)}</span> },
              { key: 'acciones', label: t.columnas.acciones, render: (r: TokenAgente) => <span className="mono" style={{ whiteSpace: 'normal', font: 'var(--type-caption)' }}>{r.acciones.join(' · ')}</span> },
              { key: 'caduca', label: t.columnas.caduca, align: 'right', render: (r: TokenAgente) => <span className="mono">{r.caduca ? formatearMarca(r.caduca) : '—'}</span> },
              { key: 'ultimoUso', label: t.columnas.ultimoUso, align: 'right', render: (r: TokenAgente) => <span className="mono">{r.ultimoUso ? formatearMarca(r.ultimoUso) : t.nuncaUsado}</span> },
              {
                key: 'estado',
                label: t.columnas.estado,
                render: (r: TokenAgente) => {
                  const e = estadoToken(r);
                  return (
                    <Badge tone={TONO[e]} dot>
                      {t.estados[e]}
                    </Badge>
                  );
                },
              },
              {
                key: 'accion',
                label: t.columnas.accion,
                align: 'right',
                render: (r: TokenAgente) =>
                  estadoToken(r) === 'revocado' ? null : (
                    <Button size="sm" variant="ghost" onClick={() => setRevocando(r)}>
                      {t.revocar}
                    </Button>
                  ),
              },
            ]}
          />
        )}
      </Card>

      {emitiendo && (
        <DrawerEmitir
          onCerrar={() => setEmitiendo(false)}
          alEmitir={(token) => {
            setEmitiendo(false);
            setSecreto(token);
          }}
        />
      )}
      <Dialog
        open={secreto !== null}
        title={t.secreto.titulo}
        footer={<Button onClick={() => setSecreto(null)}>{t.secreto.cerrar}</Button>}
      >
        <div style={{ display: 'grid', gap: 'var(--space-7)' }}>
          <Alert tone="warning">{t.secreto.aviso}</Alert>
          {secreto && <Copiable texto={secreto} etiqueta={t.secreto.etiqueta} />}
        </div>
      </Dialog>
      <ConfirmarAccion
        open={revocando !== null}
        accion={t.revocarTitulo}
        consecuencia={t.revocarConsecuencia}
        onCerrar={() => setRevocando(null)}
        onConfirmar={async () => {
          if (revocando) await revocar.mutateAsync(revocando.id);
        }}
      />
    </>
  );
}

interface Valores {
  nombre: string;
  usuarioId: string;
  acciones: string[];
  diasValidez: string;
  limiteConsultasDia: string;
}

function DrawerEmitir({ onCerrar, alEmitir }: { onCerrar: () => void; alEmitir: (token: string) => void }): JSX.Element {
  const usuarios = useUsuarios();
  const emitir = useEmitirToken();
  const [general, setGeneral] = useState<string | null>(null);
  const n = t.nuevo;
  const { register, handleSubmit, setValue, watch, setError, formState } = useForm<Valores>({
    defaultValues: { nombre: '', usuarioId: '', acciones: ['ia.consultar'], diasValidez: '', limiteConsultasDia: '' },
  });
  const { errors, isSubmitting } = formState;
  const acciones = watch('acciones');

  async function enviar(v: Valores) {
    setGeneral(null);
    if (v.nombre.trim() === '') return setError('nombre', { message: n.nombreObligatorio });
    if (v.usuarioId === '') return setError('usuarioId', { message: n.propietarioObligatorio });
    let cuerpo;
    try {
      cuerpo = cuerpoToken(v);
    } catch (e) {
      const mensaje = e instanceof Error ? e.message : n.error;
      if (v.acciones.length === 0) setError('acciones', { message: n.accionesObligatorias });
      else setGeneral(mensaje);
      return;
    }
    try {
      const creado = await emitir.mutateAsync(cuerpo);
      if (creado.token) alEmitir(creado.token);
      else onCerrar();
    } catch (e) {
      setGeneral(aplicarErroresApi(e, setError, ['nombre', 'usuarioId', 'acciones', 'diasValidez', 'limiteConsultasDia'], n.error));
    }
  }

  return (
    <Drawer
      title={n.titulo}
      description={n.descripcion}
      onClose={isSubmitting ? undefined : onCerrar}
      footer={
        <>
          <Button variant="ghost" onClick={onCerrar} disabled={isSubmitting}>
            {textos.comun.cancelar}
          </Button>
          <Button type="submit" form="form-token" disabled={isSubmitting}>
            {isSubmitting ? n.emitiendo : n.emitir}
          </Button>
        </>
      }
    >
      <form id="form-token" onSubmit={handleSubmit(enviar)} noValidate style={{ display: 'grid', gap: 'var(--space-7)' }}>
        <Input label={n.nombre} required hint={n.nombreHint} error={errors.nombre?.message} autoComplete="off" {...register('nombre')} />
        <Select
          label={n.propietario}
          hint={n.propietarioHint}
          placeholder="—"
          error={errors.usuarioId?.message}
          value={watch('usuarioId')}
          options={(usuarios.data ?? []).filter((u) => u.activo).map((u) => ({ value: u.id, label: `${u.nombre} · ${u.nombreAcceso}` }))}
          onChange={(ev) => setValue('usuarioId', ev.target.value)}
        />
        <fieldset style={{ border: 0, padding: 0, margin: 0, display: 'grid', gap: 'var(--space-4)' }}>
          <legend className="ar-label">{n.acciones}</legend>
          {ACCIONES_AGENTE.map((a) => (
            <Checkbox
              key={a}
              checked={acciones.includes(a)}
              label={
                <span>
                  {t.acciones[a]} <span className="mono" style={{ color: 'var(--text-subtle)' }}>{a}</span>
                </span>
              }
              onChange={(ev) => setValue('acciones', ev.target.checked ? [...acciones, a] : acciones.filter((x) => x !== a), { shouldValidate: true })}
            />
          ))}
          {errors.acciones?.message && <span className="ar-hint ar-hint--error">{errors.acciones.message}</span>}
        </fieldset>
        <Input label={n.dias} hint={n.diasHint} inputMode="numeric" className="mono" error={errors.diasValidez?.message} {...register('diasValidez')} />
        <Input label={n.limite} hint={n.limiteHint} inputMode="numeric" className="mono" error={errors.limiteConsultasDia?.message} {...register('limiteConsultasDia')} />
        {general && (
          <Alert tone="danger" title={n.error}>
            {general}
          </Alert>
        )}
      </form>
    </Drawer>
  );
}
