# F0-04 — Cuenta y carpeta de Google Drive para los backups de Atlas Rent

> **Decisión tomada el 15/10/2026: la copia de seguridad la sube la persona que usa la aplicación, con su propia cuenta de Google, mediante OAuth 2.0 en su nombre.** Eso descarta la cuenta de servicio como mecanismo de subida y descarta contratar Google Workspace. Las secciones 2 y 3 se mantienen como referencia técnica de por qué la cuenta de servicio no sirve y de qué habría que hacer si algún día se quiere un backup desatendido. **Lo que hay que hacer ahora es la sección 4 y el punto 4 de la sección 1.**

Resuelve **F0-04**: dejar el destino de los backups de Atlas Rent decidido y con un procedimiento cerrado.
Bloquea **F1-12** (backups con Hangfire + Google Drive cifrado).

No es una tarea de código. Este documento deja el procedimiento cerrado. **No he ejecutado nada de lo que aquí se describe: no hay ninguna cuenta creada, ningún proyecto de Google Cloud y ninguna clave generada.**

---

## 1. Qué hay que crear

Tres cosas distintas, y confundirlas es el error más común:

| # | Qué | Para qué |
|---|---|---|
| 1 | **Cuenta de servicio propia de Atlas Rent** (Google Cloud) | **Descartada** por la decisión del 15/10/2026: sin cuota, no puede crear archivos. Se deja aquí documentada por si algún día se opta por la unidad compartida |
| 2 | **Carpeta de destino en el Drive de cada persona** | Receptor de los backups cifrados. Hay que decidir si es una carpeta por persona o una común compartida entre las que authenticate la app |
| 3 | **Dueño humano de la cuenta** (persona que paga la cuota y a la que se avisa si falla el backup) | Alguien tiene que poder abrir esa carpeta y renovar el consentimiento OAuth |

Sobre el punto 3, conviene decidirlo **ahora**, no cuando falle el primer backup: si la cuota se agota o la clave caduca, tiene que haber alguien que pueda actuar sin buscar quién es.

