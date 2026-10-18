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
- [ ] Contraseñas BCrypt (12 rondas), mínimo 12 caracteres, comprobación contra HaveIBeenPwned (k-anonymity).
- [ ] **Bloqueo de cuenta:** 5 fallos → 30 min. Se registra en `intento_login`.
- [ ] **Bloqueo de IP:** ≥ 10 fallos desde una IP en 15 min → IP bloqueada 1 h; reincidencia en 24 h → 24 h; tercera → indefinido hasta desbloqueo por admin. [Valores configurables]
- [ ] Lista blanca de IP de oficina (nunca bloqueadas por IP; la cuenta sí).
- [ ] IP real del cliente tomada solo de cabeceras de proxies de confianza configurados (si no hay proxy, `RemoteIpAddress`).
- [ ] Rate limiting: login por IP y por usuario; API por usuario; webhook por IP.
- [ ] Mensaje de login genérico (no revela si el usuario existe ni si está bloqueado por cuenta o IP).
- [ ] MFA TOTP obligatorio para admin; códigos de recuperación de un solo uso, hasheados.
- [ ] Cookies `HttpOnly; Secure; SameSite=Strict`; CSRF double-submit (`X-CSRF-Token`).
- [ ] Inactividad 30 min, máximo 12 h; desactivar usuario o cambiar contraseña revoca todas sus sesiones en la siguiente petición.
- [ ] Aviso al admin (app + email) de entrada desde IP nueva para ese usuario.
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
- [ ] Cabeceras: CSP estricta, HSTS, `X-Content-Type-Options: nosniff`, `Referrer-Policy`, `frame-ancestors 'none'`, `Permissions-Policy`.
- [ ] CORS cerrado (mismo origen).
- [ ] Errores: ProblemDetails sin trazas; detalle solo en log interno con código de referencia.

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
