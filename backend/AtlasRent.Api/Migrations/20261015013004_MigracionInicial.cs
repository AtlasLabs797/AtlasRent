using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class MigracionInicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Las extensiones las crea el superusuario en db/scripts/02_basedatos.sql. Se repiten
            // aquí con IF NOT EXISTS para que la migración aplique también sobre una base
            // preparada a mano.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pgcrypto;");

            // Posición de cada fila en la cadena de firmas. No es una identidad de la tabla a
            // propósito: EF Core usaría RETURNING y PostgreSQL filtra esa cláusula con la
            // política de SELECT, con lo que un empleado no podría registrar su propia auditoría.
            migrationBuilder.Sql(
                """
                CREATE SEQUENCE IF NOT EXISTS auditoria_secuencia_seq;
                """);

            migrationBuilder.CreateTable(
                name: "auditoria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    accion = table.Column<string>(type: "text", nullable: false),
                    entidad = table.Column<string>(type: "text", nullable: true),
                    entidad_id = table.Column<string>(type: "text", nullable: true),
                    datos = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    ip = table.Column<string>(type: "text", nullable: false),
                    resultado = table.Column<string>(type: "text", nullable: true),
                    origen = table.Column<string>(type: "text", nullable: true),
                    // La API siempre envía created_at para que coincida con lo firmado; el default
                    // solo cubre los inserts hechos a mano (script, job, consola).
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone", nullable: false, defaultValueSql: "clock_timestamp()"),
                    firma = table.Column<string>(type: "text", nullable: false),
                    firma_anterior = table.Column<string>(type: "text", nullable: false),
                    secuencia = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auditoria", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_created_at",
                table: "auditoria",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_secuencia",
                table: "auditoria",
                column: "secuencia",
                unique: true);

            // Contenido literal de db/scripts/03_auditoria_append_only.sql, que según AGENTS.md §9
            // se aplica desde esta migración.
            migrationBuilder.Sql(
                """
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
                """);

            // F1-03: RLS base. Sin rol (app.rol vacío o ausente) no hay lectura, porque
            // current_setting(..., true) devuelve NULL y NULL IN (...) no es verdadero.
            migrationBuilder.Sql(
                """
                ALTER TABLE auditoria ENABLE ROW LEVEL SECURITY;

                CREATE POLICY auditoria_lectura ON auditoria
                  FOR SELECT TO rent_app
                  USING (COALESCE(current_setting('app.rol', true), '') IN ('admin', 'gerente'));

                -- Con RLS activa y sin política de INSERT, PostgreSQL rechaza el INSERT de
                -- cualquier usuario. La app escribe auditoría en toda mutación (AGENTS.md §6).
                CREATE POLICY auditoria_insercion ON auditoria
                  FOR INSERT TO rent_app
                  WITH CHECK (true);

                -- La fila anterior de la cadena la necesita toda mutación, también la de un
                -- empleado. SECURITY DEFINER resuelve sin abrir la lectura de auditoría a ese rol:
                -- solo devuelve el hash, nunca contenido.
                CREATE OR REPLACE FUNCTION auditoria_ultima_firma() RETURNS TABLE (firma text)
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = public, pg_temp AS $$
                  SELECT a.firma FROM auditoria a ORDER BY a.secuencia DESC LIMIT 1
                $$;

                GRANT USAGE, SELECT ON SEQUENCE auditoria_secuencia_seq TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auditoria");

            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS auditoria_secuencia_seq;");
        }
    }
}