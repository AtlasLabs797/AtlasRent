import type { Page } from '@playwright/test';
import { problema } from './apiFalsa';
import { capturarClaroOscuro } from './capturas';
import { expect, test } from './prueba';
import { DESAFIO_ALTA_MFA, DESAFIO_MFA, conSesion, identidad, sinSesion } from './fixtures';

async function rellenarCredenciales(page: Page) {
  await page.getByLabel('Usuario').fill('admin');
  await page.getByLabel('Contraseña').fill('una-contrasena-de-prueba');
  await page.getByRole('button', { name: 'Entrar' }).click();
}

test('login simple: usuario y contraseña abren la sesión y enseñan el panel', async ({ page, api }) => {
  sinSesion(api);
  api.responder('POST', '/auth/login', () => {
    conSesion(api);
    return { body: identidad() };
  });

  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Entrar' })).toBeVisible();
  await rellenarCredenciales(page);

  await expect(page.getByRole('heading', { name: 'Facturación y alquileres', level: 1 })).toBeVisible();
  await expect(page.getByText('Marta Peña')).toBeVisible();
  expect(api.ultimoCuerpo('POST', '/auth/login')).toEqual({ nombreAcceso: 'admin', contrasena: 'una-contrasena-de-prueba' });
});

test('login con credenciales incorrectas: mensaje del producto y código de referencia', async ({ page, api }) => {
  sinSesion(api);
  api.responder('POST', '/auth/login', problema(401, 'AUT-001', 'No autenticado', 'Usuario o contraseña incorrectos.'));

  await page.goto('/');
  await rellenarCredenciales(page);

  await expect(page.getByText('Usuario o contraseña incorrectos.')).toBeVisible();
  await expect(page.getByText('Código de referencia: AUT-001')).toBeVisible();
  // El 401 del login no debe provocar un intento de refresco: solo el de /auth/yo al cargar.
  expect(api.llamadas('POST', '/auth/refresh')).toBe(1);
});

test('login con TOTP: pide el segundo factor, rechaza un código malo y acepta el bueno', async ({ page, api }) => {
  sinSesion(api);
  api.responder('POST', '/auth/login', { body: DESAFIO_MFA });
  api.responder('POST', '/auth/mfa/verificar', (p) =>
    (p.cuerpo as { codigo: string }).codigo === '123456'
      ? (conSesion(api), { body: identidad() })
      : problema(401, 'SEG-004', 'Segundo factor no válido', 'El código no es válido o el token provisional caducó.'),
  );

  await page.goto('/');
  await rellenarCredenciales(page);

  await expect(page.getByRole('heading', { name: 'Verificación en dos pasos' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Usar un código de recuperación' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Usar un código de acceso de emergencia' })).toBeVisible();
  await capturarClaroOscuro(page, 'acceso-totp');

  await page.getByLabel(/^Código( \*)?$/).fill('000000');
  await page.getByRole('button', { name: 'Verificar' }).click();
  await expect(page.getByText('El código no es válido o ha caducado')).toBeVisible();

  await page.getByLabel(/^Código( \*)?$/).fill('123456');
  await page.getByRole('button', { name: 'Verificar' }).click();
  await expect(page.getByRole('heading', { name: 'Facturación y alquileres', level: 1 })).toBeVisible();
});

test('login con código de recuperación y con acceso de emergencia', async ({ page, api }) => {
  sinSesion(api);
  api.responder('POST', '/auth/login', { body: DESAFIO_MFA });
  api.responder('POST', '/auth/mfa/recuperacion', () => (conSesion(api), { body: identidad() }));

  await page.goto('/');
  await rellenarCredenciales(page);
  await page.getByRole('button', { name: 'Usar un código de recuperación' }).click();
  await expect(page.getByRole('heading', { name: 'Código de recuperación' })).toBeVisible();

  // Cambia a emergencia sin perder el paso: el token provisional sigue en la cookie.
  await page.getByRole('button', { name: 'Usar un código de acceso de emergencia' }).click();
  await expect(page.getByRole('heading', { name: 'Acceso de emergencia' })).toBeVisible();
  await capturarClaroOscuro(page, 'acceso-emergencia');

  await page.getByRole('button', { name: 'Usar un código de recuperación' }).click();
  await page.getByLabel('Código de recuperación').fill('ABCDE-FGHIJ');
  await page.getByRole('button', { name: 'Entrar con el código' }).click();
  await expect(page.getByRole('heading', { name: 'Facturación y alquileres', level: 1 })).toBeVisible();
  expect(api.ultimoCuerpo('POST', '/auth/mfa/recuperacion')).toEqual({ codigo: 'ABCDE-FGHIJ' });
});

