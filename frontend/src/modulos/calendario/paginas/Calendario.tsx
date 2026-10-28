import { useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router';
import { useVirtualizer } from '@tanstack/react-virtual';
import { Button } from '../../../design-system/components/core/Button';
import { IconButton } from '../../../design-system/components/core/IconButton';
import { Card } from '../../../design-system/components/core/Card';
import { KeyValueList } from '../../../design-system/components/data/KeyValueList';
import { Drawer } from '../../../design-system/components/feedback/Drawer';
import { EmptyState } from '../../../design-system/components/feedback/EmptyState';
import { Skeleton } from '../../../design-system/components/feedback/Skeleton';
import { Input } from '../../../design-system/components/forms/Input';
import { Select } from '../../../design-system/components/forms/Select';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { formatearFecha } from '../../../dominio/fechas';
import { textos } from '../../../i18n/textos';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { useInmuebles } from '../../operacion/api';
import { desplazarMes, etiquetaMes, hoyNegocio, inicialDiaSemana, esFinDeSemana, fechasDelMes, mesDe, rangoDelMes } from '../../operacion/fechas';
import { useCalendario } from '../api';
import {
  ETIQUETA_TIPO_DIA,
  diasLibres,
  filtrarUnidades,
  pintarDia,
  rutaDeOrigen,
  type DiaCalendario,
  type TipoDia,
  type UnidadCalendario,
} from '../modelo';
import { textosCalendario as t } from '../textos';
import '../calendario.css';

const ALTO_FILA = 40;
const LEYENDA: Array<[string, string]> = [
  ['Libre', t.leyenda.Libre],
  ['Contrato', t.leyenda.Contrato],
  ['ReservaMensual', t.leyenda.ReservaMensual],
  ['ReservaCorta', t.leyenda.ReservaCorta],
  ['Medio', t.leyenda.medio],
  ['Mantenimiento', t.leyenda.Mantenimiento],
  ['EstadoManual', t.leyenda.EstadoManual],
];

interface Seleccion {
  unidad: UnidadCalendario;
  dia: DiaCalendario;
}

/**
 * Calendario de disponibilidad (§3.1): una fila por unidad, una columna por día del mes. Las filas se
 * virtualizan (~150 unidades por inmueble). Lo que hay en cada día lo calcula el backend; aquí solo se pinta.
 */
export default function Calendario() {
  const navegar = useNavigate();
  const identidad = useSesion((s) => s.identidad);
  const hoy = hoyNegocio();
  const [mes, setMes] = useState(() => mesDe(hoy));
  const [inmuebleElegido, setInmuebleElegido] = useState<string | null>(null);
  const [busqueda, setBusqueda] = useState('');
  const [seleccion, setSeleccion] = useState<Seleccion | null>(null);

  const inmuebles = useInmuebles();
  const listaInmuebles = useMemo(() => (inmuebles.data?.data ?? []).filter((i) => i.activo), [inmuebles.data]);
  const inmuebleId = inmuebleElegido ?? listaInmuebles[0]?.id ?? null;
  const { desde, hasta } = rangoDelMes(mes);
  const fechas = useMemo(() => fechasDelMes(mes), [mes]);
  const calendario = useCalendario(inmuebleId, desde, hasta);

  const unidades = useMemo(() => filtrarUnidades(calendario.data?.unidades ?? [], busqueda), [calendario.data, busqueda]);
  const libres = unidades.reduce((suma, u) => suma + diasLibres(u), 0);

  const contenedor = useRef<HTMLDivElement>(null);
  const virtualizador = useVirtualizer({
    count: unidades.length,
    getScrollElement: () => contenedor.current,
    estimateSize: () => ALTO_FILA,
    overscan: 6,
  });

  const puedeReservar = puede(identidad, ACCIONES.clienteEditar);

  return (
    <>
      <PageHeader
        eyebrow={textos.navegacion.grupos.operacion}
        title={t.titulo}
        description={t.descripcion}
        breadcrumbs={[{ label: textos.navegacion.grupos.operacion }, { label: textos.navegacion.modulos.calendario.etiqueta }]}
      />

      {inmuebles.isError && <ErrorAlerta error={inmuebles.error} titulo={t.errorTitulo} />}

      {inmuebles.isSuccess && listaInmuebles.length === 0 ? (
        <EmptyState icon="building-2" title={t.sinInmuebles}>
          {t.sinInmueblesCuerpo}
        </EmptyState>
      ) : (
        <Card>
          <div className="ar-cal__barra" style={{ marginBottom: 'var(--space-7)' }}>
            <Select
              label={t.inmueble}
              value={inmuebleId ?? ''}
              onChange={(e) => setInmuebleElegido(e.target.value)}
              options={listaInmuebles.map((i) => ({ value: i.id, label: i.nombre }))}
              style={{ minWidth: 220 }}
            />
            <Input icon="search" label={t.buscarUnidad} value={busqueda} onChange={(e) => setBusqueda(e.target.value)} style={{ minWidth: 220 }} />
            <div className="ar-cal__barra" style={{ marginLeft: 'auto', alignSelf: 'flex-end' }}>
              <IconButton icon="chevron-left" label={t.mesAnterior} onClick={() => setMes((m) => desplazarMes(m, -1))} />
              <span className="ar-cal__mes" aria-live="polite">
                {etiquetaMes(mes)}
              </span>
              <IconButton icon="chevron-right" label={t.mesSiguiente} onClick={() => setMes((m) => desplazarMes(m, 1))} />
              <Button variant="secondary" onClick={() => setMes(mesDe(hoy))}>
                {t.hoy}
              </Button>
            </div>
          </div>

          <div className="ar-cal__leyenda" style={{ marginBottom: 'var(--space-7)' }} aria-label={t.leyenda.titulo}>
            {LEYENDA.map(([tipo, texto]) => (
              <span key={tipo}>
                <i className="ar-cal__muestra" data-tipo={tipo} />
                {texto}
              </span>
            ))}
          </div>

          {calendario.isError && <ErrorAlerta error={calendario.error} titulo={t.errorTitulo} />}
          {(calendario.isPending || inmuebles.isPending) && !calendario.isError ? (
            <div style={{ display: 'grid', gap: 'var(--space-4)' }} aria-busy="true">
              {Array.from({ length: 8 }, (_, i) => (
                <Skeleton key={i} height={ALTO_FILA - 8} />
              ))}
            </div>
          ) : calendario.data && unidades.length === 0 ? (
            <EmptyState icon="search" title={t.sinUnidades} />
          ) : calendario.data ? (
            <>
              <p className="mono" style={{ margin: '0 0 var(--space-4)', color: 'var(--text-muted)' }}>
                {t.resumen.replace('{unidades}', String(unidades.length)).replace('{libres}', String(libres))}
              </p>
              <div
                ref={contenedor}
                className="ar-cal"
                style={{ height: Math.min(560, 44 + unidades.length * ALTO_FILA + 2), ['--cal-dias' as string]: fechas.length }}
              >
                <div className="ar-cal__cabecera">
                  <div className="ar-cal__nombre" style={{ height: 44, borderBottom: 0 }}>
                    <b>{t.unidad}</b>
                  </div>
                  {fechas.map((f) => (
                    <div key={f} className="ar-cal__titulo-dia" data-hoy={f === hoy} data-finde={esFinDeSemana(f)}>
                      <span>{inicialDiaSemana(f)}</span>
                      <b>{Number(f.slice(8))}</b>
                    </div>
                  ))}
                </div>
                <div style={{ position: 'relative', height: virtualizador.getTotalSize(), width: 'max-content' }}>
                  {virtualizador.getVirtualItems().map((item) => {
                    const unidad = unidades[item.index]!;
                    return (
                      <div key={unidad.unidadId} className="ar-cal__fila" style={{ top: item.start, ['--cal-dias' as string]: fechas.length }}>
                        <div className="ar-cal__nombre">
                          <b title={unidad.unidad}>{unidad.unidad}</b>
                          <span>{t.libresDelMes.replace('{n}', String(diasLibres(unidad)))}</span>
                        </div>
                        {unidad.dias.map((dia) => {
                          const p = pintarDia(dia);
                          return (
                            <button
                              key={dia.fecha}
                              type="button"
                              className="ar-cal__celda"
                              data-tipo={p.tipo}
                              data-medio={p.medioDia}
                              aria-label={`${unidad.unidad}, ${formatearFecha(dia.fecha)}: ${ETIQUETA_TIPO_DIA[p.tipo]}`}
                              onClick={() => setSeleccion({ unidad, dia })}
                            >
                              {p.letra}
                            </button>
                          );
                        })}
                      </div>
                    );
                  })}
                </div>
              </div>
            </>
          ) : null}
        </Card>
      )}

      <Drawer
        open={seleccion !== null}
        onClose={() => setSeleccion(null)}
        width={460}
        title={seleccion ? `${seleccion.unidad.unidad} · ${formatearFecha(seleccion.dia.fecha)}` : ''}
        footer={
          seleccion && seleccion.dia.disponible && puedeReservar ? (
            <Button
              icon="calendar-plus"
              onClick={() => navegar(`/reservas?nueva=1&unidadId=${seleccion.unidad.unidadId}&fecha=${seleccion.dia.fecha}`)}
            >
              {t.detalle.crearReserva}
            </Button>
          ) : (
            <Button variant="ghost" onClick={() => setSeleccion(null)}>
              {t.detalle.cerrar}
            </Button>
          )
        }
      >
        {seleccion && <DetalleDia seleccion={seleccion} />}
      </Drawer>
    </>
  );
}

function DetalleDia({ seleccion }: { seleccion: Seleccion }) {
  const navegar = useNavigate();
  const { dia } = seleccion;
  return (
    <div style={{ display: 'grid', gap: 'var(--space-9)' }}>
      <KeyValueList
        columns={1}
        items={[
          { label: t.detalle.estado, value: ETIQUETA_TIPO_DIA[dia.tipo as TipoDia] },
          { label: t.detalle.motivo, value: dia.motivo },
        ]}
      />
      <div>
        <h3 style={{ font: 'var(--type-label)', margin: '0 0 var(--space-4)' }}>{t.detalle.ocupaciones}</h3>
        {dia.ocupaciones.length === 0 ? (
          <p style={{ margin: 0, color: 'var(--text-muted)' }}>{t.detalle.ninguna}</p>
        ) : (
          <ul style={{ listStyle: 'none', margin: 0, padding: 0, display: 'grid', gap: 'var(--space-6)' }}>
            {dia.ocupaciones.map((o) => {
              const ruta = rutaDeOrigen(o);
              return (
                <li key={`${o.origen}-${o.origenId}`} style={{ display: 'flex', justifyContent: 'space-between', gap: 'var(--space-6)', alignItems: 'center' }}>
                  <span>
                    {o.origen === 'Contrato' ? t.leyenda.Contrato : o.origen === 'ReservaMensual' ? t.leyenda.ReservaMensual : o.origen === 'ReservaCorta' ? t.leyenda.ReservaCorta : t.leyenda.Mantenimiento}
                    {' · '}
                    {o.tipoDia === 'MedioDia' ? t.detalle.medioDia : o.tipoDia === 'DiaCompleto' ? t.detalle.diaCompleto : o.finExclusivo ? t.detalle.rango.replace('{fecha}', formatearFecha(o.finExclusivo)) : t.detalle.abierto}
                  </span>
                  {ruta && (
                    <Button size="sm" variant="secondary" onClick={() => navegar(ruta)}>
                      {o.origen === 'Contrato' ? t.detalle.verContrato : t.detalle.verReserva}
                    </Button>
                  )}
                </li>
              );
            })}
          </ul>
        )}
      </div>
    </div>
  );
}
