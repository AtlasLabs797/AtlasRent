import { useEffect, useState, type FormEvent } from 'react';
import { useLocation, useNavigate } from 'react-router';
import { Button } from '../../../design-system/components/core/Button';
import { Badge } from '../../../design-system/components/core/Badge';
import { Switch } from '../../../design-system/components/forms/Switch';
import { Card } from '../../../design-system/components/core/Card';
import { Input } from '../../../design-system/components/forms/Input';
import { Select } from '../../../design-system/components/forms/Select';
import { DataTable } from '../../../design-system/components/data/DataTable';
import { EmptyState } from '../../../design-system/components/feedback/EmptyState';
import { Skeleton } from '../../../design-system/components/feedback/Skeleton';
import { FilterBar } from '../../../design-system/components/navigation/FilterBar';
import { PageHeader } from '../../../design-system/components/navigation/PageHeader';
import { Pagination } from '../../../design-system/components/navigation/Pagination';
import { Tabs } from '../../../design-system/components/navigation/Tabs';
import { ErrorAlerta } from '../../../componentes/ErrorAlerta';
import { ACCIONES } from '../../../permisos';
import { puede, useSesion } from '../../../sesion/store';
import { useListaClientes } from '../api';
import { FormularioCliente } from '../FormularioCliente';
import type { ValoresCliente } from '../logica';
import { textosClientes } from '../textos';
import type { ClienteResumen, TipoCliente } from '../tipos';
import { useRetardo } from '../useRetardo';

const t = textosClientes;
const POR_PAGINA = 25;

