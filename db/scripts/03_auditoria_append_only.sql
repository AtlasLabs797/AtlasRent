-- Se aplica desde la migración que crea la tabla "auditoria" (F1-03), ejecutada por rent_migrador.
-- 1) Permisos: la app solo inserta y lee.
REVOKE UPDATE, DELETE, TRUNCATE ON auditoria FROM rent_app;
GRANT INSERT, SELECT ON auditoria TO rent_app;

-- 2) Trigger que rechaza cualquier modificación, incluso de un rol con permisos.
CREATE OR REPLACE FUNCTION auditoria_inmutable() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'auditoria es append-only (operación % rechazada)', TG_OP;
END $$;

DROP TRIGGER IF EXISTS trg_auditoria_inmutable ON auditoria;
CREATE TRIGGER trg_auditoria_inmutable
  BEFORE UPDATE OR DELETE ON auditoria
  FOR EACH ROW EXECUTE FUNCTION auditoria_inmutable();

DROP TRIGGER IF EXISTS trg_auditoria_no_truncate ON auditoria;
CREATE TRIGGER trg_auditoria_no_truncate
  BEFORE TRUNCATE ON auditoria
  FOR EACH STATEMENT EXECUTE FUNCTION auditoria_inmutable();

-- 3) Hash encadenado: hash = sha256(hash_anterior || datos). Se calcula en la
--    aplicación dentro de la transacción con bloqueo (pg_advisory_xact_lock) para
--    mantener el orden; un job diario recorre la cadena y alerta si se rompe.
