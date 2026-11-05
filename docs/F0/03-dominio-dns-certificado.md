# F0-03 — Dominio público, DNS y certificado HTTPS

Resuelve **F0-03** (A6): tener un nombre de dominio público que resuelva al servidor y que permita emitir el certificado TLS.
Bloquea **FP-02** (despliegue en producción). De él dependen también el checklist A.1 de `docs/SEGURIDAD.md` y el alta real de 443 en el router.

No es una tarea de código: es una decisión de nombre + una reserva DNS + abrir 443. Este documento es el procedimiento cerrado para que la persona responsable lo ejecute sin ambigüedad.

---

## 1. Qué hay que decidir y comprar

**Un subdominio del dominio que la empresa ya tiene**, no un dominio nuevo. Ejemplo de forma: `rent.<dominio-empresa>` (aquí `ejemplo.do` es solo un marcador: usar el dominio real de Atlas Labs).

Por qué subdominio y no dominio nuevo:

- Un dominio nuevo cuesta dinero todos los años y hay que renovarlo; el coste depende del registrador y hay que confirmarlo con el proveedor, no está cotizado aquí.
- Un dominio nuevo exige configurar de nuevo correo, y suele abrirse a phishing porque nadie lo reconoce.
- El subdominio hereda la confianza del dominio principal y la Renovación la puede hacer el mismo responsable que hoy mantiene el DNS.
- Si el dominio de la empresa está bajo Cloudflare, Cloud DNS u otro, la validación del certificado puede aprovechar APIs que ya existen para esa zona.

**Lo que hay que tener claro sobre Let's Encrypt:** emite el certificado **gratis**, pero no es un generador de confianza. La autoridad de certificación verifica que **controlas el dominio** mediante un desafío ACME (ver §4), y el título (SAN/CN) del certificado identifica **el subdominio exacto** que emitiste. Sin un dominio público bajo control de Atlas Labs no hay certificado gratis posible. No se emite nada "porque la empresa se llama Atlas Labs".

Decisión a tomar y anotar:

| Decisión | Valor |
|---|---|
| Subdominio exacto (p. ej. `rent.<dominio>`) | __________ |
| Quién administra el DNS (registrador / proveedor) | __________ |
| ¿El dominio está detrás de Cloudflare u otro proxy? | __________ |

---

## 2. Los dos caminos técnicos que contempla el proyecto

`docs/DOC_TECNICA.md` §9 punto 5 dice literalmente: *"Certificado: HTTPS directo en Kestrel (win-acme / Let's Encrypt o certificado de la empresa) o Caddy delante"*. Los dos están contemplados; **el proyecto no obliga a ninguno**. La recomendación sale de lo que el propio repo prioriza.

### Opción A — Kestrel directo con win-acme / Let's Encrypt (recomendada si Atlas Rent va solo en el servidor)

- **Cómo funciona:** win-acme es un cliente ACMEv2 para Windows. Obtiene el certificado, lo deja en el almacén de Windows o como `.pfx`, instala el certificado en Kestrel (variables ya presentes en `config/variables-entorno.example`: `Kestrel__Certificates__Default__Path` y `...__Password`) y **crea una tarea programada que lo renueva solo** (win-acme "Automatically creates a scheduled task to renew certificates when needed").
- **Por qué la recomiendo:** es la opción que el repo nombra primero, la que `docs/SEGURIDAD.md` A.1 concreta ("Let's Encrypt con win-acme"), la que encaja con `ASPNETCORE_URLS=https://0.0.0.0:443` y con el `scripts/install.ps1` que ya crea las reglas de firewall de solo 443. No añade ningún proceso nuevo en el servidor: menos superficie, menos mantenimiento, menos que romper.
- **Requisito operativo:** la renovación automática exige que el desafío se pueda validar para siempre. Por eso el §4 no es opcional.

### Opción B — Caddy delante de Kestrel

- **Cómo funciona:** Caddy termina TLS, obtiene y renueva el certificado solo, y hace de proxy hacia Kestrel escuchando solo en `localhost`.
- **Cuándo es obligatoria:** si el 443 ya lo ocupa otra aplicación en esa misma IP (ver §6). También es la opción natural si algún día entra Cloudflare delante, porque `ForwardedHeaders` ya está previsto para un proxy de confianza.
- **Coste:** Caddy es software libre y no tiene licencia que pagar, pero **sí cuesta** un proceso más en el servidor, un archivo de configuración más que mantener y una regla de `ForwardedHeaders` que hay que limitar a Caddy (`docs/SEGURIDAD.md` A.1). Si Kestrel escucha solo en localhost y `ForwardedHeaders` queda abierto a cualquier origen, la app se puede falsear la IP del cliente y se rompe el bloqueo por IP de F1-05.