**Cuentas de servicio de Google Drive: limitation real (verificada).** La documentación oficial dice, textualmente, que *"las cuentas de servicio no tienen cuota de almacenamiento y no pueden ser propietarias de archivos; en su lugar deben subir archivos y carpetas a unidades compartidas, o usar OAuth 2.0 para subir elementos en nombre de un usuario humano"* ([about-shareddrives](https://developers.google.com/workspace/drive/api/guides/about-shareddrives), consultado el 15/10/2026). Esto tiene tres consecuencias que cambian el diseño:

1. **La carpeta de destino debe ser una unidad compartida (shared drive), no una carpeta normal dentro del Drive personal de alguien.** Las cuentas de Gmail personales no pueden crear unidades compartidas.
2. **La unidad compartida debe pertenecer a una organización de Google Workspace** con una edición que las soporte (Business Standard en adelante, según la misma documentación). Si Atlas Labs no tiene Google Workspace, esto es una decisión de compra que tiene que tomar el responsable.
3. **Si se elige la alternativa de OAuth 2.0 en nombre de una persona**, hay que gestionar un token de refresco, no una clave de servicio. Es más frágil (el token se puede revocar, la cuenta se puede bloquear con MFA) y la cuota va contra la cuenta humana.

**Consecuencia sobre las 4 copias:** aunque la cuota no la imponga la cuenta de servicio, 4 copias de BD + documentos ocupan espacio real en la unidad compartida, que cuenta contra el almacenamiento agregado de la organización. Con unidad compartida, las 4 copias gastan cuota de la empresa igual que en el Drive personal. **El tamaño del backup hay que medirlo antes de producción** (F1-12) y decidir si 4 es suficiente y cabe; hoy la rotación a 4 es una decisión cerrada (`docs/PLAN.md` F1-12, `docs/DOC_TECNICA.md` §8) y no la cambies aquí sin registrarlo.

---

## 2. Cómo se autenticará la aplicación

**API de Google Drive con cuenta de servicio** (server-to-server, sin interacción de usuario). Procedimiento real según la documentación oficial:

1. **Crear un proyecto en Google Cloud** para Atlas Rent, separado del de Atlas Balance.
2. **Habilitar la API de Google Drive** en ese proyecto (APIs & Services → Library). Sin esto, la cuenta de servicio recibe error al llamar a la API.
3. **Crear la cuenta de servicio** dentro del proyecto (IAM & Admin → Service Accounts), con un nombre reconocible (`atlasrent-backup`) y una descripción que diga para qué es.
4. **Generar una clave JSON** de tipo "Cuenta de servicio" y **descargarla una sola vez**. Google solo la muestra una vez.
5. **Compartir la carpeta (o unidad compartida) de destino** con el correo de la cuenta de servicio (`<nombre>@<proyecto>.iam.gserviceaccount.com`) con **permiso de editor** sobre esa carpeta. Sin este paso, la cuenta de servicio no ve nada: solo accede a lo que se le comparte explícitamente.
6. **Guardar la clave FUERA del repositorio**: primero en el gestor de contraseñas de la empresa; después, en los secretos del despliegue.

Sobre el scope: la documentación de Drive recomienda el scope más estrecho que sirva (`api-specific-auth`, consultado el 15/10/2026). **No lo fijo aquí**: la elección entre `drive.file` y `drive` depende de si el destino es una carpeta compartida o una unidad compartida, y se decide al implementar en F1-12 con una prueba real de subida.

### La clave JSON es un secreto

`AGENTS.md` §6 y `docs/SEGURIDAD.md` A.7 son tajantes: los secretos solo en variables de entorno del servicio / DPAPI / `dotnet user-secrets` en desarrollo, y gitleaks en CI. Concretamente:

- **Nunca en Git.** Ni en el repo, ni en una rama, ni en un gist, ni en un `.env.dev` que alguien pueda commitear por error.
- **Nunca en `.env.dev` versionado.** `.env.dev` está ignorado por git y es de desarrollo; la clave de producción no va ahí.
- **Nunca en el repositorio de backups ni en la carpeta de Drive.** Se sube el backup cifrado, no la credencial que lo sube.
- Si la clave se filtra: revocarla en Google Cloud (IAM → la cuenta → borrar la clave), generar otra y actualizar el secreto del despliegue. Por eso conviene anotar en el gestor de contraseñas **qué clave es cuál y cuándo se generó**.

---

## 3. Requisitos del backup ya decididos en el proyecto

Esto no se decide aquí; se cita para que quede claro qué tiene que cumplir el destino que se cree.

| Requisito | Dónde está definido |
|---|---|
| El archivo se **cifra antes de subir** (nunca un backup en claro en Drive) | `docs/DOC_TECNICA.md` §8 ("Backup cifrado BD + documentos"), `docs/SEGURIDAD.md` A.8, y `Backup__ClaveCifrado` en `config/variables-entorno.example` |
| **Rotación a 4 copias** | `docs/PLAN.md` F1-12 ("conservar 4"), `docs/SEGURIDAD.md` A.8, `Backup__Conservar=4` |
| **Job diario a las 02:00 hora AST** | `docs/DOC_TECNICA.md` §8 ("diario 02:00 AST") |
| **Si el job falla → alerta** | `docs/PLAN.md` F1-12, `docs/SEGURIDAD.md` A.8 ("Alerta si un backup falla o no se ejecuta") |
| Copia **en Google Drive con cuenta/carpeta propia de Atlas Rent** | `docs/SEGURIDAD.md` A.8, `docs/PLAN.md` F0-04 |
| **Restauración probada en otra máquina** (no es parte de F0-04, pero es el control que da sentido a todo lo anterior) | `docs/SEGURIDAD.md` A.8 |

**Por qué el backup va cifrado antes de subir:** porque el destino es un Drive de una organización, con gente que puede tener acceso, y el backup contiene exactamente lo más sensible de la aplicación: cédulas y RNC cifrados pero presentes, títulos de propiedad y caja (`docs/SEGURIDAD.md` SEC-004). Que el backup esté cifrado no lo salva un error de permisos; lo salva que, sin la clave, el archivo en Drive no sirve para nada.

---

## 4. Variables de entorno

Los nombres ya existen en `config/variables-entorno.example` y **no se tocan**:

- `Backup__GoogleDrive__Carpeta` → ID de la carpeta o unidad de destino.
- `Backup__GoogleDrive__Credenciales` → la credencial.
- `Backup__ClaveCifrado` → clave de cifrado del backup.
- `Backup__Conservar=4`.

**Propuesta de nombre nuevo, pendiente de tu aprobación (no lo he añadido al archivo):**

Si el destino es una **unidad compartida** en vez de una carpeta, harían falta dos datos que hoy no tienen variable propia: el ID de la unidad compartida y el ID del Drive/organización. Propongo, **si lo apruebas**:

```
Backup__GoogleDrive__EsUnidadCompartida=true|false
Backup__GoogleDrive__IdUnidadCompartida=<id>
```

Decide tú si lo hace falta. Puede que F1-12 resuelva esto sin variables nuevas (por ejemplo, detectando el tipo de carpeta) y entonces no se añade nada.

---

## 5. Comprobación para dar F0-04 por hecha

Datos que tiene que anotar la persona, **y dónde**:

| Dato | Dónde se anota | Nunca |
|---|---|---|
| Correo de la cuenta de servicio (`…@….iam.gserviceaccount.com`) | Gestor de contraseñas de la empresa, entrada "Atlas Rent — Google Cloud" | — |
| ID del proyecto de Google Cloud | Gestor de contraseñas | — |
| ID de la carpeta **o** de la unidad compartida de destino | Gestor de contraseñas | — |
| Fecha de creación de la clave y fecha de la última rotación | Gestor de contraseñas | — |
| Persona propietaria humana (la que paga y a la que se avisa) | Gestor de contraseñas | — |
| Ruta del archivo JSON de la clave | Gestor de contraseñas (como adjunto), fuera del disco del servidor de desarrollo | **En el repo, nunca** |

Pasos:

- [ ] Proyecto de Google Cloud de Atlas Rent creado, **separado del de Atlas Balance**.
- [ ] API de Google Drive **habilitada** en ese proyecto.
- [ ] Cuenta de servicio `atlasrent-backup` creada (nombre exacto el que se decida y anote).
- [ ] Clave JSON generada y **descargada una sola vez**.
- [ ] Unidad compartida (o carpeta) de destino creada y de **propiedad de Atlas Rent, no de Atlas Balance**.
- [ ] Unidad compartida compartida con el correo de la cuenta de servicio, **permiso de editor**.
- [ ] Prueba de subida hecha desde un equipo de prueba: la cuenta de servicio lista la carpeta, sube un archivo de prueba y lo borra. Sin esta prueba, F1-12 descubre el fallo en producción.
- [ ] Clave guardada en el gestor de contraseñas y **verificada que no está en el repo** (`git status`, búsqueda de `*.json` de credenciales).
- [ ] Propietario humano de la cuenta decided y anotado.
- [ ] Tamaño estimado del backup medido y contrastado con la cuota disponible (relevante para las 4 copias, §1).
- [ ] Decidido y anotado si se añade `Backup__GoogleDrive__EsUnidadCompartida` / `...__IdUnidadCompartida` al archivo de variables.
- [ ] `docs/PLAN.md`: F0-04 → `HECHA (dd/mm/aaaa)`, y entrada en `CHANGELOG.md` (`AGENTS.md` §7).

---

## Fuentes consultadas

Todas consultadas el **15/10/2026**.

- Google Drive API, "Choose Google Drive API scopes" — https://developers.google.com/workspace/drive/api/guides/api-specific-auth — scopes recomendados y su clasificación de sensibilidad; recomienda el scope más estrecho posible.
- Google Drive API, "About shared drives" — https://developers.google.com/workspace/drive/api/guides/about-shareddrives — **"Service accounts don't have storage quota and can't own files. Instead, they must upload files and folders into shared drives, or use OAuth 2.0 to upload items on behalf of a human user."** También que las cuentas Gmail personales no pueden crear unidades compartidas y que las unidades compartidas cuentan contra el almacenamiento agregado de la organización.
- Google Workspace, "Create Google Workspace API credentials" — https://developers.google.com/workspace/guides/create-credentials — tipos de credencial y pasos de creación de cuenta de servicio, asignación de roles y gestión de claves.
- Google Drive API, "Usage limits" — https://developers.google.com/workspace/drive/api/guides/limits — límites de peticiones a la API (no de almacenamiento).
- Google Identity, "Using OAuth 2.0 for Server to Server Applications" — https://developers.google.com/identity/protocols/oauth2/service-account — flujo de cuenta de servicio.
- Documentos internos del repositorio: `docs/DOC_TECNICA.md` §7, §8 y §9; `docs/SEGURIDAD.md` A.6, A.7, A.8 y SEC-004; `docs/PLAN.md` F0-04 y F1-12; `config/variables-entorno.example`.

### Nota sobre las URL que no funcionan

La ruta `https://developers.google.com/drive/api/guides/authorize-service-account` que se indicó como referencia **devuelve 404** a fecha de hoy: la documentación de Workspace se movió a `/workspace/drive/api/…`. Las equivalentes vigentes son las de la lista anterior, y ninguna de ellas tiene una sección dedicada titulada "authorize service account" para Drive; el procedimiento de abajo se reconstruyó a partir de `create-credentials` (pasos de creación de la cuenta y la clave), `api-specific-auth` (scopes) y `about-shareddrives` (la limitación de cuota). **Si en algún momento aparece una guía oficial específica de "autorizar con cuenta de servicio" para Drive, manda esa sobre este documento.**

## Qué NO se pudo verificar

- **Si Atlas Labs tiene o puede contratar Google Workspace con una edición que soporte unidades compartidas.** No hay información de eso en el repositorio y no se consultó ninguna cuenta de la empresa. **De esta respuesta depende que la Opción 1 de §1 sea viable:** sin unidad compartida, la alternativa es OAuth 2.0 en nombre de una persona, que cambia el diseño de autenticación de F1-12.
- **Si existe cuota suficiente para 4 copias.** El tamaño del backup no existe todavía (depende de la BD y de los documentos, que aún no se han migrado). No se puede calcular.
- **Procedimiento exacto de creación de la clave en la consola de Google Cloud.** La documentación describe el conceptual (crear cuenta, asignar roles, generar claves); no seguí la consola paso a paso ni hice una descarga real.
- **Cifra concreta de cuota de Drive que se aplicará.** La fuente oficial consultada no da una cifra en bytes para cuentas de servicio, sino la afirmación categórica de que **no tienen cuota**. Es más fuerte que cualquier cifra, pero no la he medido con una subida real.
- **Scope OAuth definitivo.** Se decide en F1-12 (§2); no está probado aquí.
- **Estructura concreta de `Backup__GoogleDrive__Credenciales`** (si será el JSON completo, una ruta, o un token). La implementa F1-12; este documento solo fija que la credencial no vive en el repo.