test('alta de MFA: clave copiable, confirmación, códigos de recuperación una sola vez y último paso', async ({ page, api }) => {
  sinSesion(api);
  api.responder('POST', '/auth/login', { body: DESAFIO_ALTA_MFA });
  api.responder('POST', '/auth/mfa/activar', {
    body: { uri: 'otpauth://totp/AtlasRent:admin?secret=JBSWY3DPEHPK3PXP&issuer=AtlasRent', digitos: 6, periodoSegundos: 30 },
  });
  api.responder('POST', '/auth/mfa/confirmar', (p) =>
    (p.cuerpo as { codigo: string }).codigo === '654321'
      ? { body: { codigosRecuperacion: ['AAAAA-BBBBB', 'CCCCC-DDDDD', 'EEEEE-FFFFF'] } }
      : problema(401, 'SEG-004', 'Código no válido', 'El código no es válido o el alta no está pendiente.'),
  );
  api.responder('POST', '/auth/mfa/verificar', () => (conSesion(api), { body: identidad() }));

  await page.goto('/');
  await rellenarCredenciales(page);

  await expect(page.getByRole('heading', { name: 'Configura la verificación en dos pasos' })).toBeVisible();
  await expect(page.getByLabel('Clave de configuración', { exact: true })).toHaveText('JBSWY3DPEHPK3PXP');
  await expect(page.getByLabel('Enlace de configuración', { exact: true })).toContainText('otpauth://totp/');
  await capturarClaroOscuro(page, 'acceso-alta-mfa', { paginaCompleta: true });

  await page.getByLabel('Código de 6 dígitos').fill('111111');
  await page.getByRole('button', { name: 'Confirmar y activar' }).click();
  await expect(page.getByText('El código no es válido. Comprueba la hora')).toBeVisible();

  await page.getByLabel('Código de 6 dígitos').fill('654321');
  await page.getByRole('button', { name: 'Confirmar y activar' }).click();
  await expect(page.getByRole('heading', { name: 'Guarda tus códigos de recuperación' })).toBeVisible();
  await expect(page.getByLabel('Copiar los códigos', { exact: true })).toContainText('AAAAA-BBBBB');
  await capturarClaroOscuro(page, 'acceso-codigos-recuperacion', { paginaCompleta: true });

  // No se puede seguir sin confirmar que se han guardado.
  const continuar = page.getByRole('button', { name: 'Continuar' });
  await expect(continuar).toBeDisabled();
  await page.getByLabel('He guardado los códigos').check();
  await continuar.click();

  await expect(page.getByText('Para terminar, escribe el código que muestra ahora tu aplicación.')).toBeVisible();
  await page.getByLabel(/^Código( \*)?$/).fill('222222');
  await page.getByRole('button', { name: 'Verificar' }).click();
  await expect(page.getByRole('heading', { name: 'Facturación y alquileres', level: 1 })).toBeVisible();
});

test('cambio de contraseña forzado: sustituye al menú, valida en cliente y pinta el SEG-011 del backend', async ({ page, api }) => {
  conSesion(api, identidad({ debeCambiarPassword: true }));
  let intentos = 0;
  api.responder('POST', '/auth/password', () => {
    intentos++;
    return intentos === 1
      ? problema(400, 'SEG-011', 'Contraseña no válida', 'Esa contraseña aparece en filtraciones de datos conocidas. Elige otra.')
      : { body: { contrasenaComprobada: true } };
  });

  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Cambia tu contraseña para continuar' })).toBeVisible();
  await expect(page.getByRole('navigation')).toHaveCount(0);
  await capturarClaroOscuro(page, 'cambio-password-forzado');

  await page.getByLabel('Contraseña actual').fill('la-contrasena-actual');
  await page.getByLabel('Contraseña nueva', { exact: true }).fill('corta');
  await page.getByLabel('Repite la contraseña nueva').fill('otra');
  await page.getByRole('button', { name: 'Cambiar contraseña' }).click();
  await expect(page.getByText('Debe tener entre 12 y 72 caracteres.')).toBeVisible();
  await expect(page.getByText('Las contraseñas no coinciden.')).toBeVisible();
  expect(api.llamadas('POST', '/auth/password')).toBe(0);

  await page.getByLabel('Contraseña nueva', { exact: true }).fill('una-contrasena-larga-1');
  await page.getByLabel('Repite la contraseña nueva').fill('una-contrasena-larga-1');
  await page.getByRole('button', { name: 'Cambiar contraseña' }).click();
  await expect(page.getByText('Esa contraseña aparece en filtraciones de datos conocidas. Elige otra.')).toBeVisible();

  await page.getByRole('button', { name: 'Cambiar contraseña' }).click();
  await expect(page.getByRole('heading', { name: 'Facturación y alquileres', level: 1 })).toBeVisible();
  expect(api.ultimoCuerpo('POST', '/auth/password')).toEqual({
    contrasenaActual: 'la-contrasena-actual',
    contrasenaNueva: 'una-contrasena-larga-1',
  });
});