export default function Clientes() {
  const navegar = useNavigate();
  const ubicacion = useLocation();
  const identidad = useSesion((s) => s.identidad);
  const editar = puede(identidad, ACCIONES.clienteEditar);

  const [q, setQ] = useState('');
  const [tipo, setTipo] = useState<TipoCliente | ''>('');
  const [incluirInactivos, setIncluirInactivos] = useState(false);
  const [page, setPage] = useState(1);
  const [campoDocumento, setCampoDocumento] = useState('');
  const [documento, setDocumento] = useState('');
  const qRetardada = useRetardo(q, 300);

  // Un prospecto que se convierte llega con sus datos en el estado de la navegación (no en la URL).
  const prefill = (ubicacion.state as { prefill?: ValoresCliente } | null)?.prefill;
  const [alta, setAlta] = useState(prefill !== undefined);
  const [inicial] = useState(prefill);

  useEffect(() => setPage(1), [qRetardada, tipo, incluirInactivos, documento]);

  const lista = useListaClientes({ q: qRetardada, tipo, incluirInactivos, page, pageSize: POR_PAGINA, documento });
  const filas = lista.data?.data ?? [];
  const filtrando = qRetardada.trim() !== '' || tipo !== '' || documento !== '';

  function buscarDocumento(e: FormEvent) {
    e.preventDefault();
    setDocumento(campoDocumento.trim());
  }

  const filtrosActivos = [
    ...(tipo !== '' ? [{ key: 'tipo', label: t.lista.tipo, value: t.tipos[tipo] }] : []),
    ...(documento !== '' ? [{ key: 'documento', label: t.lista.documentoQuitar, value: documento }] : []),
  ];

  return (
    <>
      <PageHeader
        eyebrow={t.eyebrow}
        title={t.titulo}
        description={t.descripcion}
        breadcrumbs={[{ label: t.eyebrow }, { label: t.titulo }]}
      >
        {editar && (
          <Button icon="plus" onClick={() => setAlta(true)}>
            {t.nuevo}
          </Button>
        )}
      </PageHeader>

      <Tabs
        tabs={[{ label: t.pestanas.clientes, value: 'clientes' }, { label: t.pestanas.prospectos, value: 'prospectos' }]}
        value="clientes"
        onChange={(v) => v === 'prospectos' && navegar('/clientes/prospectos')}
      />
      <div style={{ height: 16 }} />

      <Card padded={false}>
        <FilterBar
          searchPlaceholder={t.lista.buscar}
          search={q}
          onSearch={setQ}
          filters={filtrosActivos}
          onRemove={(k) => (k === 'tipo' ? setTipo('') : (setDocumento(''), setCampoDocumento('')))}
          onClear={() => {
            setTipo('');
            setDocumento('');
            setCampoDocumento('');
            setQ('');
          }}
        >
          <Select
            value={tipo}
            placeholder={t.lista.todosTipos}
            options={[{ value: '', label: t.lista.todosTipos }, ...(['PersonaFisica', 'Empresa'] as const).map((v) => ({ value: v, label: t.tipos[v] }))]}
            onChange={(e) => setTipo(e.target.value as TipoCliente | '')}
            style={{ minWidth: 170 }}
          />
          <form onSubmit={buscarDocumento} style={{ display: 'flex', gap: 8, alignItems: 'flex-start' }}>
            <Input
              placeholder={t.lista.documentoPlaceholder}
              aria-label={t.lista.documentoPlaceholder}
              icon="id-card"
              autoComplete="off"
              hint={t.lista.documentoAviso}
              value={campoDocumento}
              onChange={(e) => setCampoDocumento(e.target.value)}
              style={{ minWidth: 220 }}
            />
            <Button type="submit" variant="secondary" disabled={campoDocumento.trim() === ''}>
              {t.lista.documentoAccion}
            </Button>
          </form>
          <Switch label={t.lista.inactivos} checked={incluirInactivos} onChange={setIncluirInactivos} />
        </FilterBar>

        {lista.isPending ? (
          <div style={{ padding: 'var(--space-9)' }}>
            <Skeleton lines={6} height={20} />
          </div>
        ) : lista.isError ? (
          <div style={{ padding: 'var(--space-9)' }}>
            <ErrorAlerta error={lista.error} titulo={t.lista.errorTitulo} />
            <div style={{ marginTop: 12 }}>
              <Button variant="secondary" onClick={() => void lista.refetch()}>
                {t.lista.reintentar}
              </Button>
            </div>
          </div>
        ) : filas.length === 0 ? (
          <EmptyState
            icon="users"
            title={filtrando ? t.lista.vacioTitulo : t.lista.vacioSinDatosTitulo}
          >
            {filtrando ? t.lista.vacioCuerpo : t.lista.vacioSinDatosCuerpo}
          </EmptyState>
        ) : (
          <>
            <DataTable
              rows={filas}
              onRowClick={(f: ClienteResumen) => navegar(`/clientes/${f.id}`)}
              columns={[
                { key: 'numero', label: t.lista.columnas.numero, align: 'right', width: 90, render: (f: ClienteResumen) => <span className="mono">{f.numero}</span> },
                { key: 'nombre', label: t.lista.columnas.nombre, render: (f: ClienteResumen) => <strong style={{ color: 'var(--text-strong)', fontWeight: 600 }}>{f.nombre}</strong> },
                { key: 'tipo', label: t.lista.columnas.tipo, render: (f: ClienteResumen) => t.tipos[f.tipo] },
                { key: 'email', label: t.lista.columnas.email, render: (f: ClienteResumen) => f.email ?? '—' },
                { key: 'telefono', label: t.lista.columnas.telefono, render: (f: ClienteResumen) => <span className="mono">{f.telefono ?? '—'}</span> },
                {
                  key: 'estado',
                  label: t.lista.columnas.estado,
                  render: (f: ClienteResumen) => (
                    <span style={{ display: 'inline-flex', gap: 6, flexWrap: 'wrap' }}>
                      {f.activo ? <Badge status="activo" /> : <Badge tone="neutral">{t.estados.inactivo}</Badge>}
                      {f.enGestionCobro && <Badge tone="warning">{t.marcas.enGestionCobro}</Badge>}
                    </span>
                  ),
                },
              ]}
            />
            <Pagination page={lista.data?.page ?? page} pageSize={POR_PAGINA} total={lista.data?.total ?? 0} onChange={setPage} />
          </>
        )}
      </Card>

      {editar && (
        <FormularioCliente
          open={alta}
          inicial={inicial}
          onClose={() => setAlta(false)}
          onGuardado={(c) => {
            setAlta(false);
            navegar(`/clientes/${c.id}`);
          }}
        />
      )}
    </>
  );
}
