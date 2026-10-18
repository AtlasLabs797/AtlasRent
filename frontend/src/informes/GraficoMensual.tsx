import { useMemo } from 'react';
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { textos } from '../i18n/textos';
import { useTema } from '../tema/store';
import type { SerieMensualInforme } from './api';

/**
 * Facturado contra cobrado por mes, en barras agrupadas (DESIGN.md §3.1).
 *
 * Los colores se leen de los tokens CSS en tiempo de ejecución, nunca hex en el componente: es lo que
 * hace que el tema oscuro funcione sin tocar la pantalla.
 *
 * Va en su propio trozo (`React.lazy` en la página): Recharts pesa lo bastante como para que no
 * entre en la descarga de quien abre la aplicación y no entra en `/informes`.
 */

/** Valor de un token del tema activo en `<html data-theme>`. */
function token(nombre: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(nombre).trim();
}

// DESIGN.md §5: punto de miles, coma decimal, 2 decimales, como `Money`.
const formato = (valor: number) => valor.toLocaleString('de-DE', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

interface GraficoMensualProps {
  serie: SerieMensualInforme[];
}

export default function GraficoMensual({ serie }: GraficoMensualProps) {
  const { grafico } = textos.informes;
  // El tema se lee del store para volver a pedir los tokens cuando cambia: los valores se cachean en
  // `document.documentElement`, así que sin esta dependencia el gráfico se quedaría en el tema viejo.
  const tema = useTema((estado) => estado.tema);
  const colores = useMemo(
    () => ({
      // Acento para la serie principal y neutro para la segunda: `success` es un color de estado y
      // aquí no hay ningún estado que marcar (DESIGN.md §2).
      facturado: token('--accent'),
      cobrado: token('--text-muted'),
      eje: token('--text-subtle'),
      cuadricula: token('--border-subtle'),
      texto: token('--text-strong'),
      superficie: token('--surface-card'),
      borde: token('--border-default'),
    }),
    [tema],
  );

  const datos = useMemo(
    () => serie.map((mes) => ({ mes: mes.mes, facturado: mes.facturadoUsd, cobrado: mes.cobradoUsd })),
    [serie],
  );

  return (
    <figure style={{ margin: 0 }} role="img" aria-label={grafico.alternativo}>
      <figcaption style={{ color: 'var(--text-muted)', marginBottom: 'var(--space-6)' }}>
        {grafico.descripcion}
      </figcaption>
      <div style={{ width: '100%', height: 300 }}>
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={datos} margin={{ top: 8, right: 8, bottom: 8, left: 8 }}>
            <CartesianGrid stroke={colores.cuadricula} vertical={false} />
            <XAxis dataKey="mes" stroke={colores.eje} tick={{ fill: colores.eje, fontSize: 12 }} tickLine={false} />
            <YAxis
              stroke={colores.eje}
              tick={{ fill: colores.eje, fontSize: 12 }}
              tickLine={false}
              axisLine={false}
              width={96}
              tickFormatter={(valor: number) => formato(valor)}
            />
            <Tooltip
              cursor={{ fill: colores.superficie }}
              contentStyle={{
                background: colores.superficie,
                border: `1px solid ${colores.borde}`,
                borderRadius: 'var(--radius-card)',
                color: colores.texto,
              }}
              labelStyle={{ color: colores.eje }}
              itemStyle={{ color: colores.texto }}
              formatter={(valor) => formato(Number(valor))}
            />
            <Bar name={grafico.facturado} dataKey="facturado" fill={colores.facturado} radius={6} />
            <Bar name={grafico.cobrado} dataKey="cobrado" fill={colores.cobrado} radius={6} />
          </BarChart>
        </ResponsiveContainer>
      </div>
    </figure>
  );
}