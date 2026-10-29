import { useRef, useState, type JSX } from 'react';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { Button } from '../../../design-system/components/core/Button';
import { Dialog } from '../../../design-system/components/feedback/Dialog';
import { Combobox } from '../../../design-system/components/forms/Combobox';
import { Input } from '../../../design-system/components/forms/Input';
import { Select } from '../../../design-system/components/forms/Select';
import { formatearFecha } from '../../../dominio/fechas';
import { useContratos, useCrearFianza, type ContratoRef } from '../api';
import { entradaADecimal, esPositivo } from '../decimal';
import { claveParaIntento } from '../logica';
import { textosFinanzas } from '../textos';
import type { Moneda } from '../tipos';
import { useRetardado } from './Comunes';

const a = textosFinanzas.fianzas.alta;
const tc = textosFinanzas.comun;

const etiquetaContrato = (c: ContratoRef): string =>
  a.contratoEtiqueta.replace('{cliente}', c.cliente ?? '—').replace('{unidad}', c.unidad ?? '—').replace('{fecha}', formatearFecha(c.fechaInicio));

/** Alta de fianza (`POST /fianzas`, `fianza.mover`): el contrato se busca en remoto y la moneda parte de la del contrato. */
export function AltaFianza({ onCerrar, onCreada }: { onCerrar: () => void; onCreada: (id: string) => void }): JSX.Element {
  const crear = useCrearFianza();
  const [texto, setTexto] = useState('');
  const contratos = useContratos(useRetardado(texto), true);
  const [elegido, setElegido] = useState<ContratoRef | null>(null);
  const [importe, setImporte] = useState('');
  const [moneda, setMoneda] = useState<Moneda>('USD');
  const [numero, setNumero] = useState('1');
  const intento = useRef<{ huella: string; clave: string } | null>(null);

  const opciones = (contratos.data ?? []).map((c) => ({ value: c.id, label: etiquetaContrato(c) }));
  if (elegido && !opciones.some((o) => o.value === elegido.id)) opciones.unshift({ value: elegido.id, label: etiquetaContrato(elegido) });
  const importeOk = entradaADecimal(importe);
  const numeroOk = /^\d+$/.test(numero) && Number(numero) >= 1;
  const valido = elegido !== null && importeOk !== null && esPositivo(importeOk) && numeroOk;

  function enviar() {
    if (!valido || !elegido || importeOk === null) return;
    const cuerpo = { contratoId: elegido.id, importeExigido: importeOk, moneda, numeroFianzas: Number(numero) };
    intento.current = claveParaIntento(intento.current, JSON.stringify(cuerpo), () => crypto.randomUUID());
    crear.mutate({ cuerpo, clave: intento.current.clave }, { onSuccess: (r) => onCreada(r.fianza.id) });
  }

  return (
    <Dialog
      open
      title={a.titulo}
      description={a.descripcion}
      onClose={onCerrar}
      footer={
        <>
          <Button variant="ghost" onClick={onCerrar}>{tc.cancelar}</Button>
          <Button disabled={!valido || crear.isPending} onClick={enviar}>{a.crear}</Button>
        </>
      }
    >
      <div style={{ display: 'grid', gap: 'var(--space-6)' }}>
        <div onInput={(e) => setTexto((e.target as HTMLInputElement).value)}>
          <Combobox
            label={a.contrato}
            required
            placeholder={a.buscarContrato}
            options={opciones}
            value={elegido?.id ?? null}
            onChange={(v) => {
              setTexto('');
              const c = (contratos.data ?? []).find((x) => x.id === v) ?? null;
              setElegido(c);
              if (c) setMoneda(c.moneda);
            }}
          />
        </div>
        <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr', gap: 'var(--space-6)' }}>
          <Input
            label={a.importe}
            required
            inputMode="decimal"
            suffix={moneda}
            value={importe}
            error={importe !== '' && importeOk === null ? 'Importe no válido' : undefined}
            onChange={(e) => setImporte(e.target.value)}
          />
          <Select label={a.moneda} value={moneda} options={['USD', 'DOP']} onChange={(e) => setMoneda(e.target.value as Moneda)} />
        </div>
        <Input
          label={a.numero}
          required
          inputMode="numeric"
          value={numero}
          error={numeroOk ? undefined : a.numeroInvalido}
          onChange={(e) => setNumero(e.target.value)}
        />
        {crear.isError && <ErrorAlerta error={crear.error} titulo={a.error} />}
      </div>
    </Dialog>
  );
}
