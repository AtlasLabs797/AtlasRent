import { problema } from './apiFalsa';
import { capturarClaroOscuro } from './capturas';
import { ACCIONES } from '../src/permisos';
import { EMPLEADO, conSesion, identidad } from './fixtures';
import { expect, test } from './prueba';

const ADMIN_ID = '11111111-1111-1111-1111-111111111111';
const OTRO_ADMIN = '55555555-5555-5555-5555-555555555555';
const SOLICITUD = '66666666-6666-6666-6666-666666666666';

const USUARIOS = {
  data: [
    { id: ADMIN_ID, nombreAcceso: 'admin', nombre: 'Marta Peña', email: null, activo: true, rol: 'admin' },
    { id: OTRO_ADMIN, nombreAcceso: 'admin2', nombre: 'Rafael Núñez', email: null, activo: true, rol: 'admin' },
    { id: '33333333-3333-3333-3333-333333333333', nombreAcceso: 'gerente1', nombre: 'Carlos Díaz', email: null, activo: true, rol: 'gerente' },
  ],
  total: 3,
  page: 1,
  pageSize: 3,
  totalPages: 1,
};

const lista = <T>(data: T[]) => ({ data, total: data.length, page: 1, pageSize: data.length, totalPages: 1 });

// Formas exactas de `IpBloqueadaResumen` y `CuentaBloqueadaResumen` (UsuarioDtos.cs). Datos inventados.
const IP_TEMPORAL = { ip: '203.0.113.25', nivel: 1, fallos: 5, desde: '2026-10-28T14:00:00Z', hasta: '2026-10-28T14:15:00Z', indefinido: false };
const IP_INDEFINIDA = { ip: '198.51.100.7', nivel: 3, fallos: 20, desde: '2026-10-27T09:30:00Z', hasta: null, indefinido: true };
const CUENTA = { usuarioId: OTRO_ADMIN, nombreAcceso: 'admin2', nombre: 'Rafael Núñez', desde: '2026-10-28T14:00:00Z', hasta: '2026-10-28T14:30:00Z' };

test('bloqueos: lista las IPs bloqueadas, desbloquea desde la fila y refresca; la lista blanca es de solo lectura', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', '/seguridad/bloqueos/cuentas', { body: lista([]) });
  let bloqueadas = [IP_TEMPORAL, IP_INDEFINIDA];
  api.responder('GET', '/seguridad/bloqueos/ips', () => ({ body: lista(bloqueadas) }));
  api.responder('GET', '/seguridad/bloqueos/lista-blanca', { body: lista(['10.0.0.1', '192.0.2.10']) });
  api.responder('POST', '/seguridad/bloqueos/ip/desbloquear', (p) => {
    const ip = (p.cuerpo as { ip: string }).ip;
    if (!bloqueadas.some((b) => b.ip === ip)) return problema(404, 'REC-001', 'IP no bloqueada', 'Esa IP no tiene un bloqueo que levantar.');
    bloqueadas = bloqueadas.filter((b) => b.ip !== ip);
    return { body: { ip, desbloqueada: true } };
  });

  await page.goto('/bloqueos');
  await expect(page.getByRole('heading', { name: 'Bloqueos y acceso de emergencia', level: 1 })).toBeVisible();
  await page.getByRole('tab', { name: 'IPs' }).click();
  await expect(page.getByRole('row', { name: /203\.0\.113\.25/ })).toBeVisible();
  await expect(page.getByText('Hasta que un administrador la levante')).toBeVisible();
  await expect(page.getByText('192.0.2.10')).toBeVisible();
  await expect(page.getByText('Solo lectura: se cambia en la configuración del servidor')).toBeVisible();
  // Lista blanca sin controles de edición: solo un botón por IP bloqueada.
  await expect(page.getByRole('button', { name: 'Desbloquear', exact: true })).toHaveCount(2);
  await capturarClaroOscuro(page, 'bloqueos-ips', { paginaCompleta: true });

  await page.getByRole('row', { name: /203\.0\.113\.25/ }).getByRole('button', { name: 'Desbloquear' }).click();
  await expect(page.getByText('IP desbloqueada')).toBeVisible();
  expect(api.ultimoCuerpo('POST', '/seguridad/bloqueos/ip/desbloquear')).toEqual({ ip: '203.0.113.25' });
  await expect(page.getByRole('row', { name: /203\.0\.113\.25/ })).toHaveCount(0);
  expect(api.llamadas('GET', '/seguridad/bloqueos/ips')).toBe(2);
});

