import assert from 'node:assert/strict';
import { test } from 'node:test';
import { AxiosError, type AxiosAdapter, type InternalAxiosRequestConfig } from 'axios';
import { clienteHttp, registrarManejadoresSesion } from '../src/api/cliente.ts';
import {
  ErrorApi,
  aErrorApi,
  claveDeCampo,
  erroresPorCampo,
  mensajeDeError,
  repartirErrores,
} from '../src/api/errores.ts';
import { crearRefrescador, debeRefrescar, refrescoDefinitivo } from '../src/api/refresco.ts';

// El interceptor de petición lee la cookie de CSRF del documento.
Object.assign(globalThis, { document: { cookie: 'atlas-rent-csrf=tok%3D1' } });

function respuesta(config: InternalAxiosRequestConfig, status: number, data: unknown) {
  return { data, status, statusText: String(status), headers: {}, config };
}

/** Adaptador falso: cada ruta responde con lo que diga `rutas`, contando las llamadas. */
function simular(rutas: Record<string, (n: number, config: InternalAxiosRequestConfig) => { status: number; data?: unknown }>) {
  const llamadas: Record<string, number> = {};
  const adaptador: AxiosAdapter = async (config) => {
    const clave = `${(config.method ?? 'get').toUpperCase()} ${config.url}`;
    llamadas[clave] = (llamadas[clave] ?? 0) + 1;
    const manejador = rutas[clave];
    assert.ok(manejador, `ruta no simulada: ${clave}`);
    const { status, data } = manejador(llamadas[clave]!, config);
    const r = respuesta(config, status, data ?? {});
    if (status >= 400) throw new AxiosError(`HTTP ${status}`, 'ERR_BAD_REQUEST', config, null, r);
    return r;
  };
  clienteHttp.defaults.adapter = adaptador;
  return llamadas;
}

test('solo un 401 fuera de las rutas de autenticación y sin repetir justifica refrescar', () => {
  assert.equal(debeRefrescar(401, '/clientes', false), true);
  assert.equal(debeRefrescar(401, '/auth/yo', false), true);
  assert.equal(debeRefrescar(401, '/clientes', true), false, 'una repetida no vuelve a refrescar');
  assert.equal(debeRefrescar(403, '/clientes', false), false);
  assert.equal(debeRefrescar(500, '/clientes', false), false);
  for (const ruta of ['/auth/login', '/auth/refresh', '/auth/logout', '/auth/mfa/verificar', '/auth/emergencia/entrar']) {
    assert.equal(debeRefrescar(401, ruta, false), false, ruta);
  }
});

test('el refrescador es single-flight: las concurrentes esperan al mismo y después se puede refrescar de nuevo', async () => {
  let llamadas = 0;
  const refrescar = crearRefrescador(async () => {
    llamadas++;
    await new Promise((r) => setTimeout(r, 10));
    return llamadas;
  });
  const [a, b, c] = await Promise.all([refrescar(), refrescar(), refrescar()]);
  assert.deepEqual([a, b, c], [1, 1, 1]);
  assert.equal(await refrescar(), 2);
});

test('un refresco que falla deja libre el siguiente intento', async () => {
  let n = 0;
  const refrescar = crearRefrescador(async () => {
    n++;
    if (n === 1) throw new Error('boom');
    return n;
  });
  await assert.rejects(refrescar());
  assert.equal(await refrescar(), 2);
});

test('un 4xx del refresco es definitivo y un fallo de red o un 5xx no', () => {
  assert.equal(refrescoDefinitivo(401), true);
  assert.equal(refrescoDefinitivo(403), true);
  assert.equal(refrescoDefinitivo(500), false);
  assert.equal(refrescoDefinitivo(undefined), false);
});

test('interceptor: un 401 hace UN refresco compartido y repite cada petición una vez', async () => {
  let identidades = 0;
  registrarManejadoresSesion({ sesionPerdida: () => assert.fail('no debe perder la sesión'), identidadRefrescada: () => identidades++, passwordPendiente: () => {} });
  const llamadas = simular({
    'GET /a': (n) => (n === 1 ? { status: 401 } : { status: 200, data: { ok: 'a' } }),
    'GET /b': (n) => (n === 1 ? { status: 401 } : { status: 200, data: { ok: 'b' } }),
    'GET /c': (n) => (n === 1 ? { status: 401 } : { status: 200, data: { ok: 'c' } }),
    'POST /auth/refresh': () => ({ status: 200, data: { id: 'u' } }),
  });
  const [a, b, c] = await Promise.all([clienteHttp.get('/a'), clienteHttp.get('/b'), clienteHttp.get('/c')]);
  assert.deepEqual([a.data, b.data, c.data], [{ ok: 'a' }, { ok: 'b' }, { ok: 'c' }]);
  assert.equal(llamadas['POST /auth/refresh'], 1, 'un solo refresco para tres peticiones');
  assert.equal(llamadas['GET /a'], 2);
  assert.equal(identidades, 1);
});

test('interceptor: si el refresco falla con 401 la sesión se pierde y no hay bucle', async () => {
  let perdidas = 0;
  registrarManejadoresSesion({ sesionPerdida: () => perdidas++, identidadRefrescada: () => {}, passwordPendiente: () => {} });
  const llamadas = simular({
    'GET /a': () => ({ status: 401 }),
    'POST /auth/refresh': () => ({ status: 401, data: { codigoReferencia: 'AUT-004' } }),
  });
  await assert.rejects(clienteHttp.get('/a'), (e) => aErrorApi(e).status === 401);
  assert.equal(perdidas, 1);
  assert.equal(llamadas['POST /auth/refresh'], 1);
  assert.equal(llamadas['GET /a'], 1, 'no se repite si no hubo refresco');
});

