-- Base de datos del servidor de jobs (Hangfire). Ejecutar como superusuario (postgres) UNA vez por
-- servidor, después de 01_roles.sql y 02_basedatos.sql.
--
-- 01_roles.sql ya creó el rol rent_jobs (con su contraseña por psql -v; nunca se escribe aquí).
--   psql -U postgres -f 04_base_jobs.sql
--
-- POR QUÉ UNA BASE PROPIA Y NO UN ESQUEMA EN atlas_rent
--
-- `pg_dump` hace LOCK TABLE de todo lo que encuentra. Con el esquema `hangfire` dentro de
-- `atlas_rent`, la copia de seguridad diaria fallaba con:
--
--     permission denied for schema hangfire
--
-- El backup que se rompía a sí mismo era el único que podía detectar el problema, y un backup que
-- falla sin copia de la anterior deja al sistema sin red de seguridad justo el día que la necesita.
-- Por eso los jobs van a `atlas_rent_jobs`.
--
-- El esquema `hangfire` y sus tablas los crea el propio Hangfire al arrancar
-- (PrepareSchemaIfNecessary); no hay que crearlos aquí.
--
-- SEC-020: la conexión del servidor de jobs usa ConnectionStrings__Jobs (rol rent_jobs), dueño de
-- esta base y SOLO de esta. Antes usaba rent_migrador, que pertenece a rent_owner: el proceso de la
-- API tenía así una credencial capaz de desactivar el trigger append-only de `auditoria`. rent_jobs
-- no es miembro de rent_owner y no tiene CONNECT sobre atlas_rent, así que aunque se comprometa la
-- API no llega a atlas_rent con él. rent_app y rent_migrador tampoco conectan aquí.
--
-- El resto de permisos de 02_basedatos.sql NO se repiten aquí: valen para atlas_rent, no para esta.
CREATE DATABASE atlas_rent_jobs OWNER rent_jobs ENCODING 'UTF8' TEMPLATE template0;
\connect atlas_rent_jobs
REVOKE ALL ON DATABASE atlas_rent_jobs FROM PUBLIC;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;