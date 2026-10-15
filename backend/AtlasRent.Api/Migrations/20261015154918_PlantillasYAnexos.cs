using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class PlantillasYAnexos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plantilla_anexo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    secciones = table.Column<string>(type: "jsonb", nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plantilla_anexo", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plantilla_contrato",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    secciones = table.Column<string>(type: "jsonb", nullable: false),
                    anexos_tipos = table.Column<string>(type: "jsonb", nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plantilla_contrato", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contrato_plantilla_congelada",
                columns: table => new
                {
                    contrato_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plantilla_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    secciones = table.Column<string>(type: "jsonb", nullable: false),
                    anexos = table.Column<string>(type: "jsonb", nullable: false),
                    congelada_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contrato_plantilla_congelada", x => x.contrato_id);
                    table.ForeignKey(
                        name: "fk_contrato_plantilla_congelada_contrato_contrato_id",
                        column: x => x.contrato_id,
                        principalTable: "contrato",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contrato_plantilla_congelada_plantilla_contrato_plantilla_id",
                        column: x => x.plantilla_id,
                        principalTable: "plantilla_contrato",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contrato_plantilla_congelada_plantilla_id",
                table: "contrato_plantilla_congelada",
                column: "plantilla_id");

            migrationBuilder.CreateIndex(
                name: "ix_plantilla_anexo_activa_nombre",
                table: "plantilla_anexo",
                columns: new[] { "activa", "nombre" });

            migrationBuilder.CreateIndex(
                name: "ix_plantilla_anexo_tipo",
                table: "plantilla_anexo",
                column: "tipo",
                unique: true,
                filter: "(activa)");

            migrationBuilder.CreateIndex(
                name: "ix_plantilla_contrato_activa_nombre",
                table: "plantilla_contrato",
                columns: new[] { "activa", "nombre" });

            migrationBuilder.CreateIndex(
                name: "ix_plantilla_contrato_codigo",
                table: "plantilla_contrato",
                column: "codigo",
                unique: true);

            // F2-07: las tablas las crea rent_migrador y 02_basedatos.sql solo declara permisos por
            // defecto para rent_owner (docs/DOC_TECNICA.md §4.3), así que sin esta línea la migración
            // aplica bien y la aplicación falla en runtime con "permission denied" cuando escribe una
            // plantilla, un anexo o la copia congelada de un contrato.
            // DELETE no entra: una plantilla, un anexo y una copia congelada no se borran nunca
            // (AGENTS.md §6). Desactivar es poner un estado, así que la aplicación no necesita ese
            // permiso.
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT, UPDATE ON plantilla_contrato, plantilla_anexo, contrato_plantilla_congelada TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contrato_plantilla_congelada");

            migrationBuilder.DropTable(
                name: "plantilla_anexo");

            migrationBuilder.DropTable(
                name: "plantilla_contrato");
        }
    }
}
