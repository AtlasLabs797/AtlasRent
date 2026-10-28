import { useState, type JSX } from 'react';
import { useForm } from 'react-hook-form';
import { aErrorApi } from '../../../api/errores';
import { aplicarErroresApi } from '../../../api/formularios';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { DataTable } from '../../../design-system/components/data/DataTable';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { Drawer } from '../../../design-system/components/feedback/Drawer';
import { Skeleton } from '../../../design-system/components/feedback/Skeleton';
import { Input } from '../../../design-system/components/forms/Input';
import { Pagination } from '../../../design-system/components/navigation/Pagination';
import { formatearFecha, formatearMarca } from '../../../dominio/fechas';
import { useCargarTipoCambio, useTipoCambioActual, useTiposCambio, type TipoCambio } from '../api';
import { cuerpoTipoCambio, formatearTasa } from '../config';
import { textosConfiguracion as textos } from '../textos';

const t = textos.cambio;
const TAMANO = 15;

/** Histórico del tipo de cambio y carga manual (§3.12). El valor viaja como string con hasta 6 decimales. */
export function Cambio({ puedeCargar }: { puedeCargar: boolean }): JSX.Element {
  const [pagina, setPagina] = useState(1);
  const historico = useTiposCambio(pagina, TAMANO);
  const actual = useTipoCambioActual();
  const [abierto, setAbierto] = useState(false);
  const [cargado, setCargado] = useState(false);
  const sinTasaHoy = actual.isError && aErrorApi(actual.error).status === 409;

  return (
    <div style={{ display: 'grid', gap: 'var(--space-7)' }}>
      {cargado && <Alert tone="success" title={t.cargado} />}
      {sinTasaHoy && <Alert tone="warning">{t.vigenteVacio}</Alert>}
      {actual.isError && !sinTasaHoy && <ErrorAlerta error={actual.error} titulo={t.vigenteError} />}
      <Card
        title={t.titulo}
        padded={false}
        actions={
          puedeCargar ? (
            <Button size="sm" icon="plus" variant="secondary" onClick={() => setAbierto(true)}>
              {t.cargar}
            </Button>
          ) : undefined
        }
      >
        <div style={{ padding: '0 var(--space-9) var(--space-6)', display: 'grid', gap: 'var(--space-4)' }}>
          <p style={{ margin: 0, color: 'var(--text-muted)' }}>{t.descripcion}</p>
          {actual.data && (
            <p style={{ margin: 0 }}>
              {t.vigente}: <strong className="mono">{formatearTasa(actual.data.valor)} DOP</strong> por 1 USD · <span className="mono">{formatearFecha(actual.data.fecha)}</span>
            </p>
          )}
        </div>
        {historico.isError && (
          <div style={{ padding: '0 var(--space-9) var(--space-6)' }}>
            <ErrorAlerta error={historico.error} titulo={t.error} />
          </div>
        )}
        {historico.isPending ? (
          <div style={{ padding: 'var(--space-9)' }}>
            <Skeleton lines={4} height={20} />
          </div>
        ) : (
          <>
            <DataTable
              rows={historico.data?.data ?? []}
              empty={t.vacio}
              columns={[
                { key: 'fecha', label: t.columnas.fecha, render: (r: TipoCambio) => <span className="mono">{formatearFecha(r.fecha)}</span> },
                { key: 'moneda', label: t.columnas.moneda },
                { key: 'valor', label: t.columnas.valor, align: 'right', render: (r: TipoCambio) => <span className="mono">{formatearTasa(r.valor)}</span> },
                { key: 'fuente', label: t.columnas.fuente, render: (r: TipoCambio) => t.fuentes[r.fuente] ?? r.fuente },
                { key: 'registradoAt', label: t.columnas.registrado, align: 'right', render: (r: TipoCambio) => <span className="mono">{formatearMarca(r.registradoAt)}</span> },
              ]}
            />
            <div style={{ padding: 'var(--space-6) var(--space-9)' }}>
              <Pagination page={pagina} pageSize={TAMANO} total={historico.data?.total ?? 0} onChange={setPagina} />
            </div>
          </>
        )}
      </Card>
      {abierto && <DrawerCarga onCerrar={() => setAbierto(false)} alCargar={() => setCargado(true)} />}
    </div>
  );
}

interface Valores {
  fecha: string;
  valor: string;
}

function DrawerCarga({ onCerrar, alCargar }: { onCerrar: () => void; alCargar: () => void }): JSX.Element {
  const cargar = useCargarTipoCambio();
  const [general, setGeneral] = useState<string | null>(null);
  const { register, handleSubmit, setError, formState } = useForm<Valores>({ defaultValues: { fecha: '', valor: '' } });
  const { errors, isSubmitting } = formState;

  async function enviar(v: Valores) {
    setGeneral(null);
    let cuerpo;
    try {
      cuerpo = cuerpoTipoCambio(v.fecha, v.valor);
    } catch {
      setError('valor', { message: t.valorInvalido });
      return;
    }
    try {
      await cargar.mutateAsync(cuerpo);
      alCargar();
      onCerrar();
    } catch (e) {
      setGeneral(aplicarErroresApi(e, setError, ['fecha', 'valor'], t.errorCargar));
    }
  }

  return (
    <Drawer
      title={t.cargarTitulo}
      description={t.cargarDescripcion}
      onClose={isSubmitting ? undefined : onCerrar}
      footer={
        <>
          <Button variant="ghost" onClick={onCerrar} disabled={isSubmitting}>
            {textos.comun.cancelar}
          </Button>
          <Button type="submit" form="form-cambio" disabled={isSubmitting}>
            {isSubmitting ? textos.comun.guardando : textos.comun.guardar}
          </Button>
        </>
      }
    >
      <form id="form-cambio" onSubmit={handleSubmit(enviar)} noValidate style={{ display: 'grid', gap: 'var(--space-7)' }}>
        <Input label={t.fecha} type="date" hint={t.fechaHint} error={errors.fecha?.message} {...register('fecha')} />
        <Input label={t.moneda} value="DOP" disabled readOnly />
        <Input label={t.valor} hint={t.valorHint} inputMode="decimal" className="mono" suffix="DOP" autoComplete="off" error={errors.valor?.message} {...register('valor')} />
        {general && (
          <Alert tone="danger" title={t.errorCargar}>
            {general}
          </Alert>
        )}
      </form>
    </Drawer>
  );
}
