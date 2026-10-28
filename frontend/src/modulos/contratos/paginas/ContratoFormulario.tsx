import { useEffect, useMemo, useRef, useState } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate, useParams, useSearchParams } from 'react-router';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { Skeleton } from '../../../design-system/components/feedback/Skeleton';
import { Combobox } from '../../../design-system/components/forms/Combobox';
import { DatePicker } from '../../../design-system/components/forms/DatePicker';
import { Input } from '../../../design-system/components/forms/Input';
import { Select } from '../../../design-system/components/forms/Select';
import { Textarea } from '../../../design-system/components/forms/Textarea';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { SegmentedControl } from '../../../design-system/components/navigation/SegmentedControl';
import { textos } from '../../../i18n/textos';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { useTransferencia } from '../../cotizaciones/api';
import { useClientes, useInmuebles } from '../../operacion/api';
import { MONEDAS, REGLAS_RENOVACION, type ReglaRenovacionApi } from '../../operacion/etiquetas';
import { SelectorUnidad } from '../../operacion/Selectores';
import type { MonedaApi, UnidadConInmueble } from '../../operacion/tipos';
import { AvisoBloqueo } from '../../reservas/AvisoBloqueo';
import { useReserva } from '../../reservas/api';
import { useContrato, useGuardarContrato, usePlantillas, type ContratoDetalle } from '../api';
import { CONTRATO_VACIO, cuerpoContrato, hayErroresContrato, mesesDeRegla, validarContrato, type FormularioContrato } from '../modelo';
import { textosContratos } from '../textos';

const t = textosContratos.formulario;

function valoresDeContrato(c: ContratoDetalle): FormularioContrato {
  return {
    clienteId: c.clienteId,
    unidadId: c.unidadId,
    reservaId: null,
    facturadoraId: c.facturadoraId,
    fechaInicio: c.fechaInicio,
    duracionInicialMeses: String(c.duracionInicialMeses),
    renta: String(c.renta),
    moneda: c.moneda,
    diaPago: String(c.diaPago),
    fianza: String(c.fianza),
    reglaRenovacion: c.reglaRenovacion,
    renovacionMeses: String(c.renovacionMeses),
    plantillaContratoId: c.plantillaContratoId,
    observaciones: c.observaciones ?? '',
  };
}

