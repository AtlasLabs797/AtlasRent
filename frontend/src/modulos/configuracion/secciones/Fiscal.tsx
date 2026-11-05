import { useMemo, useState, type JSX } from 'react';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Badge } from '../../../design-system/components/core/Badge';
import { Card } from '../../../design-system/components/core/Card';
import { DataTable } from '../../../design-system/components/data/DataTable';
import { Alert } from '../../../design-system/components/feedback/Alert';
import { Skeleton } from '../../../design-system/components/feedback/Skeleton';
import { formatearFecha } from '../../../dominio/fechas';
import { useCatalogo, useGuardarNumerador, useNumeradores, type NumeradorFiscal } from '../api';
import { CAMPOS_NUMERADOR } from '../catalogos';
import { estadoNumerador, hoyNegocio, validarNumerador, type EstadoNumerador } from '../config';
import type { Fila, Valores } from '../esquema';
import { textosConfiguracion as t } from '../textos';
import { FormularioEsquema } from './FormularioEsquema';

interface FilaFiscal {
  serieId: string;
  serieNumero: number;
  numerador: NumeradorFiscal | null;
  estado: EstadoNumerador | 'sinConfigurar';
}

const TONO: Record<FilaFiscal['estado'], 'success' | 'warning' | 'danger' | 'neutral'> = {
  vigente: 'success',
  porAgotar: 'warning',
  agotado: 'danger',
  vencido: 'danger',
  sinConfigurar: 'neutral',
};

/** Numeradores de comprobante fiscal (§3.7) con alertas de agotado, por agotar y vencido. */
export function Fiscal(): JSX.Element {
  const series = useCatalogo('/config/series');
  const numeradores = useNumeradores();
  const guardar = useGuardarNumerador();
  const [editando, setEditando] = useState<FilaFiscal | null>(null);
  const f = t.fiscal;
  const hoy = hoyNegocio();

  const filas = useMemo<FilaFiscal[]>(() => {
    const porSerie = new Map((numeradores.data ?? []).map((n) => [n.serieId, n]));
    const lista: FilaFiscal[] = [];
    for (const s of series.data ?? []) {
      const n = porSerie.get(String(s.id)) ?? null;
      if (!n && !(s.generaComprobante === true && s.activa === true)) continue;
      lista.push({ serieId: String(s.id), serieNumero: Number(s.numero), numerador: n, estado: n ? estadoNumerador(n, hoy) : 'sinConfigurar' });
    }
    return lista.sort((a, b) => a.serieNumero - b.serieNumero);
  }, [series.data, numeradores.data, hoy]);

  const alertas = filas.flatMap((x): Array<{ tono: 'danger' | 'warning'; texto: string; clave: string }> => {
    const serie = String(x.serieNumero);
    if (x.estado === 'agotado') return [{ tono: 'danger' as const, texto: f.alertaAgotado.replace('{serie}', serie), clave: `${serie}-a` }];
    if (x.estado === 'vencido' && x.numerador)
      return [{ tono: 'danger' as const, texto: f.alertaVencido.replace('{serie}', serie).replace('{fecha}', formatearFecha(x.numerador.fechaVencimiento)), clave: `${serie}-v` }];
    if (x.estado === 'porAgotar' && x.numerador)
      return [{ tono: 'warning' as const, texto: f.alertaPorAgotar.replace('{serie}', serie).replace('{n}', String(x.numerador.restantes)), clave: `${serie}-p` }];
    if (x.estado === 'sinConfigurar') return [{ tono: 'warning' as const, texto: f.alertaSinConfigurar.replace('{serie}', serie), clave: `${serie}-s` }];
    return [];
  });

  const cargando = series.isPending || numeradores.isPending;

  return (
    <div style={{ display: 'grid', gap: 'var(--space-7)' }}>
      {alertas.map((a) => (
        <Alert key={a.clave} tone={a.tono}>
          {a.texto}
        </Alert>
      ))}
      {series.isError && <ErrorAlerta error={series.error} titulo={t.comun.errorLista} />}
      {numeradores.isError && <ErrorAlerta error={numeradores.error} titulo={t.comun.errorLista} />}
      <Card title={f.titulo} padded={false}>
        <p style={{ margin: 0, padding: '0 var(--space-9) var(--space-6)', color: 'var(--text-muted)' }}>{f.descripcion}</p>
        {cargando ? (
          <div style={{ padding: 'var(--space-9)' }}>
            <Skeleton lines={3} height={20} />
          </div>
        ) : (
          <DataTable
            rows={filas}
            rowKey="serieId"
            onRowClick={(x: FilaFiscal) => setEditando(x)}
            columns={[
              { key: 'serie', label: f.columnas.serie, render: (x: FilaFiscal) => <span className="mono">{x.serieNumero}</span> },
              { key: 'tipo', label: f.columnas.tipo, render: (x: FilaFiscal) => <span className="mono">{x.numerador?.tipoPrefijo ?? '—'}</span> },
              { key: 'rango', label: f.columnas.rango, align: 'right', render: (x: FilaFiscal) => <span className="mono">{x.numerador ? `${x.numerador.rangoDesde} – ${x.numerador.rangoHasta}` : '—'}</span> },
              { key: 'siguiente', label: f.columnas.siguiente, align: 'right', render: (x: FilaFiscal) => <span className="mono">{x.numerador?.siguienteNumero ?? '—'}</span> },
              { key: 'restantes', label: f.columnas.restantes, align: 'right', render: (x: FilaFiscal) => <span className="mono">{x.numerador?.restantes ?? '—'}</span> },
              { key: 'vence', label: f.columnas.vence, align: 'right', render: (x: FilaFiscal) => <span className="mono">{x.numerador ? formatearFecha(x.numerador.fechaVencimiento) : '—'}</span> },
              {
                key: 'estado',
                label: f.columnas.estado,
                render: (x: FilaFiscal) => (
                  <Badge tone={TONO[x.estado]} dot>
                    {f.estados[x.estado]}
                  </Badge>
                ),
              },
            ]}
            empty={f.vacio}
          />
        )}
      </Card>
      {editando && (
        <FormularioEsquema
          titulo={f.editar.replace('{serie}', String(editando.serieNumero))}
          campos={CAMPOS_NUMERADOR}
          fila={editando.numerador}
          porDefecto={{ tipoPrefijo: '', rangoDesde: '', rangoHasta: '', fechaVencimiento: '', siguienteNumero: '', umbralAlerta: '0' }}
          validarExtra={(v: Valores) => {
            const errores = validarNumerador({ rangoDesde: Number(v.rangoDesde), rangoHasta: Number(v.rangoHasta), siguienteNumero: Number(v.siguienteNumero) });
            return Object.fromEntries(Object.entries(errores).map(([k, e]) => [k, e === 'rango' ? f.errores.rango : f.errores.siguiente]));
          }}
          onGuardar={(cuerpo: Fila) => guardar.mutateAsync({ serieId: editando.serieId, cuerpo })}
          onCerrar={() => setEditando(null)}
        />
      )}
    </div>
  );
}
