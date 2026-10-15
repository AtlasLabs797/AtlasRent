-- Base de datos del servidor de jobs (Hangfire). Ejecutar como superusuario (postgres) UNA vez por
-- servidor, después de 01_roles.sql y 02_basedatos.sql.
--
-- Las contraseñas se pasan con psql -v; nunca se escriben en este archivo.
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
-- La conexión del servidor de jobs usa ConnectionStrings__Migraciones (rol rent_migrador) con esta
-- base: va a escribir en tablas que rent_app no debe poder tocar.
--
-- El resto de permisos de 02_basedatos.sql NO se repiten aquí: valen para atlas_rent, no para esta.
CREATE DATABASE atlas_rent_jobs OWNER rent_owner ENCODING 'UTF8' TEMPLATE template0;
\connect atlas_rent_jobs
REVOKE ALL ON DATABASE atlas_rent_jobs FROM PUBLIC;
GRANT CONNECT ON DATABASE atlas_rent_jobs TO rent_migrador;