import { useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Badge } from '../../../design-system/components/core/Badge';
import { Button } from '../../../design-system/components/core/Button';
import { Card } from '../../../design-system/components/core/Card';
import { DataTable, type DataTableColumn } from '../../../design-system/components/data/DataTable';
import { Money } from '../../../design-system/components/data/Money';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { Dialog } from '../../../design-system/components/feedback/Dialog';
import { Input } from '../../../design-system/components/forms/Input';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { formatearFecha } from '../../../dominio/fechas';
import { textos } from '../../../i18n/textos';
import SinPermiso from '../../../paginas/SinPermiso';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { useGenerarPeriodica, useSimularPeriodica } from '../api';
import { SelectorSerie } from '../componentes/Comunes';
import { claveParaIntento } from '../logica';
import { textosFinanzas } from '../textos';
import type { FacturaItemSimulado, GeneracionPeriodicaRespuesta, SimulacionPeriodicaRespuesta } from '../tipos';

const t = textosFinanzas.periodicas;
const tc = textosFinanzas.comun;

/** Unidad e inmueble; si se llaman igual se muestra uno solo. */
function ubicacion(i: FacturaItemSimulado): string {
  return i.unidadNombre === i.inmuebleNombre ? i.unidadNombre : `${i.inmuebleNombre} · ${i.unidadNombre}`;
}

function etiquetaContrato(i: FacturaItemSimulado): string {
  return `${i.clienteNombre} (${ubicacion(i)})`;
}

function mesActual(): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Santo_Domingo', year: 'numeric', month: '2-digit' }).format(new Date()).slice(0, 7);
}

