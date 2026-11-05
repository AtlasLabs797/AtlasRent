-- Ejecutar como superusuario (postgres) UNA vez por servidor.
-- Las contraseñas se pasan con psql -v; nunca se escriben en este archivo.
--   psql -U postgres -v pw_migrador="'...'" -v pw_app="'...'" -v pw_backup="'...'" -v pw_jobs="'...'" -f 01_roles.sql
CREATE ROLE rent_owner NOLOGIN;
CREATE ROLE rent_migrador LOGIN PASSWORD :pw_migrador IN ROLE rent_owner;
CREATE ROLE rent_app      LOGIN PASSWORD :pw_app;
CREATE ROLE rent_backup   LOGIN PASSWORD :pw_backup;
-- Rol del servidor de jobs (Hangfire), SEC-020. Deliberadamente NO es miembro de rent_owner: con
-- rent_migrador (que sí lo es) la API podía hacer ALTER TABLE auditoria DISABLE TRIGGER y anular el
-- append-only. rent_jobs solo es dueño de atlas_rent_jobs (04_base_jobs.sql) y no tiene CONNECT sobre
-- atlas_rent.
CREATE ROLE rent_jobs     LOGIN PASSWORD :pw_jobs;
ALTER ROLE rent_app    SET statement_timeout = '30s';
ALTER ROLE rent_app    SET idle_in_transaction_session_timeout = '60s';

-- El rol de backup necesita BYPASSRLS, y es el punto más delicado de este archivo.
--
-- `pg_dump` extrae con `COPY ... TO STDOUT`, y PostgreSQL rechaza esa sentencia (SQLSTATE 55000,
-- "query would be affected by row-level security policy") sobre una tabla con RLS activada si el
-- rol no la ignora. Como `auditoria` y `documento_inmueble` la tienen activada, sin esta línea el
-- volcado falla; y si alguien "lo arregla" degradando el fallo, el backup sale VACÍO sin error.
-- Ninguna política RLS lo resuelve: el bloqueo es de la sentencia COPY.
--
-- Solo un superusuario puede concederlo, y el script se ejecuta como tal.
--
-- Riesgo asumido: si `rent_backup` se compromete, se lee todo, incluidas las tablas con RLS. Es
-- inherente a poder respaldar la base. Se compensa con que el backup va cifrado y con que este rol
-- no sirve para operar la aplicación. Al restoring, usa un superusuario, nunca este rol.
ALTER ROLE rent_backup BYPASSRLS;
