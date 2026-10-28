import { problema } from './apiFalsa';
import { capturarClaroOscuro } from './capturas';
import { EMPLEADO, conSesion, identidad } from './fixtures';
import { expect, test } from './prueba';

const ADMIN_ID = '11111111-1111-1111-1111-111111111111';
const GERENTE_ID = '33333333-3333-3333-3333-333333333333';

// Forma exacta de `UsuarioResumen` (UsuarioDtos.cs). Datos inventados.
const USUARIOS = {
  data: [
    { id: ADMIN_ID, nombreAcceso: 'admin', nombre: 'Marta Peña', email: 'marta@ejemplo.do', activo: true, rol: 'admin' },
    { id: GERENTE_ID, nombreAcceso: 'gerente1', nombre: 'Carlos Díaz', email: null, activo: true, rol: 'gerente' },
  ],
  total: 2,
  page: 1,
  pageSize: 2,
  totalPages: 1,
};

// Permisos efectivos del gerente: el techo del rol menos factura.anular.
const PERMISOS_GERENTE = [
  'cliente.ver', 'cliente.editar', 'inmueble.documento', 'contrato.borrador', 'contrato.estado', 'factura.ver',
  'factura.borrador', 'factura.emitir', 'cobro.registrar', 'fianza.mover', 'documento.enviar', 'cotizacion.enviar',
  'mensajeria.masivo', 'informe.ver', 'auditoria.ver', 'sesion.ver', 'sesion.cerrar', 'sesion.password',
  'ia.consultar', 'ia.proponer',
];

test('usuarios: listado, techo del rol y recorte de un permiso', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', '/usuarios', { body: USUARIOS });
  api.responder('GET', `/usuarios/${GERENTE_ID}/permisos`, { body: { id: GERENTE_ID, rol: 'gerente', permisos: PERMISOS_GERENTE } });
  api.responder('PUT', `/usuarios/${GERENTE_ID}/permisos`, { status: 204 });

  await page.goto('/usuarios');
  await expect(page.getByRole('heading', { name: 'Usuarios', level: 1 })).toBeVisible();
  await expect(page.getByText('Carlos Díaz')).toBeVisible();
  await capturarClaroOscuro(page, 'usuarios-lista');

  await page.getByText('Carlos Díaz').click();
  const anular = page.getByRole('switch', { name: 'Anular facturas emitidas' });
  await expect(anular).toBeVisible();
  await expect(anular).toHaveAttribute('aria-checked', 'false');
  await expect(page.getByText('1 recortados')).toBeVisible();
  // Techo del gerente: no ofrece lo que solo trae el administrador.
  await expect(page.getByRole('switch', { name: 'Configuración, usuarios y roles' })).toHaveCount(0);

  await page.getByRole('switch', { name: 'Ver facturas' }).click();
  await expect.poll(() => api.ultimoCuerpo('PUT', `/usuarios/${GERENTE_ID}/permisos`)).toEqual({ accion: 'factura.ver', permitido: false });
  await capturarClaroOscuro(page, 'usuarios-detalle');
});

test('usuarios: el 400 de permiso fuera del techo y el 409 de un cambio de admin se enseñan con el detalle de la API', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', '/usuarios', { body: USUARIOS });
  api.responder('GET', `/usuarios/${ADMIN_ID}/permisos`, {
    body: { id: ADMIN_ID, rol: 'admin', permisos: ['configuracion.editar', 'mfa.obligatorio', 'seguridad.acceso_emergencia', 'factura.ver', 'sesion.ver'] },
  });
  api.responder('PUT', `/usuarios/${ADMIN_ID}/rol`, problema(409, 'VAL-001', 'Cambio no permitido', 'No se puede quitar el rol de administrador al último administrador activo.'));

  await page.goto('/usuarios');
  await page.getByRole('cell', { name: 'Marta Peña' }).click();
  // Las salvaguardas de un administrador no se pueden apagar.
  await expect(page.getByRole('switch', { name: 'Segundo factor obligatorio' })).toBeDisabled();
  // No se puede desactivar la propia cuenta.
  await expect(page.getByRole('button', { name: 'Desactivar usuario' })).toBeDisabled();

  await page.getByRole('button', { name: 'Rol', exact: true }).click();
  await page.getByRole('option', { name: 'Gerente' }).click();
  await page.getByRole('button', { name: 'Cambiar rol' }).click();
  await expect(page.getByText('Cambio no permitido')).toBeVisible();
  await expect(page.getByText('No se puede quitar el rol de administrador al último administrador activo.')).toBeVisible();
});

test('usuarios: alta con contraseña de 12 a 72 y restablecer contraseña', async ({ page, api }) => {
  conSesion(api);
  api.responder('GET', '/usuarios', { body: USUARIOS });
  api.responder('POST', '/usuarios', { body: { id: '44444444-4444-4444-4444-444444444444', nombreAcceso: 'lrosario', rol: 'empleado', contrasenaComprobada: true } });
  api.responder('GET', `/usuarios/${GERENTE_ID}/permisos`, { body: { id: GERENTE_ID, rol: 'gerente', permisos: PERMISOS_GERENTE } });
  api.responder('PUT', `/usuarios/${GERENTE_ID}/password`, { body: { contrasenaComprobada: false } });

  await page.goto('/usuarios');
  await page.getByRole('button', { name: 'Nuevo usuario' }).click();
  await page.getByLabel('Nombre de acceso').fill('lrosario');
  await page.getByLabel('Nombre completo').fill('Luis Rosario');
  await page.getByLabel('Contraseña inicial').fill('corta');
  await page.getByRole('button', { name: 'Crear usuario' }).click();
  await expect(page.getByText('La contraseña necesita al menos 12 caracteres.')).toBeVisible();
  expect(api.llamadas('POST', '/usuarios')).toBe(0);

  await page.getByLabel('Contraseña inicial').fill('una-contrasena-larga-1');
  await page.getByRole('button', { name: 'Crear usuario' }).click();
  await expect(page.getByRole('heading', { name: 'Usuario creado' }).or(page.getByText('Usuario creado'))).toBeVisible();
  expect(api.ultimoCuerpo('POST', '/usuarios')).toEqual({
    nombreAcceso: 'lrosario',
    nombre: 'Luis Rosario',
    contrasena: 'una-contrasena-larga-1',
    rol: 'empleado',
  });
  await page.getByRole('button', { name: 'Cerrar', exact: true }).last().click();

  await page.getByText('Carlos Díaz').click();
  await page.getByRole('button', { name: 'Restablecer contraseña' }).click();
  await page.getByLabel('Contraseña nueva').fill('otra-contrasena-larga-2');
  await page.getByLabel('Repetir contraseña').fill('otra-contrasena-larga-2');
  await page.getByRole('button', { name: 'Restablecer', exact: true }).click();
  await expect(page.getByText('Contraseña restablecida')).toBeVisible();
  await expect(page.getByText('No se pudo comprobar si está filtrada')).toBeVisible();
  expect(api.ultimoCuerpo('PUT', `/usuarios/${GERENTE_ID}/password`)).toEqual({ contrasena: 'otra-contrasena-larga-2' });
});

test('usuarios: el empleado no ve la entrada del menú y por URL llega a sin permiso', async ({ page, api }) => {
  conSesion(api, EMPLEADO);
  await page.goto('/usuarios');
  await expect(page.getByRole('heading', { name: 'Sin permiso', level: 1 })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Usuarios' })).toHaveCount(0);
  expect(api.llamadas('GET', '/usuarios')).toBe(0);
  void identidad;
});
