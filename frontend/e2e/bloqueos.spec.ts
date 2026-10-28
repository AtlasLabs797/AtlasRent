import { problema } from './apiFalsa';
import { capturarClaroOscuro } from './capturas';
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

test('bloqueos: desbloquear una IP valida el formato, llama a la API y avisa si no estaba bloqueada', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', '/usuarios', { body: USUARIOS });
  api.responder('POST', '/seguridad/bloqueos/ip/desbloquear', (p) =>
    (p.cuerpo as { ip: string }).ip === '203.0.113.25'
      ? { body: { ip: '203.0.113.25', desbloqueada: true } }
      : problema(404, 'REC-001', 'IP no bloqueada', 'Esa IP no tiene un bloqueo que levantar.'),
  );

  await page.goto('/bloqueos');
  await expect(page.getByRole('heading', { name: 'Bloqueos y acceso de emergencia', level: 1 })).toBeVisible();
  await page.getByRole('tab', { name: 'IPs' }).click();
  await capturarClaroOscuro(page, 'bloqueos-ips');

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

test('bloqueos: desbloquear una cuenta desde la lista de usuarios', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', '/usuarios', { body: USUARIOS });
  api.responder('POST', '/seguridad/bloqueos/cuenta/desbloquear', { body: { usuarioId: OTRO_ADMIN, desbloqueada: true } });

  await page.goto('/bloqueos');
  await expect(page.getByText('Atlas Rent no informa de qué cuentas están bloqueadas')).toBeVisible();
  await capturarClaroOscuro(page, 'bloqueos-cuentas');
  await page.getByRole('row', { name: /admin2/ }).getByRole('button', { name: 'Desbloquear' }).click();
  await expect(page.getByText('Cuenta desbloqueada')).toBeVisible();
  expect(api.ultimoCuerpo('POST', '/seguridad/bloqueos/cuenta/desbloquear')).toEqual({ usuarioId: OTRO_ADMIN });
});

test('emergencia: pedir acceso para otro admin y aprobar una solicitud muestra el código una sola vez', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', '/usuarios', { body: USUARIOS });
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

test('bloqueos: sin acciones de seguridad no hay entrada ni pestañas', async ({ page, api }) => {
  conSesion(api, EMPLEADO);
  await page.goto('/bloqueos');
  await expect(page.getByRole('heading', { name: 'Sin permiso', level: 1 })).toBeVisible();

  // Un usuario con ip.desbloquear pero sin el acceso de emergencia no ve esa pestaña.
  const parcial = identidad({ permisosEfectivos: ['sesion.ver', 'sesion.cerrar', 'sesion.password', 'ip.desbloquear'] });
  conSesion(api, parcial);
  await page.goto('/bloqueos');
  await expect(page.getByRole('tab', { name: 'IPs' })).toBeVisible();
  await expect(page.getByRole('tab', { name: 'Acceso de emergencia' })).toHaveCount(0);
  await expect(page.getByRole('tab', { name: 'Cuentas' })).toHaveCount(0);
});