/** Facturación periódica (F3-05): simular, revisar y generar. Solo se genera lo que se vio en la simulación. */
export default function FacturasPeriodicas() {
  const identidad = useSesion((s) => s.identidad);
  const navegar = useNavigate();
  const simular = useSimularPeriodica();
  const generar = useGenerarPeriodica();
  const [mes, setMes] = useState(mesActual());
  const [serieId, setSerieId] = useState('');
  const [sim, setSim] = useState<SimulacionPeriodicaRespuesta | null>(null);
  const [resultado, setResultado] = useState<GeneracionPeriodicaRespuesta | null>(null);
  const [confirmar, setConfirmar] = useState(false);
  // Nombre de cada contrato visto en la simulación, para rotular también el resultado.
  const [nombres, setNombres] = useState<Map<string, string>>(new Map());
  const intento = useRef<{ huella: string; clave: string } | null>(null);

  if (!puede(identidad, ACCIONES.facturaEmitir)) return <SinPermiso />;

  const periodo = `${mes}-01`;
  const emiten = sim?.items.filter((i) => i.seEmite) ?? [];
  const monedas = new Set(sim?.items.map((i) => i.moneda));

  function lanzarSimulacion() {
    setResultado(null);
    simular.mutate(
      { periodo, serieId },
      {
        onSuccess: (r) => {
          setSim(r);
          setNombres(new Map(r.items.map((i) => [i.contratoId, etiquetaContrato(i)])));
        },
      },
    );
  }

  function lanzarGeneracion() {
    const cuerpo = { periodo, serieId, contratosIds: emiten.map((i) => i.contratoId) };
    intento.current = claveParaIntento(intento.current, JSON.stringify(cuerpo), () => crypto.randomUUID());
    generar.mutate(
      { cuerpo, clave: intento.current.clave },
      {
        onSuccess: (r) => {
          setResultado(r);
          setSim(null);
          setConfirmar(false);
        },
      },
    );
  }

  const columnas: DataTableColumn[] = [
    {
      key: 'contrato',
      label: t.columnas.contrato,
      render: (i: FacturaItemSimulado) => (
        <div>
          <div>{i.clienteNombre}</div>
          <div style={{ font: 'var(--type-caption)', color: 'var(--text-muted)' }}>
            {ubicacion(i)} · {t.desde.replace('{fecha}', formatearFecha(i.contratoFechaInicio))}
          </div>
        </div>
      ),
    },
    { key: 'periodo', label: t.columnas.periodo, render: (i: FacturaItemSimulado) => <span className="mono">{formatearFecha(i.periodoInicio)} → {formatearFecha(i.periodoFin)}</span> },
    {
      key: 'dias',
      label: t.columnas.dias,
      align: 'right',
      render: (i: FacturaItemSimulado) => (
        <span className="mono">
          {i.diasFacturables}/{i.diasDelMes} {i.prorrateado && <Badge tone="neutral">{t.prorrateado}</Badge>}
        </span>
      ),
    },
    { key: 'importe', label: t.columnas.importe, align: 'right', render: (i: FacturaItemSimulado) => <Money amount={i.importe} currency={i.moneda} /> },
    { key: 'vence', label: t.columnas.vence, render: (i: FacturaItemSimulado) => <span className="mono">{formatearFecha(i.vencimiento)}</span> },
    {
      key: 'resultado',
      label: t.columnas.resultado,
      render: (i: FacturaItemSimulado) =>
        i.seEmite ? (
          <Badge tone="success" dot>{t.seEmite}</Badge>
        ) : (
          <span>{i.yaExiste ? t.yaExiste.replace('{n}', String(i.facturaExistenteNumero ?? '—')) : (i.motivoOmision ?? '—')}</span>
        ),
    },
  ];

  return (
    <>
      <PageHeader
        eyebrow={textos.navegacion.grupos.finanzas}
        title={t.titulo}
        description={t.descripcion}
        breadcrumbs={[{ label: textos.navegacion.modulos.facturas.etiqueta, onClick: () => navegar('/facturas') }, { label: t.titulo }]}
      />

      <Card>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(220px,1fr))', gap: 'var(--space-6)', alignItems: 'end' }}>
          <Input label={t.periodo} type="month" value={mes} onChange={(e) => { setMes(e.target.value); setSim(null); }} />
          <SelectorSerie
            etiqueta={t.serie}
            valor={serieId}
            onChange={(v) => {
              setSerieId(v);
              setSim(null);
            }}
          />
          <div>
            <Button variant="secondary" disabled={!mes || !serieId || simular.isPending} onClick={lanzarSimulacion}>
              {t.simular}
            </Button>
          </div>
        </div>
        {simular.isError && <div style={{ marginTop: 16 }}><ErrorAlerta error={simular.error} titulo={t.simular} /></div>}
      </Card>

      <div style={{ height: 24 }} />

      {sim && (
        <Card
          title={`${formatearFecha(sim.periodoInicio)} → ${formatearFecha(sim.periodoFin)}`}
          actions={<Button disabled={emiten.length === 0} onClick={() => setConfirmar(true)}>{t.generar}</Button>}
          padded={false}
        >
          <div style={{ padding: 'var(--space-9)', paddingBottom: 0, color: 'var(--text-muted)' }}>
            {t.resumenSimulacion.replace('{emite}', String(emiten.length)).replace('{omite}', String(sim.items.length - emiten.length))}
            {monedas.size === 1 && <> · <Money amount={sim.importeTotal} currency={[...monedas][0]!} /></>}
          </div>
          <DataTable columns={columnas} rows={sim.items} rowKey="contratoId" empty={tc.vacio} />
        </Card>
      )}

      {resultado && (
        <div style={{ display: 'grid', gap: 'var(--space-7)' }}>
          {resultado.fallidas > 0 && (
            <Alert tone="danger" title={`${t.resultado.fallidasTitulo}: ${resultado.fallidas}`}>
              {t.resultado.fallidasCuerpo}
              <ul style={{ margin: '8px 0 0', paddingLeft: 18 }}>
                {resultado.fallidos.map((x) => (
                  <li key={x.contratoId}>{nombres.get(x.contratoId) ?? x.contratoId.slice(0, 8)} · {x.motivo}</li>
                ))}
              </ul>
            </Alert>
          )}
          <Card title={t.resultado.titulo}>
            <div style={{ display: 'flex', gap: 'var(--space-10)' }}>
              <div><div className="ar-overline">{t.resultado.creadas}</div><span className="mono" style={{ font: 'var(--type-h3)' }}>{resultado.creadas}</span></div>
              <div><div className="ar-overline">{t.resultado.omitidas}</div><span className="mono" style={{ font: 'var(--type-h3)' }}>{resultado.omitidas}</span></div>
              <div><div className="ar-overline">{t.resultado.fallidas}</div><span className="mono" style={{ font: 'var(--type-h3)' }}>{resultado.fallidas}</span></div>
            </div>
            {resultado.facturas.length > 0 && (
              <>
                <div className="ar-overline" style={{ margin: '24px 0 8px' }}>{t.resultado.facturasTitulo}</div>
                <ul style={{ margin: 0, paddingLeft: 18 }}>
                  {resultado.facturas.map((x) => (
                    <li key={x.facturaId}>
                      <Link className="mono" to={`/facturas/${x.facturaId}`}>{resultado.serieNumero}-{String(x.numero ?? 0).padStart(5, '0')}</Link>
                    </li>
                  ))}
                </ul>
              </>
            )}
            {resultado.omitidos.length > 0 && (
              <>
                <div className="ar-overline" style={{ margin: '24px 0 8px' }}>{t.resultado.omitidosTitulo}</div>
                <ul style={{ margin: 0, paddingLeft: 18 }}>
                  {resultado.omitidos.map((x) => (
                    <li key={x.contratoId}>{nombres.get(x.contratoId) ?? x.contratoId.slice(0, 8)} · {x.motivo}</li>
                  ))}
                </ul>
              </>
            )}
          </Card>
        </div>
      )}

      <Dialog
        open={confirmar}
        title={t.confirmarTitulo}
        onClose={() => setConfirmar(false)}
        footer={
          <>
            <Button variant="ghost" onClick={() => setConfirmar(false)}>{tc.cancelar}</Button>
            <Button disabled={generar.isPending} onClick={lanzarGeneracion}>{generar.isPending ? t.generando : t.generar}</Button>
          </>
        }
      >
        <p style={{ margin: 0 }}>{t.confirmarCuerpo.replace('{n}', String(emiten.length)).replace('{serie}', String(sim?.serieNumero ?? ''))}</p>
        {generar.isError && <div style={{ marginTop: 16 }}><ErrorAlerta error={generar.error} titulo={t.generar} /></div>}
      </Dialog>
    </>
  );
}