/** Alta (`/contratos/nuevo`, con `reservaId` o `cotizacionId` opcionales) y edición del borrador (`/contratos/:id/editar`). */
export default function ContratoFormulario() {
  const { id } = useParams();
  const [params] = useSearchParams();
  const navegar = useNavigate();
  const identidad = useSesion((s) => s.identidad);
  const reservaId = params.get('reservaId');
  const cotizacionId = params.get('cotizacionId');

  const existente = useContrato(id);
  const reserva = useReserva(reservaId ?? undefined);
  const cotizacion = useTransferencia(cotizacionId, 'contrato');
  const clientes = useClientes();
  const inmuebles = useInmuebles();
  const plantillas = usePlantillas();
  const guardar = useGuardarContrato(id);

  const { register, handleSubmit, watch, setValue, reset } = useForm<FormularioContrato>({ defaultValues: CONTRATO_VACIO });
  const [fallo, setFallo] = useState<unknown>(null);
  const [intentado, setIntentado] = useState(false);
  const [aviso, setAviso] = useState<string | null>(null);
  const cargado = useRef(false);

  // Precarga única cuando llega el origen (contrato existente, reserva o cotización).
  useEffect(() => {
    if (cargado.current) return;
    if (id && existente.data) {
      reset(valoresDeContrato(existente.data));
      cargado.current = true;
    } else if (!id && reservaId && reserva.data) {
      const r = reserva.data;
      reset({ ...CONTRATO_VACIO, reservaId: r.id, clienteId: r.clienteId, unidadId: r.unidadId, fechaInicio: r.entradaPrevista, moneda: r.moneda });
      setAviso(t.desdeReserva);
      cargado.current = true;
    } else if (!id && cotizacionId && cotizacion.data) {
      const c = cotizacion.data;
      reset({ ...CONTRATO_VACIO, clienteId: c.clienteId, unidadId: c.unidadId, moneda: c.moneda });
      setAviso(c.prospectoId && !c.clienteId ? `${t.desdeCotizacion} ${t.cotizacionProspecto}` : t.desdeCotizacion);
      cargado.current = true;
    }
  }, [id, existente.data, reservaId, reserva.data, cotizacionId, cotizacion.data, reset]);

  const v = watch();
  const errores = validarContrato(v);
  const opcionesCliente = useMemo(() => (clientes.data?.data ?? []).map((c) => ({ value: c.id, label: c.nombre, sub: c.email ?? undefined, meta: `CLI-${String(c.numero).padStart(5, '0')}` })), [clientes.data]);
  const opcionesPlantilla = (plantillas.data?.data ?? []).filter((p) => p.activa || p.codigo === v.plantillaContratoId).map((p) => ({ value: p.codigo, label: `${p.nombre} · ${p.codigo}` }));

  function alElegirUnidad(unidadId: string | null, unidad: UnidadConInmueble | null) {
    setValue('unidadId', unidadId);
    setValue('facturadoraId', unidad?.facturadoraPorDefectoId ?? null);
    const inmueble = inmuebles.data?.data.find((i) => i.id === unidad?.inmuebleId);
    if (inmueble?.plantillaContratoPorDefecto && v.plantillaContratoId === '') setValue('plantillaContratoId', inmueble.plantillaContratoPorDefecto);
    if (inmueble?.renovacionDefectoMeses && !existente.data) {
      const regla = (Object.keys(REGLAS_RENOVACION) as ReglaRenovacionApi[]).find((r) => REGLAS_RENOVACION[r].meses === inmueble.renovacionDefectoMeses);
      setValue('reglaRenovacion', regla ?? 'Personalizada');
      setValue('renovacionMeses', String(inmueble.renovacionDefectoMeses));
    }
  }

  async function enviar(f: FormularioContrato) {
    setIntentado(true);
    setFallo(null);
    if (hayErroresContrato(validarContrato(f))) return;
    try {
      const guardado = await guardar.mutateAsync(cuerpoContrato(f));
      navegar(`/contratos/${guardado.id}`);
    } catch (e) {
      setFallo(e);
    }
  }

  const ver = (m: string | undefined) => (intentado ? m : undefined);
  const cabecera = (
    <PageHeader
      eyebrow={textos.navegacion.modulos.contratos.etiqueta}
      title={id ? t.tituloEditar : t.tituloNuevo}
      description={t.descripcion}
      breadcrumbs={[{ label: textos.navegacion.modulos.contratos.etiqueta, onClick: () => navegar('/contratos') }, { label: id ? t.tituloEditar : t.tituloNuevo }]}
    />
  );

  if (!puede(identidad, ACCIONES.contratoBorrador)) {
    return (
      <>
        {cabecera}
        <Alert tone="info">{textos.sinPermiso.descripcion}</Alert>
      </>
    );
  }
  if (id && existente.isError) return <>{cabecera}<ErrorAlerta error={existente.error} titulo={t.error} /></>;
  if (id && !existente.data) return <>{cabecera}<Skeleton height={160} /></>;
  if (id && existente.data && existente.data.estado !== 'Borrador') {
    return (
      <>
        {cabecera}
        <Alert tone="warning">{t.noEditable}</Alert>
        <div style={{ marginTop: 'var(--space-9)' }}>
          <Button variant="secondary" onClick={() => navegar(`/contratos/${id}`)}>
            {textosContratos.ficha.volver}
          </Button>
        </div>
      </>
    );
  }

  return (
    <>
      {cabecera}
      {aviso && <Alert tone="info">{aviso}</Alert>}
      {(reserva.isError || cotizacion.isError) && <Alert tone="warning">{t.noCopiado}</Alert>}
      {fallo !== null && <AvisoBloqueo error={fallo} titulo={t.error} />}

      <form onSubmit={handleSubmit(enviar)} noValidate style={{ display: 'grid', gap: 'var(--space-9)', marginTop: 'var(--space-9)' }}>
        <Card>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(260px,1fr))', gap: 'var(--space-9)' }}>
            <Combobox
              label={t.cliente}
              required
              placeholder="Buscar por nombre"
              options={opcionesCliente}
              value={v.clienteId}
              onChange={(c) => setValue('clienteId', c)}
              error={ver(errores.cliente)}
              disabled={reservaId !== null}
              emptyText="Sin resultados"
            />
            <div>
              <SelectorUnidad
                etiqueta={t.unidad}
                valor={v.unidadId}
                onChange={alElegirUnidad}
                error={ver(errores.unidad) ?? ver(errores.facturadora)}
                bloqueado={reservaId !== null}
                filtrar={(u) => u.modalidades.includes('Mensual')}
              />
              <span className="ar-hint">{v.facturadoraId ? t.facturadoraDeUnidad : t.unidadAyuda}</span>
            </div>
            <DatePicker label={t.fechaInicio} required value={v.fechaInicio} onChange={(e) => setValue('fechaInicio', e.target.value)} error={ver(errores.fechaInicio)} />
            <Input id="contrato-duracion" label={t.duracion} required inputMode="numeric" error={ver(errores.duracion)} {...register('duracionInicialMeses')} />
          </div>
        </Card>

        <Card>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(220px,1fr))', gap: 'var(--space-9)' }}>
            <div className="ar-field">
              <span className="ar-label">{t.moneda}</span>
              <SegmentedControl options={MONEDAS.map((m) => ({ value: m, label: m }))} value={v.moneda} onChange={(m) => setValue('moneda', m as MonedaApi)} />
            </div>
            <Input id="contrato-renta" label={t.renta} required inputMode="decimal" suffix={v.moneda} error={ver(errores.renta)} {...register('renta')} />
            <Input id="contrato-fianza" label={t.fianza} required inputMode="decimal" suffix={v.moneda} error={ver(errores.fianza)} {...register('fianza')} />
            <Input id="contrato-dia-pago" label={t.diaPago} required inputMode="numeric" error={ver(errores.diaPago)} {...register('diaPago')} />
            <Select
              label={t.regla}
              value={v.reglaRenovacion}
              onChange={(e) => {
                const regla = e.target.value as ReglaRenovacionApi;
                setValue('reglaRenovacion', regla);
                setValue('renovacionMeses', mesesDeRegla(regla, v.renovacionMeses));
              }}
              options={(Object.keys(REGLAS_RENOVACION) as ReglaRenovacionApi[]).map((r) => ({ value: r, label: REGLAS_RENOVACION[r].texto }))}
            />
            <Input
              id="contrato-renovacion-meses"
              label={t.reglaMeses}
              inputMode="numeric"
              disabled={v.reglaRenovacion !== 'Personalizada'}
              error={ver(errores.renovacion)}
              {...register('renovacionMeses')}
            />
            <Select label={t.plantilla} required placeholder={t.plantilla} value={v.plantillaContratoId} onChange={(e) => setValue('plantillaContratoId', e.target.value)} options={opcionesPlantilla} error={ver(errores.plantilla)} />
          </div>
        </Card>

        <Card>
          <Textarea id="contrato-observaciones" label={t.observaciones} rows={3} maxLength={1000} {...register('observaciones')} />
        </Card>

        <div style={{ display: 'flex', gap: 'var(--space-6)', justifyContent: 'flex-end' }}>
          <Button variant="ghost" type="button" onClick={() => navegar(id ? `/contratos/${id}` : '/contratos')} disabled={guardar.isPending}>
            {t.cancelar}
          </Button>
          <Button type="submit" disabled={guardar.isPending}>
            {guardar.isPending ? t.guardando : t.guardar}
          </Button>
        </div>
      </form>
    </>
  );
}