test('bloqueos: lista vacía y desbloqueo manual de una IP valida el formato y avisa si no estaba bloqueada', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', '/seguridad/bloqueos/cuentas', { body: lista([]) });
  api.responder('GET', '/seguridad/bloqueos/ips', { body: lista([]) });
  api.responder('GET', '/seguridad/bloqueos/lista-blanca', { body: lista([]) });
  api.responder('POST', '/seguridad/bloqueos/ip/desbloquear', (p) =>
    (p.cuerpo as { ip: string }).ip === '203.0.113.25'
      ? { body: { ip: '203.0.113.25', desbloqueada: true } }
      : problema(404, 'REC-001', 'IP no bloqueada', 'Esa IP no tiene un bloqueo que levantar.'),
  );

  await page.goto('/bloqueos');
  await page.getByRole('tab', { name: 'IPs' }).click();
  await expect(page.getByText('No hay IPs bloqueadas.')).toBeVisible();
  await expect(page.getByText('La lista blanca está vacía.')).toBeVisible();

  await page.getByLabel('Dirección IP').fill('999.1.1.1');
  await page.getByRole('button', { name: 'Desbloquear IP' }).click();
  await expect(page.getByText('Escribe una dirección IPv4 o IPv6 válida.')).toBeVisible();
  expect(api.llamadas('POST', '/seguridad/bloqueos/ip/desbloquear')).toBe(0);

  await page.getByLabel('Dirección IP').fill('198.51.100.7');
  await page.getByRole('button', { name: 'Desbloquear IP' }).click();
  await expect(page.getByText('Esa IP no tiene un bloqueo que levantar.')).toBeVisible();

  await page.getByLabel('Dirección IP').fill('203.0.113.25');
  await page.getByRole('button', { name: 'Desbloquear IP' }).click();
  await expect(page.getByText('IP desbloqueada')).toBeVisible();
  expect(api.ultimoCuerpo('POST', '/seguridad/bloqueos/ip/desbloquear')).toEqual({ ip: '203.0.113.25' });
});

test('bloqueos: lista las cuentas bloqueadas y desbloquea desde la fila', async ({ page, api }) => {
  conSesion(api);
  let bloqueadas = [CUENTA];
  api.responder('GET', '/seguridad/bloqueos/cuentas', () => ({ body: lista(bloqueadas) }));
  api.responder('POST', '/seguridad/bloqueos/cuenta/desbloquear', () => {
    bloqueadas = [];
    return { body: { usuarioId: OTRO_ADMIN, desbloqueada: true } };
  });

  await page.goto('/bloqueos');
  await expect(page.getByRole('row', { name: /admin2/ })).toBeVisible();
  await capturarClaroOscuro(page, 'bloqueos-cuentas');
  await page.getByRole('row', { name: /admin2/ }).getByRole('button', { name: 'Desbloquear' }).click();
  await expect(page.getByText('Cuenta desbloqueada')).toBeVisible();
  expect(api.ultimoCuerpo('POST', '/seguridad/bloqueos/cuenta/desbloquear')).toEqual({ usuarioId: OTRO_ADMIN });
  await expect(page.getByText('No hay cuentas bloqueadas.')).toBeVisible();
});

