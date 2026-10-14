-- Ejecutar como superusuario (postgres) UNA vez por servidor.
-- Las contraseñas se pasan con psql -v; nunca se escriben en este archivo.
--   psql -U postgres -v pw_migrador="'...'" -v pw_app="'...'" -v pw_backup="'...'" -f 01_roles.sql
CREATE ROLE rent_owner NOLOGIN;
CREATE ROLE rent_migrador LOGIN PASSWORD :pw_migrador IN ROLE rent_owner;
CREATE ROLE rent_app      LOGIN PASSWORD :pw_app;
CREATE ROLE rent_backup   LOGIN PASSWORD :pw_backup;
ALTER ROLE rent_app    SET statement_timeout = '30s';
ALTER ROLE rent_app    SET idle_in_transaction_session_timeout = '60s';
