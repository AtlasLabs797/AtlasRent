# BUGS.md — Registro de bugs

Cada bug detectado o corregido se registra aquí. Lo más reciente arriba. No se borran entradas.

**Gravedad:** Crítica (dinero, numeración, datos perdidos o expuestos) · Alta (función principal rota) · Media (hay forma de rodearlo) · Baja (cosmético).
**Estado:** ABIERTO · EN CURSO · CORREGIDO · NO SE CORRIGE (motivo)

## Plantilla
```
### BUG-000 · Título corto
- Fecha detectado: dd/mm/aaaa · Fecha corregido: dd/mm/aaaa
- Gravedad: · Estado: · Módulo:
- Detectado por: (persona / test / agente / usuario)
- Síntoma: qué se ve.
- Cómo reproducir: pasos mínimos.
- Causa raíz: por qué pasaba (no el síntoma).
- Solución: qué se cambió y por qué así.
- Test que lo cubre: ruta del test.
- Commit/PR:
- Datos afectados: ¿hay registros en BD que corregir? ¿cómo se corrigieron?
```

## Registro

### BUG-001 · El paquete de despliegue salía sin la interfaz del frontend
- Fecha detectado: 15/10/2026 · Fecha corregido: 15/10/2026
- Gravedad: **Alta** · Estado: CORREGIDO · Módulo: despliegue / F1-13
- Detectado por: revisión adversarial del bloque F1 (subagente verificador)
- Síntoma: `Program.cs` servía el frontend compilado desde `wwwroot/`, pero `npm run build` escribía en `frontend/dist`. No había nada que pusiera una cosa donde la otra la lee, así que el ZIP de despliegue de DOC_TECNICA §9.3 («backend self-contained + frontend compilado») se publicaba sin interfaz, y el servicio instalado en `C:\AtlasRent` respondía 404 en `/`.
- Cómo reproducir: `npm run build` y después `dotnet publish backend/AtlasRent.Api -c Release -o <destino>`; en `<destino>/wwwroot/` no hay nada.
- Causa raíz: nadie connectó el paso «compilar el frontend» con el paso «publicar la API». El comentario de `Program.cs` daba por hecho un empaquetado que todavía no existía (`scripts/install.ps1` sigue sin escribirse).
- Solución: target `AtlasRentPublicarFrontend` en `backend/AtlasRent.Api/AtlasRent.Api.csproj`, que copia `frontend/dist` a `wwwroot/` del paquete en `dotnet publish`. Va en `Publish` y no en `Build` para que compilar en desarrollo y ejecutar los tests de integración no dependan de que el frontend esté construido. Si se publica sin haberlo construido, avisa por consola en vez de fallar en silencio, y el paquete sale sin interfaz. Se añade además un paso en la CI que publica y comprueba que `wwwroot/index.html` existe, para que este bug no pueda volver a colarse.
- Test que lo cubre: no hay test unitario (el fallo es de empaquetado, no de ejecución). Lo cubren la comprobación de la CI y la verificación manual con y sin `frontend/dist`.
- Commit/PR: `7dc9eea`
- Datos afectados: ninguno. No hay despliegues reales; el repositorio no se ha publicado nunca.

### BUG-002 · Renombrar una copia de respaldo con la fecha de hoy impedía la copia del día
- Fecha detectado: 15/10/2026 · Fecha corregido: 15/10/2026
- Gravedad: **Alta** · Estado: CORREGIDO · Módulo: respaldos / F1-12
- Detectado por: revisión adversarial del bloque F1 (subagente verificador)
- Síntoma: la fecha de una copia solo vivía en el nombre del archivo. Quien tuviera escritura en `Backup__CarpetaLocal` —o un script de restauración descuidado— podía renombrar una copia vieja con la fecha de hoy. Esa copia **verificaba** (es auténtica y descifrable), así que el servicio se la tomaba por la copia vigente: no generaba la del día, el log decía que todo estaba bien y la rotación acababa borrando las copias buenas creyendo que eran viejas. Es el escenario clásico de subversión de copias.
- Cómo reproducir: generar la copia de un día; renombrarla con la fecha de otro día; ejecutar el respaldo en ese segundo día. Devuelve `YaExistia = true` sin generar nada.
- Causa raíz: la fecha era un dato del sistema de archivos, no del contenido. Lo que la etiqueta GCM protege es el contenido descifrado, y la fecha no estaba dentro.
- Solución: el contenedor pasa a `ARBK2` y lleva la fecha **dentro** (`ContenedorCopia.Empaquetar(fecha, entradas)` / `Desempaquetar`). `VerificarAsync` compara la fecha del nombre con la del contenido y rechaza el archivo si no coinciden; como el contenido va cifrado y firmado, alterar la fecha del nombre ya no engaña a nadie.
- Test que lo cubre: `backend/AtlasRent.Tests/Backup/RotacionCopiaTests.cs` → `UnaCopiaRenombradaConLaFechaDeHoyNoCuentaComoLaCopiaDeHoy`; `backend/AtlasRent.Tests/Backup/CifradoCopiaTests.cs` → `LaFechaDelContenedorEsLaDelContenidoYNoLaDelNombre`. Comprobado que el primero **no es vacuo**: al desactivar la validación falla, enseñando `Fecha = 2026-10-16` frente a `FechaDelNombre = 2026-10-17`.
- Commit/PR: `38ebc78`
- Datos afectados: ninguno todavía. **Aviso de operación**: el cambio de marca (`ARBK2`) hace que las copias escritas por un binario anterior dejen de abrirse. Al desplegar un binario nuevo hay que regenerar la copia del día.

### BUG-003 · El respaldo fallido solo dejaba una línea de log
- Fecha detectado: 15/10/2026 · Fecha corregido: 15/10/2026
- Gravedad: Media · Estado: CORREGIDO · Módulo: respaldos / F1-12
- Detectado por: criterio de aceptación de F1-12 («fallo → alerta») revisado contra el código
- Síntoma: un respaldo fallido escribía con Serilog y lanzaba, pero no quedaba registro consultable. Una alerta en un log que nadie mira es la ausencia de alerta.
- Cómo reproducir: forzar un volcado vacío y comprobar que no queda nada registrado más allá del log.
- Causa raíz: nunca se implementó el registro de fallos; el código tenía un `TODO(A#)` en el punto exacto donde había que dispararlo.
- Solución: tabla `alerta` con `clave` única, entidad `Alerta` y `ServicioAlertas`, que hace `INSERT … ON CONFLICT DO UPDATE`: el mismo fallo repetido suma a `repeticiones` en vez de llenar la tabla de filas idénticas (ESPECIFICACION §3.16, «alertas sin duplicar»). `ServicioBackup` registra el fallo del día y el fallo de subida al almacén remoto. El motivo solo se traslada a la alerta si la excepción es del propio servicio, porque una inesperada puede llevar dentro la cadena de conexión.
- Test que lo cubre: `backend/AtlasRent.Tests/Backup/AlertaDeFalloTests.cs` (3 tests contra PostgreSQL real de Testcontainers) y `RotacionCopiaTests.cs` → `UnaCopiaQueFallaDejaAlertaConLaFechaDelDia`, `UnaCopiaQueSaleBienNoDejaNingunaAlerta`.
- Commit/PR: `38ebc78`, `5207e3a`
- Datos afectados: ninguno.