**Recomendación:** Opción A si el servidor es de Atlas Rent; Opción B si comparte 443 con Atlas Balance. No decidas esto antes de cerrar la §6.

**Certificado de la empresa:** `docs/DOC_TECNICA.md` §9 lo menciona como tercera posibilidad (corporativo emitido internamente). Exige proceso de emisión y de renovación propios de la empresa. No la recomiendo para este despliegue: el checklist A.1 del proyecto exige renovación automática con alerta de caducidad y nada en el repo define ese proceso.

---

## 3. Pasos de DNS

Un solo registro, el del subdominio:

| Campo | Valor |
|---|---|
| Tipo | **A** |
| Nombre | el subdominio completo (p. ej. `rent`) |
| Valor | **IP pública del servidor** (IPv4; si hay IPv6 published, también un AAAA) |
| TTL | **300–3600 s** al empezar |

Detalles que importan:

- **Si la IP pública es dinámica**, un registro A a mano se queda viejo y el certificado renewal falla sin que nadie se entere hasta que caduque. Opciones, en orden de preferencia: pedir IP fija al proveedor, usar DDNS del registrador/proveedor, o poner Cloudflare delante. Decide esto antes, no después.
- **No pongas el `www` ni el apex**: el certificado es para el subdominio elegido. Añadir nombres de más obliga a reemitir.
- **Baja el TTL a 300–3600 antes de cambiar** cualquier registro existente, y súbelo después. Sin eso, cada cambio cuesta una hora de caché.
- **No uses CNAME** para apuntar a una IP: solo sirve a otro nombre, no a una dirección.
- **No hay registro CAA obligatorio.** Si la zona ya tiene un CAA published, revisa que no bloquee `letsencrypt.org` (ni `zerossl.com` si se acaba usando win-acme contra ZeroSSL; hoy win-acme se mantiene por ZeroSSL). No lo agregues si no existe: es opcional.
- Verifica la resolución desde **fuera** de tu red, no desde el servidor. Un `nslookup` desde dentro puede resolver por caché local y darte una falsa confianza.

---

## 4. El puerto 80: por qué no se abre y cómo se valida sin él

`docs/DOC_TECNICA.md` §9 punto 4 deja el firewall de Windows con **solo 443**, y el punto 6 dice "Router: reenviar solo 443". Abrir 80 contradice el checklist A.1 de `docs/SEGURIDAD.md` y amplía la superficie de ataque sin necesidad.

