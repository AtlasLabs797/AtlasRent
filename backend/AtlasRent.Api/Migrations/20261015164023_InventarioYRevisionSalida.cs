using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class InventarioYRevisionSalida : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "inventario_congelado_at",
                table: "contrato",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "inventario_base_item",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    unidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    notas = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventario_base_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_inventario_base_item_unidad_unidad_id",
                        column: x => x.unidad_id,
                        principalTable: "unidad",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "revision_inventario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contrato_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firmada_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    firmada_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    importe_propuesto = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    observacion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_revision_inventario", x => x.id);
                    table.ForeignKey(
                        name: "fk_revision_inventario_contrato_contrato_id",
                        column: x => x.contrato_id,
                        principalTable: "contrato",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contrato_inventario_item",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contrato_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventario_base_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    notas = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    orden = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contrato_inventario_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_contrato_inventario_item_contrato_contrato_id",
                        column: x => x.contrato_id,
                        principalTable: "contrato",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contrato_inventario_item_inventario_base_item_inventario_ba",
                        column: x => x.inventario_base_id,
                        principalTable: "inventario_base_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "revision_inventario_item",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contrato_inventario_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    nota = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_revision_inventario_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_revision_inventario_item_contrato_inventario_item_contrato_",
                        column: x => x.contrato_inventario_item_id,
                        principalTable: "contrato_inventario_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_revision_inventario_item_revision_inventario_revision_id",
                        column: x => x.revision_id,
                        principalTable: "revision_inventario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contrato_inventario_item_contrato_id_orden",
                table: "contrato_inventario_item",
                columns: new[] { "contrato_id", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_contrato_inventario_item_inventario_base_id",
                table: "contrato_inventario_item",
                column: "inventario_base_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventario_base_item_unidad_id_activo_orden",
                table: "inventario_base_item",
                columns: new[] { "unidad_id", "activo", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_revision_inventario_contrato_id",
                table: "revision_inventario",
                column: "contrato_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_revision_inventario_item_contrato_inventario_item_id",
                table: "revision_inventario_item",
                column: "contrato_inventario_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_revision_inventario_item_revision_id_contrato_inventario_it",
                table: "revision_inventario_item",
                columns: new[] { "revision_id", "contrato_inventario_item_id" },
                unique: true);

            // F2-08: las cuatro tablas las crea rent_migrador y 02_basedatos.sql solo declara permisos
            // por defecto para rent_owner (docs/DOC_TECNICA.md §4.3), así que sin esta línea la
            // migración aplica bien y la aplicación falla en runtime con "permission denied" al
            // escribir el inventario base, la copia congelada o la revisión de salida.
            // DELETE no entra, y no por casualidad: ninguna de las cuatro se borra nunca
            // (AGENTS.md §6). Un elemento del inventario base que sobra se desactiva, la copia
            // congelada no se reescribe y una revisión firmada no se vuelve a firmar. La aplicación
            // no necesita ese permiso ni debe tenerlo por él.
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT, UPDATE ON inventario_base_item, contrato_inventario_item, revision_inventario, revision_inventario_item TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "revision_inventario_item");

            migrationBuilder.DropTable(
                name: "contrato_inventario_item");

            migrationBuilder.DropTable(
                name: "revision_inventario");

            migrationBuilder.DropTable(
                name: "inventario_base_item");

            migrationBuilder.DropColumn(
                name: "inventario_congelado_at",
                table: "contrato");
        }
    }
}
