# SEGURIDAD.md — Control y registro de seguridad

**(A) Checklist**: se revisa en cada revisión de seguridad (`docs/PROCEDIMIENTOS.md` §5), al cerrar cada fase y antes de producción. **(B) Riesgos aceptados**: decisiones del responsable con riesgo conocido. **(C) Registro**: cada problema detectado y cómo se resolvió. Lo más reciente arriba. No se borran entradas.

**Gravedad:** Crítica (exposición de datos o dinero, ejecución remota, saltarse permisos) · Alta · Media · Baja.
**Estado:** ABIERTO · MITIGADO · CORREGIDO · ACEPTADO (con motivo y quién lo acepta)

> Atlas Rent es un proyecto independiente: todos los controles se implementan y se prueban en este repositorio.
>
> Contexto: la app está **expuesta a internet** (sin VPN) en un Windows Server propio, con datos personales (cédulas, RNC, pasaportes), títulos de propiedad y caja. Eso hace que los controles de acceso de A.2 sean la primera línea de defensa.

---

## A. Checklist

### A.1 Exposición del servidor
- [ ] Windows Firewall: entrante solo **443/TCP**. RDP, PostgreSQL (5432), Hangfire y Watchdog no accesibles desde fuera.
- [ ] RDP solo desde la red interna; nunca publicado en el router.
- [ ] PostgreSQL `listen_addresses = 'localhost'`; `pg_hba.conf` solo `127.0.0.1/32` y `::1/128` con `scram-sha-256`.
- [ ] Certificado TLS válido (Let's Encrypt con win-acme o el de la empresa) con renovación automática y alerta de caducidad.
- [ ] TLS 1.2+ únicamente; HSTS.
- [ ] Panel de Hangfire deshabilitado en producción. Swagger/OpenAPI deshabilitado en producción.
- [ ] Watchdog sin puerto accesible desde la red.
- [ ] Actualizaciones solo desde GitHub Releases con verificación de integridad y firma.
- [ ] Windows Server con actualizaciones automáticas de seguridad; antivirus/Defender activo.
- [ ] Servicios ejecutándose con cuentas de servicio sin privilegios de administrador.
- [ ] Si se usa Caddy delante: Kestrel solo en localhost y `ForwardedHeaders` limitado a Caddy.
- [ ] Recomendado: proxy de Cloudflare delante (oculta la IP del servidor, filtra ataques volumétricos). Opcional.

### A.2 Acceso y sesión
- [x] Contraseñas BCrypt (12 rondas), mínimo 12 caracteres, comprobación contra HaveIBeenPwned (k-anonymity).
- [ ] **Bloqueo de cuenta:** 5 fallos → 30 min. Se registra en `intento_login`.
- [ ] **Bloqueo de IP:** ≥ 10 fallos desde una IP en 15 min → IP bloqueada 1 h; reincidencia en 24 h → 24 h; tercera → indefinido hasta desbloqueo por admin. [Valores configurables]
- [ ] Lista blanca de IP de oficina (nunca bloqueadas por IP; la cuenta sí).
- [x] IP real del cliente tomada solo de cabeceras de proxies de confianza configurados (si no hay proxy, `RemoteIpAddress`).
- [ ] Rate limiting: login por IP y por usuario; API por usuario; webhook por IP.
- [ ] Mensaje de login genérico (no revela si el usuario existe ni si está bloqueado por cuenta o IP).
- [ ] MFA TOTP obligatorio para admin; códigos de recuperación de un solo uso, hasheados.
- [ ] Cookies `HttpOnly; Secure; SameSite=Strict`; CSRF double-submit (`X-CSRF-Token`).
- [x] Inactividad 30 min, máximo 12 h; desactivar usuario o cambiar contraseña revoca todas sus sesiones en la siguiente petición.
- [x] Aviso al admin (app + email) de entrada desde IP nueva para ese usuario.
- [ ] Panel admin: sesiones activas, IP bloqueadas, cuentas bloqueadas, desbloqueo.

### A.3 Autorización
- [ ] Todos los endpoints declaran acción y pasan por `IPolicyService`; fallback global = denegar.
- [ ] Test de matriz de permisos por rol en CI.
- [ ] Sin IDOR: cada lectura por ID comprueba alcance (documentos de propiedad, auditoría, informes).
- [ ] El empleado no obtiene totales por ningún endpoint (listas, agregados, exportación).
- [ ] Denegaciones auditadas con usuario, IP y acción.

### A.4 Datos
- [ ] Roles de BD separados: `rent_owner` (propietario), `rent_migrador` (DDL), `rent_app` (DML, sin DDL ni superusuario).
- [ ] `auditoria`: REVOKE UPDATE/DELETE/TRUNCATE para `rent_app` + trigger que rechaza cambios + firma de registros y verificación diaria de integridad.
- [ ] RLS activo en `documento_inmueble` y `auditoria`; la API fija usuario/rol en la sesión de BD en cada conexión.
- [ ] Si se comparte instancia PostgreSQL con otras aplicaciones: ningún rol de Atlas Rent accede a otras bases ni al revés.
- [ ] Documento de identidad cifrado AES-256-GCM con clave fuera de la BD; índice ciego HMAC para búsqueda exacta. Visible completo en pantalla para quien tiene permiso (D16).
- [ ] Clave de cifrado protegida con DPAPI (máquina) o variable de entorno del servicio; nunca en el repositorio ni en `appsettings.json`.
- [ ] Serilog con redacción: sin contraseñas, tokens, documentos de identidad, teléfonos, emails de clientes ni cuerpos de petición.
- [ ] Documentos subidos: solo PDF, tamaño máximo, verificación por magic bytes, nombre generado por el servidor, carpeta fuera del webroot, descarga solo vía API con permiso.
- [x] Exportaciones neutralizan fórmulas (`=`, `+`, `-`, `@`, tabulador al inicio de celda). **Hecho en F5-02**: función única `TextoSeguro`, en el punto de escritura de texto del xlsx y del CSV. **Solo cadena**: los importes y las fechas van por ruta tipada y no se neutralizan en ninguno de los dos formatos — un importe negativo empieza por `-` y debe seguir siendo un número (`UnCampoCsvDeNumeroNegativoNoSeNeutraliza`). Cubierto por tests; **el fichero no se ha abierto en Excel real**. Cierra SEC-005.
- [ ] **Corregido en F4-01b: `subempresa.rnc` va cifrado.** Se guardaba en claro contra lo que pide AGENTS.md §6, con un `TODO(A#)` en `Data/Subempresa.cs` y esta línea. Ahora se cifra con el mismo AES-256-GCM y el mismo formato `v1:nonce:cifrado` que el documento de identidad del cliente, y se ve **completo** en la pantalla y en la cabecera del PDF de factura (`DESIGN.md §7`), con el mismo tratamiento (D16). **Sin índice ciego**: no hay ningún endpoint que busque subempresas por RNC —la lista solo ordena por nombre—, así que `rnc_hmac` sería una columna que nadie consulta; el índice ciego existe para buscar y detectar duplicados y aquí no hay ninguna de las dos cosas. A diferencia del documento del cliente, el RNC tampoco va a la auditoría.

### A.5 Entradas y salidas
- [ ] Validación de entradas en todos los DTO con la validación de modelo de ASP.NET Core; rechazo de campos desconocidos (`JsonUnmappedMemberHandling.Disallow`).
- [ ] EF Core parametrizado; SQL crudo solo con parámetros y revisado.
- [ ] PDFs con QuestPDF (por código, sin HTML ni navegador). **Ningún compositor llama a `Html(`, `Svg(`, `.ParseHtml` ni `MinifyHtml`** —vigilado por `InterpretacionEnPdfEnCodigoTests` (F4-01)— y el PDF generado no lleva `/JavaScript`, `/JS`, `/OpenAction`, `/URI`, `/Launch` ni `/EmbeddedFile`.
- [ ] Plantillas de email/mensaje: variables escapadas; sin HTML libre de usuarios.
- [x] Cabeceras: CSP estricta, HSTS, `X-Content-Type-Options: nosniff`, `Referrer-Policy`, `frame-ancestors 'none'`, `Permissions-Policy`.
- [ ] CORS cerrado (mismo origen).
- [x] Errores: ProblemDetails sin trazas; detalle solo en log interno con código de referencia.

### A.6 Integraciones
- [ ] Webhook de WhatsApp: firma validada en tiempo constante, idempotencia por ID de mensaje, rate limit, tamaño máximo.
- [ ] Mensajes entrantes como texto inerte; nunca HTML; nunca instrucciones para agentes.
- [ ] Adjuntos entrantes: no se descargan automáticamente o se guardan sin abrir, con tipo y tamaño limitados.
- [ ] Credenciales SMTP y APIs cifradas en configuración; contraseña de aplicación de Google o OAuth2, nunca la contraseña de la cuenta.
- [ ] Salidas HTTP solo a hosts conocidos (SMTP, ExchangeRate-API, proveedor WhatsApp).

### A.7 Secretos y dependencias
- [ ] Secretos solo en variables de entorno del servicio / DPAPI / `dotnet user-secrets` en desarrollo.
- [ ] **Clave del servidor de correo fuera de la base de datos** (F4-02, §3.14): está en `Correo__Clave`, en el entorno o en `dotnet user-secrets`, y **no hay ninguna columna donde escribirla**. Si se configurara en la base acabaría en los backups y en la tabla, que es justo lo que no se quiere con una credencial. El ejemplo está en `config/variables-entorno.example`.
- [ ] **El motivo de un envío fallido no lleva credenciales** (F4-02). `envio.ultimo_error` se escribe con el criterio de F3-01: solo el mensaje de una excepción propia y, de cualquier otra, solo el tipo —el mensaje de una excepción de SMTP puede traer el host, la respuesta del servidor o la cadena de conexión—. La columna se lee en la lista de envíos, fuera.
- [ ] gitleaks en CI.
- [ ] `dotnet list package --vulnerable --include-transitive` y `npm audit` en CI sin altas/críticas.
- [ ] Lockfiles versionados (`packages.lock.json`, `package-lock.json`).
- [ ] Datos reales de Factusol y backups nunca en el repositorio.

### A.8 Continuidad
- [ ] Backup diario cifrado (BD + documentos) por Hangfire; se conservan **4**; copia cifrada en Google Drive (cuenta/carpeta propia de Atlas Rent).
- [ ] Alerta si un backup falla o no se ejecuta. *(F1-12 cubre las dos mitades con lo que hay hoy: cada fallo del día deja fila en `alerta` con la fecha como clave y el mismo fallo repetido suma repeticiones; y `TrabajoVigilanciaCopia`, a las 04:00, abre el archivo que debería haber, lo descifra y avisa si no está o no verifica —que es lo único que detecta una copia que no llegó a hacerse, que no produce ningún error en ningún sitio.* Quedan dos huecos: la alerta se guarda en la misma base de datos del respaldo, así que si lo que cae es PostgreSQL no hay dónde escribirla; y la copia manual del Watchdog no la deja, por ejecutarse en una consola.)*
- [ ] Restauración probada en otra máquina (última prueba: ____ ).
- [ ] Runbook de recuperación y de "admin bloqueado" (break-glass) documentado.
- [ ] SAI en el servidor.

### A.9 Cumplimiento
- [ ] Conservación de datos económicos 10 años (art. 50 h, Ley 11-92).
- [ ] **[Verificar]** con abogado: Ley 172-13 de protección de datos personales.

---

## B. Riesgos aceptados

### SEC-R02 · Aplicación expuesta a internet sin VPN
- Fecha: 14/10/2026 · Aceptado por: responsable del proyecto · Estado: ACEPTADO
- Riesgo: cualquiera en internet llega a la pantalla de login; ataques de fuerza bruta, credenciales robadas y vulnerabilidades en la app son explotables desde fuera.
- Mitigación obligatoria: A.1 completo, A.2 completo (bloqueo de cuenta e IP, rate limit, HIBP, aviso de IP nueva), revisión de seguridad (FP-01) antes de abrir el puerto.
- Recomendación pendiente: proxy de Cloudflare delante.

### SEC-R01 · MFA solo para el administrador
- Fecha: 14/10/2026 · Aceptado por: responsable del proyecto · Estado: ACEPTADO
- Riesgo: con la contraseña de un gerente (phishing, reutilización), un atacante puede registrar y anular cobros, anular facturas, descargar documentos de propiedad y datos de clientes, desde internet.
- Mitigación: A.2 (HIBP, bloqueos, aviso de IP nueva), auditoría con IP, informe periódico de anulaciones. La activación de MFA para gerente queda como opción de configuración (desactivada) para poder activarla sin desarrollo.

---

## C. Registro

### Plantilla
```
### SEC-000 · Título corto
- Fecha detectado: · Fecha resuelto:
- Gravedad: · Estado: · Componente:
- Detectado por: (revisión / test / herramienta / agente / externo)
- Descripción: qué podía pasar y quién podía explotarlo.
- Evidencia: cómo se comprobó (sin secretos ni datos reales).
- Solución: qué se cambió.
- Verificación: test o prueba que lo demuestra.
- Commit/PR:
- ¿Hubo exposición real? Sí/No. Si sí: qué datos y a quién se notificó.
```

### SEC-030 · Endurecimiento varios
- Fecha detectado: 26/10/2026 · Fecha resuelto: 26/10/2026 (punto e abierto) · Gravedad: Baja · Estado: **MITIGADO** · Componente: API / cifrado
- Detectado por: auditoría (agentes)
- Descripción: (a) DTOs sin `JsonUnmappedMemberHandling.Disallow` (`IpDesbloquearPeticion`, `CuentaDesbloquearPeticion`, `TarifaPeticion`…). (b) `TarifaPeticion.Importe` sin tope: 500 al desbordar `numeric(18,2)`. (c) `GET /clientes?documento=<cédula>` pone un dato personal en la URL. (d) Códigos de recuperación con SHA-256 sin sal. (e) AES-GCM sin datos asociados. (f) `intento_login` no se purga. (g) La descarga de documentos de propiedad no se audita. (h) `GET /api/v1/envios` con solo `factura.ver` devuelve asuntos (plausible fuga si la plantilla lleva `{pendiente}`).
- Evidencia: Varios archivos de `Controllers/`, `DTOs/` y `Services/`.
- Solución: (a) `UnmappedMemberHandling.Disallow` global. (b) `ImporteJsonConverter` global con tope `numeric(18,2)`; lee números y texto decimal invariante (el frontend manda el dinero como texto: regresión de 21 fallos corregida en 712b140, BUG-054). (c) `POST /api/v1/clientes/busqueda` con el documento en el cuerpo; `GET ?documento=` da 400. (d) Códigos de recuperación nuevos como `h1:` + HMAC-SHA256. (f) Purga de `intento_login` con la función `SECURITY DEFINER` `purgar_intentos_login` (migración `PurgaIntentosLogin`), variable `Seguridad__Limites__DiasConservarIntentosLogin` (mínimo 30, 90 por defecto). (g) Descarga de documentos de propiedad auditada. (h) Asunto de los envíos de estado de cuenta oculto sin `informe.ver`. **(e) No se hace, a propósito:** AAD en AES-GCM cambiaría el formato de todas las columnas cifradas y obligaría a reescribirlas con la clave en la mano; el valor es bajo mientras las claves estén fuera de la BD. Queda como riesgo documentado. **Pendiente menor:** los códigos de acceso de emergencia (break-glass) siguen con SHA-256 sin sal.
- Corrección: AUD-03
- Verificación: Seguridad/EndurecimientoVariosTests, Inmuebles/InmuebleUnidadTests, Clientes/ClienteCifradoYBusquedaTests, Seguridad/CodigosRecuperacionTests, Inmuebles/DocumentoInmueblePermisosTests, Mensajeria/EnvioCopiaPermisosTests. Suite completa 1083/1083 (28/10/2026).
- Commit/PR: rama aud/sec018, commit 4c77fe6; merge efcb4f3. Regresión de integración corregida en 712b140 (BUG-054).
- NO verificado / pendiente de despliegue: AAD de AES-GCM (e) sin hacer. Códigos de emergencia con SHA-256 sin sal. Los códigos de recuperación ya emitidos con el formato antiguo siguen siendo válidos hasta que se regeneren. La purga de `intento_login` no se ha ejecutado contra un servidor real con Hangfire.
- ¿Hubo exposición real? Exposición real: no consta (no está en producción).

### SEC-029 · Límites de IA con TOCTOU y auditoría de agente incompleta
- Fecha detectado: 26/10/2026 · Fecha resuelto: 26/10/2026 · Gravedad: Baja · Estado: **CORREGIDO** · Componente: IA / agente
- Detectado por: auditoría (agentes)
- Descripción: Peticiones paralelas superan el límite; un 500 no deja fila de auditoría; el 401 de un token no cuenta para el bloqueo de IP.
- Evidencia: `ServicioConsultaIa.ExigirLimitesAsync` cuenta filas y escribe después; `AutenticacionAgenteMiddleware.cs:80-93` sin try/finally.
- Solución: Reserva atómica de cupo con `pg_advisory_xact_lock` antes de llamar al proveedor (fila 'reservado'). Auditoría del middleware de agente en `finally`. El 401 por token inválido cuenta para el bloqueo de IP.
- Corrección: AUD-03
- Verificación: IA/ConsultaIaTests (3). Fallaban antes: sí.
- Commit/PR: rama aud/sec021, commit 21965be; merge 99e572a.
- NO verificado / pendiente de despliegue: La concurrencia del tope mensual no está probada (comparte cerrojo con el diario).
- ¿Hubo exposición real? Exposición real: no consta (no está en producción).

### SEC-028 · Validación débil de email y caracteres de control en el borde
- Fecha detectado: 26/10/2026 · Fecha resuelto: 26/10/2026 · Gravedad: Baja · Estado: **CORREGIDO** · Componente: clientes / correo
- Detectado por: auditoría (agentes)
- Descripción: (Plausible, sin comprobar con prueba.) Un email o nombre con CRLF llegaría a `MailboxAddress`/`Subject`.
- Evidencia: `ClientePeticion.Email` sin `[EmailAddress]`; `Nombre` sin filtrar caracteres de control.
- Solución: Atributos `[EmailSeguro]` y `[UnaLinea]` (DTOs/ValidacionTexto.cs) aplicados a emails y a nombres y asunto.
- Corrección: AUD-03
- Verificación: Clientes/ClienteEntradaTextoTests (3, fallaban antes) y tests unitarios.
- Commit/PR: rama aud/sec016, commit afb927a; merge 14afd07.
- NO verificado / pendiente de despliegue: Ninguno en código.
- ¿Hubo exposición real? Exposición real: no consta (no está en producción).

### SEC-027 · Email de cliente en logs
- Fecha detectado: 26/10/2026 · Fecha resuelto: 26/10/2026 · Gravedad: Baja · Estado: **CORREGIDO** · Componente: logs
- Detectado por: auditoría (agentes)
- Descripción: El destinatario queda en el log de nivel Information. Contra SEGURIDAD A.4.
- Evidencia: `Services/ServicioCorreoMailKit.cs:79`.
- Solución: El log de correo no escribe el destinatario ni el nombre del adjunto. Era el único Log de AtlasRent.Api con datos personales.
- Corrección: AUD-03
- Verificación: Sin test específico: verificado leyendo el código de `Services/ServicioCorreoMailKit.cs`.
- Commit/PR: rama aud/sec016, commit afb927a; merge 14afd07.
- NO verificado / pendiente de despliegue: Ningún test cubre que el log no contenga el destinatario.
- ¿Hubo exposición real? Exposición real: no consta (no está en producción).

### SEC-026 · Seudonimización frágil y test con falso verde
- Fecha detectado: 26/10/2026 · Fecha resuelto: 26/10/2026 · Gravedad: Media · Estado: **CORREGIDO** · Componente: IA
- Detectado por: auditoría (agentes)
- Descripción: La lista cerrada por nombre de propiedad puede dejar pasar datos y `GetValue<string>` lanza si llega un número. El test no detectaría un nombre con tilde filtrado.
- Evidencia: `Services/MapaSeudonimos.cs:38-55` y `:131`. `Tests/IA/ConsultaIaTests.cs:161-167` busca nombres con NotContain sobre JSON que escapa los caracteres no ASCII; solo prueba el alcance clientes, no el teléfono.
- Solución: `Recorrer` convierte cualquier tipo a texto. Seudonimiza por nombre de propiedad que contenga email, correo, telefono, movil, celular, direccion, documento, cedula, rnc, pasaporte, razonsocial o titular. Red de seguridad antes de enviar: si aparece un valor personal conocido de 4 caracteres o más, o algo con forma de email, no se envía (503, auditado como fuga_datos_personales).
- Corrección: AUD-03
- Verificación: IA/MapaSeudonimosTests (9: cubre los 5 alcances, con tilde, teléfono y email). ConsultaIaTests reescrito sobre texto des-escapado.
- Commit/PR: rama aud/sec021, commit 61b3673; merge 99e572a.
- NO verificado / pendiente de despliegue: Riesgo residual: los falsos positivos de la red de seguridad bloquean con 503.
- ¿Hubo exposición real? Exposición real: no consta (no está en producción).

### SEC-025 · Brechas en MFA y break-glass
- Fecha detectado: 26/10/2026 · Fecha resuelto: 26/10/2026 · Gravedad: Media · Estado: **CORREGIDO** · Componente: autenticación / MFA
- Detectado por: auditoría (agentes)
- Descripción: (1) `VerificarSegundoFactorAsync` y `ServicioEmergencia.EntrarAsync` no miran `CuentaBloqueada`. (2) Los fallos de `EntrarAsync`/`ConfirmarAsync` no cuentan como intentos. (3) `AprobarAsync` concurrente reescribe `CodigoHash` sin recomprobar `AprobadoPorId`. (4) Un admin puede quitar `mfa.obligatorio`/`seguridad.acceso_emergencia` a otro. (5) No hay protección de «último admin» al cambiar el rol.
- Evidencia: `ServicioMfa.cs:263`, `ServicioEmergencia.EntrarAsync`, `AprobarAsync`.
- Solución: Segundo factor, códigos de recuperación, `ConfirmarAsync` y `EntrarAsync` (emergencia) respetan `CuentaBloqueada` y cuentan los fallos. `AprobarAsync` de emergencia con UPDATE condicional. No se quita `mfa.obligatorio` ni `seguridad.acceso_emergencia` a un admin. No se quita el rol admin al último admin activo (409, ReglaDeAdministradorException).
- Corrección: AUD-03
- Verificación: Seguridad/BrechasMfaEmergenciaTests (9). El test de aprobación concurrente no reproduce la carrera de forma determinista.
- Commit/PR: rama aud/sec012, commit ca9f63d (con SEC-012 y SEC-013); merge 9e46749.
- NO verificado / pendiente de despliegue: La carrera de `AprobarAsync` queda cubierta por el UPDATE condicional, no por un test determinista que falle antes.
- ¿Hubo exposición real? Exposición real: no consta (no está en producción).

### SEC-024 · No existe el interruptor de MFA para gerente
- Fecha detectado: 26/10/2026 · Fecha resuelto: 26/10/2026 · Gravedad: Media · Estado: **CORREGIDO** · Componente: autenticación / MFA
- Detectado por: auditoría (agentes)
- Descripción: La opción prometida en SEC-R01 no existe: F1-06 la aplazó a F1-07 y nunca se hizo.
- Evidencia: `ServicioMfa.NecesitaMfaAsync` con `TODO(A#)`.
- Solución: Ajuste `ajuste_general.mfa_gerente` (default false) en `PUT /api/v1/config/ajustes`, campo `mfaGerente`, solo admin, auditado.
- Corrección: AUD-03
- Verificación: Seguridad/MfaGerenteTests (3).
- Commit/PR: rama aud/sec023, commit 171613e; merge e90e145.
- NO verificado / pendiente de despliegue: Ninguno en código. El interruptor queda desactivado por defecto; activarlo es decisión del responsable.
- ¿Hubo exposición real? Exposición real: no consta (no está en producción).

### SEC-023 · No existe el aviso de IP nueva al admin
- Fecha detectado: 26/10/2026 · Fecha resuelto: 26/10/2026 · Gravedad: Media · Estado: **CORREGIDO** · Componente: autenticación / bloqueos
- Detectado por: auditoría (agentes)
- Descripción: Falta la mitigación de SEC-R01/R02: la spec (línea ~367) y SEGURIDAD A.2 piden aviso en app y por correo.
- Evidencia: `Services/ServicioBloqueos.cs:98` solo audita; hay un `TODO(A#)`.
- Solución: `ServicioAvisosAcceso` registra alerta de tipo seguridad con clave `ip-nueva:{usuario}:{ip}` y envía correo a los admins mediante la cola Hangfire (`TrabajoAvisoIpNueva`) si el correo está configurado. Decisión: el aviso salta para cualquier usuario, no solo para admins.
- Corrección: AUD-03
- Verificación: Seguridad/AvisoIpNuevaAdminTests (3).
- Commit/PR: rama aud/sec023, commit 171613e; merge e90e145 (con SEC-024).
- NO verificado / pendiente de despliegue: El envío real de correo al admin no está comprobado; depende del SMTP real.
- ¿Hubo exposición real? Exposición real: no consta (no está en producción).

### SEC-022 · Un empleado puede fijar el número de cliente
- Fecha detectado: 26/10/2026 · Fecha resuelto: 26/10/2026 · Gravedad: Media · Estado: **CORREGIDO** · Componente: clientes
- Detectado por: auditoría (agentes)
- Descripción: Con `numero=2147483646` el siguiente `nextval` desborda el int; `setval(GREATEST(...))` puede retroceder la secuencia bajo concurrencia.
- Evidencia: `Services/ServicioClientes.cs:185-206`; `ClienteDtos.cs:30` `Range(1,int.MaxValue)`; endpoint con `cliente.editar`.
- Solución: Fijar `numero` exige `configuracion.editar` (solo admin; 403 auditado si no). Tope 99.999.999. `AlinearSecuenciaAsync` con `pg_advisory_xact_lock` para no retroceder la secuencia.
- Corrección: AUD-03
- Verificación: Clientes/ClienteNumeroTests (2). Fallaban antes: sí.
- Commit/PR: rama aud/sec018, commit 4c77fe6; merge efcb4f3.
- NO verificado / pendiente de despliegue: Residual: queda una ventana de microsegundos entre un `nextval` ajeno y el `setval`.
- ¿Hubo exposición real? Exposición real: no consta (no está en producción).

### SEC-021 · Watchdog aplica actualizaciones sin validar firma ni rollback
- Fecha detectado: 26/10/2026 · Fecha resuelto: 26/10/2026 · Gravedad: Media · Estado: **CORREGIDO** · Componente: Watchdog
- Detectado por: auditoría (agentes)
- Descripción: Un ZIP manipulado se extrae con overwrite sobre la instalación. Contra SEGURIDAD A.1.
- Evidencia: `backend/AtlasRent.Watchdog/OperacionesWatchdog.cs:112-120`; el TODO apunta a F1-13 (equivocado).
- Solución: Firma ECDSA P-256 en un `.zip.sig` aparte, verificada con `Watchdog__ClavePublicaActualizacion`. Extracción a `.nueva` y sustitución con `.prev` hermana, fuera de la instalación. Rollback si falla. Rutas normalizadas. Procedimiento de firma en DOC_TECNICA §9.
- Corrección: AUD-03
- Verificación: Backup/WatchdogOperacionesTests (9).
- Commit/PR: rama aud/sec021, commit 970f960; merge 99e572a (con SEC-026 y SEC-029).
- NO verificado / pendiente de despliegue: Pendiente: automatizar el firmado en el release (`scripts/` no existe; FP-02). Despliegue: definir `Watchdog__ClavePublicaActualizacion` y firmar cada release.
- ¿Hubo exposición real? Exposición real: no consta (no está en producción).

### SEC-020 · La API usa la credencial del propietario del esquema para Hangfire
- Fecha detectado: 26/10/2026 · Fecha resuelto: 26/10/2026 · Gravedad: Media · Estado: **CORREGIDO** · Componente: base de datos / jobs
- Detectado por: auditoría (agentes)
- Descripción: `rent_migrador` pertenece a `rent_owner`, así que puede hacer `DISABLE TRIGGER` en `auditoria` y anula SEC-002.
- Evidencia: `Program.cs:253` usa `ConnectionStrings__Migraciones` para Hangfire.
- Solución: Rol nuevo `rent_jobs` (01_roles.sql), base `atlas_rent_jobs` propiedad de `rent_jobs` (04_base_jobs.sql) y `ConnectionStrings__Jobs` para Hangfire. Desviación: `PrepareSchemaIfNecessary` sigue en true, porque rent_jobs no tiene acceso a datos de negocio.
- Corrección: AUD-03
- Verificación: Datos/AuditoriaAppendOnlyTests.RentAppNoPuedeDesactivarElTriggerDeAuditoria; Jobs/AlmacenamientoJobsTests (4).
- Commit/PR: rama aud/sec014, commit 06e1c42; merge 00b8b1b.
- NO verificado / pendiente de despliegue: Despliegue pendiente: crear el rol `rent_jobs`, definir `ConnectionStrings__Jobs`, ejecutar `ALTER DATABASE atlas_rent_jobs OWNER TO rent_jobs` y reasignar el esquema hangfire en los servidores ya instalados.
- ¿Hubo exposición real? Exposición real: no consta (no está en producción).

### SEC-019 · Desactivar usuario no revoca sesiones al instante
- Fecha detectado: 26/10/2026 · Fecha resuelto: 26/10/2026 · Gravedad: Media · Estado: **CORREGIDO** · Componente: sesiones
- Detectado por: auditoría (agentes)
- Descripción: Un usuario desactivado conserva su sesión hasta 30 s por la caché; además no existe endpoint de desactivar ni de cambiar contraseña.
- Evidencia: `Services/ServicioSesiones.cs:99` (caché 30 s). `AuthEndpointsTests.cs:66` desactiva antes de calentar la caché, así que no prueba el caso real.
- Solución: `PUT /api/v1/usuarios/{id}/activo`. `ServicioSesiones.RevocarTodasDelUsuarioAsync` en la misma transacción, con la caché invalidada tras el commit. Igual al cambiar o restablecer la contraseña. Un admin no puede desactivarse.
- Corrección: AUD-03
- Verificación: AuthEndpointsTests.UsuarioDesactivadoDevuelve401EnLaSiguientePeticion, corregido: ahora calienta la caché antes de desactivar (antes no probaba el caso real).
- Commit/PR: rama aud/sec015, commit fadbe16; merge 95dd3f2.
- NO verificado / pendiente de despliegue: Ninguno en código.
- ¿Hubo exposición real? Exposición real: no consta (no está en producción).

### SEC-018 · Cédula/RNC en claro en envio.pdf_copia
- Fecha detectado: 26/10/2026 · Fecha resuelto: 26/10/2026 · Gravedad: Media · Estado: **CORREGIDO** · Componente: envíos
- Detectado por: auditoría (agentes)
- Descripción: El PDF (con el documento del cliente) se guarda en bytea sin cifrar. Choca con AGENTS §6.
- Evidencia: `Services/ServicioEnvios.cs:208`.
- Decisión del responsable (26/10/2026): cifrar `pdf_copia` con AES-256-GCM.
- Solución: `envio.pdf_copia` se cifra con `ServicioCifrado.CifrarBytes`/`DescifrarBytes` (AES-256-GCM, formato `v{versión}:{nonce}:{cifrado}`). Las filas antiguas en claro (empiezan por `%PDF-`) se leen tal cual y se cifran al reescribirse. Sin migración.
- Corrección: AUD-03
- Verificación: Mensajeria/EnvioCopiaCifradaTests (2).
- Commit/PR: rama aud/sec018, commit 7def9f3; merge efcb4f3.
- NO verificado / pendiente de despliegue: Las copias ya guardadas en claro siguen así hasta reescribirse; no hay migración de datos (no hay producción). Si hubiera datos reales habría que cifrarlas en bloque.
- ¿Hubo exposición real? Exposición real: no consta (no está en producción).

### SEC-017 · JSON de auditoría montado a mano con el motivo sin escapar
- Fecha detectado: 26/10/2026 · Fecha resuelto: 26/10/2026 · Gravedad: Media · Estado: **CORREGIDO** · Componente: auditoría / series
- Detectado por: auditoría (agentes)
- Descripción: Un motivo con comilla hace fallar el cast `::jsonb` y la anulación se revierte; un motivo manipulado falsea campos (p. ej. `numero`) en la auditoría firmada.
- Evidencia: `Services/ServicioSeries.cs:136`. Verificado leyendo el código. Hay que buscar otros `DatosJson` interpolados.
- Solución: `JsonSerializer` en `ServicioSeries`, en `ServicioEmergencia` (2 sitios) y en `ServicioUsuarios` (permiso y rol). El motivo se escapa y ya no puede pisar campos de la auditoría.
- Corrección: AUD-03
- Verificación: Configuracion/SeriesContadorTests.ElMotivoDeAnulacionNoRompeNiPisaLaAuditoria (Theory, 2 casos). Fallaba antes: sí.
- Commit/PR: rama aud/sec016, commit afb927a; merge 14afd07.
- NO verificado / pendiente de despliegue: Revisión del resto de `DatosJson` interpolados: no consta en esta entrega; comprobar con grep.
- ¿Hubo exposición real? Exposición real: no consta (no está en producción).

### SEC-016 · SMTP con StartTlsWhenAvailable
- Fecha detectado: 26/10/2026 · Fecha resuelto: 26/10/2026 · Gravedad: Media · Estado: **CORREGIDO** · Componente: correo
- Detectado por: auditoría (agentes)
- Descripción: Si el servidor no ofrece STARTTLS o un MITM lo quita, el envío cae a texto plano y expone la clave SMTP y PDFs con cédula.
- Evidencia: `Services/ServicioCorreoMailKit.cs:65`. Verificado leyendo el código.
- Solución: `StartTls` obligatorio con Ssl=true (`SecureSocketOptions.StartTls`); SslOnConnect con false; no hay modo en claro.
- Corrección: AUD-03
- Verificación: Mensajeria/SeguridadCorreoYTextoTests.ConSslElStartTlsEsObligatorioYSinSslEsTlsImplicito.
- Commit/PR: rama aud/sec016, commit afb927a; merge 14afd07 (con SEC-017, SEC-027 y SEC-028).
- NO verificado / pendiente de despliegue: SMTP real no probado.
- ¿Hubo exposición real? Exposición real: no consta (no está en producción).

### SEC-015 · HaveIBeenPwned nunca se invoca, sin longitud mínima y sin cambio de contraseña
- Fecha detectado: 26/10/2026 · Fecha resuelto: 26/10/2026 · Gravedad: Alta · Estado: **CORREGIDO** · Componente: autenticación
- Detectado por: auditoría (agentes)
- Descripción: Se aceptan contraseñas de 1 carácter o filtradas, y el usuario no puede cambiar la suya. Incumple la mitigación obligatoria de SEC-R02.
- Evidencia: `ServicioComplicidad` solo se registra en `Program.cs:74`; `DTOs/UsuarioDtos.cs:31` tiene `MinimumLength=1` (la spec pide 12); no existe endpoint de cambiar contraseña (`DebeCambiarPassword` solo se devuelve). Verificado leyendo el código.
- Solución: `ValidadorContrasena`: 12 a 72 caracteres más HIBP, error 400 SEG-011. Si HIBP no responde no bloquea: audita `hibp:no_disponible` y avisa. Cambio propio con `POST /api/v1/auth/password` (acción sesion.password, migración 20261026172835_AccionSesionPassword). Restablecimiento por admin con `PUT /api/v1/usuarios/{id}/password`, que obliga a cambiar. `ValidacionSesionMiddleware` responde 403 SEG-010 mientras haya cambio pendiente, salvo sesion.ver, sesion.cerrar y sesion.password.
- Corrección: AUD-02
- Verificación: Auth/ContrasenaYSesionesTests (16). Fallaban antes: sí.
- Commit/PR: rama aud/sec015, commit fadbe16; merge 95dd3f2 (con SEC-019).
- NO verificado / pendiente de despliegue: HIBP real no comprobado contra el servicio en vivo. Riesgo: el límite de 72 se mide en caracteres y BCrypt corta a 72 bytes.
- ¿Hubo exposición real? Exposición real: no consta (no está en producción).

### SEC-014 · Sin ForwardedHeaders, HSTS, cabeceras de seguridad ni manejador global de errores
- Fecha detectado: 26/10/2026 · Fecha resuelto: 26/10/2026 · Gravedad: Alta · Estado: **CORREGIDO** · Componente: API / despliegue
- Detectado por: auditoría (agentes)
- Descripción: Detrás de Caddy todas las peticiones llegan como 127.0.0.1, así que 10 fallos bloquean a todo el mundo; sin HSTS, CSP, nosniff, Referrer-Policy ni frame-ancestors; los errores pueden filtrar trazas. DOC_TECNICA §7 y SEGURIDAD A.5 lo dan por hecho.
- Evidencia: `Program.cs` no tiene `UseForwardedHeaders`, `UseHsts`, `UseHttpsRedirection`, `UseExceptionHandler` ni CSP/nosniff/Referrer-Policy/frame-ancestors; `Seguridad__ProxiesConfianza` no se lee y `ResolutorIpPeticion` usa `RemoteIpAddress`. Verificado leyendo el código.
- Solución: Middleware de cabeceras: nosniff, Referrer-Policy no-referrer, X-Frame-Options DENY, Permissions-Policy y CSP estricta con frame-ancestors 'none' y sin unsafe-inline en scripts (style-src-attr 'unsafe-inline' por los style={} de React). `UseForwardedHeaders` solo con `Seguridad__ProxiesConfianza` (IP o CIDR, ForwardLimit 1, sin loopback por defecto). `UseExceptionHandler` en todos los entornos: 500 ProblemDetails SYS-001 sin traza. HSTS y HTTPS fuera de Development. El script del tema pasa a `frontend/public/tema-inicial.js`.
- Corrección: AUD-02
- Verificación: Seguridad/CabecerasYProxiesTests: ForwardedHeadersTests (3), CabecerasSeguridadTests (2), ErroresYHttpsTests.
- Commit/PR: rama aud/sec014, commit 677e635; merge 00b8b1b (con SEC-020).
- NO verificado / pendiente de despliegue: CSP en navegador real no comprobada; Caddy real no probado. Despliegue: definir `Seguridad__ProxiesConfianza` (p. ej. 127.0.0.1) con Caddy delante.
- ¿Hubo exposición real? Exposición real: no consta (no está en producción).

### SEC-013 · MFA activar/confirmar aceptan JWT de sesión revocada
- Fecha detectado: 26/10/2026 · Fecha resuelto: 26/10/2026 · Gravedad: Alta · Estado: **CORREGIDO** · Componente: autenticación / MFA
- Detectado por: auditoría (agentes)
- Descripción: Con un JWT robado, hasta 15 minutos después del logout, el atacante activa su propio TOTP y bloquea al titular.
- Evidencia: `Controllers/MfaController.cs:148` y `UsuarioQueEstaActivandoAsync`: el fallback `UsuarioDeLaSesion()` toma `sub` de un JWT validado solo por firma; `ValidacionSesionMiddleware.cs:33` salta `[AllowAnonymous]`; además `[SaltarCsrf]` e ignora `DeletedAt`.
- Solución: El camino de sesión de `MfaController.UsuarioQueEstaActivandoAsync` valida la sesión en BD (`ServicioSesiones.ValidarAsync`), exige usuario activo y no borrado, y comprueba CSRF a mano en ese camino. `[SaltarCsrf]` se mantiene solo para el token provisional.
- Corrección: AUD-02
- Verificación: Seguridad/MfaSesionRevocadaTests (4).
- Commit/PR: rama aud/sec012, commit ca9f63d; merge 9e46749.
- NO verificado / pendiente de despliegue: Ninguno en código.
- ¿Hubo exposición real? Exposición real: no consta (no está en producción).

### SEC-012 · Bloqueo de cuenta prolongable indefinidamente
- Fecha detectado: 26/10/2026 · Fecha resuelto: 26/10/2026 · Gravedad: Alta · Estado: **CORREGIDO** · Componente: autenticación / bloqueos
- Detectado por: auditoría (agentes)
- Descripción: Un atacante con un intento cada menos de 30 minutos deja al admin fuera para siempre: cada fallo durante el bloqueo lo alarga y, tras expirar, un solo fallo vuelve a bloquear.
- Evidencia: `Services/ServicioBloqueos.cs:216-225`: `hasta = ahora + 30 min` y solo evita sobrescribir si `actual > hasta` (nunca ocurre); los fallos se cuentan desde el último éxito. `BloqueoCuentaTests.cs:26` no avanza el reloj. Verificado leyendo el código.
- Solución: Si el bloqueo ya está vigente no se reescribe. Los fallos cuentan desde max(último éxito, BloqueadaHasta anterior, desbloqueada_at). Nueva columna `usuario.desbloqueada_at`, fijada en el desbloqueo manual (migración 20261026203218_MfaGerenteYDesbloqueoManual). El bloqueo de IP escalado no tenía el defecto.
- Corrección: AUD-02
- Verificación: Seguridad/BloqueoCuentaTests (UnFalloConElBloqueoVigenteNoLoProrroga, TrasExpirarElBloqueoHacenFaltaOtrosCincoFallosParaBloquearDeNuevo) y Seguridad/DesbloqueoReiniciaContadorTests. Fallaban antes: sí.
- Commit/PR: rama aud/sec012, commit ca9f63d (con SEC-013 y SEC-025), merge 9e46749; seguimiento en aud/sec023, commit 171613e, merge e90e145.
- NO verificado / pendiente de despliegue: La migración 20261026203218_MfaGerenteYDesbloqueoManual debe aplicarse en cada base desplegada.
- ¿Hubo exposición real? Exposición real: no consta (no está en producción).

### SEC-011 · El agente puede aprobar su propia propuesta
- Fecha detectado: 26/10/2026 · Fecha resuelto: 26/10/2026 · Gravedad: Alta · Estado: **CORREGIDO** · Componente: agente / IA
- Detectado por: auditoría (agentes)
- Descripción: Un agente que proponga una acción puede aprobarla él mismo, anulando la aprobación humana.
- Evidencia: `Controllers/IaController.cs:163`: Aprobar/Rechazar exigen solo `mensajeria.masivo`; nada rechaza `EsAgente`. Verificado leyendo el código.
- Solución: El filtro de SEC-010 impide a un agente cualquier endpoint de aprobación o rechazo: IA, contratos, bandeja y emergencia declaran acciones fuera de la lista blanca.
- Corrección: AUD-02
- Verificación: IA/PropuestaAprobacionTests: UnAgenteNoApruebaNiRechazaSuPropuesta, UnAgenteNoApruebaContratosNiDesdeLaBandeja, NingunEndpointDeAprobacionDeclaraAccionesDeAgente (por reflexión). Se adaptó UnaPropuestaDelDiaAnteriorSigueSeAprobandoSiNadaHaCambiado, que aprobaba con token de agente.
- Commit/PR: rama aud/sec010, commit 47ec2a4; merge cc78852.
- NO verificado / pendiente de despliegue: Ninguno en código.
- ¿Hubo exposición real? Exposición real: no consta (no está en producción).

### SEC-010 · Token de agente con acciones de escritura salta la aprobación humana
- Fecha detectado: 26/10/2026 · Fecha resuelto: 26/10/2026 · Gravedad: Alta · Estado: **CORREGIDO** · Componente: agente / IA
- Detectado por: auditoría (agentes)
- Descripción: Un token de agente con `cobro.registrar`, `factura.anular` o `mensajeria.masivo` llama a `POST /api/v1/cobros` o `/facturas/{id}/anular` sin propuesta ni aprobación humana. Contradice ESPECIFICACION §3.18 y el Supuesto I3. Un token con `agente.token` puede además acuñar más tokens.
- Evidencia: `Services/ServicioAgentes.cs:66` solo valida que la acción exista en `Acciones.Todas`. `Middleware/AutenticacionAgenteMiddleware.cs:44` aplica el token a todo `[ApiController]` y `CsrfMiddleware` se salta con `EsAgente`. Verificado leyendo el código.
- Solución: Lista cerrada `Acciones.PermitidasAAgente` (ia.consultar, ia.proponer, cliente.ver, factura.ver, informe.ver). Se valida al emitir el token (400) y en cada petición en `FiltroAccionAgente` (403 auditado, origen agente), lo que cubre también los tokens ya emitidos. `agente.token` queda fuera de la lista.
- Corrección: AUD-02
- Verificación: Agentes/TokenAgenteTests: ElAltaRechazaLasAccionesFueraDeLaListaBlanca (x5), ElAltaAceptaTodaLaListaBlanca, UnTokenExistenteConCobroRegistrarNoPuedeCobrar, UnTokenExistenteConAgenteTokenNoAcunaOtrosTokens. Fallaban antes: sí.
- Commit/PR: rama aud/sec010, commit 47ec2a4; merge cc78852.
- NO verificado / pendiente de despliegue: Ninguno en código. Los tokens ya emitidos quedan limitados por la comprobación en cada petición, sin acción manual.
- ¿Hubo exposición real? Exposición real: no consta (no está en producción).

### SEC-005 · Inyección de fórmulas en exportaciones
- Fecha detectado: 13/10/2026 · Fecha resuelto: 18/10/2026 · Gravedad: Media · Estado: **CERRADO en F5-02** · Componente: exportación
- Detectado por: revisión
- Descripción: un nombre de cliente como `=HYPERLINK(...)` se ejecuta al abrir el Excel exportado.
- Evidencia: la exportación de §3.15 escribe texto externo (nombre de cliente, número, estado) en celdas. Un nombre que empieza por `=`, `+`, `-`, `@`, tabulador o salto de línea lo ejecuta la hoja al abrirse.
- Solución: `ServicioExportacionInformes.TextoSeguro` antepone una apóstrofo a **toda cadena** que empiece por esos caracteres, en el punto único de escritura de texto del escritor de Excel y del de CSV. Los importes y las fechas van por una ruta tipada (número y fecha) y **no** se neutralizan: un importe negativo empieza por `-` y debe seguir siendo un número.
- Verificación: `ElNombreDeClienteQueEsUnaFormulaNoEsUnaFormulaEnElExcel` (comprueba `HasFormula == false` y que el texto queda intacto con el prefijo de texto de Excel), `ElNombreDeClienteQueEsUnaFormulaTampocoEsUnaFormulaEnElCsv`, y `ElTextoQueEmparaPorFormulaVaConApostrofo` con 11 casos de tabla. **No se abrió el fichero en Excel ni en LibreOffice**: la protección está probada leyendo el xlsx generado con ClosedXML.
- Commit/PR: pendiente.
- ¿Hubo exposición real? No. La exportación no existía todavía.

### SEC-004 · Datos personales y documentos de propiedad
- Fecha detectado: 13/10/2026 · Gravedad: Alta · Estado: **CERRADO en F1-09 y F1-10** · Componente: clientes, inmuebles
- Descripción: cédulas, RNC, pasaportes y títulos de propiedad son el dato más sensible y la app está en internet.
- Solución prevista: cifrado de campo + índice ciego, permisos por rol, documentos fuera del webroot y servidos por API, sin datos personales en logs, backups cifrados.

### SEC-003 · Revocación de sesión con JWT
- Fecha detectado: 13/10/2026 · Gravedad: Alta · Estado: CORREGIDO e IMPLEMENTADO en F1-04
- Descripción: un JWT sigue siendo válido hasta caducar; un usuario desactivado seguiría entrando.
- Solución: la sesión se valida contra la tabla `sesion` en cada petición (o sello de seguridad), además de la firma.
- Verificado en F1-04: la validación ocurre en `ValidacionSesionMiddleware` con caché de 30 s; hay test de que un usuario desactivado recibe 401 en la siguiente petición.

### SEC-007 · Secreto TOTP en claro
- Fecha detectado: 15/10/2026 · Gravedad: **Alta** · Estado: **CERRADO en F1-09** · Componente: autenticación
- Descripción: `usuario.mfa_secreto` guardaba el secreto TOTP en claro. Es imprescindible para validar el código, pero mientras no se cifre, cualquiera que lea la tabla `usuario` —una SQL injection, una copia de la base, un backup sin cifrar— puede **generar códigos TOTP válidos de cualquier usuario con MFA**, que es exactamente lo que el segundo factor debe impedir.
- Por qué estaba abierto: el cifrado AES-256-GCM con clave fuera de la base (`Seguridad__CifradoClave`) se implementaba en F1-09, para los documentos de identidad. El secreto TOTP comparte ese requisito.
- Solución: cifrar `mfa_secreto` en reposo con AES-256-GCM y la misma clave versionada. Implementado en F1-09 y probado de extremo a extremo: activar el MFA, leer la fila cruda y entrar después con el código.
- Verificado: test `ElSecretoMfaQuedaCifradoEnLaBaseDeDatosYElSegundoFactorSigueFuncionando`.

### SEC-008 · Cuerpo de idempotencia sin cifrar
- Fecha detectado: 15/10/2026 · Gravedad: **Media** · Estado: **ABIERTO (decisión del responsable)** · Componente: API
- Descripción: `ServicioIdempotencia` guarda el **cuerpo completo de la respuesta** de una mutación en la tabla `idempotencia`, como JSON y **sin cifrar**. Aunque el dato principal esté cifrado en su tabla (caso del documento de identidad, SEC-004), cualquier dato personal que una mutación devuelva queda en claro en `idempotencia`.
- Por qué no se resolvió aquí: en F1-09 se esquivó **no devolviendo el documento de identidad en las respuestas de alta y edición** (solo en `GET /clientes/{id}`). Eso protege hoy, pero es un rodeo: el problema es de fondo y volverá con el primer campo personal nuevo que se añada a una respuesta de mutación.
- **Decisión pendiente:** (a) cifrar el cuerpo guardado con `ServicioCifrado`, o (b) guardar una versión redactada sin los campos sensibles. La opción (a) es más simple de usar y evita que cada_endpoint tenga que acordarse de qué puede devolver.
- Riesgo residual actual: contenido en claro en `idempotencia` hasta que se decida.

### SEC-002 · Auditoría borrable
- Fecha detectado: 13/10/2026 · Gravedad: Alta · Estado: CORREGIDO e IMPLEMENTADO en F1-03
- Descripción: con solo "INSERT/SELECT" el propietario aún podía TRUNCATE o cambiar permisos.
- Solución: propietario distinto del rol de la app, REVOKE, trigger de rechazo, hash encadenado.
- Verificado en F1-03: UPDATE, DELETE y TRUNCATE con `rent_app` fallan, el trigger rechaza incluso con permisos y la verificación detecta una fila alterada por debajo.

### SEC-001 · Generación de PDF con HTML y navegador
- Fecha detectado: 13/10/2026 · Gravedad: Alta · Estado: CORREGIDO en especificación
- Descripción: renderizar HTML con datos de clientes en Chromium permitía inyección de scripts y peticiones a la red interna.
- Solución: PDFs generados por código con QuestPDF, sin HTML ni navegador.

### SEC-009 · Correo con el cuerpo del cliente sin escapar
- Fecha detectado: 18/10/2026 · Gravedad: **Media** · Estado: **CERRADO en F4-02** · Componente: envío de correo
- Descripción: el mismo riesgo que SEC-001 llevado al correo. Sustituir `{cliente}`, `{numero}` o `{total}` en un cuerpo **HTML** permitiría que un nombre de cliente con una etiqueta metiera una imagen que se ejecuta al abrir el correo, o una etiqueta que enlaza a la red interna del cliente y filtra datos de su factura.
- Solución: el cuerpo va en **texto plano** (`BodyBuilder.TextBody`, sin `HtmlBody`), que es lo único que `ServicioCorreoMailKit` puede construir. La sustitución de variables es textual, **no evalúa nada y no escapa nada**, y el texto del cliente sale literal: el mismo criterio que los PDF, porque un correo en texto plano no puede ejecutar nada ni llamar a ninguna red por su cuenta. **Las plantillas tampoco admiten HTML**: no hay ninguna forma de escribir una etiqueta en `plantilla_mensaje`.
- Verificado: test `ElCuerpoSaleEnTextoPlanoYLiteral` (un cliente llamado `Roa<b> & Cia` sale tal cual en el correo, sin `&lt;`, sin `&amp;` y sin documento HTML).