test('emergencia: pedir acceso para otro admin y aprobar una solicitud muestra el código una sola vez', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', '/seguridad/bloqueos/cuentas', { body: lista([]) });
  api.responder('GET', '/usuarios', { body: USUARIOS });
  api.responder('GET', '/auth/emergencia/solicitudes', { body: lista([]) });
  api.responder('POST', '/auth/emergencia/solicitar', { body: { id: SOLICITUD, caduca: null, codigoAcceso: null } });
  api.responder('POST', '/auth/emergencia/aprobar', { body: { id: SOLICITUD, caduca: '2026-10-28T16:00:00Z', codigoAcceso: 'K7Q2-M9XD-41PA' } });

  await page.goto('/bloqueos');
  await page.getByRole('tab', { name: 'Acceso de emergencia' }).click();

  await page.getByRole('button', { name: 'Administrador bloqueado' }).click();
  // Solo aparecen otros administradores activos: ni uno mismo ni gerentes.
  await expect(page.getByRole('option')).toHaveCount(1);
  await page.getByRole('option', { name: /Rafael Núñez/ }).click();
  await page.getByLabel('Motivo').fill('corto');
  await page.getByRole('button', { name: 'Pedir acceso' }).click();
  await expect(page.getByText('El motivo necesita al menos 10 caracteres.')).toBeVisible();
  await page.getByLabel('Motivo').fill('Perdió el móvil y los códigos de recuperación');
  await page.getByRole('button', { name: 'Pedir acceso' }).click();
  await expect(page.getByText('Solicitud creada')).toBeVisible();
  expect(api.ultimoCuerpo('POST', '/auth/emergencia/solicitar')).toEqual({
    usuarioObjetivoId: OTRO_ADMIN,
    motivo: 'Perdió el móvil y los códigos de recuperación',
  });

  await page.getByLabel('Identificador de la solicitud').last().fill(SOLICITUD);
  await page.getByRole('button', { name: 'Aprobar solicitud' }).click();
  await expect(page.getByText('K7Q2-M9XD-41PA')).toBeVisible();
  await expect(page.getByText('no se vuelve a mostrar')).toBeVisible();
  expect(api.ultimoCuerpo('POST', '/auth/emergencia/aprobar')).toEqual({ solicitudId: SOLICITUD });
  await capturarClaroOscuro(page, 'bloqueos-emergencia', { paginaCompleta: true });
});

// Forma exacta de `SolicitudEmergenciaResumen` (UsuarioDtos.cs): nunca lleva el código.
const SOLICITUD_PENDIENTE = {
  id: SOLICITUD,
  usuarioId: OTRO_ADMIN,
  usuarioNombreAcceso: 'admin2',
  solicitanteId: '77777777-7777-7777-7777-777777777777',
  solicitanteNombreAcceso: 'admin3',
  motivo: 'Perdió el móvil y los códigos de recuperación',
  creadoAt: '2026-10-28T13:00:00Z',
  aprobadoPorId: null,
  aprobadoPorNombreAcceso: null,
  aprobadoAt: null,
  caducaAt: null,
  usadoAt: null,
  estado: 'pendiente',
};
const SOLICITUD_USADA = {
  ...SOLICITUD_PENDIENTE,
  id: '88888888-8888-8888-8888-888888888888',
  estado: 'usada',
  aprobadoPorId: ADMIN_ID,
  aprobadoPorNombreAcceso: 'admin',
  aprobadoAt: '2026-10-27T10:00:00Z',
  usadoAt: '2026-10-27T10:05:00Z',
};
const SOLICITUD_PROPIA = { ...SOLICITUD_PENDIENTE, id: '99999999-9999-9999-9999-999999999999', usuarioNombreAcceso: 'admin4', solicitanteId: ADMIN_ID, solicitanteNombreAcceso: 'admin' };