Los tres desafíos que existen (verificados en la documentación de Let's Encrypt, §8):

| Desafío | Puerto | Qué necesita |
|---|---|---|
| **HTTP-01** | **solo 80** (el estándar ACME no permite otro puerto) | Kestrel o algo detrás sirviendo `/.well-known/acme-challenge/<TOKEN>` en 80 |
| **TLS-ALPN-01** | **443** | Un servidor que hable el ALPN de validación en TLS |
| **DNS-01** | ninguno (TXT en `_acme-challenge.<dominio>`) | API de DNS del proveedor |

**Respuesta a la pregunta clave: sí se puede validar sin abrir el 80.** win-acme tiene plugin de validación **TLS** además de validación HTTP, y también un set de plugins DNS (SFTP/FTPS, acme-dns, Azure, Route53, Cloudflare, GoDaddy, manual…). Verificado en la documentación de win-acme (§8).

Orden de preferencia para este proyecto:

1. **TLS-ALPN-01 con win-acme.** Reusa el 443 que ya vas a abrir de todos modos y no depende de la zona DNS ni de su API. Es lo que encaja con un firewall de "solo 443".
2. **DNS-01** si el proveedor de DNS tiene API (sobre todo si el dominio está en Cloudflare/Azure) o si algún día quieres certificado comodín `*.dominio`. Exige guardar credenciales de la API DNS en el servidor, que es un secreto más que custodiar.
3. **HTTP-01 solo como recurso temporal:** abrir 80 en el router y en el firewall, emitir, cerrar. Sirve para una emisión puntual, no para una renovación automática que se repetirá cada ~60 días sin nadie mirando.

**DNS-01 es la única opción que emite comodines.** Si algún día se quiere `*.rent.dominio`, hay que pasar a DNS-01 o a un certificado de la empresa.

**Aviso sobre TLS-ALPN-01:** Let's Encrypt describe este desafío como no apto para la mayoría, pensado sobre todo para hosting providers y proxies que termine TLS. **No he podido verificar con una emisión real que win-acme lo resuelve sin servidor web detrás.** Si la emisión falla, el plan B es DNS-01, y el plan C es abrir 80 temporalmente. Decide el plan B antes de la primera emisión.

---

## 5. Requisitos de red

- **IP pública** asignada al servidor. Confirma la tuya desde fuera antes de escribir el registro A (lo que ves dentro puede ser NAT de varios niveles).
- **Router:** reenviar **443/TCP** del exterior a la IP privada del servidor. **Nada más.** Nada de 80 (§4), nada de 5432, nada del panel de Hangfire, nada del Watchdog, nada de RDP. `docs/SEGURIDAD.md` A.1 lo dice punto por punto.
- **Firewall de Windows:** regla entrante solo 443, salida libre. La crea `scripts/install.ps1` según DOC_TECNICA §9 punto 4; si la creas a mano, que coincida.
- **Dentro de la red, RDP solo desde la red interna** (A.1). No lo publiques en el router ni "solo para probar".
- **Prueba desde fuera:** abrir la URL desde un móvil en datos móviles, no desde el wifi de la oficina. Y comprobar que responde a `https://` y no a `http://`.

---

## 6. Convivencia con Atlas Balance

`docs/DOC_TECNICA.md` §9, último párrafo: *"Si comparte servidor con otras aplicaciones: puertos distintos o el mismo 443 con Caddy enrutando por subdominio; carpetas, servicios, roles de BD y claves separados."*

Las dos salidas reales:

| Situación | Solución | Consecuencia |
|---|---|---|
| **Atlas Balance ya ocupa 443 en esa IP** | Un solo **Caddy** delante de las dos apps, enrutando por subdominio (`balance.<dominio>`, `rent.<dominio>`) | Caddy pide y renueva los dos certificados. Kestrel de Atlas Rent pasa a escuchar solo en `localhost`. **Obligatorio**: `ForwardedHeaders` limitado a Caddy y sin proxies de confianza abiertos (A.1, F1-05) |
| **Se puede dar una segunda IP pública / otro puerto público** | Atlas Rent con su IP, su 443 y su win-acme | Cada app independientes. Menos acoplamiento, pero depende de lo que permita el router y el proveedor |

Lo que **no** es aceptable: dos procesos escuchando en 443 y que gane el último que arrancó. Si comparten, Caddy es obligatorio.

Separa además, en los dos casos: carpetas de instalación, cuentas de servicio de Windows, roles de BD (`rent_*` frente a los de Atlas Balance) y claves (`docs/DOC_TECNICA.md` §9).

---

## 7. Aviso de seguridad (léase antes de abrir el puerto)

`docs/SEGURIDAD.md` SEC-R02 acepta explícitamente que **la app queda expuesta a internet sin VPN**. La mitigación que ese registro declara obligatoria es:

- **A.1 completo** antes de abrir 443.
- **A.2 completo**: bloqueo de cuenta y **bloqueo de IP escalado** (F1-05), rate limiting, aviso de IP nueva.
- **Revisión de seguridad (FP-01) antes de abrir el puerto.**

Consecuencia práctica: **abrir 443 sin F1-04 (sesión validada en BD, revocación inmediata) y F1-05 (bloqueo de cuenta e IP) publicados es dejar la pantalla de login de la aplicación de facturación y cobros a internet sin defensa.** Si hay que abrir el puerto antes de que esas tareas estén hechas, el mitigante es la lista blanca de IP de oficina (`Seguridad__IpListaBlanca`) y el acceso restringido por IP en el router, no abrir a todo el mundo.

---

## 8. Coste

- **Dominio / subdominio:** sin coste si el dominio de la empresa ya está pagado. Si hay que registrarlo, el precio depende del registrador y hay que confirmarlo con el proveedor; no está cotizado aquí.
- **Let's Encrypt:** sin coste. win-acme es software libre. Lo que cuesta es el tiempo de la persona que lo configura y la alerta de caducidad.
- **Caddy:** sin coste de licencia, con coste de configuración y de un proceso más.
- **Cloudflare (opcional, recomendado en A.1):** tiene plan gratuito; el coste real depende del nivel de servicio que se elija y hay que confirmarlo. No es requisito: A.1 lo marca como "recomendado, opcional".

---

## 9. Comprobación para dar F0-03 por hecha

Datos:

- [ ] Subdominio elegido y anotado en la tabla del §1.
- [ ] Registro **A** creado con la IP pública correcta; TTL 300–3600.
- [ ] Resuelve desde fuera de la red: `nslookup`/curl a `https://<subdominio>` desde datos móviles.
- [ ] Router reenvía **solo 443/TCP** al servidor. Comprobado mirando la configuración, no de memoria.
- [ ] Firewall de Windows: regla entrante solo 443 (la de `scripts/install.ps1`).
- [ ] Definido el desafío de validación: TLS-ALPN-01 (preferido) o DNS-01. Si es HTTP-01, documentado que es temporal y que el 80 se cierra después de emitir.
- [ ] win-acme instalado y configurado; **tarea programada de renovación creada** y verificada en el Programador de tareas.
- [ ] Primera emisión hecha y `https://<subdominio>` responde con TLS válido.
- [ ] **Caducidad del certificado comprobada** (la emite Let's Encrypt con 90 días de vigencia) y alertada. Caducar = caída de la app.
- [ ] Prueba de renovación forzada realizada con éxito.
- [ ] Kestrel escucha en el puerto y con el certificado configurado (`Kestrel__Certificates__Default__Path` y `...__Password` en el entorno del servicio, nunca en el repo).
- [ ] Si comparte servidor: Caddy enrutando por subdominio **o** segunda IP/puerto, `ForwardedHeaders` limitado a ese proxy, claves y roles de BD separados de Atlas Balance.
- [ ] Revisado `docs/SEGURIDAD.md` A.1 y A.2; **F1-04 y F1-05 están** antes de abrir el puerto a todo internet.
- [ ] Coste del dominio (0 o a confirmar con el registrador) anotado por el responsable.
- [ ] `docs/PLAN.md`: F0-03 → `HECHA (dd/mm/aaaa)`, y entrada en `CHANGELOG.md` (`AGENTS.md` §7).

---

## Fuentes consultadas

Todas consultadas el **15/10/2026**.

- Let's Encrypt, "Challenge Types" (HTTP-01 solo puerto 80; TLS-ALPN-01 por 443; DNS-01 con TXT en `_acme-challenge`) — https://letsencrypt.org/docs/challenge-types/
- win-acme, README (cliente ACMEv2 para Windows, tarea programada de renovación, almacenamiento `.pfx`, plugins DNS/HTTP/TLS) — https://github.com/win-acme/win-acme/blob/master/README.md
- win-acme, "Plugins" (categorías de validación: HTTP validation, TLS validation, DNS validation) — https://win-acme.com/reference/plugins
- win-acme, sitio oficial (versión recomendada 2.2.9.1, release "pluggable" para plugins adicionales) — https://win-acme.com/
- Documentos internos del repositorio: `docs/DOC_TECNICA.md` §7, §8 y §9; `docs/SEGURIDAD.md` A.1, A.2, A.8 y SEC-R02; `docs/PLAN.md` tabla Fase 0; `config/variables-entorno.example`.

## Qué NO se pudo verificar

- **Que win-acme resuelva TLS-ALPN-01 en esta configuración concreta sin servidor web detrás.** La documentación de win-acme confirma que existe el plugin de validación TLS, pero Let's Encrypt describe TLS-ALPN-01 como pensado para proxies que terminan TLS. No hice una emisión real. Plan B: DNS-01. Plan C: abrir 80 temporalmente.
- **Disponibilidad de API DNS del proveedor del dominio de Atlas Labs.** Depende de dónde esté el DNS (registrador, Cloudflare, Azure…). No se consultó porque el dominio real no está en el repositorio y `AGENTS.md` §6 prohíbe abrir credenciales.
- **Si Atlas Balance comparte servidor/IP con Atlas Rent.** No hay información en el repositorio. Si comparte 443, la Opción B (§2) pasa a ser obligatoria.
- **Precios de dominio, de Cloudflare y de cualquier servicio de terceros.** Dependen del registrador y del nivel de servicio contratado; hay que confirmarlos con el proveedor. No se cotiza nada aquí.
- **Dirección IP pública concreta y si es fija o dinámica.** No se consultó el router ni ningún proveedor desde esta sesión.
- **La existencia del `scripts/install.ps1` y el contenido real de sus reglas de firewall.** `docs/DOC_TECNICA.md` §9 lo describe, pero los scripts aún no existen (F1-01 está `PENDIENTE`). La regla de firewall de solo 443 se creará ahí; hasta entonces es una regla manual.