test('un 403 SEG-010 en mitad de la sesión lleva a la pantalla de cambio de contraseña', async ({ page, api }) => {
  conSesion(api);
  api.responder('POST', '/ia/consulta', problema(403, 'SEG-010', 'Cambio de contraseña pendiente', 'Debes cambiar tu contraseña antes de seguir usando la aplicación.'));

  await page.goto('/asistente');
  await page.getByLabel('Pregunta', { exact: true }).fill('Cuántos contratos vencen este mes');
  await page.getByRole('button', { name: 'Consultar' }).click();

  await expect(page.getByRole('heading', { name: 'Cambia tu contraseña para continuar' })).toBeVisible();
});

test('refresco transparente: un 401 pide un solo refresco, repite la petición y el usuario no se entera', async ({ page, api }) => {
  conSesion(api);
  api.responder('POST', '/auth/refresh', { body: identidad() });
  api.responder('POST', '/ia/consulta', (p) =>
    p.numero === 1
      ? problema(401, 'AUT-002', 'No autenticado', 'Falta la cookie de sesión o no es válida.')
      : { body: { respuesta: 'Dos contratos vencen este mes.', alcance: 'contratos', proveedor: 'proveedor-de-prueba', modelo: 'modelo-de-prueba' } },
  );

  await page.goto('/asistente');
  await page.getByLabel('Pregunta', { exact: true }).fill('Cuántos contratos vencen este mes');
  await page.getByRole('button', { name: 'Consultar' }).click();

  await expect(page.getByText('Dos contratos vencen este mes.')).toBeVisible();
  expect(api.llamadas('POST', '/auth/refresh')).toBe(1);
  expect(api.llamadas('POST', '/ia/consulta')).toBe(2);
});

test('si el refresco falla la sesión pasa a sin-sesion, sin bucle de peticiones', async ({ page, api }) => {
  conSesion(api);
  api.responder('POST', '/auth/refresh', problema(401, 'AUT-004', 'No autenticado', 'El token de refresco no es válido.'));
  api.responder('POST', '/ia/consulta', problema(401, 'AUT-003', 'No autenticado', 'La sesión ha caducado.'));

  await page.goto('/asistente');
  await page.getByLabel('Pregunta', { exact: true }).fill('Una pregunta');
  await page.getByRole('button', { name: 'Consultar' }).click();

  await expect(page.getByRole('heading', { name: 'Entrar' })).toBeVisible();
  expect(api.llamadas('POST', '/auth/refresh')).toBe(1);
  expect(api.llamadas('POST', '/ia/consulta')).toBe(1);
});

test('capturas: pantalla de acceso, panel con menú, menú de usuario, Mi cuenta y componentes en claro y oscuro', async ({ page, api }) => {
  sinSesion(api);
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Entrar' })).toBeVisible();
  await capturarClaroOscuro(page, 'acceso');

  conSesion(api);
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Facturación y alquileres', level: 1 })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Informes' }).or(page.getByText('Informes').first())).toBeVisible();
  await capturarClaroOscuro(page, 'layout-panel');

  await page.getByRole('button', { name: 'Mi cuenta' }).click();
  await expect(page.getByRole('menuitem', { name: 'Mi cuenta' })).toBeVisible();
  await capturarClaroOscuro(page, 'layout-menu-usuario');
  await page.getByRole('menuitem', { name: 'Mi cuenta' }).click();
  await expect(page.getByRole('heading', { name: 'Mi cuenta', level: 1 })).toBeVisible();
  await capturarClaroOscuro(page, 'mi-cuenta', { paginaCompleta: true });

  await page.goto('/componentes');
  await expect(page.getByRole('heading', { name: 'Resto de componentes y patrones' })).toBeVisible();
  await capturarClaroOscuro(page, 'componentes', { paginaCompleta: true });
});
