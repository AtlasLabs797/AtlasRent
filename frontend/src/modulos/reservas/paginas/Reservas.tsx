import { useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { DataTable } from '../../../design-system/components/data/DataTable';
import { Money } from '../../../design-system/components/data/Money';
import { EmptyState } from '../../../design-system/components/feedback/EmptyState';
import { Checkbox } from '../../../design-system/components/forms/Checkbox';
import { Select } from '../../../design-system/components/forms/Select';
import { FilterBar } from '../../../design-system/components/navigation/FilterBar';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { Pagination } from '../../../design-system/components/navigation/Pagination';
import { formatearFecha } from '../../../dominio/fechas';
import { textos } from '../../../i18n/textos';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { BadgeEstado, CargandoFilas } from '../../operacion/Comunes';
import { ESTADOS_RESERVA, etiquetaModalidad, type EstadoReservaApi } from '../../operacion/etiquetas';
import { useTransferencia } from '../../cotizaciones/api';
import { TAMANO_PAGINA, useReservas, type FiltrosReservas, type ReservaResumen } from '../api';
import { type FormularioReserva, type TipoReservaApi } from '../cuerpo';
import { FormularioReservaDrawer } from '../FormularioReserva';
import { textosReservas as t } from '../textos';

export default function Reservas() {
  const navegar = useNavigate();
  const [params, setParams] = useSearchParams();
  const identidad = useSesion((s) => s.identidad);
  const [filtros, setFiltros] = useState<FiltrosReservas>({ page: 1, q: '', estado: '', tipo: '', incluirInactivas: false });
  const lista = useReservas(filtros);
  const filas = lista.data?.data ?? [];
  const hayFiltros = filtros.q !== '' || filtros.estado !== '' || filtros.tipo !== '';

  // `?nueva=1` abre el formulario; el calendario añade `unidadId` y `fecha`, y una cotización aceptada, `cotizacionId`.
  const abierto = params.get('nueva') === '1';
  const unidadId = params.get('unidadId');
  const fecha = params.get('fecha');
  const cotizacionId = params.get('cotizacionId');
  const transferencia = useTransferencia(cotizacionId, 'reserva');
  const inicial = useMemo<Partial<FormularioReserva>>(() => {
    const base: Partial<FormularioReserva> = {};
    if (unidadId) base.unidadId = unidadId;
    if (fecha) {
      base.entradaPrevista = fecha;
      base.dias = [{ fecha, horaOrientativa: '' }];
    }
    const x = transferencia.data;
    if (x) {
      base.unidadId = x.unidadId;
      base.destinatarioTipo = x.prospectoId ? 'prospecto' : 'cliente';
      base.destinatarioId = x.clienteId ?? x.prospectoId;
      base.moneda = x.moneda;
      base.importe = String(x.total);
    }
    return base;
  }, [unidadId, fecha, transferencia.data]);

  function cerrar() {
    setParams({}, { replace: true });
  }

  return (
    <>
      <PageHeader
        eyebrow={textos.navegacion.grupos.operacion}
        title={t.titulo}
        description={t.descripcion}
        breadcrumbs={[{ label: textos.navegacion.grupos.operacion }, { label: textos.navegacion.modulos.reservas.etiqueta }]}
      >
        {puede(identidad, ACCIONES.clienteEditar) && (
          <Button icon="plus" onClick={() => setParams({ nueva: '1' })}>
            {t.nueva}
          </Button>
        )}
      </PageHeader>

      {lista.isError && <ErrorAlerta error={lista.error} titulo={t.errorTitulo} />}

      <Card padded={false}>
        <FilterBar
          searchPlaceholder={t.buscar}
          search={filtros.q}
          onSearch={(q) => setFiltros({ ...filtros, q, page: 1 })}
          filters={[
            ...(filtros.estado ? [{ key: 'estado', label: t.estado, value: ESTADOS_RESERVA[filtros.estado].texto }] : []),
            ...(filtros.tipo ? [{ key: 'tipo', label: t.tipo, value: filtros.tipo === 'Corta' ? 'Corta' : 'Mensual' }] : []),
          ]}
          onRemove={(k) => setFiltros({ ...filtros, [k]: '', page: 1 })}
          onClear={() => setFiltros({ ...filtros, q: '', estado: '', tipo: '', page: 1 })}
        >
          <Select
            placeholder={t.todosEstados}
            value={filtros.estado}
            onChange={(e) => setFiltros({ ...filtros, estado: e.target.value as EstadoReservaApi | '', page: 1 })}
            options={(Object.keys(ESTADOS_RESERVA) as EstadoReservaApi[]).map((k) => ({ value: k, label: ESTADOS_RESERVA[k].texto }))}
            style={{ minWidth: 180 }}
          />
          <Select
            placeholder={t.todosTipos}
            value={filtros.tipo}
            onChange={(e) => setFiltros({ ...filtros, tipo: e.target.value as TipoReservaApi | '', page: 1 })}
            options={[
              { value: 'Mensual', label: 'Mensual' },
              { value: 'Corta', label: 'Corta' },
            ]}
            style={{ minWidth: 160 }}
          />
          <Checkbox label={t.incluirCanceladas} checked={filtros.incluirInactivas} onChange={(e) => setFiltros({ ...filtros, incluirInactivas: e.target.checked, page: 1 })} />
        </FilterBar>

        {lista.isPending ? (
          <CargandoFilas />
        ) : filas.length === 0 && !lista.isError ? (
          <EmptyState icon="calendar-check" title={hayFiltros ? t.sinResultados : t.vacioTitulo}>
            {hayFiltros ? undefined : t.vacioCuerpo}
          </EmptyState>
        ) : (
          <DataTable
            rows={filas}
            onRowClick={(r: ReservaResumen) => navegar(`/reservas/${r.id}`)}
            columns={[
              { key: 'unidad', label: t.columnas.unidad },
              { key: 'cliente', label: t.columnas.para, render: (r: ReservaResumen) => r.cliente ?? '—' },
              { key: 'tipo', label: t.columnas.tipo, render: (r: ReservaResumen) => (r.tipo === 'Corta' ? `Corta · ${etiquetaModalidad[r.modalidad]}` : 'Mensual') },
              { key: 'entrada', label: t.columnas.entrada, align: 'right', render: (r: ReservaResumen) => <span className="mono">{formatearFecha(r.entradaPrevista)}</span> },
              { key: 'dias', label: t.columnas.dias, align: 'right', render: (r: ReservaResumen) => <span className="mono">{r.tipo === 'Corta' ? r.dias : '—'}</span> },
              { key: 'importe', label: t.columnas.importe, align: 'right', render: (r: ReservaResumen) => <Money amount={r.importe} currency={r.moneda} /> },
              { key: 'estado', label: t.columnas.estado, render: (r: ReservaResumen) => <BadgeEstado estado={ESTADOS_RESERVA[r.estado]} /> },
            ]}
          />
        )}
        {lista.data && lista.data.total > 0 && (
          <Pagination page={lista.data.page} pageSize={TAMANO_PAGINA} total={lista.data.total} onChange={(page) => setFiltros({ ...filtros, page })} />
        )}
      </Card>

      <FormularioReservaDrawer
        open={abierto}
        inicial={inicial}
        aviso={transferencia.data ? t.formulario.desdeCotizacion : transferencia.isError ? t.formulario.cotizacionNoCopiada : undefined}
        onCerrar={cerrar}
        onGuardada={(id) => {
          cerrar();
          navegar(`/reservas/${id}`);
        }}
      />
    </>
  );
}