test('emergencia: lista las solicitudes pendientes, filtra por estado y aprueba desde la fila', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', '/seguridad/bloqueos/cuentas', { body: lista([]) });
  api.responder('GET', '/usuarios', { body: USUARIOS });
  let pendientes = [SOLICITUD_PENDIENTE, SOLICITUD_PROPIA];
  api.responder('GET', '/auth/emergencia/solicitudes', (p) => ({
    body: lista(p.query.includes('estado=todas') ? [...pendientes, SOLICITUD_USADA] : p.query.includes('estado=pendiente') ? pendientes : []),
  }));
  api.responder('POST', '/auth/emergencia/aprobar', () => {
    pendientes = [SOLICITUD_PROPIA];
    return { body: { id: SOLICITUD, caduca: '2026-10-28T16:00:00Z', codigoAcceso: 'K7Q2-M9XD-41PA' } };
  });

  await page.goto('/bloqueos');
  await page.getByRole('tab', { name: 'Acceso de emergencia' }).click();
  // Por defecto, las pendientes.
  await expect(page.getByRole('row', { name: /admin3/ })).toBeVisible();
  // La propia solicitud no se puede aprobar.
  await expect(page.getByRole('row', { name: /admin4/ }).getByRole('button', { name: 'Aprobar' })).toBeDisabled();
  await capturarClaroOscuro(page, 'bloqueos-emergencia-solicitudes', { paginaCompleta: true });

  await page.getByRole('button', { name: 'Estado' }).click();
  await page.getByRole('option', { name: 'Todas' }).click();
  await expect(page.getByRole('row', { name: /Usada/ })).toBeVisible();

  await page.getByRole('button', { name: 'Estado' }).click();
  await page.getByRole('option', { name: 'Pendientes' }).click();
  await page.getByRole('row', { name: /admin3/ }).getByRole('button', { name: 'Aprobar' }).click();
  expect(api.ultimoCuerpo('POST', '/auth/emergencia/aprobar')).toEqual({ solicitudId: SOLICITUD });
  await expect(page.getByText('K7Q2-M9XD-41PA')).toBeVisible();
  await expect(page.getByRole('row', { name: /admin3/ })).toHaveCount(0);
});

test('bloqueos: sin acciones de seguridad no hay entrada ni pestañas', async ({ page, api }) => {
  conSesion(api, EMPLEADO);
  await page.goto('/bloqueos');
  await expect(page.getByRole('heading', { name: 'Sin permiso', level: 1 })).toBeVisible();

  // Un usuario con ip.desbloquear pero sin el acceso de emergencia no ve esa pestaña.
  const parcial = identidad({ permisosEfectivos: ['sesion.ver', 'sesion.cerrar', 'sesion.password', 'ip.desbloquear'] });
  conSesion(api, parcial);
  api.responder('GET', '/seguridad/bloqueos/ips', { body: lista([]) });
  api.responder('GET', '/seguridad/bloqueos/lista-blanca', { body: lista([]) });
  await page.goto('/bloqueos');
  await expect(page.getByRole('tab', { name: 'IPs' })).toBeVisible();
  await expect(page.getByRole('tab', { name: 'Acceso de emergencia' })).toHaveCount(0);
  await expect(page.getByRole('tab', { name: 'Cuentas' })).toHaveCount(0);
});

test('bloqueos: gerente y empleado no ven la entrada y por URL llegan a sin permiso', async ({ page, api }) => {
  const gerente = identidad({
    nombreAcceso: 'gerente1',
    rol: 'gerente',
    permisosEfectivos: [ACCIONES.clienteVer, ACCIONES.facturaVer, ACCIONES.informeVer, ACCIONES.sesionVer, ACCIONES.sesionCerrar, ACCIONES.sesionPassword],
  });
  for (const quien of [gerente, EMPLEADO]) {
    conSesion(api, quien);
    await page.goto('/bloqueos');
    await expect(page.getByRole('heading', { name: 'Sin permiso', level: 1 })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Bloqueos' })).toHaveCount(0);
  }
  expect(api.llamadas('GET', '/seguridad/bloqueos/ips')).toBe(0);
  expect(api.llamadas('GET', '/seguridad/bloqueos/cuentas')).toBe(0);
  expect(api.llamadas('GET', '/auth/emergencia/solicitudes')).toBe(0);
});
