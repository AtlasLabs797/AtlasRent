-- Ejecutar como superusuario tras 01_roles.sql
CREATE DATABASE atlas_rent OWNER rent_owner ENCODING 'UTF8' TEMPLATE template0;
\connect atlas_rent
REVOKE ALL ON DATABASE atlas_rent FROM PUBLIC;
GRANT CONNECT ON DATABASE atlas_rent TO rent_migrador, rent_app, rent_backup;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
ALTER SCHEMA public OWNER TO rent_owner;
CREATE EXTENSION IF NOT EXISTS btree_gist;
CREATE EXTENSION IF NOT EXISTS pgcrypto;
-- Permisos por defecto para objetos que cree el migrador (como rent_owner)
ALTER DEFAULT PRIVILEGES FOR ROLE rent_owner IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO rent_app;
ALTER DEFAULT PRIVILEGES FOR ROLE rent_owner IN SCHEMA public
  GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO rent_app;
ALTER DEFAULT PRIVILEGES FOR ROLE rent_owner IN SCHEMA public
  GRANT SELECT ON TABLES TO rent_backup;
GRANT USAGE ON SCHEMA public TO rent_app, rent_backup;