test('interceptor: si la repetición vuelve a dar 401 no se refresca otra vez', async () => {
  registrarManejadoresSesion({ sesionPerdida: () => {}, identidadRefrescada: () => {}, passwordPendiente: () => {} });
  const llamadas = simular({
    'GET /a': () => ({ status: 401 }),
    'POST /auth/refresh': () => ({ status: 200, data: {} }),
  });
  await assert.rejects(clienteHttp.get('/a'));
  assert.equal(llamadas['POST /auth/refresh'], 1);
  assert.equal(llamadas['GET /a'], 2);
});

test('interceptor: un fallo de red del refresco no cierra la sesión', async () => {
  let perdidas = 0;
  registrarManejadoresSesion({ sesionPerdida: () => perdidas++, identidadRefrescada: () => {}, passwordPendiente: () => {} });
  simular({
    'GET /a': () => ({ status: 401 }),
    'POST /auth/refresh': () => ({ status: 503 }),
  });
  await assert.rejects(clienteHttp.get('/a'));
  assert.equal(perdidas, 0);
});

test('interceptor: el 401 del login no intenta refrescar', async () => {
  const llamadas = simular({ 'POST /auth/login': () => ({ status: 401, data: { codigoReferencia: 'AUT-001' } }) });
  await assert.rejects(clienteHttp.post('/auth/login', {}));
  assert.equal(llamadas['POST /auth/login'], 1);
});

test('interceptor: un 403 SEG-010 avisa del cambio de contraseña pendiente y otro 403 no', async () => {
  let pendientes = 0;
  registrarManejadoresSesion({ sesionPerdida: () => {}, identidadRefrescada: () => {}, passwordPendiente: () => pendientes++ });
  simular({
    'GET /a': () => ({ status: 403, data: { codigoReferencia: 'SEG-010' } }),
    'GET /b': () => ({ status: 403, data: { codigoReferencia: 'SEG-007' } }),
  });
  await assert.rejects(clienteHttp.get('/a'));
  await assert.rejects(clienteHttp.get('/b'));
  assert.equal(pendientes, 1);
});

test('interceptor: envía el token CSRF de la cookie', async () => {
  let cabecera: unknown;
  simular({
    'POST /x': (_n, config) => {
      cabecera = config.headers.get('X-CSRF-Token');
      return { status: 200 };
    },
  });
  await clienteHttp.post('/x', {});
  assert.equal(cabecera, 'tok=1');
});

test('ProblemDetails -> ErrorApi con title, detail, código y errores por campo', () => {
  const config = { headers: {} } as InternalAxiosRequestConfig;
  const axiosError = new AxiosError('x', 'ERR_BAD_REQUEST', config, null, respuesta(config, 400, {
    title: 'Datos no válidos',
    detail: 'Revisa el formulario.',
    codigoReferencia: 'API-001',
    errors: { ContrasenaNueva: ['Demasiado corta.', 'Otra.'], 'Lineas[0].Precio': ['Obligatorio.'] },
  }));
  const e = aErrorApi(axiosError);
  assert.ok(e instanceof ErrorApi);
  assert.equal(e.status, 400);
  assert.equal(e.title, 'Datos no válidos');
  assert.equal(e.detail, 'Revisa el formulario.');
  assert.equal(e.codigoReferencia, 'API-001');
  assert.deepEqual(erroresPorCampo(e), { contrasenaNueva: 'Demasiado corta.', 'lineas.0.precio': 'Obligatorio.' });
  assert.equal(aErrorApi(e), e, 'idempotente');
});

test('sin respuesta (red caída) se usa el texto de reserva y no el título técnico', () => {
  const sinRespuesta = new AxiosError('Network Error', 'ERR_NETWORK');
  assert.equal(aErrorApi(sinRespuesta).status, undefined);
  assert.equal(mensajeDeError(sinRespuesta, 'Sin conexión.'), 'Sin conexión.');
  assert.equal(aErrorApi('cualquier cosa').title.length > 0, true);
});

test('claveDeCampo pasa de PascalCase del DTO a la ruta del formulario', () => {
  assert.equal(claveDeCampo('ContrasenaNueva'), 'contrasenaNueva');
  assert.equal(claveDeCampo('Lineas[2].Precio'), 'lineas.2.precio');
  assert.equal(claveDeCampo('$.motivo'), '$.motivo');
});

test('repartirErrores: lo que el formulario conoce va al campo y el resto no se pierde', () => {
  const config = { headers: {} } as InternalAxiosRequestConfig;
  const validacion = new AxiosError('x', 'E', config, null, respuesta(config, 400, {
    title: 'Datos no válidos',
    errors: { ContrasenaNueva: ['Muy corta.'], Otro: ['Algo más.'] },
  }));
  assert.deepEqual(repartirErrores(validacion, ['contrasenaNueva'], 'reserva'), {
    porCampo: { contrasenaNueva: 'Muy corta.' },
    general: 'Algo más.',
  });

  const conflicto = new AxiosError('x', 'E', config, null, respuesta(config, 409, { title: 'Conflicto', detail: 'Ya existe.' }));
  assert.deepEqual(repartirErrores(conflicto, ['nombre'], 'reserva'), { porCampo: {}, general: 'Ya existe.' });
});
